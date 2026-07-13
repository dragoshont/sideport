using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Sideport.Api.AppleAccess;
using Sideport.Api.Authentik;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Diagnostics;
using Sideport.Api.DiagnosticsIssues;
using Sideport.Api.GitHubCatalog;
using Sideport.Api.Onboarding;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
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
if (TimeSpan.TryParse(
        builder.Configuration["Sideport:Orchestrator:InstallTimeout"],
        out TimeSpan installTimeout))
{
    orchestratorOptions.InstallTimeout = installTimeout;
}
if (TimeSpan.TryParse(
        builder.Configuration["Sideport:Orchestrator:InstallCancellationGrace"],
        out TimeSpan installCancellationGrace))
{
    orchestratorOptions.InstallCancellationGrace = installCancellationGrace;
}
// Optional fixed re-sign cadence (e.g. "1.00:00:00" = daily) so signatures are
// renewed well before the 7-day profile expiry, keeping a fresh safety margin.
if (TimeSpan.TryParse(builder.Configuration["Sideport:Scheduler:ResignInterval"], out TimeSpan resignInterval))
    orchestratorOptions.ResignInterval = resignInterval;
var certClockSeedPath = builder.Configuration["Sideport:Catalog:SeedCertClockPath"]
    ?? Environment.GetEnvironmentVariable("SIDEPORT_CERT_CLOCK_IPA")
    ?? "/var/lib/altserver/ipa/CertCountdown.ipa";
long catalogMaxUploadBytes = builder.Configuration.GetValue<long?>("Sideport:Catalog:MaxUploadBytes")
    ?? 268_435_456;
AppCatalogImportRoot[] catalogImportRoots = builder.Configuration
    .GetSection("Sideport:Catalog:ImportRoots")
    .GetChildren()
    .Select(section => new AppCatalogImportRoot(
        section["Id"] ?? string.Empty,
        section["Label"] ?? string.Empty,
        section["Path"] ?? string.Empty))
    .ToArray();
Uri publicOrigin = BrowserSecurityPolicy.ParsePublicOrigin(
    builder.Configuration["Sideport:PublicOrigin"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_PUBLIC_ORIGIN")
        ?? "http://127.0.0.1:8080/");
GitHubConfiguredSource[] githubConfiguredSources = builder.Configuration
    .GetSection("Sideport:Catalog:GitHub:Sources")
    .GetChildren()
    .Select(section => new GitHubConfiguredSource(
        section["Id"] ?? string.Empty,
        section["Repository"] ?? string.Empty,
        section["Visibility"] ?? "public",
        ParseOptionalPositiveInt64(section["RepositoryId"]),
        ParseOptionalPositiveInt64(section["InstallationId"]),
        bool.TryParse(section["AllowPrereleases"], out bool allowPrereleases) && allowPrereleases,
        section["AccessTokenEnvironmentVariable"]))
    .ToArray();
var githubCatalogOptions = new GitHubCatalogOptions(
    Path.Combine(stateDirectory, "github-catalog.json"),
    Path.Combine(stateDirectory, "github-downloads"),
    catalogMaxUploadBytes,
    publicOrigin)
{
    AppId = ParseOptionalPositiveInt64(
        builder.Configuration["Sideport:Catalog:GitHub:AppId"]
            ?? Environment.GetEnvironmentVariable("SIDEPORT_GITHUB_APP_ID")),
    AppSlug = builder.Configuration["Sideport:Catalog:GitHub:AppSlug"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_GITHUB_APP_SLUG"),
    AppPrivateKeyPath = builder.Configuration["Sideport:Catalog:GitHub:AppPrivateKeyPath"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_GITHUB_APP_PRIVATE_KEY_PATH"),
    UiStatusPath = builder.Configuration["Sideport:Catalog:GitHub:UiStatusPath"]
        ?? "/settings/apps",
    ConfiguredSources = githubConfiguredSources,
};
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = catalogMaxUploadBytes + 1_048_576;
});
var appStoreConnectOptions = new AppStoreConnectOptions(
    builder.Configuration["Sideport:AppStoreConnect:KeyId"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_KEY_ID"),
    builder.Configuration["Sideport:AppStoreConnect:IssuerId"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_ISSUER_ID"),
    builder.Configuration["Sideport:AppStoreConnect:PrivateKeyPath"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_ASC_KEY_PATH"),
    builder.Configuration["Sideport:AppStoreConnect:BaseUrl"]
        ?? "https://api.appstoreconnect.apple.com");
var credentialSource = AppleCredentialSources.Normalize(
    builder.Configuration["Sideport:Apple:CredentialSource"]
        ?? Environment.GetEnvironmentVariable("SIDEPORT_CREDENTIAL_SOURCE"));
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

// OIDC (Authentik) interactive login for the admin UI (native relying party).
// Optional: when Sideport:Oidc:Enabled is false (default) the service behaves
// exactly as before (bearer-only /api, open UI shell) so tests + local dev are
// unaffected. When true, the browser UI is gated behind OpenID Connect and the
// authenticated session cookie additionally authorizes /api/* (the bearer token
// stays valid for machine clients).
var oidcEnabled = builder.Configuration.GetValue("Sideport:Oidc:Enabled", false);
var oidcAuthority = builder.Configuration["Sideport:Oidc:Authority"];
var oidcClientId = builder.Configuration["Sideport:Oidc:ClientId"];
var oidcClientSecret = builder.Configuration["Sideport:Oidc:ClientSecret"];
Uri? authentikBaseUrl = ParseOptionalHttpsUri(builder.Configuration["Sideport:Authentik:BaseUrl"], "Sideport:Authentik:BaseUrl");
string? authentikApiToken = builder.Configuration["Sideport:Authentik:ApiToken"];
string authentikEnrollmentFlowSlug = builder.Configuration["Sideport:Authentik:EnrollmentFlowSlug"] ?? "sideport-enrollment";
Guid? authentikEnrollmentFlowId = ParseOptionalGuid(
    builder.Configuration["Sideport:Authentik:EnrollmentFlowId"],
    "Sideport:Authentik:EnrollmentFlowId");
Uri? authentikRecoveryUrl = ParseOptionalHttpsUri(
    builder.Configuration["Sideport:Authentik:RecoveryUrl"],
    "Sideport:Authentik:RecoveryUrl");
int authentikInvitationMinutes = builder.Configuration.GetValue("Sideport:Authentik:InvitationLifetimeMinutes", 15);
if (authentikInvitationMinutes is < 5 or > 60)
    throw new InvalidOperationException("Sideport:Authentik:InvitationLifetimeMinutes must be from 5 to 60.");
var authentikEnrollmentOptions = new AuthentikEnrollmentOptions(
    authentikBaseUrl,
    authentikApiToken,
    authentikEnrollmentFlowSlug,
    authentikEnrollmentFlowId,
    TimeSpan.FromMinutes(authentikInvitationMinutes),
    new Uri(publicOrigin, "/login?returnUrl=%2Finvite"));
string oidcProviderId = builder.Configuration["Sideport:Oidc:ProviderId"] ?? "oidc";
string oidcProviderLabel = builder.Configuration["Sideport:Oidc:ProviderLabel"] ?? "Identity provider";
string oidcLoginLabel = builder.Configuration["Sideport:Oidc:LoginLabel"] ?? "Continue to sign in";
IPAddress[] trustedProxies = ReadConfigurationList(builder.Configuration, "Sideport:ReverseProxy:KnownProxies")
    .Select(ParseTrustedProxy)
    .ToArray();
System.Net.IPNetwork[] trustedProxyNetworks = ReadConfigurationList(
        builder.Configuration,
        "Sideport:ReverseProxy:KnownNetworks")
    .Select(ParseTrustedProxyNetwork)
    .ToArray();
bool useForwardedHeaders = trustedProxies.Length != 0 || trustedProxyNetworks.Length != 0;

var appleCredentialRateLimitOptions = new AppleCredentialRateLimitOptions(
    builder.Configuration.GetValue<int?>("Sideport:Apple:CredentialRateLimit:ClientPermitLimit")
        ?? AppleCredentialRateLimitOptions.Default.ClientPermitLimit,
    TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("Sideport:Apple:CredentialRateLimit:ClientWindowSeconds")
        ?? (int)AppleCredentialRateLimitOptions.Default.ClientWindow.TotalSeconds),
    builder.Configuration.GetValue<int?>("Sideport:Apple:CredentialRateLimit:AccountPermitLimit")
        ?? AppleCredentialRateLimitOptions.Default.AccountPermitLimit,
    TimeSpan.FromSeconds(builder.Configuration.GetValue<int?>("Sideport:Apple:CredentialRateLimit:AccountWindowSeconds")
        ?? (int)AppleCredentialRateLimitOptions.Default.AccountWindow.TotalSeconds));

var operationLogs = new OperationLogStore(
    builder.Configuration.GetValue("Sideport:Logs:Capacity", 500));
builder.Services.AddSingleton(operationLogs);
builder.Logging.AddProvider(new OperationLogProvider(operationLogs));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = catalogMaxUploadBytes + 1_048_576;
    options.ValueLengthLimit = 16_384;
});
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-Sideport-CSRF";
    options.Cookie.Name = "sideport.csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddSingleton(new AppleCredentialRateLimiter(appleCredentialRateLimitOptions));

if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
            ForwardedHeaders.XForwardedProto |
            ForwardedHeaders.XForwardedHost;
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (IPAddress proxy in trustedProxies)
            options.KnownProxies.Add(proxy);
        foreach (System.Net.IPNetwork network in trustedProxyNetworks)
            options.KnownIPNetworks.Add(network);
    });
}

// --- Seams (design §4): IAnisetteProvider / ISigner / IDeviceController /
//     IAppleDeveloperPortal. -----------------------------------------------
builder.Services.AddSingleton(signerOptions);
builder.Services.AddSingleton<ISigner, ProcessSigner>();
builder.Services.AddDeviceController();

// GrandSlam auth (P3) + developer portal, with their configured HttpClients.
var allowInsecureTls = builder.Configuration.GetValue("Sideport:Apple:AllowInsecureTls", false);
var allowInsecureCredentialEntryOnLoopback = builder.Configuration.GetValue(
    "Sideport:Apple:AllowInsecureCredentialEntryOnLoopback",
    false);
string dataProtectionKeyRingDirectory = Path.Combine(stateDirectory, "data-protection-keys");
PrivateAppleStoreFiles.EnsureDirectory(dataProtectionKeyRingDirectory);
builder.Services
    .AddDataProtection()
    .SetApplicationName("Sideport.ManagedAppleCredential")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingDirectory));
// The signing identity (minted cert + key) MUST persist on the PVC, not the
// pod's ephemeral /tmp — otherwise every restart loses it, the next refresh mints
// a NEW certificate, and the user has to re-trust the developer on the device
// (and the free-tier cert quota churns). The per-call signer staging stays
// ephemeral under the default WorkDirectory.
builder.Services.AddAppleDeveloperPortal(new Uri(anisetteBaseUrl), deviceId, allowInsecureTls,
    signingIdentityDirectory: Path.Combine(stateDirectory, "identities"));

// Refresh orchestrator + scheduler (P6): the single-flight re-sign loop.
var runScheduler = builder.Configuration.GetValue("Sideport:Scheduler:Enabled", true);

// Credential source is explicit: managed is a Data Protection envelope under
// the durable state root; environment and Keychain remain read-only providers.
// Unknown configured values fail startup instead of silently falling back.
builder.Services.AddSingleton(new AppleAccountStateStoreOptions(
    Path.Combine(stateDirectory, "apple-account.json")));
builder.Services.AddSingleton<AppleAccountStateStore>();
if (string.Equals(credentialSource, AppleCredentialSources.Managed, StringComparison.Ordinal))
{
    string credentialDirectory = Path.Combine(stateDirectory, "apple-credentials");
    PrivateAppleStoreFiles.EnsureDirectory(credentialDirectory);
    builder.Services.AddSingleton(new ManagedAppleCredentialStoreOptions(
        credentialDirectory,
        dataProtectionKeyRingDirectory));
    builder.Services.AddSingleton<ManagedAppleCredentialStore>();
    builder.Services.AddSingleton<IAppleCredentialProvider>(sp =>
        sp.GetRequiredService<ManagedAppleCredentialStore>());
    builder.Services.AddSingleton<IAppleCredentialManagement>(sp =>
        sp.GetRequiredService<ManagedAppleCredentialStore>());
}
else if (string.Equals(credentialSource, AppleCredentialSources.Keychain, StringComparison.Ordinal))
{
    var keychainOptions = new KeychainCredentialOptions(
        ServiceName: builder.Configuration["Sideport:Keychain:ServiceName"]
            ?? Environment.GetEnvironmentVariable("SIDEPORT_KEYCHAIN_SERVICE")
            ?? "sideport-apple-pw");
    builder.Services.AddSingleton(keychainOptions);
    builder.Services.AddSingleton<IAppleCredentialProvider>(
        sp => new AppleKeychainCredentialProvider(sp.GetRequiredService<KeychainCredentialOptions>()));
    builder.Services.AddSingleton<IAppleCredentialManagement>(
        new ReadOnlyAppleCredentialManagement(AppleCredentialSources.Keychain));
}
else
{
    builder.Services.AddSingleton<IAppleCredentialProvider, EnvironmentCredentialProvider>();
    builder.Services.AddSingleton<IAppleCredentialManagement>(
        new ReadOnlyAppleCredentialManagement(AppleCredentialSources.Environment));
}

builder.Services.AddRefreshOrchestrator(orchestratorOptions, runScheduler: false);
builder.Services.AddSingleton(new AppCatalogOptions(
    Path.Combine(stateDirectory, "catalog.json"),
    Path.Combine(stateDirectory, "imports"),
    catalogMaxUploadBytes,
    [new AppCatalogSeed(
        "cert-clock",
        "Cert Clock",
        certClockSeedPath,
        "com.example.certcountdown",
        "First signing and expiry-countdown test app.")],
    catalogImportRoots));
builder.Services.AddSingleton<IAppCatalog, FileAppCatalog>();
builder.Services.AddSingleton(githubCatalogOptions);
builder.Services.AddSingleton(new GitHubCatalogStore(githubCatalogOptions.StatePath));
builder.Services.AddSingleton<IGitHubDnsResolver, SystemGitHubDnsResolver>();
builder.Services.AddSingleton(sp => new HttpClient(
    GitHubHttpHandlerFactory.Create(
        sp.GetRequiredService<GitHubCatalogOptions>(),
        sp.GetRequiredService<IGitHubDnsResolver>()),
    disposeHandler: true)
{
    Timeout = Timeout.InfiniteTimeSpan,
});
builder.Services.AddSingleton<GitHubCatalogService>();
builder.Services.AddSingleton<IGitHubCatalogService>(sp => sp.GetRequiredService<GitHubCatalogService>());
builder.Services.AddSingleton<IGitHubCatalogImportService, GitHubCatalogImportService>();
builder.Services.AddSingleton(appStoreConnectOptions);
builder.Services.AddSingleton(personalAppleOptions);
builder.Services.AddSingleton(_ => new OperationStore(Path.Combine(stateDirectory, "operations.json")));
builder.Services.AddSingleton(new SchedulerSettingsStore(
    Path.Combine(stateDirectory, "scheduler.json")));
