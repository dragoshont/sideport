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
using Sideport.Api.DeviceInventory;
using Sideport.Api.Onboarding;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Packaging;
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
        bool oidc = false,
        IDeviceController? deviceController = null,
        DeviceEnrollmentOptions? enrollmentOptions = null,
        bool schedulerEnabled = false,
        ISigningIdentityProvider? signingIdentityProvider = null)
    {
        signerPath ??= File.Exists("/usr/bin/true")
            ? "/usr/bin/true"
            : Environment.ProcessPath ?? typeof(ApiSmokeTests).Assembly.Location;
        stateDirectory ??= Path.Combine(Path.GetTempPath(), "sideport-api-tests", Guid.NewGuid().ToString("N"));

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Scheduler:Enabled", schedulerEnabled ? "true" : "false");
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
                if (enrollmentOptions is not null)
                {
                    services.RemoveAll<DeviceEnrollmentOptions>();
                    services.AddSingleton(enrollmentOptions);
                }
                if (!operationWorker)
                    services.RemoveAll<IHostedService>();
                services.AddSingleton<ISigningIdentityProvider>(
                    signingIdentityProvider ?? new StubSigningIdentityProvider());
                services.AddSingleton<ISigner, StubSigner>();
                services.AddSingleton<IDeviceController>(deviceController ?? new StubDeviceController());
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
    public async Task Readyz_AnisetteDown_RemainsAvailableForRepairUi()
    {
        using var factory = Factory(anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ReadyDto>();
        Assert.True(body!.ready);
    }

    [Fact]
    public async Task Readyz_SignerMissing_RemainsAvailableForRepairUi()
    {
        using var factory = Factory(signerPath: "/nonexistent/zsign");
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/readyz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SystemStatus_AnisetteDown_ReportsOperationalFailure()
    {
        using var factory = Factory(apiToken: "s3cr3t-token", anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        SystemStatusResponse body = (await client.GetFromJsonAsync<SystemStatusResponse>("/api/system/status"))!;

        Assert.False(body.operational);
        Assert.Contains(body.checks, check => check.id == "anisette-headers" && check.status == "fail");
    }

    [Fact]
    public async Task SchedulerStatus_ProjectsDurablePolicy_AndRejectsUnsafeEnable()
    {
        using var factory = Factory(apiToken: "s3cr3t-token", schedulerEnabled: false);
        using HttpClient client = HttpsTokenClient(factory);

        SchedulerStatusResponse status = (await client.GetFromJsonAsync<SchedulerStatusResponse>(
            "/api/scheduler/status"))!;
        Assert.False(status.enabled);
        Assert.Equal("due-only", status.policy.mode);
        Assert.Equal(TimeSpan.FromHours(1), status.policy.evaluationInterval);
        Assert.Equal(100, status.historyRetention.maxEvaluations);
        Assert.Equal("idle", status.concurrency.lockState);

        HttpResponseMessage rejected = await client.PutAsJsonAsync(
            "/api/scheduler/settings",
            new { enabled = true });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        OperationErrorDto error = (await rejected.Content.ReadFromJsonAsync<OperationErrorDto>())!;
        Assert.Equal("scheduler-prerequisites-not-met", error.error);

        HttpResponseMessage noOp = await client.PutAsJsonAsync(
            "/api/scheduler/settings",
            new { enabled = false });
        Assert.Equal(HttpStatusCode.OK, noOp.StatusCode);
        Assert.False(((await noOp.Content.ReadFromJsonAsync<SchedulerStatusResponse>())!).enabled);
    }

    [Fact]
    public async Task SchedulerSettings_EnableRequiresAndAcceptsVerifiedSignerDeviceLineage()
    {
        string dir = TestDir();
        const string bundleId = "com.example.schedulerready";
        string ipaPath = WriteTestIpa(dir, bundleId, "Scheduler Ready", "1", "1.0");
        var portal = new StubApplePortal();
        portal.Certificates.Add(new AppleDevelopmentCertificate(
            "cert_sideport",
            "A1B2C3D4",
            DateTimeOffset.UtcNow.AddMonths(6)));
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: portal,
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: false,
            signingIdentityProvider: new ReusableSigningIdentityProvider("C3D4"));
        using HttpClient client = HttpsTokenClient(factory);
        string profile = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            profile,
            finishOnboarding: false,
            idempotencyKey: "scheduler-ready-install");
        HttpResponseMessage queued = await client.PostAsJsonAsync("/api/operations/install", request);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        OperationRecordDto operation = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        Assert.Equal("succeeded", (await WaitForTerminalOperationAsync(client, operation.operationId)).status);

        HttpResponseMessage enabled = await client.PutAsJsonAsync(
            "/api/scheduler/settings",
            new { enabled = true });

        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        SchedulerStatusResponse status = (await enabled.Content.ReadFromJsonAsync<SchedulerStatusResponse>())!;
        Assert.True(status.enabled);
        Assert.NotNull(status.nextEvaluationAt);
    }

    [Fact]
    public async Task VerifyExistingRegistration_QueuesReadOnlyMigration_AndReplaysWithoutAnotherDeviceRead()
    {
        string dir = TestDir();
        const string bundleId = "com.example.legacyverify";
        string ipaPath = WriteTestIpa(dir, bundleId, "Legacy Verify", "1", "1.0");
        var controller = new ExistingInstallDeviceController("TEST-UDID", bundleId, "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath));
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        HttpResponseMessage queued = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = "verify-existing-once" });

        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        OperationRecordDto initial = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, initial.operationId);
        Assert.Equal("verify-existing-registration", terminal.type);
        Assert.Equal("succeeded", terminal.status);
        Assert.Equal("1.0", terminal.result?.version);
        Assert.NotNull(terminal.result?.expiresAt);
        Assert.Equal(0, controller.InstallCalls);
        Assert.Equal(1, controller.InstalledAppReads);

        AppRegistration registration = (await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!;
        Assert.Equal(initial.operationId, registration.LastVerifiedOperationId);
        Assert.Equal("active", registration.Lifecycle);

        HttpResponseMessage replay = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = "verify-existing-once" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(
            initial.operationId,
            ((await replay.Content.ReadFromJsonAsync<OperationRecordDto>())!).operationId);
        Assert.Equal(1, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task VerifyExistingRegistration_RejectsPendingOrUnacceptedRegistrationBeforeQueueing()
    {
        string dir = TestDir();
        const string bundleId = "com.example.verificationblocked";
        string ipaPath = WriteTestIpa(dir, bundleId, "Verification Blocked", "1", "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: new ExistingInstallDeviceController("TEST-UDID", bundleId, "1.0"));
        using HttpClient client = HttpsTokenClient(factory);
        IAppRegistry registry = factory.Services.GetRequiredService<IAppRegistry>();
        await registry.UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath,
            Lifecycle: "pending-install"));

        HttpResponseMessage pending = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = "verify-pending" });
        Assert.Equal(HttpStatusCode.Conflict, pending.StatusCode);
        Assert.Equal(
            "registration-pending-install",
            ((await pending.Content.ReadFromJsonAsync<OperationErrorDto>())!).error);

        await registry.UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath));
        HttpResponseMessage unaccepted = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = "verify-unaccepted" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, unaccepted.StatusCode);
        Assert.Equal(
            "device-not-accepted",
            ((await unaccepted.Content.ReadFromJsonAsync<OperationErrorDto>())!).error);
        Assert.Empty(await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null));
    }

    [Theory]
    [InlineData(true, "device-not-reachable")]
    [InlineData(false, "device-trust-check-unavailable")]
    public async Task VerifyExistingRegistration_DeviceProbeFailuresReturnStructuredErrors(
        bool failEnumeration,
        string expectedCode)
    {
        string dir = TestDir();
        const string bundleId = "com.example.probefailure";
        string ipaPath = WriteTestIpa(dir, bundleId, "Probe Failure", "1", "1.0");
        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            listException: failEnumeration ? new IOException("usbmux failed") : null,
            trustException: failEnumeration ? null : new IOException("lockdown failed"));
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath));
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = $"probe-{failEnumeration}" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expectedCode, ((await response.Content.ReadFromJsonAsync<OperationErrorDto>())!).error);
        Assert.Empty(await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null));
    }

    [Fact]
    public async Task VerifyExistingRegistration_VersionMismatchOffersInstallWithoutMutatingOrInstalling()
    {
        string dir = TestDir();
        const string bundleId = "com.example.verificationmismatch";
        string ipaPath = WriteTestIpa(dir, bundleId, "Verification Mismatch", "1", "1.0");
        var controller = new ExistingInstallDeviceController("TEST-UDID", bundleId, "2.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath));
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        HttpResponseMessage queued = await client.PostAsJsonAsync(
            $"/api/apps/TEST-UDID/{bundleId}/verify",
            new { idempotencyKey = "verify-version-mismatch" });
        OperationRecordDto initial = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, initial.operationId);

        Assert.Equal("blocked", terminal.status);
        Assert.Equal("installed-app-version-mismatch", terminal.error?.code);
        Assert.Equal(0, controller.InstallCalls);
        Assert.Equal(1, controller.InstalledAppReads);
        Assert.Null((await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);
    }

    [Theory]
    [InlineData(5, false, "succeeded", null)]
    [InlineData(-1, false, "blocked", "installed-profile-expired-after-verification")]
    [InlineData(5, true, "blocked", "registration-lineage-changed")]
    public async Task VerifyExistingRegistration_RestartFinalizesSavedEvidenceWithoutRepeatingDeviceRead(
        int expiryOffsetDays,
        bool replaceWithSameVersionArtifact,
        string expectedStatus,
        string? expectedError)
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        Directory.CreateDirectory(stateDir);
        const string bundleId = "com.example.verificationrecovery";
        const string operationId = "op_verify_existing_recovery";
        string ipaPath = WriteTestIpa(dir, bundleId, "Verification Recovery", "1", "1.0");
        await new FileAppRegistry(Path.Combine(stateDir, "apps.json")).UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            ipaPath));

        DateTimeOffset verifiedAt = DateTimeOffset.UtcNow.AddHours(-1);
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(expiryOffsetDays);
        string artifactSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(ipaPath)));
        var evidence = new Sideport.Api.Operations.OperationRecordDto(
            operationId,
            "verify-existing-registration",
            "running",
            verifiedAt.AddMinutes(-1),
            verifiedAt.AddMinutes(-1),
            verifiedAt,
            null,
            new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client"),
            "verify-recovery",
            1,
            new Sideport.Api.Operations.OperationTargetDto(
                "TEST-UDID",
                bundleId,
                TeamId: "TEAMID1234",
                Kind: "app",
                AccountProfileId: Sideport.Api.AppleAccess.AppleAccountIdentity.ProfileIdFor("developer@example.com"),
                Version: "1.0",
                CatalogSha256: artifactSha256),
            [
                new Sideport.Api.Operations.OperationStageDto(
                    "preflight", "Preflight", "succeeded", verifiedAt.AddMinutes(-1), verifiedAt.AddMinutes(-1), "Ready."),
                new Sideport.Api.Operations.OperationStageDto(
                    "verify", "Verify existing app", "succeeded", verifiedAt, verifiedAt, "Verified."),
                new Sideport.Api.Operations.OperationStageDto(
                    "activate-registration", "Save verification", "running", verifiedAt, null, "Saving."),
            ],
            new Sideport.Api.Operations.OperationResultDto(
                true,
                bundleId,
                expiresAt,
                null,
                Version: "1.0"),
            null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId);
        await File.WriteAllTextAsync(
            Path.Combine(stateDir, "operations.json"),
            JsonSerializer.Serialize(
                new[] { evidence },
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        if (replaceWithSameVersionArtifact)
        {
            _ = WriteTestIpa(
                dir,
                bundleId,
                "Different bytes, same version",
                "1",
                "1.0");
        }

        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            reachable: false);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);

        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, operationId);

        Assert.Equal(expectedStatus, terminal.status);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
        AppRegistration registration = (await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!;
        if (string.Equals(expectedStatus, "succeeded", StringComparison.Ordinal))
            Assert.Equal(operationId, registration.LastVerifiedOperationId);
        else
        {
            Assert.Equal(expectedError, terminal.error?.code);
            Assert.Null(registration.LastVerifiedOperationId);
        }
    }

    [Fact]
    public async Task ReconcileUnknownRefresh_MatchingDeviceEvidence_LinksChildWithoutInstalling_AndReplays()
    {
        string dir = TestDir();
        const string bundleId = "com.example.reconcilepresent";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconcile Present", "1", "1.0");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            signatureExpiresAt: expiry);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        Sideport.Api.Operations.OperationRecordDto source = await SeedUnknownRefreshAsync(
            factory,
            bundleId,
            ipaPath,
            expiry,
            "op_reconcile_present_source");

        HttpResponseMessage queued = await client.PostAsJsonAsync(
            $"/api/operations/{source.OperationId}/reconcile",
            new { idempotencyKey = "reconcile-present", note = "Checked after reconnecting USB." });
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        OperationRecordDto child = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, child.operationId);

        Assert.Equal("reconcile", terminal.type);
        Assert.Equal("succeeded", terminal.status);
        Assert.True(terminal.result?.success);
        Assert.False(terminal.result?.safeToRerun);
        Assert.Equal(source.OperationId, terminal.result?.reconciledOperationId);
        Assert.Equal(source.OperationId, terminal.parentOperationId);
        Assert.Equal(1, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
        Assert.Equal(
            child.operationId,
            (await factory.Services.GetRequiredService<IAppRegistry>()
                .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);
        Assert.Equal(
            "unknown",
            (await factory.Services.GetRequiredService<OperationStore>()
                .FindAsync(source.OperationId))!.Status);

        HttpResponseMessage replay = await client.PostAsJsonAsync(
            $"/api/operations/{source.OperationId}/reconcile",
            new { idempotencyKey = "reconcile-present" });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(
            child.operationId,
            ((await replay.Content.ReadFromJsonAsync<OperationRecordDto>())!).operationId);
        Assert.Equal(1, controller.InstalledAppReads);
    }

    [Fact]
    public async Task ReconcileUnknownRefresh_RejectsWhileManagedTransferIsStillActive()
    {
        string dir = TestDir();
        const string bundleId = "com.example.reconcileactive";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconcile Active", "1", "1.0");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        var controller = new BlockingInstallDeviceController("TEST-UDID", bundleId, "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        Sideport.Api.Operations.OperationRecordDto source = await SeedUnknownRefreshAsync(
            factory,
            bundleId,
            ipaPath,
            expiry,
            "op_reconcile_active_source");
        RefreshOrchestrator orchestrator = factory.Services.GetRequiredService<RefreshOrchestrator>();
        Task<RefreshResult> activeRefresh = orchestrator.RefreshAsync("TEST-UDID", bundleId);
        await controller.WaitForInstallStartAsync();
        Assert.True(orchestrator.IsDeviceMutationActive("TEST-UDID"));
        try
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                $"/api/operations/{source.OperationId}/reconcile",
                new { idempotencyKey = "reconcile-while-active" });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(
                "device-operation-still-active",
                ((await response.Content.ReadFromJsonAsync<OperationErrorDto>())!).error);
            Assert.DoesNotContain(
                await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null),
                operation => operation.Type == "reconcile");
            Assert.Equal(0, controller.InstalledAppReads);
            Assert.Equal(1, controller.InstallCalls);
        }
        finally
        {
            controller.CompleteInstall();
        }
        Assert.True((await activeRefresh).Success);
    }

    [Fact]
    public async Task ReconcileUnknownRefresh_AbsentApp_RecordsSafeToRerunWithoutSchedulerEvidence()
    {
        string dir = TestDir();
        const string bundleId = "com.example.reconcileabsent";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconcile Absent", "1", "1.0");
        string stateDir = Path.Combine(dir, "state");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            signatureExpiresAt: expiry,
            installed: false);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        Sideport.Api.Operations.OperationRecordDto source = await SeedUnknownRefreshAsync(
            factory,
            bundleId,
            ipaPath,
            expiry,
            "op_reconcile_absent_source");

        HttpResponseMessage queued = await client.PostAsJsonAsync(
            $"/api/operations/{source.OperationId}/reconcile",
            new { idempotencyKey = "reconcile-absent" });
        OperationRecordDto child = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, child.operationId);

        Assert.Equal("succeeded", terminal.status);
        Assert.False(terminal.result?.success);
        Assert.True(terminal.result?.safeToRerun);
        AppRegistration registration = (await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!;
        Assert.Equal($"{source.OperationId}_prior", registration.LastVerifiedOperationId);
        SchedulerStatusResponse scheduler = (await client.GetFromJsonAsync<SchedulerStatusResponse>("/api/scheduler/status"))!;
        Assert.NotEqual("held", scheduler.concurrency.lockState);
        OperationStore operationStore = factory.Services.GetRequiredService<OperationStore>();
        var schedulerSettings = new SchedulerSettingsStore(Path.Combine(stateDir, "scheduler-absence-test.json"));
        await schedulerSettings.InitializeAsync(
            requestedEnabled: true,
            prerequisitesSatisfied: true,
            nextEvaluationAt: DateTimeOffset.UtcNow);
        var operationScheduler = new OperationScheduler(
            factory.Services.GetRequiredService<IAppRegistry>(),
            factory.Services.GetRequiredService<OperationService>(),
            operationStore,
            schedulerSettings,
            factory.Services.GetRequiredService<OrchestratorOptions>());
        await operationScheduler.RunOnceAsync(CancellationToken.None);
        Assert.DoesNotContain(
            await operationStore.ListAsync(limit: null),
            operation => operation.Actor.DisplayName == "system:scheduler");
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task ReconcileUnknownRefresh_MismatchedVersion_RemainsBlockedAndNonRerunnable()
    {
        string dir = TestDir();
        const string bundleId = "com.example.reconcilemismatch";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconcile Mismatch", "1", "1.0");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "2.0",
            signatureExpiresAt: expiry);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        Sideport.Api.Operations.OperationRecordDto source = await SeedUnknownRefreshAsync(
            factory,
            bundleId,
            ipaPath,
            expiry,
            "op_reconcile_mismatch_source");

        OperationRecordDto child = (await (await client.PostAsJsonAsync(
            $"/api/operations/{source.OperationId}/reconcile",
            new { idempotencyKey = "reconcile-mismatch" })).Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, child.operationId);

        Assert.Equal("blocked", terminal.status);
        Assert.Equal("reconciliation-evidence-mismatch", terminal.error?.code);
        Assert.False(terminal.result?.safeToRerun);
        Assert.Equal(
            $"{source.OperationId}_prior",
            (await factory.Services.GetRequiredService<IAppRegistry>()
                .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task ReconcileUnknownRefresh_RestartFinalizesSavedEvidenceWithoutAnotherDeviceRead()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.reconcilerecovery";
        const string childId = "op_reconcile_recovery_child";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconcile Recovery", "1", "1.0");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        Sideport.Api.Operations.OperationRecordDto source;

        using (var seedFactory = Factory(
                   apiToken: "s3cr3t-token",
                   stateDirectory: stateDir,
                   operationWorker: false,
                   deviceController: new ExistingInstallDeviceController("TEST-UDID", bundleId, "1.0")))
        {
            source = await SeedUnknownRefreshAsync(
                seedFactory,
                bundleId,
                ipaPath,
                expiry,
                "op_reconcile_recovery_source");
            DateTimeOffset verifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            var child = new Sideport.Api.Operations.OperationRecordDto(
                childId,
                "reconcile",
                "running",
                verifiedAt.AddMinutes(-1),
                verifiedAt.AddMinutes(-1),
                verifiedAt,
                CompletedAt: null,
                new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client"),
                "reconcile-recovery",
                Attempt: 1,
                source.Target with { Kind = "reconciliation" },
                [
                    new Sideport.Api.Operations.OperationStageDto(
                        "preflight", "Reconciliation preflight", "succeeded", verifiedAt.AddMinutes(-1), verifiedAt.AddMinutes(-1), "Ready."),
                    new Sideport.Api.Operations.OperationStageDto(
                        "verify", "Check iPhone", "succeeded", verifiedAt, verifiedAt, "Verified."),
                    new Sideport.Api.Operations.OperationStageDto(
                        "activate-registration", "Save verified state", "running", verifiedAt, null, "Saving."),
                ],
                new Sideport.Api.Operations.OperationResultDto(
                    true,
                    bundleId,
                    expiry,
                    null,
                    Version: "1.0",
                    SafeToRerun: false,
                    ReconciledOperationId: source.OperationId),
                Error: null,
                Cancelable: false,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: childId,
                ParentOperationId: source.OperationId);
            await seedFactory.Services.GetRequiredService<OperationStore>()
                .AddIfIdempotentMissingAsync(child);
        }

        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            reachable: false,
            installed: false);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);

        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, childId);

        Assert.Equal("succeeded", terminal.status);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
        Assert.Equal(
            childId,
            (await factory.Services.GetRequiredService<IAppRegistry>()
                .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);
    }

    [Fact]
    public async Task Api_WithTokenConfigured_RejectsMissingBearer()
    {
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal());
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
        using var factory = Factory(apiToken: "s3cr3t-token", anisetteHealthy: false);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

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
    public async Task FirstInstall_PathFreeHappyPath_VerifiesActivatesReceiptsAndReplaysAcrossOfflineRestart()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.firstinstall";
        string ipaPath = WriteTestIpa(dir, bundleId, "First Install", "1", "1.0");
        var controller = new FirstInstallDeviceController("TEST-UDID", bundleId);
        string operationId;

        using (var factory = Factory(
                   apiToken: "s3cr3t-token",
                   stateDirectory: stateDir,
                   seedCatalogPath: ipaPath,
                   personalAppleId: "developer@example.com",
                   personalApplePassword: "configured-host-secret",
                   personalApplePortal: new StubApplePortal(),
                   deviceController: controller,
                   schedulerEnabled: true))
        using (HttpClient client = HttpsTokenClient(factory))
        {
            string accountProfileId = await PrepareAppleTeamAsync(client);
            await AcceptKnownDeviceAsync(factory, "TEST-UDID");
            ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
                client,
                "TEST-UDID",
                bundleId,
                accountProfileId,
                finishOnboarding: true,
                idempotencyKey: "install-once");

            HttpResponseMessage queued = await client.PostAsJsonAsync("/api/operations/install", request);
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            string queuedJson = await queued.Content.ReadAsStringAsync();
            Assert.DoesNotContain(ipaPath, queuedJson, StringComparison.Ordinal);
            Assert.DoesNotContain(Path.Combine(stateDir, "ipas"), queuedJson, StringComparison.Ordinal);
            OperationRecordDto initial = JsonSerializer.Deserialize<OperationRecordDto>(queuedJson, JsonOptions())!;
            operationId = initial.operationId;

            OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, operationId);
            Assert.Equal("succeeded", terminal.status);
            Assert.Contains(terminal.stages, stage => stage.id == "verify" && stage.status == "succeeded");
            Assert.Equal("cert-clock", terminal.installIntent?.catalogAppId);
            Assert.True(terminal.installIntent?.finishOnboarding);
            Assert.Equal(1, controller.InstallCalls);

            AppRegistration registration = (await factory.Services.GetRequiredService<IAppRegistry>()
                .FindAsync("TEST-UDID", bundleId))!;
            Assert.Equal("active", registration.Lifecycle);
            Assert.Equal(operationId, registration.LastVerifiedOperationId);
            Assert.True(File.Exists(registration.InputIpaPath));
            Assert.NotEqual(ipaPath, registration.InputIpaPath);

            OnboardingCompletionReceipt receipt = (await factory.Services
                .GetRequiredService<Sideport.Api.Onboarding.OnboardingCompletionStore>()
                .ReadAsync())!;
            Assert.Equal(operationId, receipt.VerifiedOperationId);
            Assert.StartsWith("settings_", receipt.SchedulerSettingsVersion, StringComparison.Ordinal);
            Assert.NotEqual(default, receipt.OperationalCheckedAt);

            HttpResponseMessage replay = await client.PostAsJsonAsync("/api/operations/install", request);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            OperationRecordDto replayed = (await replay.Content.ReadFromJsonAsync<OperationRecordDto>())!;
            Assert.Equal(operationId, replayed.operationId);
            Assert.Equal(1, controller.InstallCalls);

            HttpResponseMessage conflict = await client.PostAsJsonAsync("/api/operations/install", new
            {
                deviceUdid = "DIFFERENT-UDID",
                bundleId,
                catalogAppId = "cert-clock",
                accountProfileId,
                finishOnboarding = true,
                idempotencyKey = "install-once",
            });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            Assert.Contains("idempotency-target-conflict", await conflict.Content.ReadAsStringAsync());

            OnboardingInstallStatusDto status = (await client.GetFromJsonAsync<OnboardingInstallStatusDto>("/api/onboarding/status"))!;
            Assert.True(status.firstRunComplete);
            Assert.Equal("complete", status.setupState);
            Assert.Equal("cert-clock", status.selectedCatalogAppId);
            Assert.Null(status.activeInstallOperationId);
            Assert.Equal(bundleId, status.completionReceipt?.registrationKey.bundleId);
        }

        using (var restarted = Factory(
                   apiToken: "s3cr3t-token",
                   stateDirectory: stateDir,
                   seedCatalogPath: ipaPath,
                   deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId, reachable: false),
                   schedulerEnabled: true))
        using (HttpClient client = HttpsTokenClient(restarted))
        {
            OperationRecordDto durableOperation = (await client.GetFromJsonAsync<OperationRecordDto>($"/api/operations/{operationId}"))!;
            Assert.Equal("cert-clock", durableOperation.installIntent?.catalogAppId);
            Assert.True(durableOperation.installIntent?.finishOnboarding);
            OnboardingInstallStatusDto status = (await client.GetFromJsonAsync<OnboardingInstallStatusDto>("/api/onboarding/status"))!;
            Assert.True(status.firstRunComplete);
            Assert.Equal("complete", status.setupState);
            Assert.Equal(operationId, status.completionReceipt?.verifiedOperationId);
            Assert.Equal("TEST-UDID", status.completionReceipt?.registrationKey.deviceUdid);
        }
    }

    [Fact]
    public async Task InstallPreflight_ReturnsBoundPlanGroupsLimitsAndCertificateImpact()
    {
        string dir = TestDir();
        const string bundleId = "com.example.preflight";
        string ipaPath = WriteTestIpa(dir, bundleId, "Preflight", "1", "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        OperationPreflightDto preflight = await GetInstallPreflightAsync(
            client, "TEST-UDID", bundleId, finishOnboarding: true, accountProfileId: accountProfileId);

        Assert.True(preflight.ready);
        Assert.StartsWith("install_preflight_", preflight.preflightId);
        Assert.StartsWith("sha256:", preflight.planVersion);
        Assert.StartsWith("sha256:", preflight.inventoryVersion);
        Assert.True(preflight.expiresAt > DateTimeOffset.UtcNow);
        Assert.Equal("cert-clock", preflight.target?.catalogAppId);
        Assert.Equal(accountProfileId, preflight.target?.accountProfileId);
        Assert.Equal("mint-new", preflight.signing?.impact);
        Assert.Contains(preflight.warnings!, warning => warning.code == "signing-certificate-will-be-created");
        Assert.Contains(preflight.plannedMutations, mutation => mutation.Contains("development certificate", StringComparison.Ordinal));
        Assert.Contains(preflight.scarceLimits, limit => limit.code == "apple-development-certificates" && limit.used == 0);
        Assert.Contains(preflight.checkGroups!, group => group.id == "signing" && group.checks.Count > 0);
    }

    [Fact]
    public async Task InstallPreflight_BindsOperationalSystemFailuresBeforeAnyMutation()
    {
        string dir = TestDir();
        const string bundleId = "com.example.anisetteblocked";
        string ipaPath = WriteTestIpa(dir, bundleId, "Blocked", "1", "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            anisetteHealthy: false,
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        OperationPreflightDto preflight = await GetInstallPreflightAsync(
            client,
            "TEST-UDID",
            bundleId,
            finishOnboarding: true,
            accountProfileId: Sideport.Api.AppleAccess.AppleAccountIdentity.ProfileIdFor("developer@example.com"));

        Assert.False(preflight.ready);
        Assert.Contains(preflight.blockers, blocker => blocker.code == "anisette-headers");
        Assert.Empty(await factory.Services.GetRequiredService<IAppRegistry>().ListAsync());
        Assert.Empty(await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null));
    }

    [Fact]
    public async Task Install_WhenCertificateInventoryDrifts_ReturnsReplacementAndDoesNotMutate()
    {
        string dir = TestDir();
        const string bundleId = "com.example.preflightdrift";
        string ipaPath = WriteTestIpa(dir, bundleId, "Preflight Drift", "1", "1.0");
        var portal = new StubApplePortal();
        var controller = new FirstInstallDeviceController("TEST-UDID", bundleId);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: portal,
            operationWorker: false,
            deviceController: controller,
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");
        ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            accountProfileId,
            finishOnboarding: true,
            idempotencyKey: "certificate-drift");
        portal.Certificates.Add(new AppleDevelopmentCertificate(
            "cert_other",
            "11223344",
            DateTimeOffset.UtcNow.AddMonths(6)));

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/install", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("install-preflight-stale", body.RootElement.GetProperty("error").GetString());
        JsonElement replacement = body.RootElement.GetProperty("replacementPreflight");
        Assert.False(replacement.GetProperty("ready").GetBoolean());
        Assert.Contains(
            replacement.GetProperty("blockers").EnumerateArray(),
            blocker => blocker.GetProperty("code").GetString() == "signing-cutover-required");
        Assert.Empty(await factory.Services.GetRequiredService<IAppRegistry>().ListAsync());
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task Install_WithoutExactMutationConfirmation_DoesNotCreateRegistration()
    {
        string dir = TestDir();
        const string bundleId = "com.example.confirmation";
        string ipaPath = WriteTestIpa(dir, bundleId, "Confirmation", "1", "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");
        ConfirmedInstallRequestDto confirmed = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            accountProfileId,
            finishOnboarding: true,
            idempotencyKey: "missing-confirmation");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/operations/install",
            confirmed with { confirmedPlannedMutations = false });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("install-confirmation-required", await response.Content.ReadAsStringAsync());
        Assert.Empty(await factory.Services.GetRequiredService<IAppRegistry>().ListAsync());

        HttpResponseMessage mismatched = await client.PostAsJsonAsync(
            "/api/operations/install",
            confirmed with { planVersion = "sha256:confirmed-plan-does-not-match" });
        Assert.Equal(HttpStatusCode.Conflict, mismatched.StatusCode);
        Assert.Contains("install-preflight-stale", await mismatched.Content.ReadAsStringAsync());
        Assert.Empty(await factory.Services.GetRequiredService<IAppRegistry>().ListAsync());

        HttpResponseMessage accepted = await client.PostAsJsonAsync("/api/operations/install", confirmed);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        HttpResponseMessage reusedPreflight = await client.PostAsJsonAsync(
            "/api/operations/install",
            confirmed with { idempotencyKey = "reused-confirmed-preflight" });
        Assert.Equal(HttpStatusCode.Conflict, reusedPreflight.StatusCode);
        Assert.Contains("install-preflight-stale", await reusedPreflight.Content.ReadAsStringAsync());
        Assert.Single(await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null));
    }

    [Fact]
    public async Task FirstInstall_RestartAfterActivationBeforeReceipt_ExactReplayFinalizesWithoutReinstall()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.activationcrash";
        string ipaPath = WriteTestIpa(dir, bundleId, "Activation Crash", "1", "1.0");
        VerifiedInstallCrashFixture crash = await SeedVerifiedInstallCrashStateAsync(
            stateDir,
            ipaPath,
            bundleId,
            writeReceipt: false);
        var recoveryController = new FirstInstallDeviceController(
            "TEST-UDID",
            bundleId,
            reachable: true);

        using var restarted = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: recoveryController,
            schedulerEnabled: true,
            signingIdentityProvider: new ReusableSigningIdentityProvider("TEST"));
        using HttpClient client = HttpsTokenClient(restarted);
        _ = await PrepareAppleTeamAsync(client);
        OperationQueue queue = restarted.Services.GetRequiredService<OperationQueue>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using IAsyncEnumerator<string> queuedOperations = queue
            .ReadAllAsync(timeout.Token)
            .GetAsyncEnumerator(timeout.Token);

        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/operations/install", new
        {
            deviceUdid = "TEST-UDID",
            bundleId,
            catalogAppId = "cert-clock",
            accountProfileId = crash.AccountProfileId,
            finishOnboarding = true,
            idempotencyKey = crash.IdempotencyKey,
        });

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.True(await queuedOperations.MoveNextAsync());
        Assert.Equal(crash.OperationId, queuedOperations.Current);
        await restarted.Services.GetRequiredService<OperationService>()
            .ProcessQueuedOperationAsync(queuedOperations.Current);

        OperationRecordDto terminal = (await client.GetFromJsonAsync<OperationRecordDto>(
            $"/api/operations/{crash.OperationId}"))!;
        Assert.True(
            string.Equals(terminal.status, "succeeded", StringComparison.Ordinal),
            $"Expected recovered install success, got {terminal.status}: {terminal.error?.code} {terminal.error?.message}");
        Assert.Equal(crash.ExpiresAt, terminal.result?.expiresAt);
        Assert.Contains(terminal.stages, stage =>
            stage.id == "activate-registration" && stage.status == "succeeded");
        Assert.Equal(0, recoveryController.InstallCalls);

        OnboardingCompletionReceipt receipt = (await restarted.Services
            .GetRequiredService<OnboardingCompletionStore>()
            .ReadAsync())!;
        Assert.Equal(crash.OperationId, receipt.VerifiedOperationId);
        OnboardingInstallStatusDto status = (await client.GetFromJsonAsync<OnboardingInstallStatusDto>(
            "/api/onboarding/status"))!;
        Assert.True(status.firstRunComplete);
        Assert.Equal("complete", status.setupState);
    }

    [Fact]
    public async Task FirstInstall_SavedDeviceEvidence_BlocksIfArtifactBytesChangeBeforeFinalization()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.artifactdrift";
        string ipaPath = WriteTestIpa(dir, bundleId, "Artifact Drift", "1", "1.0");
        VerifiedInstallCrashFixture crash = await SeedVerifiedInstallCrashStateAsync(
            stateDir,
            ipaPath,
            bundleId,
            writeReceipt: false);
        AppRegistration registration = (await new FileAppRegistry(Path.Combine(stateDir, "apps.json"))
            .FindAsync("TEST-UDID", bundleId))!;
        string replacement = WriteTestIpa(
            Path.Combine(dir, "replacement"),
            bundleId,
            "Different bytes, same version",
            "1",
            "1.0");
        File.Copy(replacement, registration.InputIpaPath, overwrite: true);
        var recoveryController = new FirstInstallDeviceController("TEST-UDID", bundleId);

        using var restarted = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: recoveryController,
            schedulerEnabled: true,
            signingIdentityProvider: new ReusableSigningIdentityProvider("TEST"));
        using HttpClient client = HttpsTokenClient(restarted);
        _ = await PrepareAppleTeamAsync(client);

        HttpResponseMessage completion = await client.PostAsJsonAsync("/api/onboarding/complete", new
        {
            verifiedOperationId = crash.OperationId,
            idempotencyKey = "artifact-drift-finalization",
        });

        Assert.Equal(HttpStatusCode.Conflict, completion.StatusCode);
        OperationRecordDto terminal = (await client.GetFromJsonAsync<OperationRecordDto>(
            $"/api/operations/{crash.OperationId}"))!;
        Assert.Equal("blocked", terminal.status);
        Assert.Equal("onboarding-artifact-lineage-unavailable", terminal.error?.code);
        Assert.Null(await restarted.Services.GetRequiredService<OnboardingCompletionStore>().ReadAsync());
        Assert.Equal(0, recoveryController.InstallCalls);
    }

    [Fact]
    public async Task OnboardingComplete_OriginalUnknownInstallId_ResumesVerifiedReconciliationWithoutDeviceRead()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.reconciledonboarding";
        const string sourceId = "op_onboarding_unknown_source";
        const string childId = "op_onboarding_reconcile_child";
        string ipaPath = WriteTestIpa(dir, bundleId, "Reconciled Onboarding", "1", "1.0");
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(6);
        var controller = new ExistingInstallDeviceController(
            "TEST-UDID",
            bundleId,
            "1.0",
            signatureExpiresAt: expiry);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: controller,
            schedulerEnabled: true,
            signingIdentityProvider: new ReusableSigningIdentityProvider("TEST"));
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");
        Sideport.Api.Catalog.CatalogAppDto catalogApp = Assert.Single(
            await factory.Services.GetRequiredService<Sideport.Api.Catalog.IAppCatalog>().ListAsync());
        Assert.Equal(bundleId, catalogApp.BundleId);
        Assert.NotNull(catalogApp.Sha256);

        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            bundleId,
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            catalogApp.IpaPath,
            Lifecycle: "pending-install",
            CatalogAppId: catalogApp.Id,
            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-3),
            CatalogVersion: catalogApp.CatalogVersion,
            CatalogSha256: catalogApp.Sha256));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var actor = new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client");
        var target = new Sideport.Api.Operations.OperationTargetDto(
            "TEST-UDID",
            bundleId,
            TeamId: "TEAMID1234",
            Kind: "first-install",
            CatalogAppId: catalogApp.Id,
            AccountProfileId: accountProfileId,
            CatalogVersion: catalogApp.CatalogVersion,
            Version: "1.0",
            CatalogSha256: catalogApp.Sha256);
        var intent = new Sideport.Api.Operations.InstallOperationIntentDto(
            "TEST-UDID",
            catalogApp.Id,
            accountProfileId,
            bundleId,
            FinishOnboarding: true,
            RegistrationKey: $"TEST-UDID:{bundleId}",
            CatalogVersion: catalogApp.CatalogVersion,
            CatalogSha256: catalogApp.Sha256);
        var unknownIssue = new Sideport.Api.Operations.OperationIssueDto(
            "install-outcome-unknown",
            "The original install outcome is unknown.");
        var source = new Sideport.Api.Operations.OperationRecordDto(
            sourceId,
            "install",
            "unknown",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            CompletedAt: null,
            actor,
            "onboarding-unknown",
            Attempt: 1,
            target,
            [
                new Sideport.Api.Operations.OperationStageDto(
                    "preflight", "Preflight", "succeeded", now.AddMinutes(-2), now.AddMinutes(-2), "Ready."),
                new Sideport.Api.Operations.OperationStageDto(
                    "install", "Install app", "unknown", now.AddMinutes(-2), null, unknownIssue.Message, unknownIssue),
                new Sideport.Api.Operations.OperationStageDto(
                    "verify", "Verify on iPhone", "pending", null, null, "Waiting for reconciliation."),
            ],
            new Sideport.Api.Operations.OperationResultDto(
                false,
                bundleId,
                expiry,
                unknownIssue.Message),
            unknownIssue,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: sourceId,
            InstallIntent: intent);
        var child = new Sideport.Api.Operations.OperationRecordDto(
            childId,
            "reconcile",
            "waiting",
            now.AddMinutes(-1),
            now.AddMinutes(-1),
            now,
            CompletedAt: null,
            actor,
            "reconcile-onboarding",
            Attempt: 1,
            target with { Kind = "reconciliation" },
            [
                new Sideport.Api.Operations.OperationStageDto(
                    "preflight", "Reconciliation preflight", "succeeded", now.AddMinutes(-1), now.AddMinutes(-1), "Ready."),
                new Sideport.Api.Operations.OperationStageDto(
                    "verify", "Check iPhone", "succeeded", now.AddMinutes(-1), now.AddMinutes(-1), "Verified."),
                new Sideport.Api.Operations.OperationStageDto(
                    "activate-registration", "Save verified state", "waiting", now, null, "Saving."),
                new Sideport.Api.Operations.OperationStageDto(
                    "enable-scheduler", "Enable automatic refresh", "pending", null, null, "Waiting."),
                new Sideport.Api.Operations.OperationStageDto(
                    "compute-next-evaluation", "Schedule next check", "pending", null, null, "Waiting."),
                new Sideport.Api.Operations.OperationStageDto(
                    "write-completion-receipt", "Finish setup", "pending", null, null, "Waiting."),
            ],
            new Sideport.Api.Operations.OperationResultDto(
                true,
                bundleId,
                expiry,
                Error: null,
                Version: "1.0",
                SafeToRerun: false,
                ReconciledOperationId: sourceId),
            Error: null,
            Cancelable: false,
            Retryable: true,
            Rerunnable: false,
            CorrelationId: childId,
            ParentOperationId: sourceId);
        OperationStore store = factory.Services.GetRequiredService<OperationStore>();
        Assert.True((await store.AddIfIdempotentMissingAsync(source)).Created);
        Assert.True((await store.AddIfIdempotentMissingAsync(child)).Created);

        HttpResponseMessage completion = await client.PostAsJsonAsync("/api/onboarding/complete", new
        {
            verifiedOperationId = sourceId,
            idempotencyKey = "compatibility-reconciled-onboarding",
        });

        Assert.Equal(HttpStatusCode.Created, completion.StatusCode);
        OnboardingCompletionDto receipt = (await completion.Content.ReadFromJsonAsync<OnboardingCompletionDto>())!;
        Assert.Equal(childId, receipt.verifiedOperationId);
        Assert.Equal(bundleId, receipt.registrationKey.bundleId);
        Assert.Equal("unknown", (await store.FindAsync(sourceId))!.Status);
        Assert.Equal("succeeded", (await store.FindAsync(childId))!.Status);
        Assert.Equal(childId, (await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);

        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/onboarding/complete", new
        {
            verifiedOperationId = sourceId,
            idempotencyKey = "compatibility-reconciled-onboarding",
        });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task FirstInstall_RestartAfterReceiptBeforeTerminalTransition_CompletesWithoutReinstall()
    {
        string dir = TestDir();
        string stateDir = Path.Combine(dir, "state");
        const string bundleId = "com.example.receiptcrash";
        string ipaPath = WriteTestIpa(dir, bundleId, "Receipt Crash", "1", "1.0");
        VerifiedInstallCrashFixture crash = await SeedVerifiedInstallCrashStateAsync(
            stateDir,
            ipaPath,
            bundleId,
            writeReceipt: true);
        var recoveryController = new FirstInstallDeviceController(
            "TEST-UDID",
            bundleId,
            reachable: false);

        using var restarted = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDir,
            deviceController: recoveryController,
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(restarted);

        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, crash.OperationId);

        Assert.Equal("succeeded", terminal.status);
        Assert.Equal(crash.ExpiresAt, terminal.result?.expiresAt);
        Assert.Equal(0, recoveryController.InstallCalls);
        OnboardingCompletionReceipt receipt = (await restarted.Services
            .GetRequiredService<OnboardingCompletionStore>()
            .ReadAsync())!;
        Assert.Equal(crash.OperationId, receipt.VerifiedOperationId);
        Assert.Equal(crash.ReceiptCompletedAt, receipt.CompletedAt);
    }

    [Fact]
    public async Task FirstInstall_FinishOnboardingPlansToEnableDisabledSchedulerWithoutCreatingRegistration()
    {
        string dir = TestDir();
        const string bundleId = "com.example.scheduler";
        string ipaPath = WriteTestIpa(dir, bundleId, "Scheduler", "1", "1.0");
        using var factory = Factory(apiToken: "s3cr3t-token", seedCatalogPath: ipaPath, schedulerEnabled: false);
        using HttpClient client = HttpsTokenClient(factory);

        OperationPreflightDto preflight = await GetInstallPreflightAsync(
            client, "TEST-UDID", bundleId, finishOnboarding: true, accountProfileId: "acct_test");

        Assert.False(preflight.ready);
        Assert.DoesNotContain(preflight.blockers, blocker => blocker.code == "scheduler-disabled");
        Assert.Contains(
            preflight.plannedMutations,
            mutation => mutation.Contains("Enable automatic hourly due-only refresh", StringComparison.Ordinal));
        Assert.Empty(await factory.Services.GetRequiredService<IAppRegistry>().ListAsync());
    }

    [Fact]
    public async Task FirstInstall_RejectsUnacceptedDeviceAndMissingCatalog()
    {
        string dir = TestDir();
        string ipaPath = WriteTestIpa(dir, "com.example.rejected", "Rejected", "1", "1.0");
        var controller = new FirstInstallDeviceController("TEST-UDID", "com.example.rejected");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            deviceController: controller,
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string profile = await PrepareAppleTeamAsync(client);

        OperationPreflightDto unaccepted = await GetInstallPreflightAsync(
            client, "TEST-UDID", "com.example.rejected", finishOnboarding: true, accountProfileId: profile);
        Assert.False(unaccepted.ready);
        Assert.Contains(unaccepted.blockers, blocker => blocker.code == "device-not-accepted");

        await AcceptKnownDeviceAsync(factory, "TEST-UDID");
        OperationPreflightDto missingCatalog = await GetInstallPreflightAsync(
            client,
            "TEST-UDID",
            "com.example.rejected",
            finishOnboarding: true,
            catalogAppId: "missing-app",
            accountProfileId: profile);
        Assert.False(missingCatalog.ready);
        Assert.Contains(missingCatalog.blockers, blocker => blocker.code == "catalog-app-not-found");
    }

    [Fact]
    public async Task FirstInstall_RejectsWifiOnlyAndStaleAppleContext()
    {
        string wifiDir = TestDir();
        string wifiIpa = WriteTestIpa(wifiDir, "com.example.wifi", "WiFi", "1", "1.0");
        var wifiController = new FirstInstallDeviceController(
            "TEST-UDID",
            "com.example.wifi",
            connection: DeviceConnection.Wifi);
        using (var factory = Factory(
                   apiToken: "s3cr3t-token",
                   seedCatalogPath: wifiIpa,
                   personalAppleId: "developer@example.com",
                   personalApplePassword: "configured-host-secret",
                   personalApplePortal: new StubApplePortal(),
                   deviceController: wifiController,
                   schedulerEnabled: true))
        using (HttpClient client = HttpsTokenClient(factory))
        {
            string profile = await PrepareAppleTeamAsync(client);
            await AcceptKnownDeviceAsync(factory, "TEST-UDID");
            OperationPreflightDto preflight = await GetInstallPreflightAsync(
                client, "TEST-UDID", "com.example.wifi", finishOnboarding: true, accountProfileId: profile);
            Assert.False(preflight.ready);
            Assert.Contains(preflight.blockers, blocker => blocker.code == "device-usb-required");
        }

        string staleDir = TestDir();
        string staleIpa = WriteTestIpa(staleDir, "com.example.stale", "Stale", "1", "1.0");
        using (var factory = Factory(
                   apiToken: "s3cr3t-token",
                   seedCatalogPath: staleIpa,
                   personalAppleId: "developer@example.com",
                   personalApplePassword: "configured-host-secret",
                   personalApplePortal: new StubApplePortal(),
                   deviceController: new FirstInstallDeviceController("TEST-UDID", "com.example.stale"),
                   schedulerEnabled: true))
        using (HttpClient client = HttpsTokenClient(factory))
        {
            await AcceptKnownDeviceAsync(factory, "TEST-UDID");
            OperationPreflightDto preflight = await GetInstallPreflightAsync(
                client,
                "TEST-UDID",
                "com.example.stale",
                finishOnboarding: true,
                accountProfileId: Sideport.Api.AppleAccess.AppleAccountIdentity.ProfileIdFor("developer@example.com"));
            Assert.False(preflight.ready);
            Assert.Contains(preflight.blockers, blocker => blocker.code == "apple-authentication-stale");
        }
    }

    [Fact]
    public async Task FirstInstall_VerificationFailureLeavesPendingRegistrationAndNoReceipt()
    {
        string dir = TestDir();
        const string bundleId = "com.example.verifyfail";
        string ipaPath = WriteTestIpa(dir, bundleId, "Verify Fail", "1", "1.0");
        var controller = new FirstInstallDeviceController("TEST-UDID", bundleId, verifyInstalled: false);
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            deviceController: controller,
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string profile = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            profile,
            finishOnboarding: true,
            idempotencyKey: "verify-fails");
        HttpResponseMessage queued = await client.PostAsJsonAsync("/api/operations/install", request);
        OperationRecordDto initial = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        OperationRecordDto terminal = await WaitForTerminalOperationAsync(client, initial.operationId);

        Assert.Equal("failed", terminal.status);
        Assert.Equal("install-verification-failed", terminal.error?.code);
        AppRegistration registration = (await factory.Services.GetRequiredService<IAppRegistry>()
            .FindAsync("TEST-UDID", bundleId))!;
        Assert.True(registration.IsPendingInstall);
        Assert.Null(registration.LastVerifiedOperationId);
        Assert.Null(await factory.Services.GetRequiredService<Sideport.Api.Onboarding.OnboardingCompletionStore>().ReadAsync());
    }

    [Fact]
    public async Task FirstInstall_CertificateReplacementReturnsStructuredCutoverRequirement()
    {
        string dir = TestDir();
        const string bundleId = "com.example.cutover";
        string ipaPath = WriteTestIpa(dir, bundleId, "Cutover", "1", "1.0");
        var controller = new FirstInstallDeviceController("TEST-UDID", bundleId);
        var portal = new StubApplePortal();
        portal.Certificates.Add(new AppleDevelopmentCertificate(
            "cert_existing",
            "A1B2C3D4",
            DateTimeOffset.UtcNow.AddMonths(6)));
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: portal,
            deviceController: controller,
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        OperationPreflightDto preflight = await GetInstallPreflightAsync(
            client, "TEST-UDID", bundleId, finishOnboarding: true, accountProfileId: accountProfileId);

        Assert.False(preflight.ready);
        Assert.Contains(preflight.blockers, blocker => blocker.code == "signing-cutover-required");
        Assert.Equal("replace-existing", preflight.signing?.impact);
        Assert.Equal(0, controller.InstallCalls);
        Assert.Null(await factory.Services.GetRequiredService<OnboardingCompletionStore>().ReadAsync());
    }

    [Fact]
    public async Task FirstInstall_StandaloneQueuedInstallDoesNotBecomeOnboardingEvidence()
    {
        string dir = TestDir();
        const string bundleId = "com.example.standalone";
        string ipaPath = WriteTestIpa(dir, bundleId, "Standalone", "1", "1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: false);
        using HttpClient client = HttpsTokenClient(factory);
        string profile = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            profile,
            finishOnboarding: false,
            idempotencyKey: "standalone-install");
        HttpResponseMessage queued = await client.PostAsJsonAsync("/api/operations/install", request);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);

        OnboardingInstallStatusDto status = (await client.GetFromJsonAsync<OnboardingInstallStatusDto>("/api/onboarding/status"))!;
        Assert.False(status.firstRunComplete);
        Assert.Equal("in-progress", status.setupState);
        Assert.Null(status.activeInstallOperationId);
        Assert.Null(status.selectedCatalogAppId);
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
        Assert.False(string.IsNullOrWhiteSpace(uploaded.ipaPath));
        Assert.True(File.Exists(uploaded.ipaPath));
        Assert.StartsWith(
            Path.GetFullPath(Path.Combine(stateDir, "imports", ".managed")),
            Path.GetFullPath(uploaded.ipaPath),
            StringComparison.Ordinal);

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
        string catalogPath = Path.Combine(stateDir, "catalog.json");
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        HttpResponseMessage created = await client.PostAsync(
            "/api/catalog/apps/upload",
            UploadContent(firstIpa, id: "uploaded-app"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        CatalogAppDto? original = await created.Content.ReadFromJsonAsync<CatalogAppDto>();
        Assert.NotNull(original);
        Assert.False(string.IsNullOrWhiteSpace(original!.ipaPath));
        string durablePath = original.ipaPath!;
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
        Assert.Equal("validated-recently", status!.state);
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
        Assert.Equal("validated-recently", status!.state);
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
    public async Task AppRegistrations_GetExposesPendingInstallLifecycle()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            "com.example.pending",
            "developer@example.com",
            "TEAMID1234",
            "TEST-UDID",
            "/state/ipas/com.example.pending.ipa",
            Lifecycle: "pending-install",
            CatalogAppId: "catalog-pending",
            CreatedAt: DateTimeOffset.UtcNow,
            LastVerifiedOperationId: null));
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        var apps = await client.GetFromJsonAsync<IReadOnlyList<RegisteredAppDto>>("/api/apps");

        RegisteredAppDto app = Assert.Single(apps!);
        Assert.Equal("pending-install", app.lifecycle);
        Assert.Null(app.lastVerifiedOperationId);
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
    public async Task Api_WithoutAuthenticationConfigured_FailsClosed()
    {
        using var factory = Factory(apiToken: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/anisette/info");
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("authentication-required", error?.error);
    }

    [Fact]
    public async Task Api_OpenProxyModeRejectsEveryMutationBeforeEnrollmentCanPair()
    {
        var controller = new EnrollmentDeviceController();
        controller.SetDevices(EnrollmentDeviceController.Device("OPEN-MODE-PHONE", "untrusted"));
        using var factory = Factory(apiToken: null, deviceController: controller);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = "must-not-run",
            deviceUdid = "OPEN-MODE-PHONE",
        });
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("authentication-required", error?.error);
        Assert.Equal(0, controller.PairCalls);
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
    public async Task DeviceEnrollment_WaitsReplaysAndAutomaticallyAcceptsTrustedUsb()
    {
        string stateDir = Path.Combine(TestDir(), "state");
        var controller = new EnrollmentDeviceController();
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/devices/enrollments", new { idempotencyKey = "enroll-wait" });
        var operation = await started.Content.ReadFromJsonAsync<EnrollmentOperationDto>();

        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        Assert.NotNull(operation);
        Assert.Equal("enroll-device", operation!.type);
        Assert.Equal("waiting", operation.status);

        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/devices/enrollments", new { idempotencyKey = "enroll-wait" });
        var replayed = await replay.Content.ReadFromJsonAsync<EnrollmentOperationDto>();
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(operation.operationId, replayed?.operationId);

        HttpResponseMessage conflict = await client.PostAsJsonAsync("/api/devices/enrollments", new { idempotencyKey = "another-enrollment" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        controller.SetDevices(EnrollmentDeviceController.Device("PHONE-USB-1", "trusted"));
        EnrollmentOperationDto completed = await WaitForEnrollmentTerminalAsync(client, operation.operationId);
        Assert.Equal("succeeded", completed.status);
        Assert.Equal(0, controller.PairCalls);

        var accepted = await client.GetFromJsonAsync<IReadOnlyList<AcceptedKnownDeviceDto>>("/api/devices/known?includeReachable=false");
        AcceptedKnownDeviceDto device = Assert.Single(accepted!);
        Assert.Equal("accepted", device.inventoryState);
        Assert.Equal(operation.operationId, device.enrollmentOperationId);
        Assert.NotNull(device.acceptedAt);
        Assert.Equal("Recovery access", device.acceptedBy);

        OnboardingDto onboarding = (await client.GetFromJsonAsync<OnboardingDto>("/api/onboarding/status"))!;
        Assert.Contains(onboarding.steps, step => step.id == "iphone-trust-computer" && step.state == "complete");
    }

    [Fact]
    public async Task DeviceEnrollment_MultipleUsbCandidatesBlocksBeforePairingWithoutFullUdids()
    {
        var controller = new EnrollmentDeviceController();
        controller.SetDevices(
            EnrollmentDeviceController.Device("00008110-AAAA1111", "trusted", "Personal iPhone"),
            EnrollmentDeviceController.Device("00008110-BBBB2222", "trusted", "Lab iPhone"));
        using var factory = Factory(apiToken: "s3cr3t-token", deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/devices/enrollments", new { idempotencyKey = "choose-phone" });
        EnrollmentOperationDto submitted = (await started.Content.ReadFromJsonAsync<EnrollmentOperationDto>())!;
        EnrollmentOperationDto blocked = await WaitForEnrollmentTerminalAsync(client, submitted.operationId);
        string body = await (await client.GetAsync($"/api/operations/{submitted.operationId}")).Content.ReadAsStringAsync();

        Assert.Equal("blocked", blocked.status);
        Assert.Equal("device-selection-required", blocked.error?.code);
        Assert.Equal(2, blocked.candidateDevices?.Count);
        Assert.DoesNotContain("00008110-AAAA1111", body, StringComparison.Ordinal);
        Assert.DoesNotContain("00008110-BBBB2222", body, StringComparison.Ordinal);
        Assert.Equal(0, controller.PairCalls);
    }

    [Fact]
    public async Task DeviceEnrollment_IdempotencyKeyRejectsDifferentSelectedPhone()
    {
        var controller = new EnrollmentDeviceController();
        controller.SetDevices(
            EnrollmentDeviceController.Device("PHONE-TARGET-A", "trusted"),
            EnrollmentDeviceController.Device("PHONE-TARGET-B", "trusted"));
        using var factory = Factory(apiToken: "s3cr3t-token", operationWorker: false, deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage first = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = "immutable-target",
            deviceUdid = "PHONE-TARGET-A",
        });
        HttpResponseMessage conflict = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = "immutable-target",
            deviceUdid = "PHONE-TARGET-B",
        });
        var error = await conflict.Content.ReadFromJsonAsync<OperationErrorDto>();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("idempotency-target-conflict", error?.error);
        Assert.Equal(0, controller.PairCalls);
    }

    [Fact]
    public async Task DeviceEnrollment_RetryAfterDenialProbesTrustAndNeverPairsAgain()
    {
        var controller = new EnrollmentDeviceController { PairOutcome = "untrusted" };
        controller.SetDevices(EnrollmentDeviceController.Device("PHONE-RETRY", "untrusted"));
        using var factory = Factory(apiToken: "s3cr3t-token", deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = "denied-source",
            deviceUdid = "PHONE-RETRY",
        });
        EnrollmentOperationDto source = (await started.Content.ReadFromJsonAsync<EnrollmentOperationDto>())!;
        EnrollmentOperationDto denied = await WaitForEnrollmentTerminalAsync(client, source.operationId);
        Assert.Equal("failed", denied.status);
        Assert.Equal(1, controller.PairCalls);

        // Trust became available out-of-band after the first request. Retry must
        // consume that evidence passively and must not display Trust again.
        controller.SetDevices(EnrollmentDeviceController.Device("PHONE-RETRY", "trusted"));
        HttpResponseMessage retried = await client.PostAsJsonAsync($"/api/operations/{source.operationId}/retry", new
        {
            idempotencyKey = "denied-retry",
        });
        EnrollmentOperationDto retry = (await retried.Content.ReadFromJsonAsync<EnrollmentOperationDto>())!;
        EnrollmentOperationDto completed = await WaitForEnrollmentTerminalAsync(client, retry.operationId);

        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(source.operationId, retry.parentOperationId);
        Assert.Equal("succeeded", completed.status);
        Assert.Equal(1, controller.PairCalls);
    }

    [Theory]
    [InlineData("locked", "locked", "device-locked", 0)]
    [InlineData("untrusted", "untrusted", "device-lockdown-untrusted", 1)]
    public async Task DeviceEnrollment_LockOrDenialAddsNothing(
        string initialTrust,
        string pairOutcome,
        string expectedError,
        int expectedPairCalls)
    {
        var controller = new EnrollmentDeviceController { PairOutcome = pairOutcome };
        controller.SetDevices(EnrollmentDeviceController.Device("PHONE-BLOCKED", initialTrust));
        using var factory = Factory(apiToken: "s3cr3t-token", deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage started = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = $"blocked-{initialTrust}",
            deviceUdid = "PHONE-BLOCKED",
        });
        EnrollmentOperationDto submitted = (await started.Content.ReadFromJsonAsync<EnrollmentOperationDto>())!;
        EnrollmentOperationDto failed = await WaitForEnrollmentTerminalAsync(client, submitted.operationId);

        Assert.Equal("failed", failed.status);
        Assert.Equal(expectedError, failed.error?.code);
        Assert.Equal(expectedPairCalls, controller.PairCalls);
        Assert.Empty((await client.GetFromJsonAsync<IReadOnlyList<AcceptedKnownDeviceDto>>("/api/devices/known?includeReachable=false"))!);
    }

    [Fact]
    public async Task DeviceEnrollment_SelectedWifiDeviceIsRejectedBeforeOperationCreation()
    {
        var controller = new EnrollmentDeviceController();
        controller.SetDevices(EnrollmentDeviceController.Device("PHONE-WIFI", "trusted", connection: DeviceConnection.Wifi));
        using var factory = Factory(apiToken: "s3cr3t-token", deviceController: controller);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        await BootstrapWorkspaceOwnerAsync(factory);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/devices/enrollments", new
        {
            idempotencyKey = "wifi-is-not-first-pairing",
            deviceUdid = "PHONE-WIFI",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();
        Assert.Equal("selected-device-ineligible", error?.error);
        Assert.Equal(0, controller.PairCalls);
    }

    [Fact]
    public async Task DeviceEnrollment_RestartAfterPairRequestRecoversPassivelyWithoutPairingAgain()
    {
        string stateDir = Path.Combine(TestDir(), "state");
        string operationId;
        var firstController = new EnrollmentDeviceController();
        firstController.SetDevices(EnrollmentDeviceController.Device("PHONE-RECOVERY", "untrusted"));

        using (var factory = Factory(
                   apiToken: "s3cr3t-token",
                   stateDirectory: stateDir,
                   operationWorker: false,
                   deviceController: firstController))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            await BootstrapWorkspaceOwnerAsync(factory);
            HttpResponseMessage started = await client.PostAsJsonAsync("/api/devices/enrollments", new
            {
                idempotencyKey = "restart-recovery",
                deviceUdid = "PHONE-RECOVERY",
            });
            EnrollmentOperationDto submitted = (await started.Content.ReadFromJsonAsync<EnrollmentOperationDto>())!;
            operationId = submitted.operationId;

            OperationStore store = factory.Services.GetRequiredService<OperationStore>();
            DateTimeOffset pairingRequestedAt = DateTimeOffset.UtcNow;
            await store.TransitionAsync(operationId, existing => existing with
            {
                Status = "running",
                UpdatedAt = pairingRequestedAt,
                DevicePairingRequestedAt = pairingRequestedAt,
                Cancelable = false,
            });
        }

        var recoveryController = new EnrollmentDeviceController();
        recoveryController.SetDevices(EnrollmentDeviceController.Device("PHONE-RECOVERY", "trusted"));
        using (var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, deviceController: recoveryController))
        using (HttpClient client = factory.CreateClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
            EnrollmentOperationDto recovered = await WaitForEnrollmentTerminalAsync(client, operationId);

            Assert.Equal("succeeded", recovered.status);
            Assert.Equal(0, recoveryController.PairCalls);
            AcceptedKnownDeviceDto accepted = Assert.Single((await client.GetFromJsonAsync<IReadOnlyList<AcceptedKnownDeviceDto>>("/api/devices/known?includeReachable=false"))!);
            Assert.Equal(operationId, accepted.enrollmentOperationId);
        }
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
    public async Task Workspace_WithBearerToken_RequiresExplicitOwnerBootstrap()
    {
        using var factory = Factory(apiToken: "s3cr3t-token");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");

        HttpResponseMessage response = await client.GetAsync("/api/workspace");
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("workspace-bootstrap-required", error?.error);
    }

    [Fact]
    public async Task Workspace_WithoutAuthenticationConfigured_FailsClosed()
    {
        using var factory = Factory(apiToken: null);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/workspace");
        var error = await response.Content.ReadFromJsonAsync<OperationErrorDto>();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("authentication-required", error?.error);
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
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal());
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
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);
        _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");

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
        _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");

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
        Assert.Equal("recovery-bearer", operation.actor.kind);
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
    public async Task RefreshWorker_ReadbackMismatchBecomesUnknownWithoutSchedulerEvidence()
    {
        string dir = TestDir();
        const string bundleId = "com.example.refreshreadbackmismatch";
        string ipaPath = WriteTestIpa(dir, bundleId, "Readback Mismatch", "1", "1.0");
        var controller = new ExistingInstallDeviceController("TEST-UDID", bundleId, "2.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: Path.Combine(dir, "state"),
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: controller);
        using HttpClient client = HttpsTokenClient(factory);
        Assert.Equal(
            HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/apps", Registration(bundleId, ipaPath))).StatusCode);
        Sideport.Api.Operations.OperationRecordDto priorEvidence =
            await SeedVerifiedRefreshPrerequisitesAsync(
                factory,
                client,
                bundleId,
                expiresAt: DateTimeOffset.UtcNow.AddDays(1));

        HttpResponseMessage submitted = await client.PostAsJsonAsync(
            "/api/operations/refresh",
            new { deviceUdid = "TEST-UDID", bundleId, idempotencyKey = "refresh-readback-mismatch" });
        OperationRecordDto queued = (await submitted.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        await factory.Services.GetRequiredService<OperationService>()
            .ProcessQueuedOperationAsync(queued.operationId);

        OperationRecordDto terminal = (await client.GetFromJsonAsync<OperationRecordDto>(
            $"/api/operations/{queued.operationId}"))!;
        Assert.Equal("unknown", terminal.status);
        Assert.Equal("install-verification-unknown", terminal.error?.code);
        Assert.False(terminal.result?.success);
        Assert.Null(terminal.result?.version);
        Assert.Equal(1, controller.InstallCalls);
        Assert.Equal(1, controller.InstalledAppReads);
        Assert.Equal(
            priorEvidence.OperationId,
            (await factory.Services.GetRequiredService<IAppRegistry>()
                .FindAsync("TEST-UDID", bundleId))!.LastVerifiedOperationId);

        SchedulerSettingsStore settings = factory.Services.GetRequiredService<SchedulerSettingsStore>();
        _ = await settings.SetEnabledAsync(true);
        var scheduler = new OperationScheduler(
            factory.Services.GetRequiredService<IAppRegistry>(),
            factory.Services.GetRequiredService<OperationService>(),
            factory.Services.GetRequiredService<OperationStore>(),
            settings,
            factory.Services.GetRequiredService<OrchestratorOptions>());
        await scheduler.RunOnceAsync(CancellationToken.None);

        SchedulerSettingsState schedulerState = (await settings.ReadAsync())!;
        Assert.Equal(1, schedulerState.LastEvaluation?.DueCount);
        Assert.Equal(1, schedulerState.LastEvaluation?.BlockedCount);
        Assert.Equal(0, schedulerState.LastEvaluation?.QueuedCount);
        Assert.DoesNotContain(
            await factory.Services.GetRequiredService<OperationStore>().ListAsync(limit: null),
            operation => operation.Actor.DisplayName == "system:scheduler");
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
    public async Task RefreshOperation_DifferentKeyWhileActiveIsBlockedAndNotQueued()
    {
        string dir = TestDir();
        const string bundleId = "ro.hont.certcountdown";
        string ipaPath = WriteTestIpa(dir, bundleId, "Cert Clock", "1", "0.1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false);
        using HttpClient client = HttpsTokenClient(factory);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration(bundleId, ipaPath))).StatusCode);
        _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, bundleId);

        HttpResponseMessage first = await client.PostAsJsonAsync(
            "/api/operations/refresh",
            new { deviceUdid = "TEST-UDID", bundleId, idempotencyKey = "active-first" });
        HttpResponseMessage second = await client.PostAsJsonAsync(
            "/api/operations/refresh",
            new { deviceUdid = "TEST-UDID", bundleId, idempotencyKey = "active-second" });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        OperationRecordDto blocked = (await second.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        Assert.Equal("blocked", blocked.status);
        Assert.Equal("device-operation-active", blocked.error?.code);
    }

    [Fact]
    public async Task RefreshWorker_IdentityReadFailureBecomesRetryableTerminalBlock()
    {
        string dir = TestDir();
        const string bundleId = "ro.hont.certcountdown";
        string ipaPath = WriteTestIpa(dir, bundleId, "Cert Clock", "1", "0.1.0");
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            signingIdentityProvider: new FailSecondInspectionSigningIdentityProvider());
        using HttpClient client = HttpsTokenClient(factory);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration(bundleId, ipaPath))).StatusCode);
        _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, bundleId);

        HttpResponseMessage submitted = await client.PostAsJsonAsync(
            "/api/operations/refresh",
            new { deviceUdid = "TEST-UDID", bundleId, idempotencyKey = "runtime-preflight-failure" });
        OperationRecordDto queued = (await submitted.Content.ReadFromJsonAsync<OperationRecordDto>())!;
        await factory.Services.GetRequiredService<OperationService>()
            .ProcessQueuedOperationAsync(queued.operationId);
        OperationRecordDto terminal = (await client.GetFromJsonAsync<OperationRecordDto>(
            $"/api/operations/{queued.operationId}"))!;

        Assert.Equal("blocked", terminal.status);
        Assert.True(terminal.retryable);
        Assert.Equal("signing-identity-unavailable", terminal.error?.code);
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
        Directory.CreateDirectory(stateDir);
        using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        Directory.CreateDirectory(Path.Combine(stateDir, "operations.json"));

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
                Assert.Equal("blocked", failed.status);

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
                _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");
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
                string stateDir = Path.Combine(dir, "state");
                string ipaPath = WriteTestIpa(dir, "ro.hont.certcountdown", "Cert Clock", "1", "0.1.0");
                using var factory = Factory(apiToken: "s3cr3t-token", stateDirectory: stateDir, personalAppleId: "developer@example.com", personalApplePassword: "configured-host-secret", personalApplePortal: new StubApplePortal());
                using HttpClient client = factory.CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
                string ownerMemberId = await BootstrapWorkspaceOwnerAsync(factory);
                Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/apps", Registration("ro.hont.certcountdown", ipaPath))).StatusCode);

                IAppRegistry registry = factory.Services.GetRequiredService<IAppRegistry>();
                OperationStore operationStore = factory.Services.GetRequiredService<OperationStore>();
                var schedulerSettings = new SchedulerSettingsStore(Path.Combine(stateDir, "scheduler.json"));
                await schedulerSettings.InitializeAsync(requestedEnabled: true, prerequisitesSatisfied: false);
                var scheduler = new OperationScheduler(
                    registry,
                    factory.Services.GetRequiredService<OperationService>(),
                    operationStore,
                    schedulerSettings,
                    factory.Services.GetRequiredService<OrchestratorOptions>(),
                    executionAuthorization: factory.Services.GetRequiredService<WorkspaceExecutionAuthorizer>());

                // Bootstrap config may request scheduling, but the durable store
                // remains safely disabled until V2 prerequisites are proved.
                await scheduler.RunOnceAsync(CancellationToken.None);
                Assert.DoesNotContain(
                    await operationStore.ListAsync(limit: null),
                    operation => operation.Actor.DisplayName == "system:scheduler");

                await schedulerSettings.SetEnabledAsync(true);

                // Legacy registrations default to active, but remain ineligible
                // until they point at durable verification evidence.
                await scheduler.RunOnceAsync(CancellationToken.None);
                Assert.DoesNotContain(
                    await operationStore.ListAsync(limit: null),
                    operation => operation.Actor.DisplayName == "system:scheduler");

                DateTimeOffset now = DateTimeOffset.UtcNow;
                Sideport.Api.Operations.OperationRecordDto verified =
                    await SeedVerifiedRefreshPrerequisitesAsync(
                        factory,
                        client,
                        "ro.hont.certcountdown",
                        now.AddMinutes(30));
                KnownDeviceStore knownDevices = factory.Services.GetRequiredService<KnownDeviceStore>();
                KnownDeviceRecord schedulerDevice = (await knownDevices.FindAsync("TEST-UDID"))!;
                await knownDevices.UpsertAsync(schedulerDevice with { OwnerMemberId = ownerMemberId });

                const string unknownOperationId = "op_scheduler_unknown";
                var unknown = verified with
                {
                    OperationId = unknownOperationId,
                    Type = "refresh",
                    Status = "unknown",
                    CreatedAt = now.AddMinutes(-1),
                    StartedAt = now.AddMinutes(-1),
                    UpdatedAt = now.AddMinutes(-1),
                    CompletedAt = null,
                    IdempotencyKey = "scheduler-unknown",
                    Result = null,
                    Error = new Sideport.Api.Operations.OperationIssueDto(
                        "operation-terminal-state-unknown",
                        "The prior device operation requires reconciliation."),
                    CorrelationId = unknownOperationId,
                };
                await operationStore.AddIfIdempotentMissingAsync(unknown);

                // An unresolved operation quarantines the whole device.
                await scheduler.RunOnceAsync(CancellationToken.None);
                Assert.DoesNotContain(
                    await operationStore.ListAsync(limit: null),
                    operation => operation.Actor.DisplayName == "system:scheduler");

                await operationStore.TransitionAsync(unknownOperationId, existing => existing with
                {
                    Status = "failed",
                    UpdatedAt = now,
                    CompletedAt = now,
                });
                await scheduler.RunOnceAsync(CancellationToken.None);
                var operations = await client.GetFromJsonAsync<IReadOnlyList<OperationRecordDto>>("/api/operations");

                Assert.NotNull(operations);
                OperationRecordDto operation = Assert.Single(
                    operations!,
                    item => item.actor.displayName == "system:scheduler");
                Assert.Equal("system", operation.actor.kind);
                Assert.Equal("system:scheduler", operation.actor.displayName);
                Assert.False(
                    string.Equals(operation.status, "blocked", StringComparison.Ordinal),
                    $"Scheduler refresh was blocked: {operation.error?.code} {operation.error?.message}");
                await WaitForTerminalOperationAsync(client, operation.operationId);

                SchedulerSettingsState settings = (await schedulerSettings.ReadAsync())!;
                Assert.Equal(3, settings.Evaluations.Count);
                Assert.Equal("succeeded", settings.LastEvaluation?.Outcome);
                Assert.Equal(1, settings.LastEvaluation?.QueuedCount);
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

                Assert.Equal("blocked", operation.status);
                Assert.Equal("registration-missing", operation.error!.code);
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
                    Assert.Equal("blocked", operation.status);

                    var issues = await client.GetFromJsonAsync<IReadOnlyList<DiagnosticIssueDto>>("/api/diagnostics/issues");
                    DiagnosticIssueDto issue = Assert.Single(issues!);
                    Assert.Equal("registration-verification-required", issue.category);
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
            _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");
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
            _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");
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
            _ = await SeedVerifiedRefreshPrerequisitesAsync(factory, client, "ro.hont.certcountdown");
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
                Assert.Equal("blocked", operation!.status);
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
            Assert.Equal("blocked", renewal.status);
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
                Assert.Equal("unknown", operation!.status);
                Assert.Equal("operation-terminal-state-unknown", operation.error!.code);
                Assert.False(operation.retryable);
        }

    private sealed record ReadyDto(bool ready);
    private sealed record SystemStatusCheckResponse(string id, string status);
    private sealed record SystemStatusResponse(bool operational, IReadOnlyList<SystemStatusCheckResponse> checks);
    private sealed record SchedulerPolicyResponse(string mode, TimeSpan evaluationInterval);
    private sealed record SchedulerConcurrencyResponse(string lockState);
    private sealed record SchedulerHistoryRetentionResponse(int maxEvaluations);
    private sealed record SchedulerStatusResponse(
        bool enabled,
        SchedulerPolicyResponse policy,
        SchedulerConcurrencyResponse concurrency,
        SchedulerHistoryRetentionResponse historyRetention,
        DateTimeOffset? nextEvaluationAt = null);
    private sealed record OnboardingDto(bool firstRunComplete, IReadOnlyList<OnboardingStepDto> steps);
    private sealed record OnboardingRegistrationKeyDto(string deviceUdid, string bundleId);
    private sealed record OnboardingCompletionDto(
        string verifiedOperationId,
        OnboardingRegistrationKeyDto registrationKey);
    private sealed record OnboardingInstallStatusDto(
        bool firstRunComplete,
        string setupState,
        string? selectedCatalogAppId,
        string? activeInstallOperationId,
        OnboardingCompletionDto? completionReceipt);
    private sealed record OnboardingStepDto(string id, string state, string surface);
    private sealed record VerifiedInstallCrashFixture(
        string OperationId,
        string AccountProfileId,
        string IdempotencyKey,
        DateTimeOffset ExpiresAt,
        DateTimeOffset? ReceiptCompletedAt);
    private sealed record LogDto(string at, string level, string category, string message);
    private sealed record CatalogAppDto(
        string id,
        string status,
        string bundleId,
        string? shortVersion,
        bool hasEmbeddedProfile,
        string? sha256,
        string? ipaPath = null);
    private sealed record CatalogUploadErrorDto(string error, string? message, string? detail, long? limit, string? id);
    private sealed record RegisteredAppDto(
        string bundleId,
        string deviceUdid,
        string? lifecycle = null,
        string? lastVerifiedOperationId = null);
    private sealed record AppleAccessStatusDto(string state, string? keyIdSuffix, string? issuerIdSuffix, IReadOnlyList<AppleAccessCapabilityDto> capabilities);
    private sealed record AppleAccessCapabilityDto(string id, string state, int? httpStatus, int? count);
    private sealed record PersonalAppleStatusDto(string state, string? appleIdHint, string? pendingChallengeId, IReadOnlyList<PersonalAppleTeamDto> teams);
    private sealed record PersonalAppleTeamDto(string teamId, string name, string type);
    private sealed record OperationTargetDto(
        string? deviceUdid,
        string? bundleId,
        string? catalogAppId,
        string? accountProfileId,
        int? catalogVersion);
    private sealed record OperationSigningReadinessDto(
        string localIdentityState,
        int appleCertificateCount,
        string impact,
        bool requiresCutover);
    private sealed record OperationPreflightCheckDto(string code, string status);
    private sealed record OperationPreflightCheckGroupDto(string id, IReadOnlyList<OperationPreflightCheckDto> checks);
    private sealed record OperationPreflightDto(
        bool ready,
        IReadOnlyList<OperationIssueDto> blockers,
        IReadOnlyList<string> plannedMutations,
        IReadOnlyList<OperationLimitDto> scarceLimits,
        bool requiresConfirmation,
        OperationTargetDto? target = null,
        IReadOnlyList<OperationIssueDto>? warnings = null,
        string? preflightId = null,
        DateTimeOffset? expiresAt = null,
        IReadOnlyList<OperationPreflightCheckGroupDto>? checkGroups = null,
        string? inventoryVersion = null,
        string? planVersion = null,
        OperationSigningReadinessDto? signing = null);
    private sealed class OperationIssueDto
    {
        public string code { get; set; } = "";
        public string message { get; set; } = "";
        public string? source { get; set; }
        public string? detail { get; set; }
    }
    private sealed record OperationLimitDto(string code, int used, int limit);
    private sealed record OperationActorDto(string kind, string displayName, string? id = null);
    private sealed record OperationStageDto(string id, string status);
    private sealed record OperationResultDto(
        bool success,
        string bundleId,
        DateTimeOffset? expiresAt,
        string? error,
        string? version = null,
        bool? safeToRerun = null,
        string? reconciledOperationId = null);
    private sealed record InstallIntentDto(
        string deviceUdid,
        string catalogAppId,
        string accountProfileId,
        string bundleId,
        bool finishOnboarding,
        string? preflightId = null,
        string? planVersion = null,
        string? inventoryVersion = null,
        bool confirmedPlannedMutations = false);
    private sealed record ConfirmedInstallRequestDto(
        string deviceUdid,
        string bundleId,
        string catalogAppId,
        string accountProfileId,
        string preflightId,
        string planVersion,
        bool finishOnboarding,
        bool confirmedPlannedMutations,
        string idempotencyKey);
    private sealed record OperationRecordDto(
        string operationId,
        string type,
        string status,
        OperationActorDto actor,
        string? idempotencyKey,
        IReadOnlyList<OperationStageDto> stages,
        OperationResultDto? result,
        OperationIssueDto? error,
        bool retryable,
        string? parentOperationId = null,
        InstallIntentDto? installIntent = null);
    private sealed record EnrollmentCandidateDto(string udidSuffix, string displayName, string? productType, string? osVersion, string connection);
    private sealed record EnrollmentOperationDto(string operationId, string type, string status, OperationIssueDto? error, IReadOnlyList<EnrollmentCandidateDto>? candidateDevices, string? parentOperationId = null);
    private sealed record AcceptedKnownDeviceDto(string udid, string inventoryState, DateTimeOffset? acceptedAt, string? acceptedBy, string? enrollmentOperationId);
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

    private static HttpClient HttpsTokenClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "s3cr3t-token");
        return client;
    }

    private static async Task<string> BootstrapWorkspaceOwnerAsync(WebApplicationFactory<Program> factory)
    {
        WorkspaceAccessStore store = factory.Services.GetRequiredService<WorkspaceAccessStore>();
        WorkspaceAccessDocument? existing = await store.ReadAsync();
        if (existing?.Workspace.State == WorkspaceLifecycleState.Active &&
            !string.IsNullOrWhiteSpace(existing.Workspace.OwnerMemberId))
        {
            return existing.Workspace.OwnerMemberId;
        }

        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(new(
            ExpectedOwnerMemberId: null,
            ImpactVersion: null,
            Lifetime: TimeSpan.FromMinutes(15),
            IdempotencyKey: "api-smoke-owner-bootstrap",
            RequestId: "req-api-smoke-owner-bootstrap"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(
            claim.Token!,
            "req-api-smoke-owner-handoff");
        WorkspaceAcceptanceResult accepted = await store.AcceptOwnerClaimAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "api-smoke-owner"),
                "Owner",
                "owner@example.test",
                "api-smoke-owner-accept",
                "req-api-smoke-owner-accept"));
        return accepted.Member.MemberId;
    }

    private static async Task<string> PrepareAppleTeamAsync(HttpClient client)
    {
        HttpResponseMessage signedIn = await client.PostAsJsonAsync(
            "/api/apple-access/personal/sign-in",
            new { });
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using JsonDocument document = JsonDocument.Parse(await signedIn.Content.ReadAsStringAsync());
        string accountProfileId = document.RootElement.GetProperty("accountProfileId").GetString()!;

        HttpResponseMessage selected = await client.PutAsJsonAsync(
            "/api/apple-access/personal/team",
            new { accountProfileId, teamId = "TEAMID1234" });
        Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
        return accountProfileId;
    }

    private static async Task<OperationPreflightDto> GetInstallPreflightAsync(
        HttpClient client,
        string deviceUdid,
        string bundleId,
        bool finishOnboarding,
        string? catalogAppId = "cert-clock",
        string? accountProfileId = null)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/operations/preflight", new
        {
            type = "install",
            deviceUdid,
            bundleId,
            finishOnboarding,
            catalogAppId,
            accountProfileId,
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OperationPreflightDto>())!;
    }

    private static async Task<ConfirmedInstallRequestDto> ConfirmedInstallRequestAsync(
        HttpClient client,
        string deviceUdid,
        string bundleId,
        string accountProfileId,
        bool finishOnboarding,
        string idempotencyKey,
        string catalogAppId = "cert-clock")
    {
        OperationPreflightDto preflight = await GetInstallPreflightAsync(
            client,
            deviceUdid,
            bundleId,
            finishOnboarding,
            catalogAppId,
            accountProfileId);
        Assert.True(preflight.ready, string.Join("; ", preflight.blockers.Select(blocker => blocker.code)));
        Assert.NotNull(preflight.preflightId);
        Assert.NotNull(preflight.planVersion);
        return new ConfirmedInstallRequestDto(
            deviceUdid,
            bundleId,
            catalogAppId,
            accountProfileId,
            preflight.preflightId!,
            preflight.planVersion!,
            finishOnboarding,
            confirmedPlannedMutations: true,
            idempotencyKey);
    }

    private static async Task AcceptKnownDeviceAsync(
        WebApplicationFactory<Program> factory,
        string udid)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await factory.Services.GetRequiredService<KnownDeviceStore>().UpsertAsync(new KnownDeviceRecord(
            udid,
            "Test iPhone",
            "iPhone15,2",
            "18.5",
            "usb",
            now,
            now,
            "enrollment",
            now,
            "trusted",
            Owner: null,
            Notes: null,
            UpdatedAt: now,
            InventoryState: "accepted",
            AcceptedAt: now,
            AcceptedBy: "api-token:api-token-client",
            EnrollmentOperationId: "op_enrollment_test",
            TrustReason: "Lockdown session verified over USB.",
            LockdownCheckedAt: now,
            UsableForInstall: true));
    }

    private static async Task<Sideport.Api.Operations.OperationRecordDto> SeedUnknownRefreshAsync(
        WebApplicationFactory<Program> factory,
        string bundleId,
        string ipaPath,
        DateTimeOffset expectedExpiry,
        string operationId)
    {
        const string appleId = "developer@example.com";
        const string teamId = "TEAMID1234";
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(ipaPath)));
        DateTimeOffset observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        string priorOperationId = $"{operationId}_prior";
        await factory.Services.GetRequiredService<IAppRegistry>().UpsertAsync(new AppRegistration(
            bundleId,
            appleId,
            teamId,
            "TEST-UDID",
            ipaPath,
            Lifecycle: "active",
            LastVerifiedOperationId: priorOperationId));

        var target = new Sideport.Api.Operations.OperationTargetDto(
            "TEST-UDID",
            bundleId,
            TeamId: teamId,
            Kind: "app",
            AccountProfileId: Sideport.Api.AppleAccess.AppleAccountIdentity.ProfileIdFor(appleId),
            Version: "1.0",
            CatalogSha256: sha256);
        var prior = new Sideport.Api.Operations.OperationRecordDto(
            priorOperationId,
            "verify-existing-registration",
            "succeeded",
            observedAt.AddMinutes(-3),
            observedAt.AddMinutes(-3),
            observedAt.AddMinutes(-2),
            observedAt.AddMinutes(-2),
            new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client"),
            $"{priorOperationId}-key",
            Attempt: 1,
            target,
            [new Sideport.Api.Operations.OperationStageDto(
                "verify", "Verify existing app", "succeeded", observedAt.AddMinutes(-3), observedAt.AddMinutes(-2), "Verified.")],
            new Sideport.Api.Operations.OperationResultDto(
                true,
                bundleId,
                DateTimeOffset.UtcNow.AddMinutes(30),
                null,
                Version: "1.0"),
            Error: null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: priorOperationId);
        (Sideport.Api.Operations.OperationRecordDto _, bool priorCreated) = await factory.Services
            .GetRequiredService<OperationStore>()
            .AddIfIdempotentMissingAsync(prior);
        Assert.True(priorCreated);

        var issue = new Sideport.Api.Operations.OperationIssueDto(
            "install-outcome-unknown",
            "The original transfer outcome is unknown.");
        var source = new Sideport.Api.Operations.OperationRecordDto(
            operationId,
            "refresh",
            "unknown",
            observedAt.AddMinutes(-1),
            observedAt.AddMinutes(-1),
            observedAt,
            CompletedAt: null,
            new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client"),
            $"{operationId}-key",
            Attempt: 1,
            target,
            [
                new Sideport.Api.Operations.OperationStageDto(
                    "preflight", "Preflight", "succeeded", observedAt.AddMinutes(-1), observedAt.AddMinutes(-1), "Ready."),
                new Sideport.Api.Operations.OperationStageDto(
                    "refresh", "Sign and install", "unknown", observedAt, null, issue.Message, issue),
            ],
            new Sideport.Api.Operations.OperationResultDto(
                false,
                bundleId,
                expectedExpiry,
                issue.Message),
            issue,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId);
        (Sideport.Api.Operations.OperationRecordDto stored, bool created) = await factory.Services
            .GetRequiredService<OperationStore>()
            .AddIfIdempotentMissingAsync(source);
        Assert.True(created);
        return stored;
    }

    private static async Task<Sideport.Api.Operations.OperationRecordDto>
        SeedVerifiedRefreshPrerequisitesAsync(
            WebApplicationFactory<Program> factory,
            HttpClient client,
            string bundleId,
            DateTimeOffset? expiresAt = null)
    {
        _ = client;
        using (HttpClient protectedClient = HttpsTokenClient(factory))
            _ = await PrepareAppleTeamAsync(protectedClient);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        IAppRegistry registry = factory.Services.GetRequiredService<IAppRegistry>();
        AppRegistration registration = (await registry.FindAsync("TEST-UDID", bundleId))!;
        using (PreparedSigningInputs prepared = await factory.Services
                   .GetRequiredService<ISigningIdentityProvider>()
                   .PrepareAsync(
                       new AppleSession(registration.AppleId, "adsid", registration.AppleId, [1, 2, 3])
                       {
                           IdmsToken = "test-token",
                       },
                       registration.TeamId,
                       bundleId,
                       registration.DeviceUdid))
        {
            Assert.True(File.Exists(prepared.Pkcs12Path));
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string operationId = $"op_verified_{Guid.NewGuid():N}";
        DateTimeOffset verifiedExpiry = expiresAt ?? now.AddDays(7);
        var evidence = new Sideport.Api.Operations.OperationRecordDto(
            operationId,
            "install",
            "succeeded",
            now.AddMinutes(-2),
            now.AddMinutes(-2),
            now.AddMinutes(-1),
            now.AddMinutes(-1),
            new Sideport.Api.Operations.OperationActorDto("api-token", "api-token-client"),
            $"verification-{operationId}",
            Attempt: 1,
            new Sideport.Api.Operations.OperationTargetDto(
                registration.DeviceUdid,
                bundleId,
                TeamId: registration.TeamId,
                Kind: "app"),
            Stages:
            [
                new Sideport.Api.Operations.OperationStageDto(
                    "preflight", "Preflight", "succeeded", now.AddMinutes(-2), now.AddMinutes(-2), "Ready."),
                new Sideport.Api.Operations.OperationStageDto(
                    "install", "Sign and install", "succeeded", now.AddMinutes(-2), now.AddMinutes(-1), "Installed."),
                new Sideport.Api.Operations.OperationStageDto(
                    "verify", "Verify on iPhone", "succeeded", now.AddMinutes(-1), now.AddMinutes(-1), "Verified."),
            ],
            new Sideport.Api.Operations.OperationResultDto(true, bundleId, verifiedExpiry, null, Version: "1.0"),
            Error: null,
            Cancelable: false,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId);
        (Sideport.Api.Operations.OperationRecordDto persisted, _) = await factory.Services
            .GetRequiredService<OperationStore>()
            .AddIfIdempotentMissingAsync(evidence);
        await registry.UpsertAsync(registration with
        {
            Lifecycle = "active",
            ActivatedAt = now.AddMinutes(-1),
            LastVerifiedOperationId = persisted.OperationId,
        });
        return persisted;
    }

    private static async Task<VerifiedInstallCrashFixture> SeedVerifiedInstallCrashStateAsync(
        string stateDirectory,
        string ipaPath,
        string bundleId,
        bool writeReceipt)
    {
        const string idempotencyKey = "recover-verified-install";
        using var factory = Factory(
            apiToken: "s3cr3t-token",
            stateDirectory: stateDirectory,
            seedCatalogPath: ipaPath,
            personalAppleId: "developer@example.com",
            personalApplePassword: "configured-host-secret",
            personalApplePortal: new StubApplePortal(),
            operationWorker: false,
            deviceController: new FirstInstallDeviceController("TEST-UDID", bundleId),
            schedulerEnabled: true);
        using HttpClient client = HttpsTokenClient(factory);
        string accountProfileId = await PrepareAppleTeamAsync(client);
        await AcceptKnownDeviceAsync(factory, "TEST-UDID");

        ConfirmedInstallRequestDto request = await ConfirmedInstallRequestAsync(
            client,
            "TEST-UDID",
            bundleId,
            accountProfileId,
            finishOnboarding: true,
            idempotencyKey);
        HttpResponseMessage queued = await client.PostAsJsonAsync("/api/operations/install", request);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
        OperationRecordDto initial = (await queued.Content.ReadFromJsonAsync<OperationRecordDto>())!;

        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddHours(-2);
        DateTimeOffset installedAt = startedAt.AddMinutes(1);
        DateTimeOffset verifiedAt = startedAt.AddMinutes(2);
        DateTimeOffset activatedAt = startedAt.AddMinutes(3);
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddDays(7);
        OperationStore operations = factory.Services.GetRequiredService<OperationStore>();
        Sideport.Api.Operations.OperationRecordDto persisted = (await operations.TransitionAsync(
            initial.operationId,
            existing => existing with
            {
                Status = "running",
                CreatedAt = startedAt,
                StartedAt = startedAt,
                UpdatedAt = activatedAt,
                CompletedAt = null,
                Result = new Sideport.Api.Operations.OperationResultDto(
                    true,
                    bundleId,
                    expiresAt,
                    null,
                    Version: "1.0"),
                Error = null,
                Cancelable = false,
                Retryable = false,
                Rerunnable = false,
                Stages = existing.Stages.Select(stage => stage.Id switch
                {
                    "preflight" => stage with
                    {
                        Status = "succeeded",
                        StartedAt = startedAt,
                        CompletedAt = startedAt,
                    },
                    "install" => stage with
                    {
                        Status = "succeeded",
                        StartedAt = startedAt,
                        CompletedAt = installedAt,
                        Message = "The app was installed.",
                    },
                    "verify" => stage with
                    {
                        Status = "succeeded",
                        StartedAt = installedAt,
                        CompletedAt = verifiedAt,
                        Message = "The bundle and signature expiry were verified on the iPhone.",
                    },
                    "activate-registration" => stage with
                    {
                        Status = "running",
                        StartedAt = verifiedAt,
                        CompletedAt = null,
                        Message = "Enabling automatic refresh for this app.",
                    },
                    _ => stage,
                }).ToArray(),
            }))!;

        IAppRegistry registry = factory.Services.GetRequiredService<IAppRegistry>();
        AppRegistration pending = (await registry.FindAsync("TEST-UDID", bundleId))!;
        AppRegistration activated = pending with
        {
            Lifecycle = "active",
            ActivatedAt = activatedAt,
            LastVerifiedOperationId = initial.operationId,
        };
        await registry.UpsertAsync(activated);

        DateTimeOffset? receiptCompletedAt = null;
        if (writeReceipt)
        {
            receiptCompletedAt = activatedAt.AddMinutes(1);
            await factory.Services.GetRequiredService<OnboardingCompletionStore>().CreateAsync(
                new OnboardingCompletionReceipt(
                    OnboardingCompletionStore.CurrentSchemaVersion,
                    receiptCompletedAt.Value,
                    persisted.Actor,
                    accountProfileId,
                    activated.TeamId,
                    activated.DeviceUdid,
                    persisted.InstallIntent!.CatalogAppId,
                    persisted.InstallIntent.CatalogVersion!.Value,
                    persisted.InstallIntent.CatalogSha256!,
                    activated.BundleId,
                    persisted.OperationId,
                    SchedulerSettingsVersion: "settings_1",
                    OperationalCheckedAt: receiptCompletedAt.Value));
        }
        else
        {
            Assert.Null(await factory.Services.GetRequiredService<OnboardingCompletionStore>().ReadAsync());
        }

        return new VerifiedInstallCrashFixture(
            initial.operationId,
            accountProfileId,
            idempotencyKey,
            expiresAt,
            receiptCompletedAt);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);

    private static async Task<OperationRecordDto?> PostOperationAsync(HttpClient client, object request) =>
        await (await client.PostAsJsonAsync("/api/operations/refresh", request)).Content.ReadFromJsonAsync<OperationRecordDto>();

    private static async Task<OperationRecordDto> WaitForTerminalOperationAsync(HttpClient client, string operationId)
    {
        for (int i = 0; i < 100; i++)
        {
            OperationRecordDto? operation = await client.GetFromJsonAsync<OperationRecordDto>($"/api/operations/{operationId}");
            Assert.NotNull(operation);
            if (operation!.status is "blocked" or "succeeded" or "failed" or "canceled" or "recovery-required")
                return operation;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Operation {operationId} did not reach a terminal state.");
    }

    private static async Task<EnrollmentOperationDto> WaitForEnrollmentTerminalAsync(HttpClient client, string operationId)
    {
        for (int i = 0; i < 150; i++)
        {
            EnrollmentOperationDto? operation = await client.GetFromJsonAsync<EnrollmentOperationDto>($"/api/operations/{operationId}");
            Assert.NotNull(operation);
            if (operation!.status is "blocked" or "succeeded" or "failed" or "canceled" or "recovery-required")
                return operation;
            await Task.Delay(20);
        }

        throw new TimeoutException($"Enrollment {operationId} did not reach a terminal state.");
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
        public List<AppleDevelopmentCertificate> Certificates { get; } = [];

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

        public Task<IReadOnlyList<AppleDevelopmentCertificate>> ListDevelopmentCertificatesAsync(
            AppleSession session,
            string teamId,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AppleDevelopmentCertificate>>([.. Certificates]);

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
            healthy
                ? Task.FromResult(new AnisetteHeaders("M", "O", "R", "L", DateTimeOffset.UnixEpoch))
                : throw new HttpRequestException("anisette unreachable");
    }

    private sealed class StubSigningIdentityProvider : ISigningIdentityProvider
    {
        private int _prepared;
        private readonly DateTimeOffset _expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        public Task<SigningIdentityInspection> InspectAsync(
            string appleId,
            string teamId,
            CancellationToken ct = default) =>
            Task.FromResult(Volatile.Read(ref _prepared) == 0
                ? new SigningIdentityInspection("missing", null, null)
                : new SigningIdentityInspection("reusable", _expiresAt, "TEST"));

        public Task<PreparedSigningInputs> PrepareAsync(AppleSession session, string teamId, string bundleId, string deviceUdid, CancellationToken ct = default)
        {
            string dir = TestDir();
            string p12 = Path.Combine(dir, "identity.p12");
            string profile = Path.Combine(dir, "profile.mobileprovision");
            File.WriteAllBytes(p12, [1, 2, 3]);
            File.WriteAllBytes(profile, [4, 5, 6]);
            Volatile.Write(ref _prepared, 1);
            return Task.FromResult(new PreparedSigningInputs(p12, "", profile, _expiresAt));
        }
    }

    private sealed class ReusableSigningIdentityProvider(string serialSuffix) : ISigningIdentityProvider
    {
        private readonly DateTimeOffset _expiresAt = DateTimeOffset.UtcNow.AddMonths(6);

        public Task<SigningIdentityInspection> InspectAsync(
            string appleId,
            string teamId,
            CancellationToken ct = default) =>
            Task.FromResult(new SigningIdentityInspection(
                "reusable",
                _expiresAt,
                serialSuffix));

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            CancellationToken ct = default)
        {
            string dir = TestDir();
            string p12 = Path.Combine(dir, "identity.p12");
            string profile = Path.Combine(dir, "profile.mobileprovision");
            File.WriteAllBytes(p12, [1, 2, 3]);
            File.WriteAllBytes(profile, [4, 5, 6]);
            return Task.FromResult(new PreparedSigningInputs(
                p12,
                "",
                profile,
                DateTimeOffset.UtcNow.AddDays(7)));
        }
    }

    private sealed class FailSecondInspectionSigningIdentityProvider : ISigningIdentityProvider
    {
        private int _inspectionCount;

        public Task<SigningIdentityInspection> InspectAsync(
            string appleId,
            string teamId,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _inspectionCount) >= 2)
                throw new IOException("simulated identity read failure");
            return Task.FromResult(new SigningIdentityInspection(
                "reusable",
                DateTimeOffset.UtcNow.AddMonths(6),
                "TEST"));
        }

        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            CancellationToken ct = default)
        {
            string dir = TestDir();
            string p12 = Path.Combine(dir, "identity.p12");
            string profile = Path.Combine(dir, "profile.mobileprovision");
            File.WriteAllBytes(p12, [1, 2, 3]);
            File.WriteAllBytes(profile, [4, 5, 6]);
            return Task.FromResult(new PreparedSigningInputs(
                p12,
                "",
                profile,
                DateTimeOffset.UtcNow.AddDays(7)));
        }
    }

    private sealed class ReplacementRequiredSigningIdentityProvider : ISigningIdentityProvider
    {
        public Task<PreparedSigningInputs> PrepareAsync(
            AppleSession session,
            string teamId,
            string bundleId,
            string deviceUdid,
            CancellationToken ct = default) =>
            Task.FromException<PreparedSigningInputs>(
                new SigningCertificateReplacementRequiredException(certificateCount: 2));
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

    private sealed class BlockingInstallDeviceController(
        string udid,
        string bundleId,
        string installedVersion) : IDeviceController
    {
        private readonly ExistingInstallDeviceController _inner =
            new(udid, bundleId, installedVersion);
        private readonly TaskCompletionSource _installStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completeInstall =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _installCalls;

        public int InstallCalls => Volatile.Read(ref _installCalls);
        public int InstalledAppReads => _inner.InstalledAppReads;

        public Task WaitForInstallStartAsync() =>
            _installStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public void CompleteInstall() => _completeInstall.TrySetResult();

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            _inner.ListDevicesAsync(ct);

        public Task<DeviceTrustProbe> ProbeTrustAsync(string requestedUdid, CancellationToken ct = default) =>
            _inner.ProbeTrustAsync(requestedUdid, ct);

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string requestedUdid,
            CancellationToken ct = default) =>
            _inner.ListInstalledAppsAsync(requestedUdid, ct);

        public async Task InstallAsync(
            string requestedUdid,
            string ipaPath,
            CancellationToken ct = default)
        {
            Assert.Equal(udid, requestedUdid);
            Assert.True(File.Exists(ipaPath));
            Interlocked.Increment(ref _installCalls);
            _installStarted.TrySetResult();
            await _completeInstall.Task.WaitAsync(ct);
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            _inner.DiagnoseAsync(ct);
    }

    private sealed class ExistingInstallDeviceController(
        string udid,
        string bundleId,
        string installedVersion,
        bool reachable = true,
        DateTimeOffset? signatureExpiresAt = null,
        bool installed = true,
        Exception? listException = null,
        Exception? trustException = null) : IDeviceController
    {
        private int _installCalls;
        private int _installedAppReads;
        private readonly DateTimeOffset _signatureExpiresAt =
            signatureExpiresAt ?? DateTimeOffset.UtcNow.AddDays(7);

        public int InstallCalls => Volatile.Read(ref _installCalls);
        public int InstalledAppReads => Volatile.Read(ref _installedAppReads);

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (listException is not null)
                return Task.FromException<IReadOnlyList<DeviceInfo>>(listException);
            if (!reachable)
                return Task.FromResult<IReadOnlyList<DeviceInfo>>([]);
            return Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                udid,
                "Existing iPhone",
                "iPhone15,2",
                "18.5",
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true)]);
        }

        public Task<DeviceTrustProbe> ProbeTrustAsync(string requestedUdid, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (trustException is not null)
                return Task.FromException<DeviceTrustProbe>(trustException);
            if (!reachable || !string.Equals(requestedUdid, udid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("device unavailable");
            return Task.FromResult(new DeviceTrustProbe(
                udid,
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true));
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string requestedUdid,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(udid, requestedUdid);
            Interlocked.Increment(ref _installedAppReads);
            if (!installed)
                return Task.FromResult<IReadOnlyList<InstalledApp>>([]);
            return Task.FromResult<IReadOnlyList<InstalledApp>>([new InstalledApp(
                bundleId,
                "Existing App",
                installedVersion,
                _signatureExpiresAt)]);
        }

        public Task InstallAsync(string requestedUdid, string ipaPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _installCalls);
            return Task.CompletedTask;
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics(reachable ? "ok" : "blocked", []));
    }

    private sealed class FirstInstallDeviceController(
        string udid,
        string bundleId,
        bool verifyInstalled = true,
        bool reachable = true,
        DeviceConnection connection = DeviceConnection.Usb) : IDeviceController
    {
        private int _installCalls;
        private int _installed;

        public int InstallCalls => Volatile.Read(ref _installCalls);

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!reachable)
                return Task.FromResult<IReadOnlyList<DeviceInfo>>([]);
            return Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                udid,
                "Test iPhone",
                "iPhone15,2",
                "18.5",
                connection,
                "trusted",
                $"Lockdown session verified over {connection}.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true)]);
        }

        public Task<DeviceTrustProbe> ProbeTrustAsync(string requestedUdid, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!reachable || !string.Equals(requestedUdid, udid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("device unavailable");
            return Task.FromResult(new DeviceTrustProbe(
                udid,
                connection,
                "trusted",
                $"Lockdown session verified over {connection}.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true));
        }

        public Task InstallAsync(string requestedUdid, string ipaPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Assert.Equal(udid, requestedUdid);
            Assert.True(File.Exists(ipaPath));
            Interlocked.Increment(ref _installCalls);
            Volatile.Write(ref _installed, 1);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string requestedUdid,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!verifyInstalled || Volatile.Read(ref _installed) == 0)
                return Task.FromResult<IReadOnlyList<InstalledApp>>([]);
            return Task.FromResult<IReadOnlyList<InstalledApp>>([new InstalledApp(
                bundleId,
                "First Install",
                "1.0",
                DateTimeOffset.UtcNow.AddDays(7))]);
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics(reachable ? "ok" : "blocked", []));
    }

    private sealed class EnrollmentDeviceController : IDeviceController
    {
        private DeviceInfo[] _devices = [];
        private int _pairCalls;

        public string PairOutcome { get; init; } = "trusted";
        public int PairCalls => Volatile.Read(ref _pairCalls);

        public static DeviceInfo Device(
            string udid,
            string trustState,
            string name = "Test iPhone",
            DeviceConnection connection = DeviceConnection.Usb)
        {
            bool trusted = string.Equals(trustState, "trusted", StringComparison.Ordinal);
            return new DeviceInfo(
                udid,
                name,
                "iPhone15,2",
                "18.5",
                connection,
                trustState,
                trusted ? $"Lockdown session verified over {connection}." : trustState == "locked" ? "The iPhone is locked." : "Trust is not established.",
                DateTimeOffset.UtcNow,
                trusted);
        }

        public void SetDevices(params DeviceInfo[] devices) => Volatile.Write(ref _devices, devices);

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<DeviceInfo>>(Volatile.Read(ref _devices).ToArray());
        }

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            DeviceInfo device = Volatile.Read(ref _devices).First(item => string.Equals(item.Udid, udid, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(new DeviceTrustProbe(
                device.Udid,
                device.Connection,
                device.TrustState,
                device.TrustReason,
                DateTimeOffset.UtcNow,
                device.UsableForInstall));
        }

        public Task<DevicePairingResult> PairAsync(
            string udid,
            IProgress<DevicePairingProgress>? progress = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _pairCalls);
            DeviceInfo current = Volatile.Read(ref _devices).First(item => string.Equals(item.Udid, udid, StringComparison.OrdinalIgnoreCase));
            string state = PairOutcome;
            bool trusted = string.Equals(state, "trusted", StringComparison.Ordinal);
            if (trusted)
            {
                DeviceInfo[] updated = Volatile.Read(ref _devices)
                    .Select(device => string.Equals(device.Udid, udid, StringComparison.OrdinalIgnoreCase)
                        ? Device(device.Udid, "trusted", device.Name, DeviceConnection.Usb)
                        : device)
                    .ToArray();
                Volatile.Write(ref _devices, updated);
                progress?.Report(new DevicePairingProgress("paired", "Trust accepted."));
            }
            else
            {
                progress?.Report(new DevicePairingProgress(state == "locked" ? "locked" : "denied", "Trust was not accepted."));
            }

            return Task.FromResult(new DevicePairingResult(
                udid,
                current.Connection,
                state,
                state == "locked" ? "The iPhone is locked." : trusted ? "Trust accepted." : "Trust was declined on the iPhone.",
                DateTimeOffset.UtcNow,
                trusted));
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InstalledApp>>([]);

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default) => Task.CompletedTask;

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }

    private sealed class StubDeviceController : IDeviceController
    {
        private readonly Dictionary<string, InstalledApp> _installed = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _installedGate = new();

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceInfo>>([new DeviceInfo(
                "TEST-UDID",
                "Test iPhone",
                "iPhone15,2",
                "17.5",
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true)]);

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default) =>
            Task.FromResult(new DeviceTrustProbe(
                udid,
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true));

        public Task<DevicePairingResult> PairAsync(string udid, IProgress<DevicePairingProgress>? progress = null, CancellationToken ct = default) =>
            Task.FromResult(new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                "trusted",
                "Lockdown session verified over USB.",
                DateTimeOffset.UtcNow,
                UsableForInstall: true));

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            lock (_installedGate)
                return Task.FromResult<IReadOnlyList<InstalledApp>>([.. _installed.Values]);
        }

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IpaInfo info = IpaInspector.Inspect(ipaPath);
            string version = string.IsNullOrWhiteSpace(info.ShortVersion)
                ? info.Version ?? string.Empty
                : info.ShortVersion;
            var installed = new InstalledApp(
                info.BundleIdentifier,
                info.DisplayName ?? info.BundleIdentifier,
                version,
                DateTimeOffset.UtcNow.AddDays(7));
            lock (_installedGate)
                _installed[installed.BundleId] = installed;
            return Task.CompletedTask;
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", [new DeviceCheck("usbmux", "usbmux", "ok", "ok")]));
    }
}
