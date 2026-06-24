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
using Microsoft.Extensions.Hosting;
using Sideport.Api.Operations;
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
        long? catalogMaxUploadBytes = null,
        string? personalAppleId = null,
        string? personalApplePassword = null,
        StubApplePortal? personalApplePortal = null,
        bool operationWorker = true,
        bool oidc = false)
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
            if (catalogMaxUploadBytes is not null)
                builder.UseSetting("Sideport:Catalog:MaxUploadBytes", catalogMaxUploadBytes.Value.ToString());
            if (personalAppleId is not null)
                builder.UseSetting("Sideport:Apple:PersonalAppleId", personalAppleId);
            if (oidc)
            {
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://authentik.invalid/application/o/sideport/");
                builder.UseSetting("Sideport:Oidc:ClientId", "test-client");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-secret");
            }

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
                services.RemoveAll<ISigningIdentityProvider>();
                services.RemoveAll<ISigner>();
                services.RemoveAll<IDeviceController>();
                if (!operationWorker)
                    services.RemoveAll<IHostedService>();
                services.AddSingleton<ISigningIdentityProvider, StubSigningIdentityProvider>();
                services.AddSingleton<ISigner, StubSigner>();
                services.AddSingleton<IDeviceController, StubDeviceController>();
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
    public async Task Metrics_IsOpen_AndReturnsPrometheusText()
    {
        using var factory = Factory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("# HELP sideport_device_installed_apps_requests_total", body);
    }

    [Fact]
    public async Task Metrics_WithOidcEnabled_RemainsOpenForScraping()
    {
        using var factory = Factory(oidc: true);
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync("/metrics");

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
    public async Task Api_WithOidcEnabled_StillAcceptsBearerToken()
    {
        // The machine path must survive turning OIDC on: a valid bearer token
        // authorizes /api/* even when interactive OIDC login is enabled.
        using var factory = Factory(apiToken: "s3cr3t-token", oidc: true);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_WithOidcEnabled_RejectsUnauthenticated()
    {
        // No bearer token and no session cookie -> /api/* is 401 (not redirected).
        using var factory = Factory(apiToken: "s3cr3t-token", oidc: true);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Probes_StayOpen_WithOidcEnabled()
    {
        // Liveness/readiness must never be gated behind login (k8s probes).
        using var factory = Factory(apiToken: "s3cr3t-token", oidc: true);
        using HttpClient client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);
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
    public async Task CatalogUpload_ValidIpa_StoresDurableUploadWithoutInstalling()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        string ipaPath = WriteTestIpa(dir, "ro.hont.uploaded", "Uploaded App", "7", "1.2.3");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsync("/api/catalog/apps/upload", UploadContent(ipaPath, id: "uploaded-app", name: "Uploaded App"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<CatalogAppDto>();
        Assert.NotNull(uploaded);
        Assert.Equal("uploaded-app", uploaded!.id);
        Assert.Equal("ready", uploaded.status);
        Assert.Equal("ro.hont.uploaded", uploaded.bundleId);
        Assert.Equal("1.2.3", uploaded.shortVersion);
        Assert.True(File.Exists(Path.Combine(stateDir, "imports", "uploaded-app.ipa")));

        var apps = await client.GetFromJsonAsync<IReadOnlyList<CatalogAppDto>>("/api/catalog/apps");
        Assert.Contains(apps!, app => app.id == "uploaded-app" && app.status == "ready");
        var registrations = await client.GetFromJsonAsync<IReadOnlyList<RegisteredAppDto>>("/api/apps");
        Assert.Empty(registrations!);
    }

    [Fact]
    public async Task CatalogUpload_DuplicateId_RequiresReplace()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.uploaded", "Uploaded App", "7", "1.2.3");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/catalog/apps/upload", UploadContent(ipaPath, id: "uploaded-app"))).StatusCode);

        HttpResponseMessage conflict = await client.PostAsync("/api/catalog/apps/upload", UploadContent(ipaPath, id: "uploaded-app"));
        HttpResponseMessage replaced = await client.PostAsync("/api/catalog/apps/upload", UploadContent(ipaPath, id: "uploaded-app", replace: true));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var error = await conflict.Content.ReadFromJsonAsync<CatalogUploadErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("catalog-id-conflict", error!.error);
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
    }

    [Fact]
    public async Task CatalogUpload_RejectsTooLargeUploadBeforeCatalogMutation()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.uploaded", "Uploaded App", "7", "1.2.3");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: Path.Combine(dir, "state"), catalogMaxUploadBytes: 10);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsync("/api/catalog/apps/upload", UploadContent(ipaPath, id: "too-large"));
        var apps = await client.GetFromJsonAsync<IReadOnlyList<CatalogAppDto>>("/api/catalog/apps");

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CatalogUploadErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("upload-too-large", error!.error);
        Assert.DoesNotContain(apps!, app => app.id == "too-large");
    }

    [Fact]
    public async Task CatalogUpload_InvalidExtension_ReturnsUnsupportedMediaType()
    {
        string dir = TestDir();
        string path = Path.Combine(dir, "not-ipa.txt");
        await File.WriteAllTextAsync(path, "not an ipa");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsync("/api/catalog/apps/upload", UploadContent(path, id: "not-ipa"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CatalogUploadErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("unsupported-media-type", error!.error);
    }

    [Fact]
    public async Task CatalogUpload_InvalidIpa_ReturnsInspectionFailure()
    {
        string dir = TestDir();
        string path = Path.Combine(dir, "broken.ipa");
        await File.WriteAllTextAsync(path, "not a zip archive");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsync("/api/catalog/apps/upload", UploadContent(path, id: "broken"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CatalogUploadErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("ipa-inspection-failed", error!.error);
    }

    [Fact]
    public async Task CatalogUpload_ReplaceSaveFailure_RestoresPreviousDurableIpa()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        string firstIpa = WriteTestIpa(Path.Combine(dir, "first"), "ro.hont.uploaded", "Uploaded App", "1", "1.0.0");
        string secondIpa = WriteTestIpa(Path.Combine(dir, "second"), "ro.hont.uploaded", "Uploaded App", "2", "2.0.0");
        string durablePath = Path.Combine(stateDir, "imports", "uploaded-app.ipa");
        string catalogPath = Path.Combine(stateDir, "catalog.json");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/catalog/apps/upload", UploadContent(firstIpa, id: "uploaded-app"))).StatusCode);
        byte[] originalBytes = await File.ReadAllBytesAsync(durablePath);
        File.Delete(catalogPath);
        Directory.CreateDirectory(catalogPath);

        HttpResponseMessage response = await client.PostAsync("/api/catalog/apps/upload", UploadContent(secondIpa, id: "uploaded-app", replace: true));
        byte[] restoredBytes = await File.ReadAllBytesAsync(durablePath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<CatalogUploadErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("catalog-store-unavailable", error!.error);
        Assert.Equal(originalBytes, restoredBytes);
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
    public async Task AppRegistration_PersistsInputIpaIntoState_SurvivingSourceLoss()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");

        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage created = await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // The ephemeral upload path is wiped (as a pod restart would) ...
        File.Delete(ipaPath);

        // ... but a durable copy lives under the PVC state dir, so the scheduler's
        // refresh inputs survive the restart.
        string ipasDir = Path.Combine(stateDir, "ipas");
        Assert.True(Directory.Exists(ipasDir));
        Assert.Single(Directory.GetFiles(ipasDir, "*.ipa", SearchOption.AllDirectories));
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

    [Fact]
    public async Task KnownDevices_List_MergesReachableCurrentPoll()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var devices = await client.GetFromJsonAsync<IReadOnlyList<KnownDeviceDto>>("/api/devices/known");

        Assert.NotNull(devices);
        KnownDeviceDto device = Assert.Single(devices!);
        Assert.Equal("TEST-UDID", device.udid);
        Assert.Equal("Test iPhone", device.displayName);
        Assert.Equal("usb", device.connection);
        Assert.Equal("current-poll", device.lastSeenSource);
        Assert.Null(device.lastSeenAt);
        Assert.NotNull(device.currentPollAt);
        Assert.Equal("healthy", device.health.state);
        Assert.Equal("derived", device.health.source);
    }

    [Fact]
    public async Task KnownDevices_ReachableOverlay_KeepsDurableLastSeenSeparateFromCurrentPoll()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        Directory.CreateDirectory(stateDir);
        DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-3);
        await File.WriteAllTextAsync(Path.Combine(stateDir, "known-devices.json"), $$"""
        [
          {
            "udid": "TEST-UDID",
            "displayName": "Stored iPhone",
            "productType": "iPhone14,5",
            "osVersion": "16.0",
            "connection": "wifi",
            "firstSeenAt": "{{old:O}}",
            "lastSeenAt": "{{old:O}}",
            "lastSeenSource": "live-poll",
            "currentPollAt": "{{old:O}}",
            "trustState": "trusted",
            "owner": "lab",
            "notes": "stored",
            "updatedAt": "{{old:O}}"
          }
        ]
        """);
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var devices = await client.GetFromJsonAsync<IReadOnlyList<KnownDeviceDto>>("/api/devices/known");

        Assert.NotNull(devices);
        KnownDeviceDto device = Assert.Single(devices!);
        Assert.Equal("Stored iPhone", device.displayName);
        Assert.Equal("usb", device.connection);
        Assert.Equal(old, device.lastSeenAt);
        Assert.NotNull(device.currentPollAt);
        Assert.True(device.currentPollAt > old);
        Assert.Equal("live-poll", device.lastSeenSource);
        Assert.Equal("derived", device.health.source);
    }

    [Fact]
    public async Task KnownDevices_ManualRecord_PersistsAcrossRestartAndPatchesMetadata()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            HttpResponseMessage created = await client.PostAsJsonAsync("/api/devices/known", new
            {
                udid = "OFFLINE-UDID",
                displayName = "Shelf iPhone",
                owner = "lab",
                notes = "not plugged in",
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            HttpResponseMessage patched = await client.PatchAsJsonAsync("/api/devices/known/OFFLINE-UDID", new
            {
                displayName = "Shelf iPhone 2",
                notes = "USB drawer",
            });
            Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            var devices = await client.GetFromJsonAsync<IReadOnlyList<KnownDeviceDto>>("/api/devices/known?includeReachable=false");

            Assert.NotNull(devices);
            KnownDeviceDto device = Assert.Single(devices!);
            Assert.Equal("OFFLINE-UDID", device.udid);
            Assert.Equal("Shelf iPhone 2", device.displayName);
            Assert.Equal("lab", device.owner);
            Assert.Equal("USB drawer", device.notes);
            Assert.Equal("offline", device.connection);
            Assert.Equal("manual", device.lastSeenSource);
        }
    }

    [Fact]
    public async Task Workspace_WithBearerToken_ReturnsCapabilityContractWithoutUserAdmin()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var workspace = await client.GetFromJsonAsync<WorkspaceDto>("/api/workspace");

        Assert.NotNull(workspace);
        Assert.Equal("Sideport workspace", workspace!.name);
        Assert.Equal("bearer-token", workspace.authMode);
        Assert.True(workspace.authDelegated);
        Assert.Equal("advisory", workspace.roleEnforcement);
        Assert.False(workspace.supportsUserAdministration);
        Assert.Equal("api-token-client", workspace.currentMember.id);
        Assert.Equal("owner", workspace.currentMember.role);
        Assert.True(workspace.capabilities["operations.cancel.queued"]);
        Assert.False(workspace.capabilities["operations.cancel.running"]);
        Assert.True(workspace.capabilities["operations.retry"]);
        Assert.True(workspace.capabilities["operations.rerun"]);
        Assert.False(workspace.capabilities["users.invite"]);
        Assert.Contains(workspace.roles, role => role.id == "owner" && role.capabilities.Contains("catalog.import"));
    }

    [Fact]
    public async Task Workspace_WithoutTokenConfigured_ReportsOpenProxyMode()
    {
        using var factory = Factory(apiToken: null);
        using HttpClient client = factory.CreateClient();

        var workspace = await client.GetFromJsonAsync<WorkspaceDto>("/api/workspace");

        Assert.NotNull(workspace);
        Assert.Equal("open-behind-proxy", workspace!.authMode);
        Assert.True(workspace.authDelegated);
        Assert.False(workspace.supportsUserAdministration);
        Assert.Equal("api-token-client", workspace.currentMember.id);
    }

    [Fact]
    public async Task KnownDevices_DeleteBlocksWhenRegistrationsExist()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/devices/known", new { udid = "TEST-UDID", displayName = "Test iPhone" })).StatusCode);

        HttpResponseMessage response = await client.DeleteAsync("/api/devices/known/TEST-UDID");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<KnownDeviceErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("device-has-registrations", error!.error);
        Assert.Equal(1, error.registrationCount);
    }

    [Fact]
    public async Task KnownDevices_CorruptHistory_ReturnsStructuredError()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        Directory.CreateDirectory(stateDir);
        await File.WriteAllTextAsync(Path.Combine(stateDir, "known-devices.json"), "{not-json");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/devices/known");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<KnownDeviceErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("known-device-store-unavailable", error!.error);
    }

        [Fact]
        public async Task KnownDevices_UpdateSaveFailure_RollsBackInMemoryRecord()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-1);
                string storePath = Path.Combine(stateDir, "known-devices.json");
                await File.WriteAllTextAsync(storePath, $$"""
                [
                    {
                        "udid": "OFFLINE-UDID",
                        "displayName": "Original",
                        "productType": null,
                        "osVersion": null,
                        "connection": "unknown",
                        "firstSeenAt": "{{old:O}}",
                        "lastSeenAt": null,
                        "lastSeenSource": "manual",
                        "currentPollAt": null,
                        "trustState": "unknown",
                        "owner": null,
                        "notes": null,
                        "updatedAt": "{{old:O}}"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                Assert.NotNull(await client.GetFromJsonAsync<IReadOnlyList<KnownDeviceDto>>("/api/devices/known?includeReachable=false"));
                File.Delete(storePath);
                Directory.CreateDirectory(storePath);

                HttpResponseMessage failedPatch = await client.PatchAsJsonAsync("/api/devices/known/OFFLINE-UDID", new { displayName = "Mutated" });
                var devices = await client.GetFromJsonAsync<IReadOnlyList<KnownDeviceDto>>("/api/devices/known?includeReachable=false");

                Assert.Equal(HttpStatusCode.ServiceUnavailable, failedPatch.StatusCode);
                Assert.NotNull(devices);
                KnownDeviceDto device = Assert.Single(devices!);
                Assert.Equal("Original", device.displayName);
        }

    [Fact]
    public async Task OperationPreflight_MissingRegistration_ReturnsBlockedContract()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/preflight", new
        {
            type = "refresh",
            deviceUdid = "TEST-UDID",
            bundleId = "ro.hont.certcountdown",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preflight = await response.Content.ReadFromJsonAsync<OperationPreflightDto>();
        Assert.NotNull(preflight);
        Assert.False(preflight!.ready);
        Assert.Contains(preflight.blockers, blocker => blocker.code == "registration-missing");
        Assert.True(preflight.requiresConfirmation);
    }

    [Fact]
    public async Task OperationPreflight_RegisteredApp_ReturnsPlannedMutationsAndLimits()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);

        HttpResponseMessage preflightResponse = await client.PostAsJsonAsync("/api/operations/preflight", new
        {
            type = "refresh",
            deviceUdid = "TEST-UDID",
            bundleId = "ro.hont.certcountdown",
        });
        var preflight = await preflightResponse.Content.ReadFromJsonAsync<OperationPreflightDto>();

        Assert.NotNull(preflight);
        Assert.True(preflight!.ready);
        Assert.Empty(preflight.blockers);
        Assert.Contains(preflight.plannedMutations, mutation => mutation.Contains("Re-sign IPA", StringComparison.Ordinal));
        Assert.Contains(preflight.scarceLimits, limit => limit.code == "free-device-app-slots" && limit.used == 1 && limit.limit == 3);
    }

    [Fact]
    public async Task RefreshOperation_MissingRegistration_RecordsBlockedOperation()
    {
        using var factory = Factory(apiToken: "s3cr3t-token", personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/refresh", new
        {
            deviceUdid = "TEST-UDID",
            bundleId = "ro.hont.certcountdown",
            idempotencyKey = "blocked-1",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var operation = await response.Content.ReadFromJsonAsync<OperationRecordDto>();
        Assert.NotNull(operation);
        Assert.Equal("blocked", operation!.status);
        Assert.Equal("registration-missing", operation.error!.code);
        Assert.Contains(operation.stages, stage => stage.id == "preflight" && stage.status == "blocked");
    }

    [Fact]
    public async Task RefreshOperation_RegisteredApp_RecordsSucceededOperationAndRenewal()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token", personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/refresh", new
        {
            deviceUdid = "TEST-UDID",
            bundleId = "ro.hont.certcountdown",
            idempotencyKey = "refresh-1",
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var submitted = await response.Content.ReadFromJsonAsync<OperationRecordDto>();
        Assert.NotNull(submitted);
        Assert.Equal("queued", submitted!.status);
        var operation = await WaitForTerminalOperationAsync(client, submitted.operationId);
        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.status);
        Assert.Equal("api-token", operation.actor.kind);
        Assert.Equal("refresh-1", operation.idempotencyKey);
        Assert.Contains(operation.stages, stage => stage.id == "preflight" && stage.status == "succeeded");
        Assert.Contains(operation.stages, stage => stage.id == "refresh" && stage.status == "succeeded");
        Assert.True(operation.result!.success);

        var renewals = await client.GetFromJsonAsync<IReadOnlyList<RenewalDto>>("/api/renewals");
        Assert.NotNull(renewals);
        RenewalDto renewal = Assert.Single(renewals!);
        Assert.Equal(operation.operationId, renewal.operationId);
        Assert.Equal("idle", renewal.status);
    }

    [Fact]
    public async Task RefreshOperation_IdempotencyKey_ReturnsExistingRecordWithoutSecondRun()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
        var request = new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = "same-key" };

        var first = await (await client.PostAsJsonAsync("/api/operations/refresh", request)).Content.ReadFromJsonAsync<OperationRecordDto>();
        HttpResponseMessage secondResponse = await client.PostAsJsonAsync("/api/operations/refresh", request);
        var second = await secondResponse.Content.ReadFromJsonAsync<OperationRecordDto>();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(first!.operationId, second!.operationId);
        var operations = await client.GetFromJsonAsync<IReadOnlyList<OperationRecordDto>>("/api/operations");
        Assert.Single(operations!);
    }

    [Fact]
    public async Task RefreshOperation_ConcurrentSameIdempotencyKey_RecordsOneOperation()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        using var factory = Factory(apiToken: "s3cr3t-token", personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
        var request = new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = "same-concurrent-key" };

        Task<OperationRecordDto?> firstTask = PostOperationAsync(client, request);
        Task<OperationRecordDto?> secondTask = PostOperationAsync(client, request);
        OperationRecordDto?[] records = await Task.WhenAll(firstTask, secondTask);

        Assert.All(records, Assert.NotNull);
        Assert.Equal(records[0]!.operationId, records[1]!.operationId);
        var operations = await client.GetFromJsonAsync<IReadOnlyList<OperationRecordDto>>("/api/operations");
        Assert.Single(operations!);
    }

    [Fact]
    public async Task RefreshOperation_WhenOperationStoreCannotSave_ReturnsStructuredErrorBeforeRefresh()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");
        File.WriteAllText(stateDir, "not a directory");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/refresh", new
        {
            deviceUdid = "TEST-UDID",
            bundleId = "ro.hont.certcountdown",
            idempotencyKey = "store-fails",
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("operation-store-unavailable", error!.error);
    }

        [Fact]
        public async Task OperationCancel_QueuedOperation_CancelsWithoutRunning()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                string now = DateTimeOffset.UtcNow.ToString("O");
                await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), $$"""
                [
                    {
                        "operationId": "op_queued_cancel",
                        "type": "refresh",
                        "status": "queued",
                        "createdAt": "{{now}}",
                        "startedAt": null,
                        "updatedAt": "{{now}}",
                        "completedAt": null,
                        "actor": { "kind": "api-token", "displayName": "api-token-client" },
                        "idempotencyKey": null,
                        "attempt": 1,
                        "target": { "deviceUdid": "TEST-UDID", "bundleId": "ro.hont.certcountdown" },
                        "stages": [
                            { "id": "preflight", "label": "Preflight", "status": "succeeded", "startedAt": "{{now}}", "completedAt": "{{now}}", "message": "Ready to refresh.", "error": null },
                            { "id": "refresh", "label": "Sign and install", "status": "pending", "startedAt": null, "completedAt": null, "message": "Waiting for the single-flight signer.", "error": null }
                        ],
                        "result": null,
                        "error": null,
                        "cancelable": true,
                        "retryable": false,
                        "rerunnable": false,
                        "correlationId": "op_queued_cancel",
                        "parentOperationId": null,
                        "source": "live"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, operationWorker: false);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/op_queued_cancel/cancel", new { reason = "test cancel" });
                using JsonDocument operation = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

                Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
                Assert.Equal("canceled", operation.RootElement.GetProperty("status").GetString());
                Assert.Equal("operation-canceled", operation.RootElement.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task OperationRetry_FailedOperation_CreatesChildAttempt()
        {
                string dir = TestDir();
                string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
                string stateDir = Path.Combine(dir, "state");
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
                var failed = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = "retry-source" })).Content.ReadFromJsonAsync<OperationRecordDto>();
                failed = await WaitForTerminalOperationAsync(client, failed!.operationId);
                Assert.Equal("failed", failed.status);

                HttpResponseMessage response = await client.PostAsJsonAsync($"/api/operations/{failed.operationId}/retry", new { idempotencyKey = "retry-child" });
                var child = await response.Content.ReadFromJsonAsync<OperationRecordDto>();

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.NotNull(child);
                Assert.Equal(failed.operationId, child!.parentOperationId);
        }

        [Fact]
        public async Task OperationRerun_SucceededOperation_CreatesChildOperation()
        {
                string dir = TestDir();
                string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
                string stateDir = Path.Combine(dir, "state");
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
                var succeeded = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = "rerun-source" })).Content.ReadFromJsonAsync<OperationRecordDto>();
                succeeded = await WaitForTerminalOperationAsync(client, succeeded!.operationId);
                Assert.Equal("succeeded", succeeded.status);

                HttpResponseMessage response = await client.PostAsJsonAsync($"/api/operations/{succeeded.operationId}/rerun", new { idempotencyKey = "rerun-child" });
                var child = await response.Content.ReadFromJsonAsync<OperationRecordDto>();

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.NotNull(child);
                Assert.Equal(succeeded.operationId, child!.parentOperationId);
        }

            [Fact]
            public async Task Scheduler_EnqueuesRefreshAsSystemOperation()
            {
                string dir = TestDir();
                string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
                using var factory = Factory(apiToken: "s3cr3t-token", personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
                var scheduler = new OperationScheduler(
                    factory.Services.GetRequiredService<IAppRegistry>(),
                    factory.Services.GetRequiredService<RefreshOrchestrator>(),
                    factory.Services.GetRequiredService<OperationService>(),
                    factory.Services.GetRequiredService<OrchestratorOptions>());

                await scheduler.RunOnceAsync(CancellationToken.None);
                var operations = await client.GetFromJsonAsync<IReadOnlyList<OperationRecordDto>>("/api/operations");

                Assert.NotNull(operations);
                OperationRecordDto operation = Assert.Single(operations!);
                Assert.Equal("system", operation.actor.kind);
                Assert.Equal("system:scheduler", operation.actor.displayName);
                await WaitForTerminalOperationAsync(client, operation.operationId);
            }

        [Fact]
        public async Task Operations_QueuedRecordAfterRestart_RequeuesToTerminalState()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                string now = DateTimeOffset.UtcNow.ToString("O");
                await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), $$"""
                [
                    {
                        "operationId": "op_restart_queued",
                        "type": "refresh",
                        "status": "queued",
                        "createdAt": "{{now}}",
                        "startedAt": null,
                        "updatedAt": "{{now}}",
                        "completedAt": null,
                        "actor": { "kind": "api-token", "displayName": "api-token-client" },
                        "idempotencyKey": null,
                        "attempt": 1,
                        "target": { "deviceUdid": "TEST-UDID", "bundleId": "ro.hont.missing" },
                        "stages": [
                            { "id": "preflight", "label": "Preflight", "status": "succeeded", "startedAt": "{{now}}", "completedAt": "{{now}}", "message": "Ready to refresh.", "error": null },
                            { "id": "refresh", "label": "Sign and install", "status": "pending", "startedAt": null, "completedAt": null, "message": "Waiting for the single-flight signer.", "error": null }
                        ],
                        "result": null,
                        "error": null,
                        "cancelable": true,
                        "retryable": false,
                        "rerunnable": false,
                        "correlationId": "op_restart_queued",
                        "parentOperationId": null,
                        "source": "live"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                OperationRecordDto operation = await WaitForTerminalOperationAsync(client, "op_restart_queued");

                Assert.Equal("failed", operation.status);
                Assert.Equal("refresh-failed", operation.error!.code);
        }

            [Fact]
            public async Task DiagnosticIssues_GroupFailedOperationsAndPersistTriageState()
            {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");

                using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
                using (HttpClient client = factory.CreateClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                    Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
                    var operation = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown" })).Content.ReadFromJsonAsync<OperationRecordDto>();
                    operation = await WaitForTerminalOperationAsync(client, operation!.operationId);
                    Assert.Equal("failed", operation.status);

                    var issues = await client.GetFromJsonAsync<IReadOnlyList<DiagnosticIssueDto>>("/api/diagnostics/issues");
                    DiagnosticIssueDto issue = Assert.Single(issues!);
                    Assert.Equal("refresh-failed", issue.category);
                    Assert.Equal("unresolved", issue.status);
                    Assert.Equal(operation.operationId, issue.lastOperationId);
                    Assert.NotEmpty(issue.evidence);

                    HttpResponseMessage patched = await client.PatchAsJsonAsync($"/api/diagnostics/issues/{issue.issueId}", new { status = "investigating", note = "checking 2FA" });
                    Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
                }

                using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
                using (HttpClient client = factory.CreateClient())
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                    var issues = await client.GetFromJsonAsync<IReadOnlyList<DiagnosticIssueDto>>("/api/diagnostics/issues");
                    DiagnosticIssueDto issue = Assert.Single(issues!);
                    Assert.Equal("investigating", issue.status);
                }
            }

            [Fact]
            public async Task DiagnosticIssues_CorruptState_ReturnsStructuredError()
            {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                await File.WriteAllTextAsync(Path.Combine(stateDir, "diagnostic-issues.json"), "{not-json");
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                HttpResponseMessage response = await client.GetAsync("/api/diagnostics/issues");

                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<DiagnosticIssueErrorDto>();
                Assert.NotNull(error);
                Assert.Equal("diagnostics-store-unavailable", error!.error);
            }

        [Fact]
        public async Task DiagnosticIssues_RedactSecretLikeEvidenceMessages()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                string now = DateTimeOffset.UtcNow.ToString("O");
                await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), $$"""
                [
                    {
                        "operationId": "op_redact",
                        "type": "refresh",
                        "status": "failed",
                        "createdAt": "{{now}}",
                        "startedAt": "{{now}}",
                        "updatedAt": "{{now}}",
                        "completedAt": "{{now}}",
                        "actor": { "kind": "api-token", "displayName": "api-token-client" },
                        "idempotencyKey": null,
                        "attempt": 1,
                        "target": { "deviceUdid": "TEST-UDID", "bundleId": "ro.hont.certcountdown" },
                        "stages": [
                            { "id": "refresh", "label": "Sign and install", "status": "failed", "startedAt": "{{now}}", "completedAt": "{{now}}", "message": "failed", "error": { "code": "refresh-failed", "message": "token=abc123 password=hunter2 /tmp/secret.ipa dragos@example.com", "source": "live", "detail": null } }
                        ],
                        "result": null,
                        "error": { "code": "refresh-failed", "message": "token=abc123 password=hunter2 /tmp/secret.ipa dragos@example.com", "source": "live", "detail": null },
                        "cancelable": false,
                        "retryable": true,
                        "rerunnable": false,
                        "correlationId": "op_redact",
                        "parentOperationId": null,
                        "source": "live"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                var issues = await client.GetFromJsonAsync<IReadOnlyList<DiagnosticIssueDto>>("/api/diagnostics/issues");

                DiagnosticIssueDto issue = Assert.Single(issues!);
                string message = Assert.Single(issue.evidence).message;
                Assert.DoesNotContain("abc123", message);
                Assert.DoesNotContain("hunter2", message);
                Assert.DoesNotContain("dragos@example.com", message);
                Assert.Contains("[redacted", message);
        }

        [Fact]
        public async Task DiagnosticIssues_ResolvedIssue_ReopensWhenNewerFailureArrives()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-2);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                string issueId = "issue-refresh-failed-test-udid-ro-hont-certcountdown";
                await File.WriteAllTextAsync(Path.Combine(stateDir, "diagnostic-issues.json"), $$"""
                [{ "issueId": "{{issueId}}", "status": "resolved", "note": "fixed", "updatedAt": "{{old:O}}" }]
                """);
                await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), $$"""
                [
                    {
                        "operationId": "op_reopen",
                        "type": "refresh",
                        "status": "failed",
                        "createdAt": "{{now:O}}",
                        "startedAt": "{{now:O}}",
                        "updatedAt": "{{now:O}}",
                        "completedAt": "{{now:O}}",
                        "actor": { "kind": "api-token", "displayName": "api-token-client" },
                        "idempotencyKey": null,
                        "attempt": 1,
                        "target": { "deviceUdid": "TEST-UDID", "bundleId": "ro.hont.certcountdown" },
                        "stages": [],
                        "result": null,
                        "error": { "code": "refresh-failed", "message": "failed again", "source": "live", "detail": null },
                        "cancelable": false,
                        "retryable": true,
                        "rerunnable": false,
                        "correlationId": "op_reopen",
                        "parentOperationId": null,
                        "source": "live"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                var issues = await client.GetFromJsonAsync<IReadOnlyList<DiagnosticIssueDto>>("/api/diagnostics/issues");

                DiagnosticIssueDto issue = Assert.Single(issues!);
                Assert.Equal(issueId, issue.issueId);
                Assert.Equal("unresolved", issue.status);
        }

    [Fact]
    public async Task Operations_AreDurableAcrossApiRestart()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");
        string operationId;

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal()))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
            var operation = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown" })).Content.ReadFromJsonAsync<OperationRecordDto>();
            operationId = operation!.operationId;
            await WaitForTerminalOperationAsync(client, operationId);
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            var operation = await client.GetFromJsonAsync<OperationRecordDto>($"/api/operations/{operationId}");
            Assert.NotNull(operation);
            Assert.Equal(operationId, operation!.operationId);
        }
    }

    [Fact]
    public async Task Renewals_RecoverExpiryFromDurableOperationAcrossApiRestart()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");
        string operationId;
        DateTimeOffset expiresAt;

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal()))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
            var operation = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown" })).Content.ReadFromJsonAsync<OperationRecordDto>();
            operation = await WaitForTerminalOperationAsync(client, operation!.operationId);

            Assert.NotNull(operation?.result?.expiresAt);
            operationId = operation!.operationId;
            expiresAt = operation.result!.expiresAt!.Value;
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            var renewals = await client.GetFromJsonAsync<IReadOnlyList<RenewalDto>>("/api/renewals");

            Assert.NotNull(renewals);
            RenewalDto renewal = Assert.Single(renewals!);
            Assert.Equal(operationId, renewal.operationId);
            Assert.Equal(expiresAt, renewal.expiresAt);
            Assert.Equal("healthy", renewal.risk);
            Assert.Equal("idle", renewal.status);
        }
    }

    [Fact]
    public async Task Renewals_KeepDurableExpiryWhenNewerOperationFailsAfterRestart()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
        string stateDir = Path.Combine(dir, "state");
        DateTimeOffset successfulExpiry;
        string failedOperationId;

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal()))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
            var operation = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = "initial-success" })).Content.ReadFromJsonAsync<OperationRecordDto>();
            operation = await WaitForTerminalOperationAsync(client, operation!.operationId);

            Assert.NotNull(operation?.result?.expiresAt);
            successfulExpiry = operation!.result!.expiresAt!.Value;
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            OperationRecordDto? operation = null;
            for (int i = 0; i < 101; i++)
            {
                operation = await (await client.PostAsJsonAsync("/api/operations/refresh", new { deviceUdid = "TEST-UDID", bundleId = "ro.hont.certcountdown", idempotencyKey = $"newer-failure-{i}" })).Content.ReadFromJsonAsync<OperationRecordDto>();
                operation = await WaitForTerminalOperationAsync(client, operation!.operationId);
                Assert.NotNull(operation);
                Assert.Equal("failed", operation!.status);
            }
            failedOperationId = operation!.operationId;
        }

        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            var renewals = await client.GetFromJsonAsync<IReadOnlyList<RenewalDto>>("/api/renewals");

            Assert.NotNull(renewals);
            RenewalDto renewal = Assert.Single(renewals!);
            Assert.Equal(failedOperationId, renewal.operationId);
            Assert.Equal(successfulExpiry, renewal.expiresAt);
            Assert.Equal("blocked", renewal.risk);
            Assert.Equal("failed", renewal.status);
        }
    }

    [Fact]
    public async Task Operations_CorruptHistory_ReturnsStructuredError()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        Directory.CreateDirectory(stateDir);
        await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), "{not-json");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/operations");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();
        Assert.NotNull(error);
        Assert.Equal("operation-store-unavailable", error!.error);
    }

        [Fact]
        public async Task Operations_StaleRunningRecord_ReconcilesToUnknownTerminalFailure()
        {
                string dir = TestDir();
                string stateDir = Path.Combine(dir, "state");
                Directory.CreateDirectory(stateDir);
                string old = DateTimeOffset.UtcNow.AddHours(-2).ToString("O");
                await File.WriteAllTextAsync(Path.Combine(stateDir, "operations.json"), $$"""
                [
                    {
                        "operationId": "op_stale_running",
                        "type": "refresh",
                        "status": "running",
                        "createdAt": "{{old}}",
                        "startedAt": "{{old}}",
                        "updatedAt": "{{old}}",
                        "completedAt": null,
                        "actor": { "kind": "api-token", "displayName": "api-token-client" },
                        "idempotencyKey": null,
                        "attempt": 1,
                        "target": { "deviceUdid": "TEST-UDID", "bundleId": "ro.hont.certcountdown" },
                        "stages": [
                            { "id": "refresh", "label": "Sign and install", "status": "running", "startedAt": "{{old}}", "completedAt": null, "message": "Refresh is running.", "error": null }
                        ],
                        "result": null,
                        "error": null,
                        "cancelable": false,
                        "retryable": false,
                        "rerunnable": false,
                        "correlationId": "op_stale_running",
                        "source": "live"
                    }
                ]
                """);
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

                var operation = await client.GetFromJsonAsync<OperationRecordDto>("/api/operations/op_stale_running");

                Assert.NotNull(operation);
                Assert.Equal("failed", operation!.status);
                Assert.Equal("operation-terminal-state-unknown", operation.error!.code);
        }

    private sealed record ReadyDto(bool ready);
    private sealed record OnboardingDto(bool firstRunComplete, IReadOnlyList<OnboardingStepDto> steps);
    private sealed record OnboardingStepDto(string id, string state, string surface);
    private sealed record LogDto(string at, string level, string category, string message);
    private sealed record CatalogAppDto(string id, string status, string bundleId, string? shortVersion, bool hasEmbeddedProfile, string? sha256);
    private sealed record CatalogUploadErrorDto(string error, string? message, string? detail, long? limit, string? id);
    private sealed record RegisteredAppDto(string bundleId, string deviceUdid);
    private sealed record AppleAccessStatusDto(string state, string? keyIdSuffix, string? issuerIdSuffix, IReadOnlyList<AppleAccessCapabilityDto> capabilities);
    private sealed record AppleAccessCapabilityDto(string id, string state, int? httpStatus, int? count);
    private sealed record PersonalAppleStatusDto(string state, string? appleIdHint, string? pendingChallengeId, IReadOnlyList<PersonalAppleTeamDto> teams);
    private sealed record PersonalAppleTeamDto(string teamId, string name, string type);
    private sealed record OperationPreflightDto(bool ready, IReadOnlyList<OperationIssueDto> blockers, IReadOnlyList<string> plannedMutations, IReadOnlyList<OperationLimitDto> scarceLimits, bool requiresConfirmation);
    private sealed class OperationIssueDto
    {
        public string code { get; set; } = "";
        public string message { get; set; } = "";
        public string? source { get; set; }
        public string? detail { get; set; }
    }
    private sealed record OperationLimitDto(string code, int used, int limit);
    private sealed record OperationActorDto(string kind, string displayName);
    private sealed record OperationStageDto(string id, string status);
    private sealed record OperationResultDto(bool success, string bundleId, DateTimeOffset? expiresAt, string? error);
    private sealed record OperationRecordDto(string operationId, string status, OperationActorDto actor, string? idempotencyKey, IReadOnlyList<OperationStageDto> stages, OperationResultDto? result, OperationIssueDto? error, string? parentOperationId = null);
    private sealed record RenewalDto(string id, string status, string risk, DateTimeOffset? expiresAt, string? operationId);
    private sealed record OperationErrorDto(string error, string message, string? detail);
    private sealed record KnownDeviceHealthDto(string state, string reason, string source, DateTimeOffset checkedAt, string? nextAction);
    private sealed record KnownDeviceDto(string udid, string displayName, string connection, DateTimeOffset? lastSeenAt, string lastSeenSource, DateTimeOffset? currentPollAt, KnownDeviceHealthDto health, string? owner, string? notes);
    private sealed record KnownDeviceErrorDto(string error, string message, string? detail, int? registrationCount);
    private sealed record WorkspaceMemberDto(string id, string name, string? email, string role, string status);
    private sealed record WorkspaceRoleDto(string id, string label, IReadOnlyList<string> capabilities);
    private sealed record WorkspaceDto(string name, string authMode, bool authDelegated, string roleEnforcement, bool supportsUserAdministration, WorkspaceMemberDto currentMember, IReadOnlyList<WorkspaceRoleDto> roles, IReadOnlyDictionary<string, bool> capabilities);
    private sealed record DiagnosticEvidenceDto(string type, string label, string message, string source, string? operationId, string? stageId);
    private sealed record DiagnosticIssueDto(string issueId, string category, string severity, string status, DateTimeOffset firstSeenAt, DateTimeOffset lastSeenAt, int occurrenceCount, string? lastOperationId, string correlationId, IReadOnlyList<DiagnosticEvidenceDto> evidence, string remediation);
    private sealed record DiagnosticIssueErrorDto(string error, string message, string? detail);

    private static object Registration(string bundleId, string ipaPath) => new
    {
        bundleId,
        appleId = "developer@example.com",
        teamId = "TEAMID1234",
        deviceUdid = "TEST-UDID",
        inputIpaPath = ipaPath,
    };

    private static async Task<OperationRecordDto?> PostOperationAsync(HttpClient client, object request) =>
        await (await client.PostAsJsonAsync("/api/operations/refresh", request)).Content.ReadFromJsonAsync<OperationRecordDto>();

    private static async Task<OperationRecordDto> WaitForTerminalOperationAsync(HttpClient client, string operationId)
    {
        for (int i = 0; i < 100; i++)
        {
            OperationRecordDto? operation = await client.GetFromJsonAsync<OperationRecordDto>($"/api/operations/{operationId}");
            Assert.NotNull(operation);
            if (operation!.status is "blocked" or "succeeded" or "failed" or "canceled")
                return operation;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Operation {operationId} did not reach a terminal state.");
    }

    private static MultipartFormDataContent UploadContent(string path, string? id = null, string? name = null, string? purpose = null, bool replace = false)
    {
        var content = new MultipartFormDataContent();
        content.Add(new StreamContent(File.OpenRead(path)), "ipa", Path.GetFileName(path));
        if (id is not null) content.Add(new StringContent(id), "id");
        if (name is not null) content.Add(new StringContent(name), "name");
        if (purpose is not null) content.Add(new StringContent(purpose), "purpose");
        if (replace) content.Add(new StringContent("true"), "replace");
        return content;
    }

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

    private sealed class StubSigningIdentityProvider : ISigningIdentityProvider
    {
        public Task<PreparedSigningInputs> PrepareAsync(AppleSession session, string teamId, string bundleId, string deviceUdid, CancellationToken ct = default)
        {
            string dir = TestDir();
            string p12 = Path.Combine(dir, "identity.p12");
            string profile = Path.Combine(dir, "profile.mobileprovision");
            File.WriteAllBytes(p12, [1, 2, 3]);
            File.WriteAllBytes(profile, [4, 5, 6]);
            return Task.FromResult(new PreparedSigningInputs(p12, "", profile, DateTimeOffset.UtcNow.AddDays(7)));
        }
    }

    private sealed class StubSigner : ISigner
    {
        public Task<SignResult> SignAsync(SignRequest request, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputIpaPath)!);
            File.Copy(request.InputIpaPath, request.OutputIpaPath, overwrite: true);
            return Task.FromResult(new SignResult(true, request.OutputIpaPath, null, null));
        }
    }

    private sealed class StubDeviceController : IDeviceController
    {
        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo("TEST-UDID", "Test iPhone", "iPhone15,2", "17.5", DeviceConnection.Usb)]);

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", [new DeviceCheck("usbmux", "usbmux", "ok", "ok")]));
    }
}