builder.Services.AddSingleton(new OnboardingCompletionStore(
    Path.Combine(stateDirectory, "onboarding-completion.json")));
builder.Services.AddSingleton(new FirstInstallOptions(runScheduler));
builder.Services.AddSingleton(new SystemStatusOptions(
    stateDirectory,
    orchestratorOptions.WorkDirectory,
    MutationProtected: !string.IsNullOrWhiteSpace(apiToken) || oidcEnabled));
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<SchedulerStatusService>();
builder.Services.AddSingleton<OperationQueue>();
builder.Services.AddSingleton<OperationService>();
builder.Services.AddSingleton<PendingRegistrationService>();
builder.Services.AddHostedService<OperationWorker>();
builder.Services.AddHostedService<OperationScheduler>();
builder.Services.AddSingleton(_ => new KnownDeviceStore(Path.Combine(stateDirectory, "known-devices.json")));
builder.Services.AddSingleton<KnownDeviceService>();
builder.Services.AddSingleton(new DeviceEnrollmentOptions());
builder.Services.AddSingleton<DeviceEnrollmentQueue>();
builder.Services.AddSingleton<DeviceEnrollmentService>();
builder.Services.AddHostedService<DeviceEnrollmentWorker>();
builder.Services.AddSingleton(_ => new DiagnosticIssueStore(Path.Combine(stateDirectory, "diagnostic-issues.json")));
builder.Services.AddSingleton<DiagnosticIssueService>();
builder.Services.AddSingleton(new WorkspaceAccessStore(stateDirectory));
builder.Services.AddSingleton<IGitHubSetupActorAuthorizer>(sp =>
    new WorkspaceGitHubSetupActorAuthorizer(
        sp.GetRequiredService<WorkspaceAccessStore>(),
        recoveryBearerConfigured: !string.IsNullOrWhiteSpace(apiToken)));
builder.Services.AddSingleton(_ => new WorkspaceLinkRateLimiter());
builder.Services.AddSingleton(sp => new WorkspaceExecutionAuthorizer(
    sp.GetRequiredService<WorkspaceAccessStore>(),
    sp.GetRequiredService<KnownDeviceStore>()));
builder.Services.AddSingleton<FamilyResourceAccess>();
builder.Services.AddSingleton(authentikEnrollmentOptions);
builder.Services.AddHttpClient<AuthentikEnrollmentAdapter>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<IAuthentikEnrollmentAdapter>(sp =>
    authentikEnrollmentOptions.Enabled
        ? sp.GetRequiredService<AuthentikEnrollmentAdapter>()
        : DisabledAuthentikEnrollmentAdapter.Instance);
builder.Services.AddSingleton(sp => new WorkspaceRequestPrincipalResolver(
    sp.GetRequiredService<WorkspaceAccessStore>(),
    apiToken,
    oidcEnabled));
builder.Services.AddSingleton(sp => new WorkspaceImpactService(
    sp.GetRequiredService<WorkspaceAccessStore>(),
    sp.GetRequiredService<KnownDeviceStore>(),
    sp.GetRequiredService<IAppRegistry>(),
    sp.GetRequiredService<OperationStore>(),
    sp.GetRequiredService<OperationService>()));
builder.Services.AddHttpClient("app-store-connect");
builder.Services.AddSingleton<IAppleAccessProbe>(sp => new AppStoreConnectProbe(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("app-store-connect"),
    sp.GetRequiredService<AppStoreConnectOptions>()));
builder.Services.AddSingleton<IPersonalAppleAccess, PersonalAppleAccess>();
builder.Services.AddSingleton(sp => new Lazy<IPersonalAppleAccess>(
    () => sp.GetRequiredService<IPersonalAppleAccess>()));
builder.Services.AddSingleton(new AppleAuthorityCutoverCoordinatorOptions(
    Path.Combine(stateDirectory, "apple-authority-cutover.json")));
builder.Services.AddSingleton<AppleAuthorityCutoverCoordinator>();
builder.Services.AddHostedService<AppleAuthorityCutoverRecoveryService>();
builder.Services.AddSingleton<SigningCutoverService>();
builder.Services.AddSingleton<SignerAuthorityGate>();
builder.Services.AddSingleton<AppleAccountReplacementCandidateService>();

// --- OIDC + cookie auth (only when enabled, so tests/local dev are unchanged) ---
if (oidcEnabled)
{
    if (string.IsNullOrWhiteSpace(oidcAuthority) ||
        string.IsNullOrWhiteSpace(oidcClientId) ||
        string.IsNullOrWhiteSpace(oidcClientSecret))
    {
        throw new InvalidOperationException(
            "Sideport:Oidc:Enabled is true but Sideport:Oidc:Authority/ClientId/ClientSecret are not all set.");
    }

    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie(options =>
        {
            options.Cookie.Name = "sideport.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                string? issuer = identity?.FindFirst(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType)?.Value;
                string? subject = identity?.FindFirst("sub")?.Value;
                if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                {
                    context.RejectPrincipal();
                    return;
                }

                try
                {
                    WorkspaceAccessDocument? workspace = await context.HttpContext.RequestServices
                        .GetRequiredService<WorkspaceAccessStore>()
                        .ReadAsync(context.HttpContext.RequestAborted)
                        .ConfigureAwait(false);
                    if (workspace?.Workspace.State == WorkspaceLifecycleState.Active)
                    {
                        string? epoch = identity!.FindFirst(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType)?.Value;
                        if (!string.Equals(epoch, workspace.Workspace.SecurityEpoch, StringComparison.Ordinal))
                            context.RejectPrincipal();
                    }
                }
                catch (WorkspaceAccessException)
                {
                    // Keep the cryptographically valid cookie identity intact.
                    // WorkspaceApiSecurity resolves the store again and returns
                    // a structured fail-closed 503 instead of misreporting a
                    // durable-store failure as an unauthenticated 401.
                }
            };
        })
        .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            options.Authority = oidcAuthority;
            options.ClientId = oidcClientId;
            options.ClientSecret = oidcClientSecret;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.GetClaimsFromUserInfoEndpoint = true;
            options.MapInboundClaims = false;
            options.CallbackPath = "/signin-oidc";
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.Scope.Clear();
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
            options.TokenValidationParameters.NameClaimType = "preferred_username";
            options.Events.OnTokenValidated = async context =>
            {
                var identity = context.Principal?.Identity as ClaimsIdentity;
                string? issuer = context.SecurityToken?.Issuer;
                string? subject = identity?.FindFirst("sub")?.Value;
                if (identity is null || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                {
                    context.Fail("The identity token is missing its validated issuer or subject.");
                    return;
                }

                foreach (Claim claim in identity.FindAll(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType).ToArray())
                    identity.RemoveClaim(claim);
                foreach (Claim claim in identity.FindAll(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType).ToArray())
                    identity.RemoveClaim(claim);
                identity.AddClaim(new Claim(
                    WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType,
                    issuer));

                try
                {
                    WorkspaceAccessDocument? workspace = await context.HttpContext.RequestServices
                        .GetRequiredService<WorkspaceAccessStore>()
                        .ReadAsync(context.HttpContext.RequestAborted)
                        .ConfigureAwait(false);
                    if (workspace?.Workspace.State == WorkspaceLifecycleState.Active)
                    {
                        identity.AddClaim(new Claim(
                            WorkspaceRequestPrincipalResolver.SecurityEpochClaimType,
                            workspace.Workspace.SecurityEpoch));
                    }
                }
                catch (WorkspaceAccessException)
                {
                    context.Fail("Workspace access is unavailable.");
                }
            };
        });

    builder.Services.AddAuthorization();
}

var app = builder.Build();
SchedulerSettingsStore schedulerSettingsStore = app.Services.GetRequiredService<SchedulerSettingsStore>();
try
{
    _ = await schedulerSettingsStore.InitializeAsync(
        requestedEnabled: runScheduler,
        prerequisitesSatisfied: false).ConfigureAwait(false);
}
catch (SchedulerSettingsStoreException ex)
{
    // Readiness is intentionally shallow: a damaged or unavailable durable
    // store must not prevent the admin shell from loading its repair guidance.
    app.Logger.LogError(ex, "scheduler settings initialization failed; scheduler APIs will report the store error");
}
bool hasAdminBundle = app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists;
var requestLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sideport.Api.Requests");
var appleCredentialRateLimiter = app.Services.GetRequiredService<AppleCredentialRateLimiter>();

// Only explicitly configured proxy addresses/networks may influence the
// effective scheme, host, or client IP. This applies equally to bearer-only and
// OIDC deployments so TLS termination behaves consistently without allowing a
// direct client to forge X-Forwarded-Proto.
if (useForwardedHeaders)
{
    app.UseWhen(
        context => IsTrustedProxy(
            context.Connection.RemoteIpAddress,
            trustedProxies,
            trustedProxyNetworks),
        proxyBranch => proxyBranch.UseForwardedHeaders());
}

// The bundled admin shell is the credential-entry surface. Keep executable
// content and API connections same-origin and deny all framing, including for
// older clients that do not understand frame-ancestors.
app.Use(async (context, next) =>
{
    bool privateLinkShell = context.Request.Path.Equals("/invite", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/owner-claim", StringComparison.OrdinalIgnoreCase);
    context.Response.Headers["Content-Security-Policy"] = privateLinkShell
        ? "default-src 'self'; base-uri 'none'; object-src 'none'; script-src 'self'; " +
          "style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; " +
          "form-action 'self'; frame-ancestors 'none'"
        : "default-src 'self'; base-uri 'self'; object-src 'none'; " +
          "script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
          "font-src 'self'; connect-src 'self'; form-action 'self'; frame-ancestors 'none'";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = privateLinkShell ? "no-referrer" : "same-origin";
    if (privateLinkShell)
        context.Response.Headers.CacheControl = "no-store";
    await next();
});

// OIDC pipeline (when enabled): forwarded headers -> auth -> gate the UI shell so
// the SPA is only served to an authenticated session; everything else 302s to
// Authentik. Must run before the static-file middleware below.
if (oidcEnabled)
{
    app.UseAuthentication();
    app.UseAuthorization();

    app.Use(async (context, next) =>
    {
        PathString path = context.Request.Path;
        bool isApi = path.StartsWithSegments("/api");
        bool isProbe = path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/readyz", StringComparison.OrdinalIgnoreCase);
        bool isSafeNavigation = HttpMethods.IsGet(context.Request.Method) ||
            HttpMethods.IsHead(context.Request.Method);
        bool isPrivateLinkShell = isSafeNavigation &&
            (path.Equals("/invite", StringComparison.OrdinalIgnoreCase) ||
             path.Equals("/owner-claim", StringComparison.OrdinalIgnoreCase));
        bool isPublicAdminAsset = isSafeNavigation && hasAdminBundle &&
            IsPublicAdminAsset(path, app.Environment.WebRootFileProvider);
        bool isAuthRoute = path.Equals("/signin-oidc", StringComparison.OrdinalIgnoreCase)
            || (HttpMethods.IsGet(context.Request.Method) &&
                path.Equals("/signout-callback-oidc", StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsGet(context.Request.Method) &&
                path.Equals("/login", StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsPost(context.Request.Method) &&
                path.Equals("/logout", StringComparison.OrdinalIgnoreCase))
            || isPrivateLinkShell
            || isPublicAdminAsset
            || (HttpMethods.IsGet(context.Request.Method) &&
                path.Equals("/github/setup/callback", StringComparison.OrdinalIgnoreCase));

        if (!isApi && !isProbe && !isAuthRoute &&
            context.User?.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync(
                OpenIdConnectDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = path + context.Request.QueryString });
            return;
        }

        await next();
    });
}

if (hasAdminBundle)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

if (string.IsNullOrEmpty(apiToken) && !oidcEnabled)
{
    app.Logger.LogWarning(
        "Sideport authentication is not configured — the /api read surface is open and all mutations are disabled. " +
        "Configure Sideport:Api:AuthToken or OIDC before onboarding or operating Sideport.");
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
    if (context.Request.Path.StartsWithSegments("/api/apple-access/personal"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.Use(async (context, next) =>
{
    await WorkspaceApiSecurity.InvokeAsync(
        context,
        _ => next(),
        context.RequestServices.GetRequiredService<WorkspaceRequestPrincipalResolver>(),
        context.RequestServices.GetRequiredService<IAntiforgery>(),
        allowInsecureCredentialEntryOnLoopback).ConfigureAwait(false);
});

// Managed credential entry and account/team mutation require effective HTTPS.
// Plain HTTP is accepted only when the operator explicitly enables the escape
// hatch and both ends of the actual connection are loopback; Host headers are
// never treated as transport evidence. This runs before endpoint body binding.
app.Use(async (context, next) =>
{
    bool isConnect = HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/api/apple-access/personal/connect", StringComparison.OrdinalIgnoreCase);
    bool isTeamSelection = HttpMethods.IsPut(context.Request.Method) &&
        context.Request.Path.Equals("/api/apple-access/personal/team", StringComparison.OrdinalIgnoreCase);
    bool isManagedTwoFactor = string.Equals(credentialSource, AppleCredentialSources.Managed, StringComparison.Ordinal) &&
        HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/api/apple-access/personal/2fa", StringComparison.OrdinalIgnoreCase);
    bool isSigningMutation = HttpMethods.IsPost(context.Request.Method) &&
        (context.Request.Path.Equals("/api/apple-access/personal/signing-preflight", StringComparison.OrdinalIgnoreCase) ||
         context.Request.Path.Equals("/api/apple-access/personal/cutover", StringComparison.OrdinalIgnoreCase) ||
         context.Request.Path.Equals("/api/apple-access/personal/replacement-candidates", StringComparison.OrdinalIgnoreCase) ||
         context.Request.Path.Equals("/api/apple-access/personal/replacement-candidates/2fa", StringComparison.OrdinalIgnoreCase));
    if (!isConnect && !isTeamSelection && !isManagedTwoFactor && !isSigningMutation)
    {
        await next();
        return;
    }

    context.Response.Headers.CacheControl = "no-store";
    if (!TryVerifiedActorFrom(context, out string actor))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "mutation-protection-required",
            message = "Configure bearer-token or OIDC authentication before changing the Apple signer.",
        });
        return;
    }

    bool oidcCookieActor = actor.StartsWith("oidc:", StringComparison.Ordinal);
    bool hasOrigin = context.Request.Headers.ContainsKey("Origin");
    if (AppleCredentialOriginPolicy.IsExplicitCrossSite(context.Request) ||
        (hasOrigin && !AppleCredentialOriginPolicy.IsSameOrigin(context.Request)) ||
        (oidcCookieActor && !AppleCredentialOriginPolicy.IsSameOrigin(context.Request)))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "origin-or-antiforgery",
            message = "The Apple account request must come from this Sideport origin.",
        });
        return;
    }

    if (oidcCookieActor)
    {
        try
        {
            IAntiforgery antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "origin-or-antiforgery",
                message = "Refresh Sideport and retry the protected Apple account request.",
            });
            return;
        }
    }

    AppleCredentialRateLimitDecision clientLimit = appleCredentialRateLimiter.AcquireClient(
        context.Connection.RemoteIpAddress?.ToString() ?? actor);
    if (!clientLimit.Allowed)
    {
        await WriteAppleRateLimitAsync(context, clientLimit.RetryAfter, "apple-credential-client-rate-limited");
        return;
    }

    if (allowInsecureTls)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "apple-tls-policy-unsafe",
            message = "Managed Apple account changes are disabled while insecure Apple TLS is enabled.",
        });
        return;
    }

    bool transportAllowed = AppleCredentialTransportPolicy.IsAllowed(
        context.Request.IsHttps,
        context.Connection.LocalIpAddress,
        context.Connection.RemoteIpAddress,
        allowInsecureCredentialEntryOnLoopback);
    if (!transportAllowed)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "credential-entry-transport-required",
            message = "Use HTTPS to change the Apple account or team.",
        });
        return;
    }

    const long maxCredentialBodyBytes = 16 * 1024;
    IHttpMaxRequestBodySizeFeature? bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodySize is { IsReadOnly: false })
        bodySize.MaxRequestBodySize = maxCredentialBodyBytes;
    if (context.Request.ContentLength is > maxCredentialBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "apple-credential-body-too-large",
            message = "The Apple credential request is too large.",
        });
        return;
    }
    if (!context.Request.HasJsonContentType())
    {
        context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "apple-credential-json-required",
            message = "The Apple credential request must use application/json.",
        });
        return;
    }

    await next();
});

