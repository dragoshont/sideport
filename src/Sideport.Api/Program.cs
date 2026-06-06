using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.Devices;
using Sideport.Orchestrator;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---------------------------------------------------------
var anisetteBaseUrl = builder.Configuration["Sideport:Anisette:BaseUrl"]
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

// --- Seams (design §4): IAnisetteProvider / ISigner / IDeviceController /
//     IAppleDeveloperPortal. -----------------------------------------------
builder.Services.AddSingleton(signerOptions);
builder.Services.AddSingleton<ISigner, ProcessSigner>();
builder.Services.AddSingleton<IDeviceController, NetimobiledeviceController>();

// GrandSlam auth (P3) + developer portal, with their configured HttpClients.
var allowInsecureTls = builder.Configuration.GetValue("Sideport:Apple:AllowInsecureTls", false);
builder.Services.AddAppleDeveloperPortal(new Uri(anisetteBaseUrl), deviceId, allowInsecureTls);

// Refresh orchestrator + scheduler (P6): the single-flight re-sign loop.
var runScheduler = builder.Configuration.GetValue("Sideport:Scheduler:Enabled", true);
builder.Services.AddRefreshOrchestrator(runScheduler: runScheduler);

var app = builder.Build();

// --- Public API skeleton (design §2 stable contract) -----------------------
app.MapGet("/", () => Results.Ok(new
{
    service = "sideport",
    status = "scaffold",
    docs = "https://github.com/dragoshont/homelab/blob/main/docs/sideport-dotnet-consolidation.md",
}));

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

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

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
