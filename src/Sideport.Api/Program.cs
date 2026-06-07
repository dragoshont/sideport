using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.Devices;
using Sideport.Orchestrator;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------
var anisetteBaseUrl = builder.Configuration["Sideport:Anisette:Url"]
    ?? builder.Configuration["Sideport:Anisette:BaseUrl"]
    ?? "http://anisette:6969/";
var deviceId = builder.Configuration["Sideport:Apple:DeviceId"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_DEVICE_ID")
    ?? throw new InvalidOperationException(
        "Sideport:Apple:DeviceId (or SIDEPORT_DEVICE_ID) must be set to a stable UUID.");
var signerOptions = new SignerOptions
{
    SignerBinaryPath = builder.Configuration["Sideport:Signer:BinaryPath"]
        ?? "/opt/sideport/zsign",
};

// API bearer token (design §P7 / invariant: the refresh trigger must not be
// open). When set, every /api/* route requires `Authorization: Bearer <token>`;
// the k8s probes (/healthz, /readyz) and root stay open. When unset, the API is
// reachable without auth — acceptable ONLY behind LAN-only + reverse-proxy auth,
// and logged loudly at startup.
var apiToken = builder.Configuration["Sideport:Api:AuthToken"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_API_TOKEN");

// --- Seams (design §4): IAnisetteProvider / ISigner / IDeviceController /
//     IAppleDeveloperPortal. -----------------------------------------------
builder.Services.AddSingleton(signerOptions);
builder.Services.AddSingleton<ISigner, ProcessSigner>();
builder.Services.AddDeviceController();

// GrandSlam auth (P3) + developer portal, with their configured HttpClients.
var allowInsecureTls = builder.Configuration.GetValue("Sideport:Apple:AllowInsecureTls", false);
builder.Services.AddAppleDeveloperPortal(new Uri(anisetteBaseUrl), deviceId, allowInsecureTls);

// Refresh orchestrator + scheduler (P6): the single-flight re-sign loop.
var runScheduler = builder.Configuration.GetValue("Sideport:Scheduler:Enabled", true);
builder.Services.AddRefreshOrchestrator(runScheduler: runScheduler);

var app = builder.Build();

if (string.IsNullOrEmpty(apiToken))
{
    app.Logger.LogWarning(
        "Sideport:Api:AuthToken is not set — the /api surface is UNAUTHENTICATED. " +
        "Only run this behind LAN-only access + reverse-proxy auth.");
}

// --- Bearer auth for /api/* (probes + root stay open) ----------------------
app.Use(async (context, next) =>
{
    if (!string.IsNullOrEmpty(apiToken) &&
        context.Request.Path.StartsWithSegments("/api"))
    {
        string? provided = null;
        if (context.Request.Headers.TryGetValue("Authorization", out var auth))
        {
            string raw = auth.ToString();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                provided = raw["Bearer ".Length..].Trim();
        }

        if (provided is null || !FixedTimeEquals(provided, apiToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
            return;
        }
    }

    await next();
});

// --- Public API skeleton (design §2 stable contract) -----------------------
app.MapGet("/", () => Results.Ok(new
{
    service = "sideport",
    status = "ok",
    docs = "https://github.com/dragoshont/homelab/blob/main/docs/sideport-implementation-plan.md",
}));

// Liveness: the process is up. Cheap, dependency-free (k8s livenessProbe).
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// Readiness: the load-bearing dependencies are usable (k8s readinessProbe).
//   - anisette reachable (the ADI sidecar — without it nothing authenticates)
//   - signer binary present + executable
app.MapGet("/readyz", async (IAnisetteProvider anisette, SignerOptions signer, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    bool anisetteOk;
    string? anisetteError = null;
    try
    {
        _ = await anisette.GetClientInfoAsync(timeout.Token);
        anisetteOk = true;
    }
    catch (Exception ex)
    {
        anisetteOk = false;
        anisetteError = ex.GetType().Name;
    }

    bool signerOk = File.Exists(signer.SignerBinaryPath);

    bool ready = anisetteOk && signerOk;
    var payload = new
    {
        ready,
        checks = new
        {
            anisette = new { ok = anisetteOk, error = anisetteError },
            signer = new { ok = signerOk, path = signer.SignerBinaryPath },
        },
    };
    return ready ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
});

// Anisette health probe — the load-bearing sidecar (design §5).
app.MapGet("/api/anisette/info", async (IAnisetteProvider anisette, CancellationToken ct) =>
    Results.Ok(await anisette.GetClientInfoAsync(ct)));

// Device plane (design §8 phase 1) — wired to the seam, implementation pending.
app.MapGet("/api/devices", async (IDeviceController devices, CancellationToken ct) =>
    Results.Ok(await devices.ListDevicesAsync(ct)));

// Refresh orchestration (P6).
app.MapGet("/api/apps", async (IAppRegistry registry, RefreshOrchestrator orchestrator, CancellationToken ct) =>
{
    var apps = await registry.ListAsync(ct);
    var now = DateTimeOffset.UtcNow;
    return Results.Ok(apps.Select(a =>
    {
        var state = orchestrator.GetState(a.DeviceUdid, a.BundleId);
        return new
        {
            a.BundleId,
            a.DeviceUdid,
            a.AppleId,
            a.TeamId,
            expiresAt = state?.ExpiresAt,
            timeUntilExpiry = state?.TimeUntilExpiry(now),
            lastSucceeded = state?.LastSucceeded,
            lastError = state?.LastError,
        };
    }));
});

app.MapPost("/api/apps", async (AppRegistration registration, IAppRegistry registry, CancellationToken ct) =>
{
    await registry.UpsertAsync(registration, ct);
    return Results.Created($"/api/apps/{registration.DeviceUdid}/{registration.BundleId}", registration);
});

app.MapDelete("/api/apps/{udid}/{bundleId}", async (string udid, string bundleId, IAppRegistry registry, CancellationToken ct) =>
    await registry.RemoveAsync(udid, bundleId, ct) ? Results.NoContent() : Results.NotFound());

app.MapPost("/api/apps/{udid}/{bundleId}/refresh",
    async (string udid, string bundleId, IRefreshOrchestrator orchestrator, CancellationToken ct) =>
{
    var result = await orchestrator.RefreshAsync(udid, bundleId, ct);
    return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
});

app.Run();

/// <summary>Constant-time token comparison (avoids leaking length/prefix via timing).</summary>
static bool FixedTimeEquals(string a, string b)
{
    byte[] x = System.Text.Encoding.UTF8.GetBytes(a);
    byte[] y = System.Text.Encoding.UTF8.GetBytes(b);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(x, y);
}

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
