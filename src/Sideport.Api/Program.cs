using System.Diagnostics;
using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.Diagnostics;
using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Packaging;
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
var stateDirectory = builder.Configuration["Sideport:State:Directory"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_STATE_DIR")
    ?? Path.Combine(Path.GetTempPath(), "sideport");
var orchestratorOptions = new OrchestratorOptions
{
    StateDirectory = stateDirectory,
    WorkDirectory = builder.Configuration["Sideport:Orchestrator:WorkDirectory"]
        ?? Path.Combine(stateDirectory, "signed"),
};
var certClockSeedPath = builder.Configuration["Sideport:Catalog:SeedCertClockPath"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_CERT_CLOCK_IPA")
    ?? "/var/lib/altserver/ipa/CertCountdown.ipa";
var appStoreConnectOptions = new AppStoreConnectOptions(
    builder.Configuration["Sideport:AppStoreConnect:KeyId"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_KEY_ID"),
    builder.Configuration["Sideport:AppStoreConnect:IssuerId"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_ISSUER_ID"),
    builder.Configuration["Sideport:AppStoreConnect:PrivateKeyPath"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_KEY_PATH"),
    builder.Configuration["Sideport:AppStoreConnect:BaseUrl"]
        ?? "https://api.appstoreconnect.apple.com");
var credentialSource = builder.Configuration["Sideport:Apple:CredentialSource"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_CREDENTIAL_SOURCE")
    ?? "environment";
var personalAppleOptions = new PersonalAppleAccessOptions(
    builder.Configuration["Sideport:Apple:PersonalAppleId"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_PERSONAL_APPLE_ID"),
    credentialSource);

// API bearer token (design §P7 / invariant: the refresh trigger must not be
// open). When set, every /api/* route requires `Authorization: Bearer <token>`;
// the k8s probes (/healthz, /readyz) and root stay open. When unset, the API is
// reachable without auth — acceptable ONLY behind LAN-only + reverse-proxy auth,
// and logged loudly at startup.
var apiToken = builder.Configuration["Sideport:Api:AuthToken"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_API_TOKEN");

var operationLogs = new OperationLogStore(
    builder.Configuration.GetValue("Sideport:Logs:Capacity", 500));
builder.Services.AddSingleton(operationLogs);
builder.Logging.AddProvider(new OperationLogProvider(operationLogs));

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

// Credential source for Apple passwords. Default "environment" reads
// SIDEPORT_APPLE_PW_* — injected from a Kubernetes Secret that is filled either
// by SOPS or by Azure Key Vault via the External Secrets Operator (the in-cluster
// path; both look identical to the app). Set "keychain" for LOCAL macOS
// development to read from the login keychain via the `security` CLI. The browser
// portal NEVER collects the password (custody invariant); it only comes from this
// host-side source. Registered BEFORE AddRefreshOrchestrator so its
// TryAddSingleton default yields to this.
if (string.Equals(credentialSource, "keychain", StringComparison.OrdinalIgnoreCase))
{
    var keychainOptions = new KeychainCredentialOptions(
        ServiceName: builder.Configuration["Sideport:Keychain:ServiceName"]
            ?? Environment.GetEnvironmentVariable("SIDEPORT_KEYCHAIN_SERVICE")
            ?? "sideport-apple-pw");
    builder.Services.AddSingleton(keychainOptions);
    builder.Services.AddSingleton<IAppleCredentialProvider>(
        sp => new AppleKeychainCredentialProvider(sp.GetRequiredService<KeychainCredentialOptions>()));
}

builder.Services.AddRefreshOrchestrator(orchestratorOptions, runScheduler: runScheduler);
builder.Services.AddSingleton(new AppCatalogOptions(
    Path.Combine(stateDirectory, "catalog.json"),
    [new AppCatalogSeed(
        "cert-clock",
        "Cert Clock",
        certClockSeedPath,
        "ro.hont.certcountdown",
        "First signing and expiry-countdown test app.")]));
builder.Services.AddSingleton<IAppCatalog, FileAppCatalog>();
builder.Services.AddSingleton(appStoreConnectOptions);
builder.Services.AddSingleton(personalAppleOptions);
builder.Services.AddHttpClient("app-store-connect");
builder.Services.AddSingleton<IAppleAccessProbe>(sp => new AppStoreConnectProbe(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("app-store-connect"),
    sp.GetRequiredService<AppStoreConnectOptions>()));
builder.Services.AddSingleton<IPersonalAppleAccess, PersonalAppleAccess>();

var app = builder.Build();
bool hasAdminBundle = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sideport.Api.Requests");

if (hasAdminBundle)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

if (string.IsNullOrEmpty(apiToken))
{
    app.Logger.LogWarning(
        "Sideport:Api:AuthToken is not set — the /api surface is UNAUTHENTICATED. " +
        "Only run this behind LAN-only access + reverse-proxy auth.");
}

// --- Request log + bearer auth for /api/* (probes + root stay open) ---------
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var sw = Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        requestLogger.LogInformation(
            "api {Method} {Path} -> {StatusCode} in {ElapsedMs}ms",
            context.Request.Method,
            context.Request.Path.Value ?? "/api",
            context.Response.StatusCode,
            sw.ElapsedMilliseconds);
    }
});

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
object ServiceInfo() => new
{
    service = "sideport",
    status = "ok",
    admin = hasAdminBundle ? "served" : "not-built",
    docs = "https://github.com/dragoshont/sideport/blob/main/docs/sideport-implementation-plan.md",
};

if (!hasAdminBundle)
    app.MapGet("/", () => Results.Ok(ServiceInfo()));

app.MapGet("/api/about", () => Results.Ok(ServiceInfo()));

// Liveness: the process is up. Cheap, dependency-free (k8s livenessProbe).
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// Readiness: the load-bearing dependencies are usable (k8s readinessProbe).
//   - anisette reachable (the ADI sidecar — without it nothing authenticates)
//   - signer binary present + executable
app.MapGet("/readyz", async (IAnisetteProvider anisette, SignerOptions signer, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    (bool anisetteOk, string? anisetteError) = await CheckAnisetteAsync(anisette, timeout.Token);

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
{
    try
    {
        return Results.Ok(await anisette.GetClientInfoAsync(ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            ok = false,
            error = ex.GetType().Name,
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/logs", (OperationLogStore logs, int? limit) =>
    Results.Ok(logs.Read(limit ?? 100)));

app.MapGet("/api/apple-access/status", async (IAppleAccessProbe probe, CancellationToken ct) =>
    Results.Ok(await probe.ProbeAsync(ct)));

app.MapGet("/api/apple-access/personal/status", async (IPersonalAppleAccess access, CancellationToken ct) =>
    Results.Ok(await access.StatusAsync(ct)));

app.MapPost("/api/apple-access/personal/sign-in", async (PersonalAppleSignInRequest request, IPersonalAppleAccess access, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await access.SignInAsync(request, ct));
    }
    catch (InvalidOperationException ex)
    {
        return Results.UnprocessableEntity(new { error = "apple-credential-missing", detail = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["appleId"] = [ex.Message] });
    }
});

app.MapPost("/api/apple-access/personal/2fa", async (PersonalAppleCompleteTwoFactorRequest request, IPersonalAppleAccess access, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await access.CompleteTwoFactorAsync(request, ct));
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = "apple-2fa-challenge-not-found", detail = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["twoFactor"] = [ex.Message] });
    }
});

// Device plane (design §8 phase 1) — wired to the seam, implementation pending.
app.MapGet("/api/devices", async (IDeviceController devices, CancellationToken ct) =>
    Results.Ok(await devices.ListDevicesAsync(ct)));

app.MapGet("/api/devices/{udid}/installed-apps", async (string udid, IDeviceController devices, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await devices.ListInstalledAppsAsync(udid, ct));
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            error = "device-installed-apps-unavailable",
            detail = ex.GetType().Name,
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// First-run onboarding status: one read-only endpoint that tells the portal
// whether the safe prerequisites are in place before any sign/install action is
// offered. This mirrors /readyz but adds operator-facing setup milestones.
app.MapGet("/api/onboarding/status", async (
    IAnisetteProvider anisette,
    SignerOptions signer,
    IDeviceController devices,
    IAppRegistry registry,
    IAppCatalog catalog,
    CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    (bool anisetteOk, string? anisetteError) = await CheckAnisetteAsync(anisette, timeout.Token);
    bool signerOk = File.Exists(signer.SignerBinaryPath);

    IReadOnlyList<DeviceInfo> reachableDevices = Array.Empty<DeviceInfo>();
    string? deviceError = null;
    try
    {
        reachableDevices = await devices.ListDevicesAsync(timeout.Token);
    }
    catch (Exception ex)
    {
        deviceError = ex.GetType().Name;
    }

    IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct);
    IReadOnlyList<CatalogAppDto> catalogApps = Array.Empty<CatalogAppDto>();
    string? catalogError = null;
    try
    {
        catalogApps = await catalog.ListAsync(ct);
    }
    catch (Exception ex)
    {
        catalogError = ex.GetType().Name;
    }

    int readyCatalogApps = catalogApps.Count(app => app.Status == "ready");
    bool apiProtected = !string.IsNullOrEmpty(apiToken);

    var steps = new[]
    {
        new OnboardingStep(
            "api-auth",
            "Protect the API",
            "Set SIDEPORT_API_TOKEN or put the portal behind trusted reverse-proxy auth before exposing refresh actions.",
            apiProtected ? "complete" : "warning",
            "portal",
            true,
            null,
            apiProtected ? "Bearer token configured." : "SIDEPORT_API_TOKEN is not configured; keep this LAN-only until fixed."),
        new OnboardingStep(
            "anisette",
            "Trust anisette identity",
            "Use the provisioned host anisette identity so first login can inherit trusted-device state instead of looping through 2FA.",
            anisetteOk ? "complete" : "blocked",
            "portal",
            true,
            null,
            anisetteError ?? "Anisette client info is available."),
        new OnboardingStep(
            "signer",
            "Verify signer binary",
            "Sideport needs the patched zsign binary before it can refresh an IPA.",
            signerOk ? "complete" : "blocked",
            "portal",
            true,
            null,
            signer.SignerBinaryPath),
        new OnboardingStep(
            "device",
            "Connect a device",
            "A reachable USB or Wi-Fi device is required before registering apps.",
            reachableDevices.Count > 0 ? "complete" : deviceError is null ? "pending" : "blocked",
            "portal",
            true,
            null,
            deviceError ?? $"{reachableDevices.Count} reachable device(s)."),
        new OnboardingStep(
            "catalog",
            "Prepare a catalog app",
            "Inspect at least one server-side IPA before saving a phone registration.",
            readyCatalogApps > 0 ? "complete" : catalogError is null ? "pending" : "blocked",
            "portal",
            true,
            null,
            catalogError ?? $"{readyCatalogApps} ready catalog app(s)."),
        new OnboardingStep(
            "iphone-trust-computer",
            "Trust this computer",
            "On the iPhone, keep the screen awake, connect over USB, tap Trust, and enter the passcode if prompted.",
            reachableDevices.Count > 0 ? "complete" : "pending",
            "iphone",
            false,
            null,
            reachableDevices.Count > 0 ? "Device discovery works from the host." : "Needed before Sideport can see a new iPhone."),
        new OnboardingStep(
            "iphone-developer-mode",
            "Enable Developer Mode",
            "On iOS 16+, open Settings > Privacy & Security > Developer Mode, enable it, then restart when prompted.",
            registrations.Count > 0 ? "warning" : "pending",
            "iphone",
            false,
            "Settings > Privacy & Security > Developer Mode",
            "Required before development-signed apps can launch on newer iOS."),
        new OnboardingStep(
            "iphone-profile-trust",
            "Trust the developer profile",
            "After the first install, open Settings > General > VPN & Device Management, choose the Apple Development profile, then tap Trust.",
            registrations.Count > 0 ? "warning" : "pending",
            "iphone",
            false,
            "Settings > General > VPN & Device Management",
            "Only appears on the iPhone after the first app is installed."),
        new OnboardingStep(
            "iphone-keep-awake",
            "Keep the iPhone awake during install",
            "Leave the iPhone unlocked on the same network while Sideport signs and installs the app.",
            "pending",
            "iphone",
            false,
            null,
            "Prevents install failures caused by the device going unreachable."),
        new OnboardingStep(
            "first-app",
            "Register first app",
            "Add an IPA path, Apple ID, team, device UDID, and bundle ID before enabling manual refresh.",
            registrations.Count > 0 ? "complete" : "pending",
            "portal",
            true,
            null,
            registrations.Count > 0 ? $"{registrations.Count} registered app(s)." : "No apps registered yet."),
        new OnboardingStep(
            "scheduler",
            "Keep scheduler off until cutover",
            "The background scheduler should remain off while AltServer is still the active signer.",
            runScheduler ? "warning" : "complete",
            "portal",
            false,
            null,
            runScheduler ? "Scheduler is enabled." : "Scheduler is disabled."),
    };

    return Results.Ok(new OnboardingStatus(
        FirstRunComplete: steps.Where(s => s.Required).All(s => s.State == "complete"),
        SchedulerEnabled: runScheduler,
        Steps: steps));
});

// Refresh orchestration (P6).
app.MapGet("/api/catalog/apps", async (IAppCatalog catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListAsync(ct)));

app.MapPost("/api/catalog/apps/inspect", async (CatalogInspectRequest request, IAppCatalog catalog, CancellationToken ct) =>
{
    try
    {
        CatalogAppDto entry = await catalog.InspectAndStoreAsync(request, ct);
        return Results.Created($"/api/catalog/apps/{entry.Id}", entry);
    }
    catch (FileNotFoundException ex)
    {
        return Results.NotFound(new { error = "ipa-not-found", path = ex.FileName });
    }
    catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is InvalidDataException)
    {
        return Results.UnprocessableEntity(new { error = "ipa-inspection-failed", detail = ex.Message });
    }
});

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
    var validationErrors = ValidateRegistration(registration);
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors);

    if (!File.Exists(registration.InputIpaPath))
        return Results.UnprocessableEntity(new { error = "ipa-not-found", path = registration.InputIpaPath });

    IpaInfo info;
    try
    {
        info = IpaInspector.Inspect(registration.InputIpaPath);
    }
    catch (Exception ex) when (ex is FormatException || ex is InvalidDataException)
    {
        return Results.UnprocessableEntity(new { error = "ipa-inspection-failed", detail = ex.Message });
    }

    if (!string.Equals(info.BundleIdentifier, registration.BundleId, StringComparison.Ordinal))
    {
        return Results.UnprocessableEntity(new
        {
            error = "bundle-mismatch",
            requestedBundleId = registration.BundleId,
            inspectedBundleId = info.BundleIdentifier,
        });
    }

    IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct);
    bool replacesExisting = apps.Any(app =>
        string.Equals(app.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(app.BundleId, registration.BundleId, StringComparison.Ordinal));
    int deviceRegistrations = apps.Count(app => string.Equals(app.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase));
    if (!replacesExisting && deviceRegistrations >= 3)
    {
        return Results.Conflict(new
        {
            error = "device-app-slot-limit",
            detail = "Free Apple developer accounts can keep three sideloaded app registrations per device.",
            limit = 3,
        });
    }

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

if (hasAdminBundle)
    app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Constant-time token comparison (avoids leaking length/prefix via timing).</summary>
static bool FixedTimeEquals(string a, string b)
{
    byte[] x = System.Text.Encoding.UTF8.GetBytes(a);
    byte[] y = System.Text.Encoding.UTF8.GetBytes(b);
    return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(x, y);
}

static async Task<(bool Ok, string? Error)> CheckAnisetteAsync(IAnisetteProvider anisette, CancellationToken ct)
{
    try
    {
        _ = await anisette.GetClientInfoAsync(ct);
        return (true, null);
    }
    catch (Exception ex)
    {
        return (false, ex.GetType().Name);
    }
}

static Dictionary<string, string[]> ValidateRegistration(AppRegistration registration)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    AddRequired(errors, nameof(AppRegistration.BundleId), registration.BundleId);
    AddRequired(errors, nameof(AppRegistration.DeviceUdid), registration.DeviceUdid);
    AddRequired(errors, nameof(AppRegistration.AppleId), registration.AppleId);
    AddRequired(errors, nameof(AppRegistration.TeamId), registration.TeamId);
    AddRequired(errors, nameof(AppRegistration.InputIpaPath), registration.InputIpaPath);
    return errors;
}

static void AddRequired(Dictionary<string, string[]> errors, string field, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        errors[field] = ["Required."];
}

public sealed record OnboardingStatus(
    bool FirstRunComplete,
    bool SchedulerEnabled,
    IReadOnlyList<OnboardingStep> Steps);

public sealed record OnboardingStep(
    string Id,
    string Label,
    string Description,
    string State,
    string Surface,
    bool Required,
    string? SettingsPath,
    string? Detail);

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