// --- Public API skeleton (design §2 stable contract) -----------------------
object ServiceInfo() => new
{
    service = "sideport",
    status = "ok",
    admin = hasAdminBundle ? "served" : "not-built",
    docs = "https://github.com/dragoshont/sideport#readme",
};

if (!hasAdminBundle)
    app.MapGet("/", () => Results.Ok(ServiceInfo()));

app.MapGet("/api/about", () => Results.Ok(ServiceInfo()));
app.MapWorkspaceAccessEndpoints(
    new WorkspaceHttpOptions(
        publicOrigin,
        RecoveryProofConfigured: !string.IsNullOrWhiteSpace(apiToken)),
    new AuthentikAuthenticationOptions(
        oidcEnabled,
        authentikEnrollmentOptions.Enabled,
        new Uri(publicOrigin, "/login?returnUrl=%2F"),
        authentikRecoveryUrl,
        oidcProviderId,
        oidcProviderLabel,
        oidcLoginLabel));

// Interactive login/logout (only meaningful when OIDC is enabled).
if (oidcEnabled)
{
    app.MapGet("/login", (string? returnUrl) =>
    {
        if (!BrowserSecurityPolicy.TryNormalizeLocalReturnPath(returnUrl, out string redirectUri))
        {
            return Results.BadRequest(new
            {
                error = "invalid-return-url",
                message = "Continue to a page inside Sideport.",
            });
        }

        return Results.Challenge(
            new AuthenticationProperties { RedirectUri = redirectUri },
            [OpenIdConnectDefaults.AuthenticationScheme]);
    });

    // RP-initiated logout mutates the local session, so it requires an
    // authenticated, exact-origin request and the ASP.NET antiforgery token.
    // The OIDC handler still owns its state-validated GET callback.
    app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        if (!AppleCredentialOriginPolicy.IsSameOrigin(context.Request))
            return OriginOrAntiforgery();

        try
        {
            await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
        }
        catch (AntiforgeryValidationException)
        {
            return OriginOrAntiforgery();
        }

        return Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    }).RequireAuthorization();
}

// Liveness: the process is up. Cheap, dependency-free (k8s livenessProbe).
app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

// Keep the public readiness probe shallow so an Apple, anisette, signer, or
// durable-store problem cannot hide the setup/repair UI behind a 503. The
// authenticated system-status endpoint below owns operational truth.
app.MapGet("/readyz", () => Results.Ok(new { ready = true }));

app.MapGet("/api/system/status", async (SystemStatusService status, CancellationToken ct) =>
    Results.Ok(await status.GetAsync(ct).ConfigureAwait(false)));

