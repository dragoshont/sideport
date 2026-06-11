using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

/// <summary>
/// Integration tests for the HTTP surface: liveness/readiness probes and the
/// bearer-token guard on <c>/api/*</c>. Driven through WebApplicationFactory so
/// the real middleware pipeline + endpoints run, with anisette stubbed (the
/// container sidecar isn't present in CI).
/// </summary>
public class ApiSmokeTests
{
    private static WebApplicationFactory<Program> Factory(
        string? apiToken = null,
        bool anisetteHealthy = true,
        string? signerPath = null,
        string? stateDirectory = null,
        string? seedCatalogPath = null,
        string? ascKeyId = null,
        string? ascIssuerId = null,
        string? ascPrivateKeyPath = null,
        HttpMessageHandler? ascHandler = null,
        string? personalAppleId = null,
        string? personalApplePassword = null,
        StubApplePortal? personalApplePortal = null)
    {
        signerPath ??= typeof(ApiSmokeTests).Assembly.Location; // a file that exists
        stateDirectory ??= Path.Combine(Path.GetTempPath(), "sideport-api-tests", Guid.NewGuid().ToString("N"));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", signerPath);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            if (apiToken is not null)
                builder.UseSetting("Sideport:Api:AuthToken", apiToken);
            if (seedCatalogPath is not null)
                builder.UseSetting("Sideport:Catalog:SeedCertClockPath", seedCatalogPath);
            if (ascKeyId is not null)
                builder.UseSetting("Sideport:AppStoreConnect:KeyId", ascKeyId);
            if (ascIssuerId is not null)
                builder.UseSetting("Sideport:AppStoreConnect:IssuerId", ascIssuerId);
            if (ascPrivateKeyPath is not null)
                builder.UseSetting("Sideport:AppStoreConnect:PrivateKeyPath", ascPrivateKeyPath);
            if (ascHandler is not null)
                builder.UseSetting("Sideport:AppStoreConnect:BaseUrl", "https://apple.test");
            if (personalAppleId is not null)
                builder.UseSetting("Sideport:Apple:PersonalAppleId", personalAppleId);

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAnisetteProvider>();
                services.AddSingleton<IAnisetteProvider>(new StubAnisette(anisetteHealthy));
                if (personalApplePortal is not null || personalApplePassword is not null)
                {
                    services.RemoveAll<IAppleDeveloperPortal>();
                    services.RemoveAll<IAppleCredentialProvider>();
                    services.AddSingleton<IAppleDeveloperPortal>(personalApplePortal ?? new StubApplePortal());
                    services.AddSingleton<IAppleCredentialProvider>(new StubCredentialProvider(personalAppleId ?? "me@example.com", personalApplePassword));
                }
                if (ascHandler is not null)
                {
                    services.RemoveAll<IHttpMessageHandlerBuilderFilter>();
                    services.AddSingleton(ascHandler);
                    services.AddSingleton<IHttpMessageHandlerBuilderFilter, StubAppleHttpClientFilter>();
                }
            });
        });
    }

    [Fact]
    public async Task Healthz_IsOpen_AndReturnsOk()
    {
        using var factory = Factory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readyz_AllDependenciesHealthy_Returns200Ready()
    {
        using var factory = Factory(anisetteHealthy: true);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyDto>();
        Assert.True(body!.ready);
    }

    [Fact]
    public async Task Readyz_AnisetteDown_Returns503NotReady()
    {
        using var factory = Factory(anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyDto>();
        Assert.False(body!.ready);
    }

    [Fact]
    public async Task Readyz_SignerMissing_Returns503NotReady()
    {
        using var factory = Factory(signerPath: "/nonexistent/zsign");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_RejectsMissingBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_RejectsWrongBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_AcceptsCorrectBearer()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnisetteInfo_WhenAnisetteDown_Returns503Json_NotDeveloperException()
    {
        using var factory = Factory(anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OnboardingStatus_WithTokenConfigured_ReturnsFirstRunSteps()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/onboarding/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OnboardingDto>();
        Assert.NotNull(body);
        Assert.Contains(body!.steps, step => step.id == "api-auth" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "anisette" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "signer" && step.state == "complete");
        Assert.Contains(body.steps, step => step.id == "catalog" && step.state == "pending");
        Assert.Contains(body.steps, step => step.id == "iphone-developer-mode" && step.surface == "iphone");
        Assert.False(body.firstRunComplete);
    }

    [Fact]
    public async Task Logs_ReturnRecentApiRequests()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/anisette/info")).StatusCode);

        var logs = await client.GetFromJsonAsync<IReadOnlyList<LogDto>>("/api/logs?limit=20");

        Assert.NotNull(logs);
        Assert.Contains(logs!, log => log.message.Contains("/api/anisette/info", StringComparison.Ordinal));
        Assert.All(logs!, log => Assert.False(string.IsNullOrWhiteSpace(log.category)));
    }

    [Fact]
    public async Task CatalogApps_WithSeedIpa_ReturnsInspectedMetadata()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token", seedCatalogPath: ipaPath);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var catalog = await client.GetFromJsonAsync<IReadOnlyList<CatalogAppDto>>("/api/catalog/apps");

        Assert.NotNull(catalog);
        CatalogAppDto app = Assert.Single(catalog!);
        Assert.Equal("cert-clock", app.id);
        Assert.Equal("ready", app.status);
        Assert.Equal("ro.hont.certcountdown", app.bundleId);
        Assert.Equal("0.1.0", app.shortVersion);
        Assert.False(app.hasEmbeddedProfile);
        Assert.False(string.IsNullOrWhiteSpace(app.sha256));
    }

    [Fact]
    public async Task CatalogInspect_MissingPath_Returns404()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/catalog/apps/inspect", new
        {
            ipaPath = Path.Combine(TestDir(), "missing.ipa"),
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AppleAccessStatus_WithoutAscConfig_ReturnsNotConfigured()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var status = await client.GetFromJsonAsync<AppleAccessStatusDto>("/api/apple-access/status");

        Assert.NotNull(status);
        Assert.Equal("not-configured", status!.state);
        Assert.All(status.capabilities, capability => Assert.Equal("not-checked", capability.state));
    }

    [Fact]
    public async Task AppleAccessStatus_WithAscConfig_ProbesReadOnlyEndpoints()
    {
        string dir = TestDir();
        string keyPath = WriteEcPrivateKey(dir);
        using var handler = new StubAppleHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"id\":\"one\"}]}", Encoding.UTF8, "application/json"),
        });
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            ascKeyId: "ASC1234567",
            ascIssuerId: "00000000-1111-2222-3333-444444445555",
            ascPrivateKeyPath: keyPath,
            ascHandler: handler);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var status = await client.GetFromJsonAsync<AppleAccessStatusDto>("/api/apple-access/status");

        Assert.NotNull(status);
        Assert.Equal("read-only-verified", status!.state);
        Assert.Equal("...4567", status.keyIdSuffix);
        Assert.Equal("...5555", status.issuerIdSuffix);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(status.capabilities, capability =>
        {
            Assert.Equal("verified", capability.state);
            Assert.Equal(200, capability.httpStatus);
            Assert.Equal(1, capability.count);
        });
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        });
    }

    [Fact]
    public async Task AppleAccessStatus_WhenEndpointForbidden_ReturnsDeniedCapability()
    {
        string dir = TestDir();
        string keyPath = WriteEcPrivateKey(dir);
        using var handler = new StubAppleHandler(request => new HttpResponseMessage(
            request.RequestUri!.AbsolutePath.EndsWith("/profiles", StringComparison.Ordinal)
                ? HttpStatusCode.Forbidden
                : HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json"),
        });
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            ascKeyId: "ASC1234567",
            ascIssuerId: "00000000-1111-2222-3333-444444445555",
            ascPrivateKeyPath: keyPath,
            ascHandler: handler);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var status = await client.GetFromJsonAsync<AppleAccessStatusDto>("/api/apple-access/status");

        Assert.NotNull(status);
        Assert.Equal("partial", status!.state);
        Assert.Contains(status.capabilities, capability => capability.id == "profiles" && capability.state == "denied" && capability.httpStatus == 403);
    }

    [Fact]
    public async Task PersonalAppleStatus_WithoutConfiguredAppleId_ReturnsNotConfigured()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var status = await client.GetFromJsonAsync<PersonalAppleStatusDto>("/api/apple-access/personal/status");

        Assert.NotNull(status);
        Assert.Equal("not-configured", status!.state);
        Assert.Null(status.pendingChallengeId);
    }

    [Fact]
    public async Task PersonalAppleSignIn_WithConfiguredCredential_ReturnsTeams()
    {
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "me@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/apple-access/personal/sign-in", new { appleId = "me@example.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<PersonalAppleStatusDto>();
        Assert.NotNull(status);
        Assert.Equal("authenticated", status!.state);
        Assert.Equal("m***@example.com", status.appleIdHint);
        Assert.Contains(status.teams, team => team.teamId == "TEAMID1234");
    }

    [Fact]
    public async Task PersonalAppleSignIn_WhenTwoFactorRequired_ReturnsPendingChallengeThenCompletes()
    {
        var portal = new StubApplePortal { RequireTwoFactor = true };
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "me@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: portal);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage signIn = await client.PostAsJsonAsync("/api/apple-access/personal/sign-in", new { appleId = "me@example.com" });
        var pending = await signIn.Content.ReadFromJsonAsync<PersonalAppleStatusDto>();

        Assert.NotNull(pending);
        Assert.Equal("two-factor-required", pending!.state);
        Assert.False(string.IsNullOrWhiteSpace(pending.pendingChallengeId));

        portal.RequireTwoFactor = false;
        HttpResponseMessage complete = await client.PostAsJsonAsync("/api/apple-access/personal/2fa", new { challengeId = pending.pendingChallengeId, code = "123456" });
        var status = await complete.Content.ReadFromJsonAsync<PersonalAppleStatusDto>();

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal("authenticated", status!.state);
        Assert.Equal("123456", portal.LastTwoFactorCode);
    }

    [Fact]
    public async Task AppRegistrations_AreDurableAcrossApiRestart()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            HttpResponseMessage created = await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            var apps = await client.GetFromJsonAsync<IReadOnlyList<RegisteredAppDto>>("/api/apps");
            Assert.NotNull(apps);
            RegisteredAppDto app = Assert.Single(apps!);
            Assert.Equal("ro.hont.certcountdown", app.bundleId);
            Assert.Equal("TEST-UDID", app.deviceUdid);
        }
    }

    [Fact]
    public async Task AppRegistration_RejectsBundleMismatch()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.other", ipaPath));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task AppRegistration_EnforcesPerDeviceSlotCap()
    {
        string dir = TestDir();
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        for (int i = 1; i <= 3; i++)
        {
            string bundleId = $"ro.hont.app{i}";
            string ipaPath = WriteTestIpa(dir, bundleId, $"App {i}", i.ToString(), $"0.{i}.0");
            HttpResponseMessage created = await client.PostAsJsonAsync("/api/apps", Registration(bundleId, ipaPath));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        string fourthPath = WriteTestIpa(dir, "ro.hont.app4", "App 4", "4", "0.4.0");
        HttpResponseMessage fourth = await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.app4", fourthPath));

        Assert.Equal(HttpStatusCode.Conflict, fourth.StatusCode);
    }

    [Fact]
    public async Task Probes_StayOpen_EvenWhenTokenConfigured()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);
    }

    [Fact]
    public async Task Api_WithoutTokenConfigured_IsOpen()
    {
        using var factory = Factory(apiToken: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed record ReadyDto(bool ready);
    private sealed record OnboardingDto(bool firstRunComplete, IReadOnlyList<OnboardingStepDto> steps);
    private sealed record OnboardingStepDto(string id, string state, string surface);
    private sealed record LogDto(string at, string level, string category, string message);
    private sealed record CatalogAppDto(string id, string status, string bundleId, string? shortVersion, bool hasEmbeddedProfile, string? sha256);
    private sealed record RegisteredAppDto(string bundleId, string deviceUdid);
    private sealed record AppleAccessStatusDto(string state, string? keyIdSuffix, string? issuerIdSuffix, IReadOnlyList<AppleAccessCapabilityDto> capabilities);
    private sealed record AppleAccessCapabilityDto(string id, string state, int? httpStatus, int? count);
    private sealed record PersonalAppleStatusDto(string state, string? appleIdHint, string? pendingChallengeId, IReadOnlyList<PersonalAppleTeamDto> teams);
    private sealed record PersonalAppleTeamDto(string teamId, string name, string type);

    private static object Registration(string bundleId, string ipaPath) => new
    {
        bundleId,
        appleId = "developer@example.com",
        teamId = "TEAMID1234",
        deviceUdid = "TEST-UDID",
        inputIpaPath = ipaPath,
    };

    private static string TestDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string WriteTestIpa(string dir, string bundleId, string displayName, string version, string shortVersion)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"{bundleId}.ipa");
        using FileStream stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry("Payload/Test.app/Info.plist");
        using Stream entryStream = entry.Open();
        byte[] plist = Encoding.UTF8.GetBytes($$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleIdentifier</key><string>{{bundleId}}</string>
                <key>CFBundleDisplayName</key><string>{{displayName}}</string>
                <key>CFBundleExecutable</key><string>Test</string>
                <key>CFBundleVersion</key><string>{{version}}</string>
                <key>CFBundleShortVersionString</key><string>{{shortVersion}}</string>
            </dict>
            </plist>
            """);
        entryStream.Write(plist);
        return path;
    }

    private static string WriteEcPrivateKey(string dir)
    {
        Directory.CreateDirectory(dir);
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string path = Path.Combine(dir, "AuthKey_TEST.p8");
        File.WriteAllText(path, key.ExportPkcs8PrivateKeyPem());
        return path;
    }

    private sealed class StubAppleHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(Clone(request));
            return Task.FromResult(responder(request));
        }

        private static HttpRequestMessage Clone(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return clone;
        }
    }

    private sealed class StubAppleHttpClientFilter(HttpMessageHandler handler) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            if (builder.Name == "app-store-connect")
                builder.PrimaryHandler = handler;
        };
    }

    private sealed class StubCredentialProvider(string appleId, string? password) : IAppleCredentialProvider
    {
        public Task<string?> GetPasswordAsync(string requestedAppleId, CancellationToken ct = default) =>
            Task.FromResult(string.Equals(requestedAppleId, appleId, StringComparison.OrdinalIgnoreCase) ? password : null);
    }

    private sealed class StubApplePortal : IAppleDeveloperPortal
    {
        public bool RequireTwoFactor { get; set; }
        public string? LastTwoFactorCode { get; private set; }

        public Task<AppleLoginResult> AuthenticateAsync(string appleId, string password, CancellationToken ct = default) =>
            Task.FromResult<AppleLoginResult>(RequireTwoFactor
                ? new AppleLoginResult.TwoFactorRequired(new AppleLoginChallenge("adsid", "idms", TwoFactorKind.TrustedDevice))
                : new AppleLoginResult.Success(new AppleSession(appleId, "adsid", appleId, [1, 2, 3]) { IdmsToken = "token" }));

        public Task SubmitTwoFactorCodeAsync(AppleLoginChallenge challenge, string code, CancellationToken ct = default)
        {
            LastTwoFactorCode = code;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppleTeam>>([new AppleTeam("TEAMID1234", "Personal Team", "Individual")]);

        public Task RegisterDeviceAsync(AppleSession session, string teamId, string udid, string name, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SigningCertificate> EnsureCertificateAsync(AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default) =>
            Task.FromResult(new SigningCertificate("serial", [], DateTimeOffset.UtcNow.AddDays(365)));

        public Task<ProvisioningProfile> EnsureProfileAsync(AppleSession session, string teamId, string bundleId, CancellationToken ct = default) =>
            Task.FromResult(new ProvisioningProfile("profile", bundleId, [], DateTimeOffset.UtcNow.AddDays(7)));
    }

    private sealed class StubAnisette(bool healthy) : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            healthy
                ? Task.FromResult(new AnisetteClientInfo("<TestClient>", "akd/1.0"))
                : throw new HttpRequestException("anisette unreachable");

        public Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteHeaders("M", "O", "R", "L", DateTimeOffset.UnixEpoch));
    }
}
