using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sideport.Api.AppleAccess;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class ExecutionAuthorizationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "sideport-execution-authorization-tests",
        Guid.NewGuid().ToString("N"));

    public ExecutionAuthorizationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task FamilySuspendedAfterEnrollmentSubmission_MakesNoLaterDeviceCall()
    {
        WorkspaceFixture workspace = await BootstrapWorkspaceAsync();
        WorkspaceMemberRecord family = await AddFamilyAsync(workspace);
        var controller = new CountingDeviceController();
        EnrollmentFixture enrollment = CreateEnrollmentFixture(workspace.Store, controller);

        DeviceEnrollmentSubmissionResult submitted = await enrollment.Service.StartAsync(
            new DeviceEnrollmentRequest("family-enrollment-after-submit", TargetMemberId: family.MemberId),
            new OperationActorDto("member", family.DisplayName, family.MemberId),
            actorMemberId: family.MemberId);
        Assert.True(submitted.Created);

        await workspace.Store.SetFamilyMemberStatusAsync(
            family.MemberId,
            new WorkspaceMemberStatusRequest(
                WorkspaceActorRecord.ForMember(workspace.Owner.MemberId),
                WorkspaceMemberStatus.Suspended,
                family.Version,
                "suspend-after-enrollment-submit",
                "req-suspend-after-submit"));

        await enrollment.Service.ProcessAsync(submitted.Record!.OperationId);

        OperationRecordDto terminal = (await enrollment.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("blocked", terminal.Status);
        Assert.Equal("member-access-disabled", terminal.Error?.Code);
        Assert.Equal(0, controller.ListCalls);
        Assert.Equal(0, controller.ProbeCalls);
        Assert.Equal(0, controller.PairCalls);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);

    }

    [Fact]
    public async Task OwnershipMismatchAfterEnrollmentSubmission_IsDeniedBeforeDeviceCall()
    {
        WorkspaceFixture workspace = await BootstrapWorkspaceAsync();
        WorkspaceMemberRecord family = await AddFamilyAsync(workspace);
        var controller = new CountingDeviceController();
        EnrollmentFixture enrollment = CreateEnrollmentFixture(workspace.Store, controller);

        DeviceEnrollmentSubmissionResult submitted = await enrollment.Service.StartAsync(
            new DeviceEnrollmentRequest("ownership-race-enrollment", TargetMemberId: family.MemberId),
            new OperationActorDto("member", family.DisplayName, family.MemberId),
            actorMemberId: family.MemberId);
        Assert.True(submitted.Created);

        const string udid = "ownership-race-udid";
        await enrollment.KnownDevices.UpsertAsync(AcceptedDevice(udid, workspace.Owner.MemberId));
        await enrollment.Operations.TransitionAsync(submitted.Record!.OperationId, record => record with
        {
            Target = record.Target with { DeviceUdid = udid },
        });

        await enrollment.Service.ProcessAsync(submitted.Record.OperationId);

        OperationRecordDto terminal = (await enrollment.Operations.FindAsync(submitted.Record.OperationId))!;
        Assert.Equal("blocked", terminal.Status);
        Assert.Equal("resource-ownership-changed", terminal.Error?.Code);
        Assert.Equal(0, controller.ListCalls);
        Assert.Equal(0, controller.ProbeCalls);
        Assert.Equal(0, controller.PairCalls);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);

    }

    [Fact]
    public async Task FamilySuspendedAfterQueuedInstall_MakesNoLaterAppleOrDeviceCall()
    {
        string stateDirectory = Path.Combine(_directory, "queued-install-state");
        var controller = new CountingDeviceController();
        var apple = new CountingPersonalAppleAccess();
        using WebApplicationFactory<Program> factory = CreateFactory(stateDirectory, controller, apple);
        IServiceProvider services = factory.Services;
        WorkspaceAccessStore workspaceStore = services.GetRequiredService<WorkspaceAccessStore>();
        WorkspaceFixture workspace = await BootstrapWorkspaceAsync(workspaceStore);
        WorkspaceMemberRecord family = await AddFamilyAsync(workspace);
        KnownDeviceStore knownDevices = services.GetRequiredService<KnownDeviceStore>();
        await knownDevices.UpsertAsync(AcceptedDevice("queued-install-udid", family.MemberId));

        OperationStore operationStore = services.GetRequiredService<OperationStore>();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var record = new OperationRecordDto(
            "op_install_authorization_race",
            "install",
            "queued",
            now,
            null,
            now,
            null,
            new OperationActorDto("member", family.DisplayName, family.MemberId),
            "queued-install-authorization-race",
            Attempt: 1,
            new OperationTargetDto(
                "queued-install-udid",
                "com.example.family",
                TeamId: "TEAMID1234",
                Kind: "catalog-app",
                CatalogAppId: "catalog_family",
                AccountProfileId: "acct_family",
                CatalogVersion: 1,
                Version: "1.0",
                CatalogSha256: new string('a', 64)),
            [
                new OperationStageDto("preflight", "Preflight", "succeeded", now, now, "Ready."),
                new OperationStageDto("install", "Sign and install", "pending", null, null, "Waiting."),
                new OperationStageDto("verify", "Verify", "pending", null, null, "Waiting."),
            ],
            Result: null,
            Error: null,
            Cancelable: true,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: "op_install_authorization_race",
            InstallIntent: new InstallOperationIntentDto(
                "queued-install-udid",
                "catalog_family",
                "acct_family",
                "com.example.family",
                FinishOnboarding: false,
                RegistrationKey: "queued-install-udid\ncom.example.family",
                CatalogVersion: 1,
                CatalogSha256: new string('a', 64)),
            ActorMemberId: family.MemberId,
            OwnerMemberId: family.MemberId);
        await operationStore.AddIfIdempotentMissingAsync(record);

        await workspace.Store.SetFamilyMemberStatusAsync(
            family.MemberId,
            new WorkspaceMemberStatusRequest(
                WorkspaceActorRecord.ForMember(workspace.Owner.MemberId),
                WorkspaceMemberStatus.Suspended,
                family.Version,
                "suspend-after-install-submit",
                "req-suspend-after-install-submit"));

        OperationService operationService = services.GetRequiredService<OperationService>();

        await operationService.ProcessQueuedInstallAsync(record.OperationId);

        OperationRecordDto terminal = (await operationStore.FindAsync(record.OperationId))!;
        Assert.Equal("blocked", terminal.Status);
        Assert.Equal("member-access-disabled", terminal.Error?.Code);
        Assert.Equal(0, apple.Calls);
        Assert.Equal(0, controller.ListCalls);
        Assert.Equal(0, controller.ProbeCalls);
        Assert.Equal(0, controller.PairCalls);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);

        OperationRecordDto retryable = record with
        {
            OperationId = "op_refresh_retry_authorization_race",
            Type = "refresh",
            Status = "failed",
            StartedAt = now,
            UpdatedAt = now,
            CompletedAt = now,
            IdempotencyKey = "retry-source-authorization-race",
            Target = record.Target with { BundleId = "com.example.retry" },
            InstallIntent = null,
            Retryable = true,
            Rerunnable = false,
            CorrelationId = "op_refresh_retry_authorization_race",
        };
        await operationStore.AddIfIdempotentMissingAsync(retryable);
        (OperationRecordDto? retryRecord, bool retryCreated, string? retryError) = await operationService.RetryAsync(
            retryable.OperationId,
            retryable.Actor,
            "retry-after-suspension",
            actorMemberId: family.MemberId,
            ownerMemberId: family.MemberId);
        Assert.False(retryCreated);
        Assert.Equal(retryable.OperationId, retryRecord?.OperationId);
        Assert.Equal("member-access-disabled", retryError);

        OperationRecordDto rerunnable = retryable with
        {
            OperationId = "op_refresh_rerun_authorization_race",
            Status = "succeeded",
            IdempotencyKey = "rerun-source-authorization-race",
            Result = new OperationResultDto(
                Success: true,
                BundleId: "com.example.family",
                ExpiresAt: now.AddDays(6),
                Error: null,
                Version: "1.0"),
            Error = null,
            Retryable = false,
            Rerunnable = true,
            CorrelationId = "op_refresh_rerun_authorization_race",
        };
        await operationStore.AddIfIdempotentMissingAsync(rerunnable);
        (OperationRecordDto? rerunRecord, bool rerunCreated, string? rerunError) = await operationService.RerunAsync(
            rerunnable.OperationId,
            rerunnable.Actor,
            "rerun-after-suspension",
            actorMemberId: family.MemberId,
            ownerMemberId: family.MemberId);
        Assert.False(rerunCreated);
        Assert.Equal(rerunnable.OperationId, rerunRecord?.OperationId);
        Assert.Equal("member-access-disabled", rerunError);
        Assert.Equal(0, apple.Calls);
        Assert.Equal(0, controller.ListCalls);
        Assert.Equal(0, controller.ProbeCalls);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);

        OperationRecordDto verified = rerunnable with
        {
            OperationId = "op_install_scheduler_authorization_race",
            Type = "install",
            IdempotencyKey = "scheduler-evidence-authorization-race",
            Target = record.Target,
            Result = new OperationResultDto(
                Success: true,
                BundleId: "com.example.family",
                ExpiresAt: now.AddMinutes(30),
                Error: null,
                Version: "1.0"),
            CorrelationId = "op_install_scheduler_authorization_race",
        };
        await operationStore.AddIfIdempotentMissingAsync(verified);
        IAppRegistry registry = services.GetRequiredService<IAppRegistry>();
        await registry.UpsertAsync(new AppRegistration(
            "com.example.family",
            "developer@example.test",
            "TEAMID1234",
            "queued-install-udid",
            Path.Combine(_directory, "scheduler-artifact.ipa"),
            Lifecycle: "active",
            CatalogAppId: "catalog_family",
            CreatedAt: now.AddDays(-1),
            ActivatedAt: now.AddDays(-1),
            LastVerifiedOperationId: verified.OperationId,
            CatalogVersion: 1,
            CatalogSha256: new string('a', 64)));
        var schedulerSettings = new SchedulerSettingsStore(Path.Combine(_directory, "scheduler-authorization.json"));
        await schedulerSettings.InitializeAsync(
            requestedEnabled: true,
            prerequisitesSatisfied: true,
            nextEvaluationAt: now);
        var scheduler = new OperationScheduler(
            registry,
            operationService,
            operationStore,
            schedulerSettings,
            services.GetRequiredService<OrchestratorOptions>(),
            executionAuthorization: new WorkspaceExecutionAuthorizer(workspaceStore, knownDevices));

        await scheduler.RunOnceAsync(CancellationToken.None);

        SchedulerSettingsState schedulerState = (await schedulerSettings.ReadAsync())!;
        Assert.Equal(1, schedulerState.LastEvaluation?.DueCount);
        Assert.Equal(0, schedulerState.LastEvaluation?.QueuedCount);
        Assert.True(schedulerState.LastEvaluation?.SkippedCount >= 1);
        Assert.DoesNotContain(
            await operationStore.ListAsync(limit: null),
            operation => string.Equals(operation.Actor.Kind, "system", StringComparison.Ordinal));
        Assert.Equal(0, apple.Calls);
        Assert.Equal(0, controller.ListCalls);
        Assert.Equal(0, controller.ProbeCalls);
        Assert.Equal(0, controller.InstalledAppReads);
        Assert.Equal(0, controller.InstallCalls);
    }

    [Fact]
    public async Task MissingWorkspace_AllowsRecoveryOnUnassignedLegacyOnly_AndBlocksSchedulerAndOidc()
    {
        var workspace = new WorkspaceAccessStore(Path.Combine(_directory, "missing-workspace"));
        var knownDevices = new KnownDeviceStore(Path.Combine(_directory, "migration-known.json"));
        var authorization = new WorkspaceExecutionAuthorizer(workspace, knownDevices);

        WorkspaceExecutionDecision recovery = await authorization.AuthorizeSubmissionAsync(
            new OperationActorDto("recovery-bearer", "Recovery access"),
            actorMemberId: null,
            ownerMemberId: null,
            deviceUdid: "legacy-unassigned",
            enrollmentTarget: false,
            assignDefaultOwner: false);
        WorkspaceExecutionDecision scheduler = await authorization.AuthorizeSchedulerTargetAsync("legacy-unassigned");
        WorkspaceExecutionDecision newEnrollment = await authorization.AuthorizeSubmissionAsync(
            new OperationActorDto("recovery-bearer", "Recovery access"),
            actorMemberId: null,
            ownerMemberId: null,
            deviceUdid: null,
            enrollmentTarget: true,
            assignDefaultOwner: true);
        WorkspaceExecutionDecision oidc = await authorization.AuthorizeSubmissionAsync(
            new OperationActorDto("oidc-user", "Legacy OIDC user"),
            actorMemberId: null,
            ownerMemberId: null,
            deviceUdid: "legacy-unassigned",
            enrollmentTarget: false,
            assignDefaultOwner: false);

        Assert.True(recovery.IsAllowed);
        Assert.False(scheduler.IsAllowed);
        Assert.Equal("workspace-bootstrap-required", scheduler.ErrorCode);
        Assert.False(newEnrollment.IsAllowed);
        Assert.Equal("workspace-bootstrap-required", newEnrollment.ErrorCode);
        Assert.False(oidc.IsAllowed);
        Assert.Equal("workspace-bootstrap-required", oidc.ErrorCode);

        await knownDevices.UpsertAsync(AcceptedDevice("assigned-without-workspace", "member_orphaned"));
        WorkspaceExecutionDecision assignedRecovery = await authorization.AuthorizeSubmissionAsync(
            new OperationActorDto("api-token", "api-token-client"),
            actorMemberId: null,
            ownerMemberId: null,
            deviceUdid: "assigned-without-workspace",
            enrollmentTarget: false,
            assignDefaultOwner: false);
        Assert.False(assignedRecovery.IsAllowed);
        Assert.Equal("workspace-bootstrap-required", assignedRecovery.ErrorCode);
    }

    private EnrollmentFixture CreateEnrollmentFixture(
        WorkspaceAccessStore workspace,
        CountingDeviceController controller)
    {
        var operations = new OperationStore(Path.Combine(_directory, $"operations-{Guid.NewGuid():N}.json"));
        var knownDevices = new KnownDeviceStore(Path.Combine(_directory, $"known-{Guid.NewGuid():N}.json"));
        var inventory = new KnownDeviceService(knownDevices, controller, new InMemoryAppRegistry());
        var service = new DeviceEnrollmentService(
            operations,
            knownDevices,
            inventory,
            controller,
            new DeviceEnrollmentQueue(),
            executionAuthorization: new WorkspaceExecutionAuthorizer(workspace, knownDevices));
        return new EnrollmentFixture(service, operations, knownDevices);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string stateDirectory,
        IDeviceController controller,
        IPersonalAppleAccess apple) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-DEVICE-UUID");
            builder.UseSetting("Sideport:Signer:BinaryPath", File.Exists("/usr/bin/true")
                ? "/usr/bin/true"
                : Environment.ProcessPath ?? typeof(ExecutionAuthorizationTests).Assembly.Location);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IDeviceController>();
                services.RemoveAll<IPersonalAppleAccess>();
                services.AddSingleton(controller);
                services.AddSingleton(apple);
            });
        });

    private async Task<WorkspaceFixture> BootstrapWorkspaceAsync()
    {
        string directory = Path.Combine(_directory, $"workspace-{Guid.NewGuid():N}");
        return await BootstrapWorkspaceAsync(new WorkspaceAccessStore(directory));
    }

    private static async Task<WorkspaceFixture> BootstrapWorkspaceAsync(WorkspaceAccessStore store)
    {
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(new(
            ExpectedOwnerMemberId: null,
            ImpactVersion: null,
            Lifetime: TimeSpan.FromMinutes(15),
            IdempotencyKey: "execution-owner-bootstrap",
            RequestId: "req-execution-owner-bootstrap"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(
            claim.Token!,
            "req-execution-owner-handoff");
        WorkspaceAcceptanceResult accepted = await store.AcceptOwnerClaimAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "execution-owner"),
                "Owner",
                "owner@example.test",
                "execution-owner-accept",
                "req-execution-owner-accept"));
        return new WorkspaceFixture(store, accepted.Member);
    }

    private static async Task<WorkspaceMemberRecord> AddFamilyAsync(WorkspaceFixture workspace)
    {
        WorkspaceInvitationCreateResult invitation = await workspace.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(workspace.Owner.MemberId),
            "Family",
            "family@example.test",
            TimeSpan.FromDays(7),
            "execution-family-invite",
            "req-execution-family-invite"));
        WorkspaceHandoffCreateResult handoff = await workspace.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-execution-family-handoff");
        WorkspaceAcceptanceResult accepted = await workspace.Store.AcceptInvitationAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "execution-family"),
                "Family",
                "family@example.test",
                "execution-family-accept",
                "req-execution-family-accept"));
        return accepted.Member;
    }

    private static KnownDeviceRecord AcceptedDevice(string udid, string? ownerMemberId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new KnownDeviceRecord(
            udid,
            "Test iPhone",
            "iPhone15,2",
            "18.5",
            "usb",
            now,
            now,
            "test",
            now,
            "trusted",
            Owner: null,
            Notes: null,
            UpdatedAt: now,
            InventoryState: "accepted",
            AcceptedAt: now,
            AcceptedBy: "test",
            EnrollmentOperationId: $"op-enroll-{udid}",
            TrustReason: "Lockdown verified.",
            LockdownCheckedAt: now,
            UsableForInstall: true,
            OwnerMemberId: ownerMemberId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed record WorkspaceFixture(WorkspaceAccessStore Store, WorkspaceMemberRecord Owner);

    private sealed record EnrollmentFixture(
        DeviceEnrollmentService Service,
        OperationStore Operations,
        KnownDeviceStore KnownDevices);

    private sealed class CountingDeviceController : IDeviceController
    {
        public int ListCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public int PairCalls { get; private set; }
        public int InstalledAppReads { get; private set; }
        public int InstallCalls { get; private set; }

        public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<DeviceInfo>>([]);
        }

        public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default)
        {
            ProbeCalls++;
            throw new InvalidOperationException("Authorization should run before probing a device.");
        }

        public Task<DevicePairingResult> PairAsync(
            string udid,
            IProgress<DevicePairingProgress>? progress = null,
            CancellationToken ct = default)
        {
            PairCalls++;
            throw new InvalidOperationException("Authorization should run before pairing a device.");
        }

        public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
            string udid,
            CancellationToken ct = default)
        {
            InstalledAppReads++;
            return Task.FromResult<IReadOnlyList<InstalledApp>>([]);
        }

        public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
        {
            InstallCalls++;
            return Task.CompletedTask;
        }

        public Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default) =>
            Task.FromResult(new DeviceDiagnostics("ok", []));
    }

    private sealed class CountingPersonalAppleAccess : IPersonalAppleAccess
    {
        public int Calls { get; private set; }

        public Task<PersonalAppleStatusDto> StatusAsync(CancellationToken ct = default) =>
            Unexpected<PersonalAppleStatusDto>();

        public Task<PersonalAppleConnectResult> ConnectAsync(
            PersonalAppleConnectRequest request,
            string actor,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleConnectResult>();

        public Task<PersonalAppleStatusDto> SignInAsync(
            PersonalAppleSignInRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleStatusDto>();

        public Task<PersonalAppleTwoFactorResult> CompleteTwoFactorAsync(
            PersonalAppleCompleteTwoFactorRequest request,
            string? actor = null,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleTwoFactorResult>();

        public string? PendingChallengeAccountProfileId(string challengeId, string actor)
        {
            Calls++;
            throw new InvalidOperationException("Authorization should run before Apple access.");
        }

        public Task<PersonalAppleInstallContext> ResolveFreshInstallContextAsync(
            string accountProfileId,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleInstallContext>();

        public Task<PersonalAppleInstallPreflightContext> ResolveFreshInstallPreflightContextAsync(
            string accountProfileId,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleInstallPreflightContext>();

        public Task<PersonalAppleStatusDto> SelectTeamAsync(
            PersonalAppleTeamSelectionRequest request,
            string actor,
            CancellationToken ct = default) =>
            Unexpected<PersonalAppleStatusDto>();

        private Task<T> Unexpected<T>()
        {
            Calls++;
            return Task.FromException<T>(new InvalidOperationException(
                "Authorization should run before Apple access."));
        }
    }
}