app.MapGet("/api/scheduler/status", async (SchedulerStatusService scheduler, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await scheduler.GetAsync(ct).ConfigureAwait(false));
    }
    catch (Exception ex) when (ex is SchedulerSettingsStoreException or OperationStoreException or
                               IOException or UnauthorizedAccessException)
    {
        return Results.Json(
            new OperationErrorDto("scheduler-store-unavailable", "Automatic refresh settings are unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPut("/api/scheduler/settings", async (
    SchedulerSettingsRequest request,
    HttpContext context,
    SchedulerStatusService scheduler,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("changing automatic refresh settings");

    try
    {
        SchedulerSettingsUpdateResult result = await scheduler
            .SetEnabledAsync(request.Enabled, ct)
            .ConfigureAwait(false);
        return result.Status is not null
            ? Results.Ok(result.Status)
            : Results.Conflict(new OperationErrorDto(
                result.Error ?? "scheduler-prerequisites-not-met",
                result.Message ?? "Automatic refresh prerequisites are not met."));
    }
    catch (Exception ex) when (ex is SchedulerSettingsStoreException or OperationStoreException or
                               IOException or UnauthorizedAccessException)
    {
        return Results.Json(
            new OperationErrorDto("scheduler-store-unavailable", "Automatic refresh settings could not be saved."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
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

app.MapGet("/api/diagnostics/issues", async (
    HttpContext context,
    DiagnosticIssueService issues,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        IReadOnlyList<DiagnosticIssueDto> all = await issues.ListAsync(ct);
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
            return Results.Ok(all);

        var projection = new List<FamilyDiagnosticIssueDto>();
        foreach (DiagnosticIssueDto issue in all)
        {
            OperationRecordDto? operation = await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                issue.LastOperationId,
                requireOwnActor: false,
                ct).ConfigureAwait(false);
            if (operation is null)
                continue;
            OwnedFamilyRegistration? registration = await familyAccess.FindOwnedRegistrationAsync(
                principal.Member.MemberId,
                issue.Affected.DeviceUdid,
                issue.Affected.BundleId,
                ct).ConfigureAwait(false);
            projection.Add(FamilyResourceProjections.Diagnostic(issue, registration));
        }
        return Results.Ok(projection);
    }
    catch (DiagnosticIssueStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("diagnostics-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/diagnostics/issues/{issueId}", async (
    string issueId,
    HttpContext context,
    DiagnosticIssueService issues,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        DiagnosticIssueDto? issue = await issues.FindAsync(issueId, ct);
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (issue is null)
            return principal.Kind == WorkspaceRequestPrincipalKind.Family
                ? FamilyResourceNotFound()
                : Results.NotFound(new DiagnosticIssueErrorDto("diagnostic-issue-not-found", "Diagnostic issue not found."));
        if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
            return Results.Ok(issue);

        OperationRecordDto? operation = await familyAccess.FindOwnedOperationAsync(
            principal.Member!.MemberId,
            issue.LastOperationId,
            requireOwnActor: false,
            ct).ConfigureAwait(false);
        if (operation is null)
            return FamilyResourceNotFound();
        OwnedFamilyRegistration? registration = await familyAccess.FindOwnedRegistrationAsync(
            principal.Member.MemberId,
            issue.Affected.DeviceUdid,
            issue.Affected.BundleId,
            ct).ConfigureAwait(false);
        return Results.Ok(FamilyResourceProjections.Diagnostic(issue, registration));
    }
    catch (DiagnosticIssueStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("diagnostics-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPatch("/api/diagnostics/issues/{issueId}", async (string issueId, DiagnosticIssuePatchRequest request, DiagnosticIssueService issues, CancellationToken ct) =>
{
    try
    {
        DiagnosticIssueDto? issue = await issues.PatchAsync(issueId, request, ct);
        return issue is null ? Results.NotFound(new DiagnosticIssueErrorDto("diagnostic-issue-not-found", "Diagnostic issue not found.")) : Results.Ok(issue);
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [ex.Message] });
    }
    catch (DiagnosticIssueStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("diagnostics-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(new DiagnosticIssueErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/apple-access/status", async (IAppleAccessProbe probe, CancellationToken ct) =>
    Results.Ok(await probe.ProbeAsync(ct)));

app.MapGet("/api/apple-access/personal/status", async (
    HttpContext context,
    IPersonalAppleAccess access,
    IAntiforgery antiforgery,
    CancellationToken ct) =>
{
    try
    {
        if (TryVerifiedActorFrom(context, out string actor) &&
            actor.StartsWith("oidc:", StringComparison.Ordinal) &&
            context.Request.IsHttps)
        {
            AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
            if (!string.IsNullOrWhiteSpace(tokens.RequestToken))
                context.Response.Headers["X-Sideport-CSRF"] = tokens.RequestToken;
        }
        PersonalAppleStatusDto status = await access.StatusAsync(ct);
        return Results.Ok(PersonalAppleStatusForRequest(
            status,
            context,
            allowInsecureCredentialEntryOnLoopback,
            allowInsecureTls));
    }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException)
    {
        return Results.Json(new { error = "apple-credential-store-unavailable", message = "The Apple credential state is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apple-access/personal/connect", async (
    PersonalAppleConnectRequest request,
    HttpContext context,
    IPersonalAppleAccess access,
    CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired("changing the Apple signer");
    try
    {
        string appleId = PersonalAppleAccess.ValidateAppleId(request.AppleId);
        AppleCredentialRateLimitDecision accountLimit = appleCredentialRateLimiter.AcquireAccount(
            AppleAccountIdentity.ProfileIdFor(appleId));
        if (!accountLimit.Allowed)
            return AppleRateLimitResult(context, accountLimit.RetryAfter, "apple-credential-account-rate-limited");

        PersonalAppleConnectResult result = await access.ConnectAsync(request, actor, ct);
        PersonalAppleStatusDto responseStatus = PersonalAppleStatusForRequest(
            result.Status,
            context,
            allowInsecureCredentialEntryOnLoopback,
            allowInsecureTls);
        return result.Outcome switch
        {
            "created" => Results.Created("/api/apple-access/personal/status", responseStatus),
            "updated" => Results.Ok(responseStatus),
            "two-factor-required" => Results.Json(responseStatus, statusCode: StatusCodes.Status202Accepted),
            _ => Results.UnprocessableEntity(new { error = "apple-authentication-failed", message = "Apple authentication failed." }),
        };
    }
    catch (AppleCredentialSourceReadOnlyException)
    {
        return Results.Conflict(new { error = "credential-source-read-only", message = "This deployment reads its Apple credential from host-side custody." });
    }
    catch (AppleAccountReplacementRequiresCutoverException)
    {
        return Results.Conflict(new { error = "apple-account-replacement-requires-cutover", message = "A different Apple account is already connected. Sideport will not replace it implicitly." });
    }
    catch (AppleAuthenticationFailedException)
    {
        return Results.UnprocessableEntity(new { error = "apple-authentication-failed", message = "Apple rejected the account credentials." });
    }
    catch (AppleUpstreamRateLimitedException)
    {
        return AppleRateLimitResult(context, TimeSpan.FromMinutes(1), "apple-auth-rate-limited");
    }
    catch (AppleUpstreamUnavailableException)
    {
        return Results.Json(
            new { error = "apple-authentication-unavailable", message = "Apple services are temporarily unavailable." },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "validation-failed", message = ex.Message });
    }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException)
    {
        return Results.Json(new { error = "apple-credential-store-unavailable", message = "The managed Apple credential store is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apple-access/personal/sign-in", async (PersonalAppleSignInRequest request, HttpContext context, IPersonalAppleAccess access, CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        TryVerifiedActorFrom(context, out string actor);
        PersonalAppleStatusDto status = await access.SignInAsync(request, actor, ct);
        return Results.Ok(PersonalAppleStatusForRequest(
            status,
            context,
            allowInsecureCredentialEntryOnLoopback,
            allowInsecureTls));
    }
    catch (AppleAuthenticationFailedException)
    {
        return Results.UnprocessableEntity(new { error = "apple-authentication-failed", message = "Apple authentication failed." });
    }
    catch (AppleUpstreamRateLimitedException)
    {
        return AppleRateLimitResult(context, TimeSpan.FromMinutes(1), "apple-auth-rate-limited");
    }
    catch (AppleUpstreamUnavailableException)
    {
        return Results.Json(
            new { error = "apple-authentication-unavailable", message = "Apple services are temporarily unavailable." },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (InvalidOperationException)
    {
        return Results.UnprocessableEntity(new { error = "apple-credential-missing", message = "No matching Apple credential is configured in server-side custody." });
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["appleId"] = [ex.Message] });
    }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException)
    {
        return Results.Json(new { error = "apple-credential-store-unavailable", message = "The Apple credential state is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apple-access/personal/2fa", async (PersonalAppleCompleteTwoFactorRequest request, HttpContext context, IPersonalAppleAccess access, CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    try
    {
        TryVerifiedActorFrom(context, out string actor);
        string? accountProfileId = access.PendingChallengeAccountProfileId(
            request.ChallengeId ?? string.Empty,
            actor);
        if (accountProfileId is not null)
        {
            AppleCredentialRateLimitDecision accountLimit = appleCredentialRateLimiter.AcquireAccount(accountProfileId);
            if (!accountLimit.Allowed)
                return AppleRateLimitResult(context, accountLimit.RetryAfter, "apple-credential-account-rate-limited");
        }
        PersonalAppleTwoFactorResult result = await access.CompleteTwoFactorAsync(request, actor, ct);
        PersonalAppleStatusDto responseStatus = PersonalAppleStatusForRequest(
            result.Status,
            context,
            allowInsecureCredentialEntryOnLoopback,
            allowInsecureTls);
        return result.Outcome switch
        {
            "connected-created" => Results.Created("/api/apple-access/personal/status", responseStatus),
            "connected-updated" => Results.Ok(responseStatus),
            _ => Results.Ok(responseStatus),
        };
    }
    catch (AppleChallengeNotFoundException)
    {
        return Results.NotFound(new { error = "apple-2fa-challenge-not-found", message = "The pending Apple challenge was not found." });
    }
    catch (AppleChallengeExpiredException)
    {
        return Results.Conflict(new { error = "apple-challenge-expired", message = "The pending Apple challenge expired." });
    }
    catch (AppleTwoFactorInvalidException)
    {
        return Results.UnprocessableEntity(new { error = "apple-two-factor-invalid", message = "Apple rejected the two-factor code." });
    }
    catch (AppleUpstreamRateLimitedException)
    {
        return AppleRateLimitResult(context, TimeSpan.FromMinutes(1), "apple-auth-rate-limited");
    }
    catch (AppleUpstreamUnavailableException)
    {
        return Results.Json(
            new { error = "apple-authentication-unavailable", message = "Apple services are temporarily unavailable." },
            statusCode: StatusCodes.Status502BadGateway);
    }
    catch (AppleCredentialSourceReadOnlyException)
    {
        return Results.Conflict(new { error = "credential-source-read-only", message = "This deployment reads its Apple credential from host-side custody." });
    }
    catch (AppleAccountReplacementRequiresCutoverException)
    {
        return Results.Conflict(new { error = "apple-account-replacement-requires-cutover", message = "A different Apple account is already connected. Sideport will not replace it implicitly." });
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["twoFactor"] = [ex.Message] });
    }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException)
    {
        return Results.Json(new { error = "apple-credential-store-unavailable", message = "The managed Apple credential store is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPut("/api/apple-access/personal/team", async (
    PersonalAppleTeamSelectionRequest request,
    HttpContext context,
    IPersonalAppleAccess access,
    CancellationToken ct) =>
{
    context.Response.Headers.CacheControl = "no-store";
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired("selecting an Apple developer team");
    try
    {
        AppleCredentialRateLimitDecision accountLimit = appleCredentialRateLimiter.AcquireAccount(
            request.AccountProfileId?.Trim() ?? string.Empty);
        if (!accountLimit.Allowed)
            return AppleRateLimitResult(context, accountLimit.RetryAfter, "apple-credential-account-rate-limited");

        PersonalAppleStatusDto status = await access.SelectTeamAsync(request, actor, ct);
        return Results.Ok(PersonalAppleStatusForRequest(
            status,
            context,
            allowInsecureCredentialEntryOnLoopback,
            allowInsecureTls));
    }
    catch (AppleAccountProfileNotFoundException)
    {
        return Results.NotFound(new { error = "apple-account-profile-not-found", message = "The Apple account profile was not found." });
    }
    catch (AppleTeamSelectionStaleException)
    {
        return Results.Conflict(new { error = "apple-team-selection-stale", message = "Sign in to Apple again before selecting a team." });
    }
    catch (AppleTeamNotReturnedException)
    {
        return Results.UnprocessableEntity(new { error = "apple-team-not-returned", message = "Apple did not return that team for the authenticated account." });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = "validation-failed", message = ex.Message });
    }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException)
    {
        return Results.Json(new { error = "apple-account-store-unavailable", message = "The Apple account state store is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apple-access/personal/signing-preflight", async (
    PersonalAppleSigningPreflightRequest request,
    HttpContext context,
    SigningCutoverService cutover,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired("reviewing the Apple signer");
    try { return Results.Ok(await cutover.PreflightAsync(request, actor, ct)); }
    catch (AppleTeamSelectionStaleException) { return Results.Conflict(new { error = "apple-team-selection-stale", message = "Sign in to Apple again before reviewing signing." }); }
    catch (AppleTeamNotReturnedException) { return Results.UnprocessableEntity(new { error = "apple-team-not-returned", message = "Apple did not return that team for the authenticated account." }); }
    catch (AppleCertificateInventoryUnavailableException) { return Results.Json(new { error = "apple-certificate-inventory-unavailable", message = "Apple's development-certificate inventory is temporarily unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = "validation-failed", message = ex.Message }); }
});

app.MapPost("/api/apple-access/personal/replacement-candidates", async (
    AppleAccountReplacementConnectRequest request,
    HttpContext context,
    AppleAccountReplacementCandidateService candidates,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor)) return MutationProtectionRequired("replacing the Apple account");
    try
    {
        AppleAccountReplacementCandidateDto result = await candidates.ConnectAsync(request, actor, ct);
        return Results.Json(result, statusCode: result.State == "two-factor-required" ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
    }
    catch (AppleAccountReplacementNotDifferentException) { return Results.Conflict(new { error = "apple-account-replacement-not-different", message = "Use normal sign-in or password rotation for the active Apple account." }); }
    catch (AppleCredentialSourceReadOnlyException) { return Results.Conflict(new { error = "credential-source-read-only", message = "This deployment reads its Apple credential from host-side custody." }); }
    catch (AppleAuthenticationFailedException) { return Results.UnprocessableEntity(new { error = "apple-authentication-failed", message = "Apple rejected the replacement account credentials." }); }
    catch (AppleUpstreamRateLimitedException) { return AppleRateLimitResult(context, TimeSpan.FromMinutes(1), "apple-auth-rate-limited"); }
    catch (AppleUpstreamUnavailableException) { return Results.Json(new { error = "apple-authentication-unavailable", message = "Apple services are temporarily unavailable." }, statusCode: StatusCodes.Status502BadGateway); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = "validation-failed", message = ex.Message }); }
    catch (Exception ex) when (ex is AppleCredentialStoreException or AppleAccountStateStoreException) { return Results.Json(new { error = "apple-credential-store-unavailable", message = "The managed Apple credential store is unavailable." }, statusCode: StatusCodes.Status503ServiceUnavailable); }
});

app.MapPost("/api/apple-access/personal/replacement-candidates/2fa", async (
    AppleAccountReplacementTwoFactorRequest request,
    HttpContext context,
    AppleAccountReplacementCandidateService candidates,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor)) return MutationProtectionRequired("verifying the replacement Apple account");
    try { return Results.Ok(await candidates.CompleteTwoFactorAsync(request, actor, ct)); }
    catch (AppleChallengeExpiredException) { return Results.Conflict(new { error = "apple-replacement-candidate-expired", message = "Authenticate the replacement Apple account again." }); }
    catch (AppleTwoFactorInvalidException) { return Results.UnprocessableEntity(new { error = "apple-two-factor-invalid", message = "Apple rejected the two-factor code." }); }
    catch (AppleUpstreamRateLimitedException) { return AppleRateLimitResult(context, TimeSpan.FromMinutes(1), "apple-auth-rate-limited"); }
    catch (AppleUpstreamUnavailableException) { return Results.Json(new { error = "apple-authentication-unavailable", message = "Apple services are temporarily unavailable." }, statusCode: StatusCodes.Status502BadGateway); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = "validation-failed", message = ex.Message }); }
});

app.MapPost("/api/apple-access/personal/cutover", async (
    PersonalAppleCutoverRequest request,
    HttpContext context,
    SigningCutoverService cutover,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actorName))
        return MutationProtectionRequired("replacing the Apple signer");
    try
    {
        (OperationRecordDto record, bool created) = await cutover.CutoverAsync(
            request,
            new OperationActorDto(actorName.StartsWith("oidc:", StringComparison.Ordinal) ? "oidc-user" : "api-token", actorName),
            ct);
        return created ? Results.Json(record, statusCode: StatusCodes.Status202Accepted) : Results.Ok(record);
    }
    catch (SigningPreflightExpiredException) { return Results.Conflict(new { error = "signing-preflight-expired", message = "Review the current signing impact again." }); }
    catch (SigningAcknowledgementMismatchException) { return Results.UnprocessableEntity(new { error = "signing-acknowledgement-mismatch", message = "Confirm the exact current certificate impact." }); }
    catch (SigningIdempotencyTargetConflictException) { return Results.Conflict(new { error = "idempotency-target-conflict", message = "This idempotency key was already used for another signing cutover." }); }
    catch (SigningAccountReauthenticationRequiredException) { return Results.Conflict(new { error = "signing-account-reauthentication-required", message = "Authenticate the replacement Apple account again before resuming this cutover." }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = "validation-failed", message = ex.Message }); }
});

// Device plane (design §8 phase 1) — wired to the seam, implementation pending.
app.MapGet("/api/devices", async (IDeviceController devices, CancellationToken ct) =>
    Results.Ok(await devices.ListDevicesAsync(ct)));

app.MapGet("/api/devices/known", async (
    HttpContext context,
    KnownDeviceService inventory,
    bool? includeReachable,
    CancellationToken ct) =>
{
    try
    {
        IReadOnlyList<KnownDeviceDto> devices = await inventory.ListAsync(includeReachable ?? true, ct);
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
            return Results.Ok(devices);

        string memberId = principal.Member!.MemberId;
        return Results.Ok(devices
            .Where(device =>
                string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal) &&
                string.Equals(device.OwnerMemberId, memberId, StringComparison.Ordinal))
            .Select(FamilyResourceProjections.Device)
            .ToArray());
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new KnownDeviceErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/devices/known", async (KnownDeviceUpsertRequest request, KnownDeviceService inventory, CancellationToken ct) =>
{
    try
    {
        (KnownDeviceDto device, bool created) = await inventory.UpsertAsync(request, ct);
        return created ? Results.Created($"/api/devices/known/{Uri.EscapeDataString(device.Udid)}", device) : Results.Ok(device);
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["udid"] = [ex.Message] });
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new KnownDeviceErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/devices/enrollments", async (
    DeviceEnrollmentRequest request,
    HttpContext context,
    DeviceEnrollmentService enrollments,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            string memberId = principal.Member!.MemberId;
            if (!string.IsNullOrWhiteSpace(request.TargetMemberId) &&
                !string.Equals(request.TargetMemberId, memberId, StringComparison.Ordinal))
            {
                return FamilyResourceNotFound();
            }
            bool replay = await familyAccess.HasEnrollmentReplayAsync(
                memberId,
                request.IdempotencyKey,
                ct).ConfigureAwait(false);
            if (!replay &&
                await familyAccess.HasAcceptedDeviceAsync(memberId, ct).ConfigureAwait(false))
            {
                return FamilyOwnerActionRequired(
                    "additional-device-owner-required",
                    "Ask the home Owner to add another iPhone for you.");
            }
            request = request with { TargetMemberId = memberId };
        }

        DeviceEnrollmentSubmissionResult result = await enrollments.StartAsync(
            request,
            ActorFrom(context),
            ActorMemberIdFrom(context),
            ct);
        if (result.Error is null && result.Record is not null)
        {
            object response = await OperationResponseForAsync(
                principal,
                result.Record,
                familyAccess,
                ct).ConfigureAwait(false);
            return result.Created
                ? Results.Accepted($"/api/operations/{result.Record.OperationId}", response)
                : Results.Ok(response);
        }

        var error = new OperationErrorDto(
            result.Error ?? "device-enrollment-failed",
            result.Message ?? "Sideport could not start iPhone enrollment.");
        return result.Error switch
        {
            "idempotency-key-required" => Results.ValidationProblem(
                new Dictionary<string, string[]> { ["idempotencyKey"] = [error.Message] }),
            "device-enrollment-active" or "idempotency-target-conflict" => Results.Conflict(error),
            "selected-device-ineligible" => Results.UnprocessableEntity(error),
            "device-discovery-unavailable" => Results.Json(error, statusCode: StatusCodes.Status503ServiceUnavailable),
            _ => Results.UnprocessableEntity(error),
        };
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPatch("/api/devices/known/{udid}", async (
    string udid,
    KnownDevicePatchRequest request,
    HttpContext context,
    KnownDeviceService inventory,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            if (request.Owner is not null || request.Notes is not null)
            {
                return Results.Json(
                    new OperationErrorDto(
                        "family-device-fields-not-allowed",
                        "Family members can change only their own iPhone's display name."),
                    statusCode: StatusCodes.Status403Forbidden);
            }
            if (await familyAccess.FindOwnedAcceptedDeviceAsync(
                    principal.Member!.MemberId,
                    udid,
                    ct).ConfigureAwait(false) is null)
            {
                return FamilyResourceNotFound();
            }
        }

        KnownDeviceDto? device = await inventory.PatchAsync(udid, request, ct);
        if (device is null)
            return principal.Kind == WorkspaceRequestPrincipalKind.Family
                ? FamilyResourceNotFound()
                : Results.NotFound(new KnownDeviceErrorDto("known-device-not-found", "Known device not found."));
        return principal.Kind == WorkspaceRequestPrincipalKind.Family
            ? Results.Ok(FamilyResourceProjections.Device(device))
            : Results.Ok(device);
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["udid"] = [ex.Message] });
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new KnownDeviceErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapDelete("/api/devices/known/{udid}", async (string udid, KnownDeviceService inventory, CancellationToken ct) =>
{
    try
    {
        (bool removed, int registrationCount) = await inventory.RemoveAsync(udid, ct);
        if (registrationCount > 0)
        {
            return Results.Conflict(new KnownDeviceErrorDto(
                "device-has-registrations",
                "Remove app registrations before deleting this known device record.",
                RegistrationCount: registrationCount));
        }

        return removed ? Results.NoContent() : Results.NotFound(new KnownDeviceErrorDto("known-device-not-found", "Known device not found."));
    }
    catch (ArgumentException ex)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["udid"] = [ex.Message] });
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new KnownDeviceErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Device connectivity self-test (built-in troubleshooting): walks the transport
// chain usbmux -> device enumeration -> per-device trust/lockdown and reports
// where it breaks, with operator-facing remediation for each failing layer.
app.MapGet("/api/devices/diagnostics", async (IDeviceController devices, CancellationToken ct) =>
    Results.Ok(await devices.DiagnoseAsync(ct)));

app.MapGet("/api/devices/{udid}/installed-apps", async (
    string udid,
    HttpContext context,
    IDeviceController devices,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
            await familyAccess.FindOwnedAcceptedDeviceAsync(
                principal.Member!.MemberId,
                udid,
                ct).ConfigureAwait(false) is null)
        {
            return FamilyResourceNotFound();
        }
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
    SignerOptions signer,
    IDeviceController devices,
    KnownDeviceStore knownDevices,
    IAppRegistry registry,
    IAppCatalog catalog,
    OperationStore operationStore,
    OnboardingCompletionStore completionStore,
    SchedulerSettingsStore schedulerSettings,
    SystemStatusService systemStatus,
    IPersonalAppleAccess personalApple,
    ISigningIdentityProvider signingIdentity,
    CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    SystemStatusDto operationalStatus = await systemStatus.GetAsync(ct).ConfigureAwait(false);
    SystemStatusCheckDto? anisetteCheck = operationalStatus.Checks.FirstOrDefault(check => check.Id == "anisette-headers");
    SystemStatusCheckDto? signerCheck = operationalStatus.Checks.FirstOrDefault(check => check.Id == "signer-executable");
    bool anisetteOk = anisetteCheck?.Status == "pass";
    string? anisetteError = anisetteOk ? null : anisetteCheck?.Reason;
    bool signerOk = signerCheck?.Status == "pass";

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

    IReadOnlyList<KnownDeviceRecord> knownDeviceRecords = Array.Empty<KnownDeviceRecord>();
    try
    {
        knownDeviceRecords = await knownDevices.ListAsync(ct);
    }
    catch (KnownDeviceStoreException ex)
    {
        deviceError ??= ex.InnerException?.GetType().Name ?? ex.GetType().Name;
    }

    HashSet<string> acceptedUdids = knownDeviceRecords
        .Where(device => string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal))
        .Select(device => device.Udid)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    DeviceInfo[] acceptedReachableDevices = reachableDevices
        .Where(device => acceptedUdids.Contains(device.Udid) &&
                         string.Equals(device.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) &&
                         device.UsableForInstall)
        .ToArray();

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
    bool apiProtected = !string.IsNullOrEmpty(apiToken) || oidcEnabled;

    OnboardingCompletionReceipt? completionReceipt;
    SchedulerSettingsState? schedulerState;
    PersonalAppleStatusDto personalAppleStatus;
    IReadOnlyList<OperationRecordDto> allOperations;
    IReadOnlyList<OperationRecordDto> installOperations;
    try
    {
        completionReceipt = await completionStore.ReadAsync(ct).ConfigureAwait(false);
        schedulerState = await schedulerSettings.ReadAsync(ct).ConfigureAwait(false);
        personalAppleStatus = await personalApple.StatusAsync(ct).ConfigureAwait(false);
        allOperations = await operationStore.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        installOperations = allOperations
            .Where(operation => string.Equals(operation.Type, "install", StringComparison.Ordinal))
            .ToArray();
    }
    catch (Exception ex) when (ex is OnboardingCompletionStoreException or OperationStoreException or
                               SchedulerSettingsStoreException or AppleCredentialStoreException or
                               AppleAccountStateStoreException)
    {
        return Results.Json(
            new OperationErrorDto("onboarding-state-unavailable", "The onboarding completion state is unavailable."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    OperationRecordDto? latestOnboardingInstall = installOperations.FirstOrDefault(operation =>
        operation.InstallIntent?.FinishOnboarding == true);
    OperationRecordDto? activeInstall = installOperations.FirstOrDefault(operation =>
        operation.InstallIntent?.FinishOnboarding == true &&
        (operation.Status is "queued" or "waiting" or "running"));
    AppRegistration? pendingRegistration = registrations
        .Where(registration => registration.IsPendingInstall &&
            !installOperations.Any(operation =>
                operation.InstallIntent?.FinishOnboarding == false &&
                string.Equals(operation.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(operation.Target.BundleId, registration.BundleId, StringComparison.Ordinal)))
        .OrderByDescending(registration => registration.CreatedAt)
        .FirstOrDefault();
    string? selectedCatalogAppId = completionReceipt?.CatalogAppId
        ?? pendingRegistration?.CatalogAppId
        ?? latestOnboardingInstall?.InstallIntent?.CatalogAppId;
    OnboardingCompletionReceiptDto? receiptProjection = completionReceipt is null
        ? null
        : ProjectOnboardingReceipt(completionReceipt);
    SigningIdentityInspection? currentSigningIdentity = null;
    if (completionReceipt is not null)
    {
        AppRegistration? receiptRegistration = registrations.FirstOrDefault(registration =>
            string.Equals(registration.DeviceUdid, completionReceipt.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(registration.BundleId, completionReceipt.BundleId, StringComparison.Ordinal));
        if (receiptRegistration is not null)
        {
            try
            {
                currentSigningIdentity = await signingIdentity.InspectAsync(
                    receiptRegistration.AppleId,
                    receiptRegistration.TeamId,
                    timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                currentSigningIdentity = null;
            }
        }
    }

    OnboardingWorkflowDto workflow = OnboardingWorkflowBuilder.Build(new OnboardingWorkflowContext(
        operationalStatus,
        personalAppleStatus,
        acceptedReachableDevices,
        acceptedUdids.Count,
        catalogApps,
        registrations,
        allOperations,
        completionReceipt,
        schedulerState,
        operationalStatus.CheckedAt,
        currentSigningIdentity));

    var steps = new[]
    {
        new OnboardingStep(
            "api-auth",
            "Protect the API",
            "Configure bearer-token or OIDC authentication before Sideport can change accounts, phones, or apps.",
            apiProtected ? "complete" : "blocked",
            "portal",
            true,
            null,
            apiProtected ? "Authenticated mutations are enabled." : "Open mode is read-only; mutations return mutation-protection-required."),
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
            "Add an iPhone",
            "Use Add iPhone once over USB. After acceptance, the phone may also be reachable over Wi-Fi.",
            acceptedReachableDevices.Length > 0 ? "complete" : deviceError is null ? "pending" : "blocked",
            "portal",
            true,
            null,
            deviceError ?? (acceptedUdids.Count > 0
                ? $"{acceptedUdids.Count} accepted iPhone(s); reconnect one to continue."
                : "No explicitly accepted iPhone yet.")),
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
            acceptedUdids.Count > 0 ? "complete" : "pending",
            "iphone",
            false,
            null,
            acceptedUdids.Count > 0 ? "A completed Add iPhone operation verified Trust over USB." : "Device discovery alone does not prove Trust."),
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
            "Automatic refresh",
            "Sideport checks hourly and refreshes only apps that are due.",
            schedulerState?.Enabled == true ? "complete" : "pending",
            "portal",
            false,
            null,
            schedulerState?.Enabled == true ? "Automatic due-only refresh is enabled." : "Automatic refresh will be enabled after the first verified install."),
    };

    return Results.Ok(new OnboardingStatus(
        FirstRunComplete: completionReceipt is not null,
        SchedulerEnabled: schedulerState?.Enabled == true,
        Steps: steps,
        SetupState: completionReceipt is null ? "in-progress" : "complete",
        SelectedCatalogAppId: selectedCatalogAppId,
        ActiveInstallOperationId: activeInstall?.OperationId,
        CompletionReceipt: receiptProjection,
        Workflow: workflow));
});

app.MapPost("/api/onboarding/complete", async (
    OnboardingCompleteRequest request,
    HttpContext context,
    OperationService operations,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("finishing setup");

    try
    {
        (OnboardingCompletionReceipt? receipt, bool created, string? error, string? message) =
            await operations.CompleteOnboardingAsync(
                request.VerifiedOperationId,
                request.IdempotencyKey,
                ActorFrom(context),
                ct).ConfigureAwait(false);
        if (receipt is not null)
        {
            OnboardingCompletionReceiptDto projection = ProjectOnboardingReceipt(receipt);
            return created
                ? Results.Created("/api/onboarding/status", projection)
                : Results.Ok(projection);
        }

        return Results.Conflict(new OperationErrorDto(
            error ?? "onboarding-incomplete",
            message ?? "Sideport setup is not ready to finish."));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new OperationErrorDto("validation-failed", ex.Message));
    }
    catch (Exception ex) when (ex is OnboardingCompletionStoreException or OperationStoreException or
                               SchedulerSettingsStoreException or KnownDeviceStoreException or
                               AppleCredentialStoreException or AppleAccountStateStoreException or
                               IOException or UnauthorizedAccessException)
    {
        return Results.Json(
            new OperationErrorDto("onboarding-store-unavailable", "Sideport could not finish the saved setup state."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Refresh orchestration (P6).
app.MapGet("/api/v2/catalog/apps", async (
    HttpContext context,
    IAppCatalog catalog,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
        return Results.Ok(await catalog.ListV2Async(ct));
    IReadOnlyList<CatalogAppV2Dto> approved = await familyAccess.ListApprovedCatalogAsync(ct);
    return Results.Ok(approved.Select(FamilyResourceProjections.Catalog).ToArray());
});

app.MapGet("/api/v2/catalog/apps/{id}/icon", async (string id, IAppCatalog catalog, CancellationToken ct) =>
{
    CatalogAppDto? app = (await catalog.ListAsync(ct)).FirstOrDefault(entry => string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase));
    if (app is null || !string.Equals(app.Status, "ready", StringComparison.Ordinal) || !File.Exists(app.IpaPath)) return Results.NotFound();
    try
    {
        byte[]? icon = IpaInspector.ExtractIconPng(app.IpaPath);
        return icon is null ? Results.NotFound() : Results.File(icon, "image/png", enableRangeProcessing: false);
    }
    catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException or UnauthorizedAccessException)
    {
        return Results.NotFound();
    }
});

app.MapGet("/api/v2/catalog/import-roots", async (IAppCatalog catalog, CancellationToken ct) =>
    Results.Ok(await catalog.ListImportRootsAsync(ct)));

app.MapPost("/api/v2/catalog/apps/inspect", async (
    CatalogRootImportRequest request,
    HttpContext context,
    IAppCatalog catalog,
    CancellationToken ct) =>
{
    try
    {
        CatalogV2MutationResult result = await catalog.ImportFromRootV2Async(
            request,
            CatalogActorFrom(context),
            ct);
        return result.Created
            ? Results.Created($"/api/v2/catalog/apps/{result.Entry.Id}", result.Entry)
            : Results.Ok(result.Entry);
    }
    catch (CatalogV2Exception ex)
    {
        return CatalogV2Error(ex);
    }
    catch (Exception ex) when (ex is FormatException or InvalidDataException)
    {
        return CatalogV2InspectionError();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CatalogStoreException)
    {
        return CatalogV2StoreError();
    }
});

app.MapPost("/api/v2/catalog/apps/upload", async (
    HttpRequest request,
    HttpContext context,
    IAppCatalog catalog,
    AppCatalogOptions options,
    CancellationToken ct) =>
{
    if (!request.HasFormContentType)
    {
        return Results.Json(
            new { error = "unsupported-media-type", message = "Upload must be multipart/form-data." },
            statusCode: StatusCodes.Status415UnsupportedMediaType);
    }

    string? tempPath = null;
    try
    {
        IFormCollection form = await request.ReadFormAsync(ct);
        IFormFile? ipa = form.Files.GetFile("ipa");
        if (ipa is null || ipa.Length == 0)
        {
            return Results.BadRequest(new
            {
                error = "validation-failed",
                message = "An .ipa file is required in the ipa multipart field.",
            });
        }

        if (!string.Equals(Path.GetExtension(ipa.FileName), ".ipa", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(
                new { error = "unsupported-media-type", message = "Catalog uploads must be .ipa files." },
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        string rawExpectedVersion = form["expectedCatalogVersion"].ToString();
        int? expectedCatalogVersion = null;
        if (!string.IsNullOrWhiteSpace(rawExpectedVersion))
        {
            if (!int.TryParse(rawExpectedVersion, out int parsedVersion) || parsedVersion < 1)
            {
                return Results.BadRequest(new
                {
                    error = "validation-failed",
                    message = "expectedCatalogVersion must be a positive integer.",
                });
            }

            expectedCatalogVersion = parsedVersion;
        }

        string tempDirectory = Path.Combine(
            Path.GetDirectoryName(options.CatalogPath) ?? Path.GetTempPath(),
            "upload-tmp");
        Directory.CreateDirectory(tempDirectory);
        tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.ipa");
        await CopyCatalogUploadBoundedAsync(ipa, tempPath, options.MaxUploadBytes, ct);

        CatalogV2MutationResult result = await catalog.ImportUploadedIpaV2Async(
            new CatalogUploadV2Request(
                tempPath,
                form["id"].ToString(),
                form["name"].ToString(),
                form["purpose"].ToString(),
                form["idempotencyKey"].ToString(),
                expectedCatalogVersion),
            CatalogActorFrom(context),
            ct);
        return result.Created
            ? Results.Created($"/api/v2/catalog/apps/{result.Entry.Id}", result.Entry)
            : Results.Ok(result.Entry);
    }
    catch (CatalogV2Exception ex)
    {
        return CatalogV2Error(ex);
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        return CatalogV2UploadTooLarge(options.MaxUploadBytes);
    }
    catch (InvalidDataException ex) when (ex.Message.Contains("multipart body length limit", StringComparison.OrdinalIgnoreCase))
    {
        return CatalogV2UploadTooLarge(options.MaxUploadBytes);
    }
    catch (Exception ex) when (ex is FormatException or InvalidDataException)
    {
        return CatalogV2InspectionError();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CatalogStoreException)
    {
        return CatalogV2StoreError();
    }
    finally
    {
        if (tempPath is not null)
            TryDeleteCatalogUpload(tempPath);
    }
});

app.MapGet("/api/v2/catalog/github/sources", async (
    HttpContext context,
    IGitHubCatalogService github,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired();
    try
    {
        return Results.Ok(await github.ListSourcesAsync(ct));
    }
    catch (GitHubCatalogException ex)
    {
        return GitHubCatalogError(ex);
    }
});

app.MapPost("/api/v2/catalog/github/connections", async (
    GitHubConnectionRequest request,
    HttpContext context,
    IGitHubCatalogService github,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired();
    try
    {
        GitHubConnectionResult result = await github.ConnectAsync(request, actor, ct);
        if (!result.Created)
            return Results.Ok(result.Connection);
        if (string.Equals(result.Connection.Status, "authorization-required", StringComparison.Ordinal))
        {
            return Results.Accepted(
                $"/api/v2/catalog/github/connections/{result.Connection.Id}",
                result.Connection);
        }
        return Results.Created(
            $"/api/v2/catalog/github/connections/{result.Connection.Id}",
            result.Connection);
    }
    catch (Exception ex) when (ex is ArgumentException or GitHubCatalogException)
    {
        return ex is GitHubCatalogException githubError
            ? GitHubCatalogError(githubError)
            : GitHubValidationError();
    }
});

app.MapGet("/api/v2/catalog/github/connections/{connectionId}", async (
    string connectionId,
    HttpContext context,
    IGitHubCatalogService github,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired();
    try
    {
        GitHubConnectionDto? connection = await github.GetConnectionAsync(connectionId, actor, owner: false, ct);
        return connection is null
            ? Results.NotFound(new { error = "github-connection-not-found", message = "The GitHub connection was not found." })
            : Results.Ok(connection);
    }
    catch (ArgumentException)
    {
        return Results.NotFound(new { error = "github-connection-not-found", message = "The GitHub connection was not found." });
    }
    catch (GitHubCatalogException ex)
    {
        return GitHubCatalogError(ex);
    }
});

app.MapGet("/api/v2/catalog/github/sources/{sourceId}/releases", async (
    string sourceId,
    int? page,
    HttpContext context,
    IGitHubCatalogService github,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired();
    try
    {
        return Results.Ok(await github.ListReleasesAsync(sourceId, page ?? 1, ct));
    }
    catch (ArgumentException)
    {
        return GitHubValidationError();
    }
    catch (GitHubCatalogException ex)
    {
        return GitHubCatalogError(ex);
    }
});

app.MapPost("/api/v2/catalog/apps/import-github", async (
    GitHubCatalogImportRequest request,
    HttpContext context,
    IGitHubCatalogImportService imports,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out string actor))
        return MutationProtectionRequired();
    try
    {
        CatalogV2MutationResult result = await imports.ImportAsync(request, actor, ct);
        return result.Created
            ? Results.Created($"/api/v2/catalog/apps/{result.Entry.Id}", result.Entry)
            : Results.Ok(result.Entry);
    }
    catch (ArgumentException)
    {
        return GitHubValidationError();
    }
    catch (GitHubCatalogException ex)
    {
        return GitHubCatalogError(ex);
    }
    catch (CatalogV2Exception ex)
    {
        return CatalogV2Error(ex);
    }
    catch (Exception ex) when (ex is FormatException or InvalidDataException)
    {
        return CatalogV2InspectionError();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CatalogStoreException)
    {
        return CatalogV2StoreError();
    }
});

app.MapGet("/github/setup/callback", async (
    HttpContext context,
    string? state,
    long? installation_id,
    string? setup_action,
    IGitHubCatalogService github,
    GitHubCatalogOptions options,
    CancellationToken ct) =>
{
    Uri redirect = BuildFixedGitHubStatusUri(options, "failed");
    try
    {
        if (string.IsNullOrWhiteSpace(state) || installation_id is not > 0)
            throw new GitHubCatalogException("github-state-invalid", "The GitHub setup state is invalid.");
        GitHubSetupCallbackResult result = await github.CompleteInstallationAsync(
            state,
            installation_id.Value,
            setup_action ?? string.Empty,
            ct);
        redirect = result.RedirectUri;
    }
    catch (Exception ex) when (ex is GitHubCatalogException or ArgumentException)
    {
        // GitHub cannot send Sideport's bearer/OIDC session. The opaque,
        // single-use setup state authorizes this callback; all errors return to
        // one configured same-origin UI route without reflecting query values.
    }

    context.Response.StatusCode = StatusCodes.Status303SeeOther;
    context.Response.Headers.Location = redirect.AbsoluteUri;
});

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

app.MapPost("/api/catalog/apps/upload", async (HttpRequest request, IAppCatalog catalog, AppCatalogOptions options, CancellationToken ct) =>
{
    if (!request.HasFormContentType)
        return Results.Json(new { error = "unsupported-media-type", message = "Upload must be multipart/form-data." }, statusCode: StatusCodes.Status415UnsupportedMediaType);

    IFormCollection form = await request.ReadFormAsync(ct);
    IFormFile? ipa = form.Files.GetFile("ipa");
    if (ipa is null || ipa.Length == 0)
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["ipa"] = ["An .ipa file is required."] });

    if (ipa.Length > options.MaxUploadBytes)
    {
        return Results.Json(new
        {
            error = "upload-too-large",
            message = "Uploaded IPA exceeds the configured Sideport:Catalog:MaxUploadBytes limit.",
            limit = options.MaxUploadBytes,
        }, statusCode: StatusCodes.Status413PayloadTooLarge);
    }

    if (!string.Equals(Path.GetExtension(ipa.FileName), ".ipa", StringComparison.OrdinalIgnoreCase))
        return Results.Json(new { error = "unsupported-media-type", message = "Catalog uploads must be .ipa files." }, statusCode: StatusCodes.Status415UnsupportedMediaType);

    string tempDir = Path.Combine(Path.GetDirectoryName(options.CatalogPath) ?? Path.GetTempPath(), "upload-tmp");
    Directory.CreateDirectory(tempDir);
    string tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.ipa");

    try
    {
        await using (FileStream stream = File.Create(tempPath))
            await ipa.CopyToAsync(stream, ct);

        bool replace = bool.TryParse(form["replace"].ToString(), out bool parsedReplace) && parsedReplace;
        (CatalogAppDto entry, bool created) = await catalog.ImportUploadedIpaAsync(new CatalogUploadRequest(
            tempPath,
            form["id"].ToString(),
            form["name"].ToString(),
            form["purpose"].ToString(),
            replace), ct);

        return created ? Results.Created($"/api/catalog/apps/{entry.Id}", entry) : Results.Ok(entry);
    }
    catch (CatalogConflictException ex)
    {
        return Results.Conflict(new { error = "catalog-id-conflict", message = ex.Message, id = ex.Id });
    }
    catch (Exception ex) when (ex is FormatException || ex is InvalidDataException)
    {
        return Results.UnprocessableEntity(new { error = "ipa-inspection-failed", detail = ex.Message });
    }
    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is CatalogStoreException)
    {
        return Results.Json(new { error = "catalog-store-unavailable", message = ex.Message, detail = ex.GetType().Name }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    finally
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
});

app.MapGet("/api/apps", async (
    HttpContext context,
    IAppRegistry registry,
    RefreshOrchestrator orchestrator,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
    {
        IReadOnlyList<OwnedFamilyRegistration> owned = await familyAccess.ListOwnedRegistrationsAsync(
            principal.Member!.MemberId,
            ct);
        return Results.Ok(owned
            .Select(item => FamilyResourceProjections.Registration(
                item,
                orchestrator.GetState(item.Registration.DeviceUdid, item.Registration.BundleId)))
            .ToArray());
    }

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
            a.Lifecycle,
            a.LastVerifiedOperationId,
            expiresAt = state?.ExpiresAt,
            timeUntilExpiry = state?.TimeUntilExpiry(now),
            lastSucceeded = state?.LastSucceeded,
            lastError = state?.LastError,
        };
    }));
});

app.MapPost("/api/apps", async (
    AppRegistrationMutationRequest request,
    PendingRegistrationService pendingRegistrations,
    IAppRegistry registry,
    IpaStore ipaStore,
    CancellationToken ct) =>
{
    bool v2Selection = !string.IsNullOrWhiteSpace(request.CatalogAppId) ||
        !string.IsNullOrWhiteSpace(request.AccountProfileId);
    if (v2Selection)
    {
        try
        {
            CatalogAppRegistrationResult result = await pendingRegistrations.CreateAsync(
                new CatalogAppRegistrationRequest(
                    request.CatalogAppId ?? string.Empty,
                    request.DeviceUdid ?? string.Empty,
                    request.AccountProfileId ?? string.Empty,
                    request.Lifecycle ?? "pending-install"),
                ct).ConfigureAwait(false);
            if (result.Registration is not null)
                return result.Created
                    ? Results.Created(
                        $"/api/apps/{result.Registration.DeviceUdid}/{result.Registration.BundleId}",
                        result.Registration)
                    : Results.Ok(result.Registration);

            var error = new OperationErrorDto(
                result.Error ?? "registration-rejected",
                result.Message ?? "The app selection could not be saved.");
            return result.Error switch
            {
                "pending-registration-conflict" or "registration-already-active" or "device-app-slot-limit" =>
                    Results.Conflict(error),
                _ => Results.UnprocessableEntity(error),
            };
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new OperationErrorDto("validation-failed", ex.Message));
        }
        catch (Exception ex) when (ex is KnownDeviceStoreException or CatalogStoreException or
                                   AppleCredentialStoreException or AppleAccountStateStoreException or
                                   IOException or UnauthorizedAccessException)
        {
            return Results.Json(
                new OperationErrorDto("registration-store-unavailable", "The app selection could not be saved."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    var registration = new AppRegistration(
        request.BundleId ?? string.Empty,
        request.AppleId ?? string.Empty,
        request.TeamId ?? string.Empty,
        request.DeviceUdid ?? string.Empty,
        request.InputIpaPath ?? string.Empty,
        request.Lifecycle ?? "active",
        request.CatalogAppId,
        request.CreatedAt,
        request.ActivatedAt,
        request.LastVerifiedOperationId);
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

    // Copy the IPA into durable PVC storage and point the registration at it, so
    // a pod restart (which wipes the ephemeral upload path) doesn't strand the
    // scheduler with "input IPA not found". The registration JSON alone isn't
    // enough — the artifact has to persist too.
    string durableIpaPath;
    try
    {
        durableIpaPath = await ipaStore.StoreAsync(
            registration.DeviceUdid, registration.BundleId, registration.InputIpaPath, ct);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        return Results.Problem(
            detail: $"could not persist the IPA to durable storage: {ex.Message}",
            statusCode: 500);
    }

    AppRegistration stored = registration with { InputIpaPath = durableIpaPath };
    await registry.UpsertAsync(stored, ct);
    return Results.Created($"/api/apps/{stored.DeviceUdid}/{stored.BundleId}", stored);
});

app.MapDelete("/api/apps/{udid}/{bundleId}", async (
    string udid,
    string bundleId,
    HttpContext context,
    IAppRegistry registry,
    IpaStore ipaStore,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
        await familyAccess.FindOwnedApprovedRegistrationAsync(
            principal.Member!.MemberId,
            udid,
            bundleId,
            ct).ConfigureAwait(false) is null)
    {
        return FamilyResourceNotFound();
    }

    bool removed = await registry.RemoveAsync(udid, bundleId, ct);
    if (removed)
    {
        try { ipaStore.Remove(udid, bundleId); }
        catch (IOException) { /* best-effort: the registration is already gone */ }
    }
    return removed ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/apps/{udid}/{bundleId}/verify", async (
    string udid,
    string bundleId,
    VerifyExistingRegistrationRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("verifying an existing app registration");

    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        string? ownerMemberId = await familyAccess.FindDeviceOwnerMemberIdAsync(
            udid,
            ct).ConfigureAwait(false);
        VerifyExistingRegistrationSubmissionResult result =
            await operations.VerifyExistingRegistrationAsync(
                udid,
                bundleId,
                ActorFrom(context),
                request.IdempotencyKey,
                ActorMemberIdFrom(context),
                ownerMemberId,
                ct).ConfigureAwait(false);
        if (result.Record is not null)
        {
            return result.Created
                ? Results.Accepted($"/api/operations/{result.Record.OperationId}", result.Record)
                : Results.Ok(result.Record);
        }

        var error = new OperationErrorDto(
            result.Error ?? "registration-verification-rejected",
            result.Message ?? "The existing app registration could not be verified.");
        return result.Error switch
        {
            "registration-not-found" => Results.NotFound(error),
            "idempotency-target-conflict" or "registration-pending-install" or
                "device-operation-still-active" => Results.Conflict(error),
            _ => Results.UnprocessableEntity(error),
        };
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new OperationErrorDto("validation-failed", ex.Message));
    }
    catch (Exception ex) when (ex is OperationStoreException or KnownDeviceStoreException or
                               IOException or UnauthorizedAccessException or NotSupportedException)
    {
        return Results.Json(
            new OperationErrorDto(
                "registration-verification-unavailable",
                "Sideport could not read the existing registration or iPhone state."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/apps/{udid}/{bundleId}/refresh",
    async (
        string udid,
        string bundleId,
        HttpContext context,
        OperationService operations,
        FamilyResourceAccess familyAccess,
        CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("refreshing an app");

    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
            await familyAccess.FindOwnedApprovedRegistrationAsync(
                principal.Member!.MemberId,
                udid,
                bundleId,
                ct).ConfigureAwait(false) is null)
        {
            return FamilyResourceNotFound();
        }
        string? ownerMemberId = await OwnerMemberIdForTargetAsync(
            principal,
            udid,
            familyAccess,
            ct).ConfigureAwait(false);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family && ownerMemberId is null)
            return FamilyResourceNotFound();

        (OperationRecordDto record, bool created) = await operations.RefreshAsync(
            udid,
            bundleId,
            ActorFrom(context),
            idempotencyKey: null,
            actorMemberId: ActorMemberIdFrom(context),
            ownerMemberId: ownerMemberId,
            parentOperationId: null,
            attempt: 1,
            ct: ct).ConfigureAwait(false);
        object response = await OperationResponseForAsync(
            principal,
            record,
            familyAccess,
            ct).ConfigureAwait(false);
        if (!created)
            return Results.Ok(response);
        return string.Equals(record.Status, "queued", StringComparison.Ordinal)
            ? Results.Accepted($"/api/operations/{record.OperationId}", response)
            : Results.Created($"/api/operations/{record.OperationId}", response);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/preflight", async (
    OperationPreflightRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    var validationErrors = ValidateOperationTarget(request.Type, request.DeviceUdid, request.BundleId);
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors);

    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
    {
        string memberId = principal.Member!.MemberId;
        if (await familyAccess.FindOwnedAcceptedDeviceAsync(
                memberId,
                request.DeviceUdid,
                ct).ConfigureAwait(false) is null)
        {
            return FamilyResourceNotFound();
        }

        if (string.Equals(request.Type, "install", StringComparison.OrdinalIgnoreCase))
        {
            if (request.FinishOnboarding || !string.IsNullOrWhiteSpace(request.AccountProfileId))
            {
                return FamilyOwnerActionRequired(
                    "owner-signing-selection-required",
                    "Sideport uses the Apple account already chosen by the home Owner.");
            }
            if (await familyAccess.FindApprovedCatalogAppAsync(
                    request.CatalogAppId,
                    request.BundleId,
                    ct).ConfigureAwait(false) is null)
            {
                return FamilyResourceNotFound();
            }
        }
        else if (string.Equals(request.Type, "refresh", StringComparison.OrdinalIgnoreCase) &&
                 await familyAccess.FindOwnedApprovedRegistrationAsync(
                     memberId,
                     request.DeviceUdid,
                     request.BundleId,
                     ct).ConfigureAwait(false) is null)
        {
            return FamilyResourceNotFound();
        }
    }

    if (string.Equals(request.Type, "refresh", StringComparison.OrdinalIgnoreCase))
    {
        OperationPreflightDto preflight = await operations.PreflightRefreshAsync(
            request.DeviceUdid,
            request.BundleId,
            ct,
            allowAppleAuthentication: principal.Kind != WorkspaceRequestPrincipalKind.Family,
            requireCurrentCatalogApproval: principal.Kind == WorkspaceRequestPrincipalKind.Family);
        return principal.Kind == WorkspaceRequestPrincipalKind.Family
            ? Results.Ok(FamilyResourceProjections.Preflight(preflight))
            : Results.Ok(preflight);
    }

    if (!string.Equals(request.Type, "install", StringComparison.OrdinalIgnoreCase))
        return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Type)] = ["Only refresh and install operations are supported."] });

    try
    {
        OperationPreflightDto preflight = await operations.PreflightInstallAsync(
            request.DeviceUdid,
            request.BundleId,
            request.FinishOnboarding,
            request.CatalogAppId,
            request.AccountProfileId,
            allowOwnerManagedAppleAuthority:
                principal.Kind != WorkspaceRequestPrincipalKind.Family,
            ct: ct);
        return principal.Kind == WorkspaceRequestPrincipalKind.Family
            ? Results.Ok(FamilyResourceProjections.Preflight(preflight))
            : Results.Ok(preflight);
    }
    catch (AppleCertificateInventoryUnavailableException)
    {
        return Results.Json(
            new OperationErrorDto(
                "apple-certificate-inventory-unavailable",
                "Apple's development-certificate inventory could not be read."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex) when (ex is OperationStoreException or KnownDeviceStoreException or
                               AppleCredentialStoreException or AppleAccountStateStoreException or
                               CatalogStoreException or IOException or UnauthorizedAccessException or
                               NotSupportedException)
    {
        return Results.Json(
            new OperationErrorDto("install-preflight-unavailable", "The install prerequisites could not be read."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/install", async (
    FirstInstallRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("installing an app");

    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        string? ownerMemberId = await familyAccess.FindDeviceOwnerMemberIdAsync(
            request.DeviceUdid,
            ct).ConfigureAwait(false);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            string memberId = principal.Member!.MemberId;
            if (!string.Equals(ownerMemberId, memberId, StringComparison.Ordinal))
                return FamilyResourceNotFound();
            if (request.FinishOnboarding || !string.IsNullOrWhiteSpace(request.AccountProfileId))
            {
                return FamilyOwnerActionRequired(
                    "owner-signing-selection-required",
                    "Sideport uses the Apple account already chosen by the home Owner.");
            }
            if (await familyAccess.FindApprovedCatalogAppAsync(
                    request.CatalogAppId,
                    request.BundleId,
                    ct).ConfigureAwait(false) is null)
                return FamilyResourceNotFound();
        }

        InstallSubmissionResult result = await operations.InstallAsync(
            request,
            ActorFrom(context),
            ActorMemberIdFrom(context),
            ownerMemberId,
            ct).ConfigureAwait(false);
        if (result.Record is not null)
        {
            object response = await OperationResponseForAsync(
                principal,
                result.Record,
                familyAccess,
                ct).ConfigureAwait(false);
            return result.Created
                ? Results.Accepted($"/api/operations/{result.Record.OperationId}", response)
                : Results.Ok(response);
        }

        if (string.Equals(result.Error, "install-preflight-stale", StringComparison.Ordinal) &&
            result.ReplacementPreflight is not null)
        {
            object replacement = principal.Kind == WorkspaceRequestPrincipalKind.Family
                ? FamilyResourceProjections.Preflight(result.ReplacementPreflight)
                : result.ReplacementPreflight;
            return Results.Json(
                new
                {
                    error = result.Error,
                    message = "The install plan changed. Review it and try again.",
                    replacementPreflight = replacement,
                },
                statusCode: StatusCodes.Status409Conflict);
        }

        var error = new OperationErrorDto(
            result.Error ?? "install-rejected",
            result.Message ?? "The install request was rejected.");
        return result.Error switch
        {
            "idempotency-target-conflict" or "pending-registration-conflict" or
                "registration-already-active" or "install-already-active" =>
                Results.Conflict(error),
            _ => Results.UnprocessableEntity(error),
        };
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new OperationErrorDto("validation-failed", ex.Message));
    }
    catch (Exception ex) when (ex is OperationStoreException or KnownDeviceStoreException or
                               AppleCredentialStoreException or AppleAccountStateStoreException or
                               CatalogStoreException or IOException or UnauthorizedAccessException or
                               NotSupportedException or AppleCertificateInventoryUnavailableException)
    {
        return Results.Json(
            new OperationErrorDto("install-service-unavailable", "The install prerequisites could not be read."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/refresh", async (
    RefreshOperationRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    var validationErrors = ValidateOperationTarget("refresh", request.DeviceUdid, request.BundleId);
    if (validationErrors.Count > 0)
        return Results.ValidationProblem(validationErrors);

    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        string? ownerMemberId = await OwnerMemberIdForTargetAsync(
            principal,
            request.DeviceUdid,
            familyAccess,
            ct).ConfigureAwait(false);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
            (ownerMemberId is null ||
             await familyAccess.FindOwnedApprovedRegistrationAsync(
                 principal.Member!.MemberId,
                 request.DeviceUdid,
                 request.BundleId,
                 ct).ConfigureAwait(false) is null))
        {
            return FamilyResourceNotFound();
        }

        (OperationRecordDto record, bool created) = await operations.RefreshAsync(
            request.DeviceUdid,
            request.BundleId,
            ActorFrom(context),
            request.IdempotencyKey,
            ActorMemberIdFrom(context),
            ownerMemberId,
            parentOperationId: null,
            attempt: 1,
            ct: ct);
        object response = await OperationResponseForAsync(
            principal,
            record,
            familyAccess,
            ct).ConfigureAwait(false);
        if (!created)
            return Results.Ok(response);
        return string.Equals(record.Status, "queued", StringComparison.Ordinal)
            ? Results.Accepted($"/api/operations/{record.OperationId}", response)
            : Results.Created($"/api/operations/{record.OperationId}", response);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/{operationId}/reconcile", async (
    string operationId,
    OperationReconcileRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    if (!TryVerifiedActorFrom(context, out _))
        return MutationProtectionRequired("reconciling an unknown device operation");

    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        OperationRecordDto? ownedSource = null;
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            ownedSource = await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                operationId,
                requireOwnActor: true,
                ct).ConfigureAwait(false);
            if (ownedSource is null)
                return FamilyResourceNotFound();
            if (!await familyAccess.IsApprovedMutableOperationAsync(
                    principal.Member.MemberId,
                    ownedSource,
                    ct).ConfigureAwait(false))
            {
                return FamilyOwnerActionRequired(
                    "approved-app-required",
                    "Ask the home Owner to review this app before continuing.");
            }
        }

        OperationReconciliationSubmissionResult result = await operations.ReconcileAsync(
            operationId,
            ActorFrom(context),
            request.IdempotencyKey,
            principal.Kind == WorkspaceRequestPrincipalKind.Family ? null : request.Note,
            ActorMemberIdFrom(context),
            ownedSource?.OwnerMemberId ?? await OwnerMemberIdForOperationAsync(
                operationId,
                familyAccess,
                ct).ConfigureAwait(false),
            ct).ConfigureAwait(false);
        if (result.Record is not null)
        {
            object response = await OperationResponseForAsync(
                principal,
                result.Record,
                familyAccess,
                ct).ConfigureAwait(false);
            return result.Created
                ? Results.Accepted($"/api/operations/{result.Record.OperationId}", response)
                : Results.Ok(response);
        }

        var error = new OperationErrorDto(
            result.Error ?? "operation-reconciliation-rejected",
            result.Message ?? "The unknown operation could not be reconciled.");
        return result.Error switch
        {
            "operation-not-found" => Results.NotFound(error),
            "idempotency-target-conflict" or "operation-not-reconcilable" or
                "operation-reconciliation-evidence-missing" or "operation-already-reconciled" or
                "device-operation-still-active" => Results.Conflict(error),
            _ => Results.UnprocessableEntity(error),
        };
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new OperationErrorDto("validation-failed", ex.Message));
    }
    catch (Exception ex) when (ex is OperationStoreException or KnownDeviceStoreException or
                               IOException or UnauthorizedAccessException or NotSupportedException)
    {
        return Results.Json(
            new OperationErrorDto(
                "operation-reconciliation-unavailable",
                "Sideport could not read the durable operation state needed for reconciliation."),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/{operationId}/cancel", async (
    string operationId,
    OperationActionRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            OperationRecordDto? source = await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                operationId,
                requireOwnActor: true,
                ct).ConfigureAwait(false);
            if (source is null)
                return FamilyResourceNotFound();
            if (!await familyAccess.IsApprovedMutableOperationAsync(
                    principal.Member.MemberId,
                    source,
                    ct).ConfigureAwait(false))
            {
                return FamilyOwnerActionRequired(
                    "approved-app-required",
                    "Ask the home Owner to review this app before continuing.");
            }
        }

        (OperationRecordDto? record, string? error) = await operations.CancelAsync(
            operationId,
            principal.Kind == WorkspaceRequestPrincipalKind.Family ? null : request.Reason,
            ct);
        object? response = record is null
            ? null
            : await OperationResponseForAsync(
                principal,
                record,
                familyAccess,
                ct).ConfigureAwait(false);
        return error switch
        {
            null => Results.Accepted($"/api/operations/{record!.OperationId}", response),
            "operation-not-found" => Results.NotFound(new OperationErrorDto("operation-not-found", "Operation not found.")),
            "operation-not-cancelable" => Results.Conflict(new OperationErrorDto("operation-not-cancelable", "Only queued or waiting operations can be canceled in this phase.")),
            _ => Results.Conflict(new OperationErrorDto(error, "Operation action failed.")),
        };
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/{operationId}/retry", async (
    string operationId,
    OperationActionRequest request,
    HttpContext context,
    OperationStore operationStore,
    OperationService operations,
    DeviceEnrollmentService enrollments,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        OperationRecordDto? source = principal.Kind == WorkspaceRequestPrincipalKind.Family
            ? await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                operationId,
                requireOwnActor: true,
                ct).ConfigureAwait(false)
            : await operationStore.FindAsync(operationId, ct);
        if (source is null)
            return principal.Kind == WorkspaceRequestPrincipalKind.Family
                ? FamilyResourceNotFound()
                : Results.NotFound(new OperationErrorDto("operation-not-found", "Operation not found."));

        if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
            !await familyAccess.IsApprovedMutableOperationAsync(
                principal.Member!.MemberId,
                source,
                ct).ConfigureAwait(false))
        {
            return FamilyOwnerActionRequired(
                "approved-app-required",
                "Ask the home Owner to review this app before continuing.");
        }

        if (string.Equals(source.Type, DeviceEnrollmentService.OperationType, StringComparison.Ordinal))
        {
            if (principal.Kind == WorkspaceRequestPrincipalKind.Family &&
                await familyAccess.HasAcceptedDeviceAsync(
                    principal.Member!.MemberId,
                    ct).ConfigureAwait(false))
            {
                return FamilyOwnerActionRequired(
                    "additional-device-owner-required",
                    "Ask the home Owner to add another iPhone for you.");
            }

            DeviceEnrollmentSubmissionResult enrollment = await enrollments.RetryAsync(
                operationId,
                ActorFrom(context),
                request.IdempotencyKey,
                ActorMemberIdFrom(context),
                ct);
            if (enrollment.Error is null && enrollment.Record is not null)
            {
                object response = await OperationResponseForAsync(
                    principal,
                    enrollment.Record,
                    familyAccess,
                    ct).ConfigureAwait(false);
                return enrollment.Created
                    ? Results.Created($"/api/operations/{enrollment.Record.OperationId}", response)
                    : Results.Ok(response);
            }

            var enrollmentError = new OperationErrorDto(
                enrollment.Error ?? "operation-not-retryable",
                enrollment.Message ?? "Device enrollment is not retryable.");
            return enrollment.Error switch
            {
                "operation-not-found" => Results.NotFound(enrollmentError),
                "operation-not-retryable" or "device-enrollment-active" or "idempotency-target-conflict" => Results.Conflict(enrollmentError),
                _ => Results.UnprocessableEntity(enrollmentError),
            };
        }

        (OperationRecordDto? record, bool created, string? error) = await operations.RetryAsync(
            operationId,
            ActorFrom(context),
            request.IdempotencyKey,
            ActorMemberIdFrom(context),
            source.OwnerMemberId,
            ct);
        return await OperationActionResultForRequestAsync(
            principal,
            record,
            created,
            error,
            "operation-not-retryable",
            "Operation is not retryable.",
            familyAccess,
            ct).ConfigureAwait(false);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (KnownDeviceStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("known-device-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapPost("/api/operations/{operationId}/rerun", async (
    string operationId,
    OperationActionRequest request,
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        OperationRecordDto? source = null;
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            source = await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                operationId,
                requireOwnActor: true,
                ct).ConfigureAwait(false);
            if (source is null)
                return FamilyResourceNotFound();
            if (!await familyAccess.IsApprovedMutableOperationAsync(
                    principal.Member.MemberId,
                    source,
                    ct).ConfigureAwait(false))
            {
                return FamilyOwnerActionRequired(
                    "approved-app-required",
                    "Ask the home Owner to review this app before continuing.");
            }
        }

        (OperationRecordDto? record, bool created, string? error) = await operations.RerunAsync(
            operationId,
            ActorFrom(context),
            request.IdempotencyKey,
            ActorMemberIdFrom(context),
            source?.OwnerMemberId ?? await OwnerMemberIdForOperationAsync(
                operationId,
                familyAccess,
                ct).ConfigureAwait(false),
            ct);
        return await OperationActionResultForRequestAsync(
            principal,
            record,
            created,
            error,
            "operation-not-rerunnable",
            "Operation is not rerunnable yet.",
            familyAccess,
            ct).ConfigureAwait(false);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/operations", async (
    HttpContext context,
    OperationStore operations,
    FamilyResourceAccess familyAccess,
    string? deviceUdid,
    string? bundleId,
    int? limit,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind == WorkspaceRequestPrincipalKind.Family)
        {
            IReadOnlyList<OperationRecordDto> owned = await familyAccess.ListOwnedOperationsAsync(
                principal.Member!.MemberId,
                deviceUdid,
                bundleId,
                limit,
                ct);
            var projected = new List<FamilyOperationDto>(owned.Count);
            foreach (OperationRecordDto operation in owned)
            {
                projected.Add(await familyAccess.ProjectOperationAsync(
                    principal.Member.MemberId,
                    operation,
                    ct).ConfigureAwait(false));
            }
            return Results.Ok(projected);
        }
        return Results.Ok(await operations.ListAsync(deviceUdid, bundleId, limit ?? 25, ct));
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/operations/{operationId}", async (
    string operationId,
    HttpContext context,
    OperationStore operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        OperationRecordDto? record = principal.Kind == WorkspaceRequestPrincipalKind.Family
            ? await familyAccess.FindOwnedOperationAsync(
                principal.Member!.MemberId,
                operationId,
                requireOwnActor: false,
                ct).ConfigureAwait(false)
            : await operations.FindAsync(operationId, ct);
        if (record is null)
            return principal.Kind == WorkspaceRequestPrincipalKind.Family
                ? FamilyResourceNotFound()
                : Results.NotFound(new OperationErrorDto("operation-not-found", "Operation not found."));
        return Results.Ok(await OperationResponseForAsync(
            principal,
            record,
            familyAccess,
            ct).ConfigureAwait(false));
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/api/renewals", async (
    HttpContext context,
    OperationService operations,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
{
    try
    {
        IReadOnlyList<RenewalItemDto> renewals = await operations.RenewalsAsync(ct);
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
            return Results.Ok(renewals);

        IReadOnlyList<OwnedFamilyRegistration> registrations = await familyAccess.ListOwnedRegistrationsAsync(
            principal.Member!.MemberId,
            ct);
        var registrationsByKey = registrations.ToDictionary(
            item => item.Registration.Key,
            StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<OperationRecordDto> ownedOperations = await familyAccess.ListOwnedOperationsAsync(
            principal.Member.MemberId,
            deviceUdid: null,
            bundleId: null,
            limit: 100,
            ct);
        FamilyRenewalDto[] projection = renewals
            .Where(renewal => registrationsByKey.ContainsKey($"{renewal.DeviceUdid}:{renewal.BundleId}"))
            .Select(renewal =>
            {
                OwnedFamilyRegistration owned = registrationsByKey[$"{renewal.DeviceUdid}:{renewal.BundleId}"];
                OperationRecordDto? latest = ownedOperations.FirstOrDefault(operation =>
                    string.Equals(operation.Target.DeviceUdid, renewal.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(operation.Target.BundleId, renewal.BundleId, StringComparison.Ordinal));
                return FamilyResourceProjections.Renewal(renewal, owned, latest);
            })
            .ToArray();
        return Results.Ok(projection);
    }
    catch (OperationStoreException ex)
    {
        return Results.Json(
            new OperationErrorDto("operation-store-unavailable", ex.Message, ex.InnerException?.GetType().Name),
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

if (hasAdminBundle)
    app.MapFallbackToFile("index.html");

app.Run();

static bool IsPublicAdminAsset(
    PathString path,
    Microsoft.Extensions.FileProviders.IFileProvider webRoot)
{
    bool approvedLocation = path.StartsWithSegments("/assets") ||
        path.Equals("/favicon.svg", StringComparison.OrdinalIgnoreCase);
    if (!approvedLocation || !path.HasValue)
        return false;

    var file = webRoot.GetFileInfo(path.Value!.TrimStart('/'));
    return file.Exists && !file.IsDirectory;
}

static IResult OriginOrAntiforgery() =>
    Results.Json(
        new
        {
            error = "origin-or-antiforgery",
            message = "Refresh Sideport and try signing out again.",
        },
        statusCode: StatusCodes.Status403Forbidden);

static long? ParseOptionalPositiveInt64(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    if (!long.TryParse(value, out long parsed) || parsed <= 0)
        throw new ArgumentException("Configured identifiers must be positive integers.");
    return parsed;
}

static string[] ReadConfigurationList(IConfiguration configuration, string key)
{
    string[] children = configuration.GetSection(key)
        .GetChildren()
        .Select(child => child.Value?.Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();
    if (children.Length != 0)
        return children;
    return (configuration[key] ?? string.Empty)
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

static IPAddress ParseTrustedProxy(string value) =>
    IPAddress.TryParse(value, out IPAddress? address)
        ? address
        : throw new InvalidOperationException(
            $"Sideport:ReverseProxy:KnownProxies contains invalid IP address '{value}'.");

static System.Net.IPNetwork ParseTrustedProxyNetwork(string value) =>
    System.Net.IPNetwork.TryParse(value, out System.Net.IPNetwork network)
        ? network
        : throw new InvalidOperationException(
            $"Sideport:ReverseProxy:KnownNetworks contains invalid CIDR network '{value}'.");

static Uri? ParseOptionalHttpsUri(string? value, string key)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
        uri.Scheme != Uri.UriSchemeHttps ||
        !string.IsNullOrEmpty(uri.UserInfo) ||
        !string.IsNullOrEmpty(uri.Query) ||
        !string.IsNullOrEmpty(uri.Fragment))
    {
        throw new InvalidOperationException($"{key} must be an absolute HTTPS origin without credentials, query, or fragment.");
    }
    return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
}

static Guid? ParseOptionalGuid(string? value, string key)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    return Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
        ? parsed
        : throw new InvalidOperationException($"{key} must be a non-empty UUID.");
}

static bool IsTrustedProxy(
    IPAddress? remoteAddress,
    IReadOnlyList<IPAddress> trustedProxies,
    IReadOnlyList<System.Net.IPNetwork> trustedNetworks)
{
    if (remoteAddress is null)
        return false;
    if (trustedProxies.Any(proxy => proxy.Equals(remoteAddress)))
        return true;
    return trustedNetworks.Any(network => network.Contains(remoteAddress));
}

static async Task WriteAppleRateLimitAsync(
    HttpContext context,
    TimeSpan retryAfter,
    string error)
{
    int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    await context.Response.WriteAsJsonAsync(new
    {
        error,
        message = "Too many Apple account attempts. Wait before trying again.",
        retryAfterSeconds,
    });
}

static IResult AppleRateLimitResult(
    HttpContext context,
    TimeSpan retryAfter,
    string error)
{
    int retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
    context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return Results.Json(new
    {
        error,
        message = "Too many Apple account attempts. Wait before trying again.",
        retryAfterSeconds,
    }, statusCode: StatusCodes.Status429TooManyRequests);
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

static async Task CopyCatalogUploadBoundedAsync(
    IFormFile upload,
    string destinationPath,
    long maxBytes,
    CancellationToken ct)
{
    if (upload.Length > maxBytes)
        throw new CatalogV2Exception(
            "upload-too-large",
            "The uploaded IPA exceeds the configured catalog size limit.",
            limit: maxBytes);

    await using Stream source = upload.OpenReadStream();
    await using var destination = new FileStream(
        destinationPath,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        81_920,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    byte[] buffer = new byte[81_920];
    long copied = 0;
    while (true)
    {
        int read = await source.ReadAsync(buffer, ct);
        if (read == 0)
            break;

        copied += read;
        if (copied > maxBytes)
        {
            throw new CatalogV2Exception(
                "upload-too-large",
                "The uploaded IPA exceeds the configured catalog size limit.",
                limit: maxBytes);
        }

        await destination.WriteAsync(buffer.AsMemory(0, read), ct);
    }

    await destination.FlushAsync(ct);
}

static IResult CatalogV2Error(CatalogV2Exception error)
{
    int statusCode = error.Code switch
    {
        "catalog-root-not-found" or "catalog-source-not-found" => StatusCodes.Status404NotFound,
        "upload-too-large" or "catalog-source-too-large" => StatusCodes.Status413PayloadTooLarge,
        "unsupported-media-type" => StatusCodes.Status415UnsupportedMediaType,
        "catalog-id-conflict" or "catalog-version-conflict" or "idempotency-target-conflict" => StatusCodes.Status409Conflict,
        "catalog-store-unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
    return Results.Json(new
    {
        error = error.Code,
        message = error.Message,
        id = error.Id,
        limit = error.Limit,
    }, statusCode: statusCode);
}

static IResult CatalogV2UploadTooLarge(long maxBytes) =>
    Results.Json(new
    {
        error = "upload-too-large",
        message = "The uploaded IPA exceeds the configured catalog size limit.",
        limit = maxBytes,
    }, statusCode: StatusCodes.Status413PayloadTooLarge);

static IResult CatalogV2InspectionError() =>
    Results.UnprocessableEntity(new
    {
        error = "ipa-inspection-failed",
        message = "The IPA could not be inspected.",
    });

static IResult CatalogV2StoreError() =>
    Results.Json(new
    {
        error = "catalog-store-unavailable",
        message = "The catalog could not be updated.",
    }, statusCode: StatusCodes.Status503ServiceUnavailable);

static IResult GitHubCatalogError(GitHubCatalogException error)
{
    int statusCode = error.Code switch
    {
        "github-repository-invalid" or "github-state-invalid" or
            "github-state-expired" or "github-state-replayed" => StatusCodes.Status400BadRequest,
        "github-source-not-found" or "github-repository-not-found" or
            "github-release-not-found" or "github-asset-not-found" => StatusCodes.Status404NotFound,
        "github-asset-too-large" => StatusCodes.Status413PayloadTooLarge,
        "github-asset-changed" or "idempotency-target-conflict" or
            "catalog-id-conflict" or "catalog-version-conflict" => StatusCodes.Status409Conflict,
        "github-rate-limited" => StatusCodes.Status429TooManyRequests,
        "github-redirect-rejected" or "github-upstream-unavailable" => StatusCodes.Status502BadGateway,
        "github-download-timeout" => StatusCodes.Status504GatewayTimeout,
        "github-app-not-configured" or "github-credential-unavailable" or
            "github-store-unavailable" => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status422UnprocessableEntity,
    };
    return Results.Json(new
    {
        error = error.Code,
        message = error.Message,
        limit = error.Limit,
        retryAfterSeconds = error.RetryAfter is null
            ? (double?)null
            : Math.Max(0, Math.Ceiling(error.RetryAfter.Value.TotalSeconds)),
    }, statusCode: statusCode);
}

static IResult GitHubValidationError() =>
    Results.BadRequest(new
    {
        error = "validation-failed",
        message = "The GitHub request is invalid.",
    });

static IResult MutationProtectionRequired(string purpose = "using GitHub sources") =>
    Results.Json(new
    {
        error = "mutation-protection-required",
        message = $"Configure bearer-token or OIDC authentication before {purpose}.",
    }, statusCode: StatusCodes.Status403Forbidden);

static bool TryVerifiedActorFrom(HttpContext context, out string actor)
{
    if (context.Items.TryGetValue(WorkspaceApiSecurity.PrincipalItemKey, out object? value) &&
        value is WorkspaceRequestPrincipal principal)
    {
        if (principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer)
        {
            actor = "recovery-bearer";
            return true;
        }
        if (principal.IsActiveMember)
        {
            actor = $"member:{principal.Member!.MemberId}";
            return true;
        }
    }

    actor = string.Empty;
    return false;
}

static PersonalAppleStatusDto PersonalAppleStatusForRequest(
    PersonalAppleStatusDto status,
    HttpContext context,
    bool allowInsecureCredentialEntryOnLoopback,
    bool allowInsecureAppleTls)
{
    PersonalAppleCredentialEntryDto? entry = status.CredentialEntry;
    if (entry?.Supported != true)
        return status;

    PersonalAppleBlockedReasonDto? blockedReason = null;
    if (!TryVerifiedActorFrom(context, out _))
    {
        blockedReason = new PersonalAppleBlockedReasonDto(
            "mutation-protection-required",
            "Configure bearer-token or OIDC authentication before entering an Apple credential.");
    }
    else if (allowInsecureAppleTls)
    {
        blockedReason = new PersonalAppleBlockedReasonDto(
            "apple-tls-policy-unsafe",
            "Credential entry is disabled while insecure Apple TLS is enabled.");
    }
    else if (!AppleCredentialTransportPolicy.IsAllowed(
                 context.Request.IsHttps,
                 context.Connection.LocalIpAddress,
                 context.Connection.RemoteIpAddress,
                 allowInsecureCredentialEntryOnLoopback))
    {
        blockedReason = new PersonalAppleBlockedReasonDto(
            "credential-entry-transport-required",
            "Use HTTPS to enter an Apple credential.");
    }

    return status with
    {
        CredentialEntry = entry with
        {
            AllowedNow = blockedReason is null,
            BlockedReason = blockedReason,
        },
    };
}

static Uri BuildFixedGitHubStatusUri(GitHubCatalogOptions options, string status)
{
    string separator = options.UiStatusPath.Contains('?') ? "&" : "?";
    var result = new Uri(
        options.UiBaseUri,
        $"{options.UiStatusPath}{separator}github={Uri.EscapeDataString(status)}");
    if (!string.Equals(
            result.GetLeftPart(UriPartial.Authority),
            options.UiBaseUri.GetLeftPart(UriPartial.Authority),
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException("The GitHub status route must stay on the configured Sideport origin.");
    }
    return result;
}

static void TryDeleteCatalogUpload(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
}

static Dictionary<string, string[]> ValidateOperationTarget(string? type, string? udid, string? bundleId)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    AddRequired(errors, "type", type);
    AddRequired(errors, "deviceUdid", udid);
    AddRequired(errors, "bundleId", bundleId);
    return errors;
}

static OperationActorDto ActorFrom(HttpContext context)
{
    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    if (principal.IsActiveMember)
    {
        return new OperationActorDto(
            "member",
            principal.Member!.DisplayName,
            principal.Member.MemberId);
    }

    return new OperationActorDto("recovery-bearer", "Recovery access");
}

static string? ActorMemberIdFrom(HttpContext context)
{
    WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
    return principal.IsActiveMember ? principal.Member!.MemberId : null;
}

static async Task<string?> OwnerMemberIdForTargetAsync(
    WorkspaceRequestPrincipal principal,
    string? deviceUdid,
    FamilyResourceAccess familyAccess,
    CancellationToken ct)
{
    string? ownerMemberId = await familyAccess.FindDeviceOwnerMemberIdAsync(
        deviceUdid,
        ct).ConfigureAwait(false);
    if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
        return ownerMemberId;
    return string.Equals(ownerMemberId, principal.Member!.MemberId, StringComparison.Ordinal)
        ? ownerMemberId
        : null;
}

static Task<string?> OwnerMemberIdForOperationAsync(
    string operationId,
    FamilyResourceAccess familyAccess,
    CancellationToken ct) =>
    familyAccess.FindOperationOwnerMemberIdAsync(operationId, ct);

static IResult FamilyResourceNotFound() =>
    Results.NotFound(new
    {
        error = "resource-not-found",
        message = "The requested Sideport item was not found.",
    });

static IResult FamilyOwnerActionRequired(string reason, string message) =>
    Results.UnprocessableEntity(new
    {
        error = "owner-action-required",
        reason,
        message,
    });

static string CatalogActorFrom(HttpContext context)
{
    if (!TryVerifiedActorFrom(context, out string actor))
        throw new InvalidOperationException("A verified workspace actor is required.");
    return actor;
}

static async Task<object> OperationResponseForAsync(
    WorkspaceRequestPrincipal principal,
    OperationRecordDto record,
    FamilyResourceAccess familyAccess,
    CancellationToken ct)
{
    if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
        return record;
    return await familyAccess.ProjectOperationAsync(
        principal.Member!.MemberId,
        record,
        ct).ConfigureAwait(false);
}

static IResult OperationActionResult(OperationRecordDto? record, bool created, string? error, string conflictCode, string conflictMessage)
{
    return error switch
    {
        null when created => Results.Created($"/api/operations/{record!.OperationId}", record),
        null => Results.Ok(record),
        "operation-not-found" => Results.NotFound(new OperationErrorDto("operation-not-found", "Operation not found.")),
        _ when error == conflictCode => Results.Conflict(new OperationErrorDto(conflictCode, conflictMessage)),
        _ => Results.UnprocessableEntity(new OperationErrorDto(error, "Operation action failed.")),
    };
}

static async Task<IResult> OperationActionResultForRequestAsync(
    WorkspaceRequestPrincipal principal,
    OperationRecordDto? record,
    bool created,
    string? error,
    string conflictCode,
    string conflictMessage,
    FamilyResourceAccess familyAccess,
    CancellationToken ct)
{
    if (principal.Kind != WorkspaceRequestPrincipalKind.Family)
        return OperationActionResult(record, created, error, conflictCode, conflictMessage);
    FamilyOperationDto? projected = record is null
        ? null
        : await familyAccess.ProjectOperationAsync(
            principal.Member!.MemberId,
            record,
            ct).ConfigureAwait(false);
    return error switch
    {
        null when created => Results.Created(
            $"/api/operations/{record!.OperationId}",
            projected),
        null => Results.Ok(projected),
        "operation-not-found" => FamilyResourceNotFound(),
        _ when error == conflictCode => Results.Conflict(
            new OperationErrorDto(conflictCode, conflictMessage)),
        _ => Results.UnprocessableEntity(
            new OperationErrorDto(error ?? "operation-action-failed", "Sideport could not complete this action.")),
    };
}

static OnboardingCompletionReceiptDto ProjectOnboardingReceipt(OnboardingCompletionReceipt receipt) =>
    new(
        receipt.SchemaVersion,
        receipt.CompletedAt,
        receipt.Actor,
        receipt.AccountProfileId,
        receipt.TeamId,
        receipt.DeviceUdid,
        new OnboardingRegistrationKeyDto(receipt.DeviceUdid, receipt.BundleId),
        receipt.CatalogAppId,
        receipt.CatalogVersion,
        receipt.CatalogSha256,
        receipt.VerifiedOperationId,
        receipt.SchedulerSettingsVersion,
        receipt.OperationalCheckedAt);

static void AddRequired(Dictionary<string, string[]> errors, string field, string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        errors[field] = ["Required."];
}

public sealed record AppRegistrationMutationRequest(
    string? BundleId,
    string? AppleId,
    string? TeamId,
    string? DeviceUdid,
    string? InputIpaPath,
    string? Lifecycle = null,
    string? CatalogAppId = null,
    string? AccountProfileId = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ActivatedAt = null,
    string? LastVerifiedOperationId = null);

public sealed record OnboardingCompleteRequest(
    string VerifiedOperationId,
    string IdempotencyKey);

public sealed record OnboardingStatus(
    bool FirstRunComplete,
    bool SchedulerEnabled,
    IReadOnlyList<OnboardingStep> Steps,
    string SetupState,
    string? SelectedCatalogAppId,
    string? ActiveInstallOperationId,
    OnboardingCompletionReceiptDto? CompletionReceipt,
    OnboardingWorkflowDto Workflow);

public sealed record OnboardingRegistrationKeyDto(string DeviceUdid, string BundleId);

public sealed record OnboardingCompletionReceiptDto(
    int SchemaVersion,
    DateTimeOffset CompletedAt,
    OperationActorDto Actor,
    string AccountProfileId,
    string TeamId,
    string DeviceUdid,
    OnboardingRegistrationKeyDto RegistrationKey,
    string CatalogAppId,
    int CatalogVersion,
    string CatalogSha256,
    string VerifiedOperationId,
    string SchedulerSettingsVersion,
    DateTimeOffset OperationalCheckedAt);

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
