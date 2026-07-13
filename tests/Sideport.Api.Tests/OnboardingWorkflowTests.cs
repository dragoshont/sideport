using Sideport.Api.AppleAccess;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Onboarding;
using Sideport.Api.Operations;
using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

public sealed class OnboardingWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly string AccountProfileId = AppleAccountIdentity.ProfileIdFor("owner@example.com");
    private const string TeamId = "TEAMID1234";
    private const string DeviceUdid = "00008101TESTDEVICE";
    private const string CatalogAppId = "sample-app";
    private const string BundleId = "com.example.sample";

    [Fact]
    public void Build_AlwaysReturnsExactlySixOrderedSteps_AndDoesNotInferCompletion()
    {
        OnboardingWorkflowContext context = Context(
            registrations: [PendingRegistration()],
            operations: [InstallOperation("op_install_succeeded", "succeeded", Now, verified: true)]);

        OnboardingWorkflowDto workflow = OnboardingWorkflowBuilder.Build(context);

        Assert.Equal(
            ["server", "apple-signer", "device", "app", "install", "ready"],
            workflow.Steps.Select(step => step.Id));
        Assert.Equal("in-progress", workflow.SetupState);
        Assert.False(workflow.ReadyNow);
        Assert.Null(workflow.CompletedAt);
        Assert.Null(workflow.VerifiedOperationId);
        Assert.Equal("action-required", Step(workflow, "install").State);
        Assert.Equal("retry-finalization", Step(workflow, "install").NextAction?.Action);
        Assert.Equal("not-started", Step(workflow, "ready").State);
    }

    [Fact]
    public void Build_ResumesActiveEnrollmentAndInstallByDurableOperationId()
    {
        OperationRecordDto enrollment = EnrollmentOperation("op_enroll_active", "waiting", Now.AddMinutes(-2));
        OnboardingWorkflowDto enrollmentWorkflow = OnboardingWorkflowBuilder.Build(Context(
            devices: [],
            acceptedDeviceCount: 0,
            operations: [enrollment]));

        OnboardingWorkflowStepDto device = Step(enrollmentWorkflow, "device");
        Assert.Equal("in-progress", device.State);
        Assert.Equal(enrollment.OperationId, device.ActiveOperationId);
        Assert.Null(enrollmentWorkflow.NextAction);

        OperationRecordDto oldFailure = InstallOperation("op_install_old", "failed", Now.AddMinutes(-5));
        OperationRecordDto activeInstall = InstallOperation("op_install_active", "waiting", Now.AddMinutes(-1));
        OnboardingWorkflowDto installWorkflow = OnboardingWorkflowBuilder.Build(Context(
            registrations: [PendingRegistration()],
            operations: [oldFailure, activeInstall]));

        OnboardingWorkflowStepDto install = Step(installWorkflow, "install");
        Assert.Equal("in-progress", install.State);
        Assert.Equal(activeInstall.OperationId, install.ActiveOperationId);
        Assert.Null(install.NextAction);
        Assert.Null(installWorkflow.NextAction);
    }

    [Fact]
    public void Build_UsesReceiptAsTheOnlyReadyAndHistoricalCompletionEvidence()
    {
        OnboardingCompletionReceipt receipt = Receipt("op_verified");
        var unavailableSystem = new SystemStatusDto(
            Operational: false,
            Now,
            [new SystemStatusCheckDto(
                "operation-store",
                "fail",
                "live",
                Now,
                "storage",
                ["operation-history"],
                "Durable operation history is unavailable.",
                "Repair the operation store.")]);
        PersonalAppleStatusDto staleApple = HealthyApple() with
        {
            State = "validation-stale",
            AuthValidatedAt = null,
        };
        SchedulerSettingsState disabledScheduler = Scheduler() with
        {
            Enabled = false,
            RequestedEnabled = false,
            NextEvaluationAt = null,
        };

        OnboardingWorkflowDto workflow = OnboardingWorkflowBuilder.Build(Context(
            system: unavailableSystem,
            apple: staleApple,
            devices: [],
            acceptedDeviceCount: 1,
            receipt: receipt,
            scheduler: disabledScheduler));

        Assert.Equal("complete", workflow.SetupState);
        Assert.False(workflow.ReadyNow);
        Assert.Equal(receipt.CompletedAt, workflow.CompletedAt);
        Assert.Equal(receipt.VerifiedOperationId, workflow.VerifiedOperationId);
        Assert.Null(workflow.NextAction);
        Assert.Equal("complete", Step(workflow, "ready").State);
        Assert.Equal(receipt.VerifiedOperationId, Step(workflow, "install").ActiveOperationId);
    }

    [Fact]
    public void Build_ReadyNowRequiresExactReceiptLineageAndReusableSigningIdentity()
    {
        const string operationId = "op_verified_lineage";
        OnboardingCompletionReceipt receipt = Receipt(operationId);
        OperationRecordDto operation = InstallOperation(operationId, "succeeded", Now, verified: true);
        AppRegistration registration = ActiveRegistration(operationId);

        OnboardingWorkflowDto ready = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation],
            receipt: receipt));
        Assert.True(ready.ReadyNow);

        OnboardingWorkflowDto secondPhoneIsUsable = OnboardingWorkflowBuilder.Build(Context(
            devices: [HealthyDevice() with { Udid = "SECOND-PHONE" }],
            registrations: [registration],
            operations: [operation],
            receipt: receipt));
        Assert.True(secondPhoneIsUsable.ReadyNow);

        OnboardingWorkflowDto missingIdentity = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation],
            receipt: receipt,
            signingIdentity: new SigningIdentityInspection("missing", null, null)));
        Assert.False(missingIdentity.ReadyNow);

        OnboardingWorkflowDto changedArtifact = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration with { CatalogSha256 = "different" }],
            operations: [operation],
            receipt: receipt));
        Assert.False(changedArtifact.ReadyNow);

        OperationRecordDto cutoverBlocked = operation with
        {
            OperationId = "op_cutover_blocked",
            Type = "refresh",
            Status = "blocked",
            CreatedAt = receipt.CompletedAt.AddMinutes(1),
            UpdatedAt = receipt.CompletedAt.AddMinutes(1),
            CompletedAt = receipt.CompletedAt.AddMinutes(1),
            Result = null,
            Error = new OperationIssueDto(
                "signing-cutover-required",
                "The persisted identity no longer matches Apple's certificate inventory."),
        };
        OnboardingWorkflowDto cutoverRequired = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation, cutoverBlocked],
            receipt: receipt));
        Assert.False(cutoverRequired.ReadyNow);

        OperationRecordDto transientSignerFailure = cutoverBlocked with
        {
            OperationId = "op_transient_signer_failure",
            Error = new OperationIssueDto(
                "signing-identity-unavailable",
                "The persisted identity could not be inspected during this refresh."),
        };
        OnboardingWorkflowDto recoveredTransientFailure = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation, transientSignerFailure],
            receipt: receipt));
        Assert.True(recoveredTransientFailure.ReadyNow);

        OperationRecordDto restoredAppleLineage = transientSignerFailure with
        {
            OperationId = "op_transient_apple_lineage_failure",
            Error = new OperationIssueDto(
                "apple-refresh-lineage-mismatch",
                "The Apple account state was temporarily stale."),
        };
        OnboardingWorkflowDto recoveredAppleLineage = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation, restoredAppleLineage],
            receipt: receipt));
        Assert.True(recoveredAppleLineage.ReadyNow);

        OperationRecordDto successfulReuse = operation with
        {
            OperationId = "op_success_after_cutover_review",
            Type = "refresh",
            CreatedAt = cutoverBlocked.CreatedAt.AddMinutes(1),
            UpdatedAt = cutoverBlocked.CreatedAt.AddMinutes(1),
            CompletedAt = cutoverBlocked.CreatedAt.AddMinutes(1),
        };
        OnboardingWorkflowDto cutoverProvenReusable = OnboardingWorkflowBuilder.Build(Context(
            registrations: [registration],
            operations: [operation, cutoverBlocked, successfulReuse],
            receipt: receipt));
        Assert.True(cutoverProvenReusable.ReadyNow);
    }

    [Theory]
    [InlineData("failed", "review-install")]
    [InlineData("unknown", "reconcile-install")]
    public void Build_ExposesRecoveryForFailedAndUnknownInstall(
        string status,
        string expectedAction)
    {
        OperationRecordDto operation = InstallOperation("op_install_recovery", status, Now.AddMinutes(-1));
        OnboardingWorkflowDto workflow = OnboardingWorkflowBuilder.Build(Context(
            registrations: [PendingRegistration()],
            operations: [operation]));

        OnboardingWorkflowStepDto install = Step(workflow, "install");
        Assert.Equal("blocked", install.State);
        Assert.Equal(operation.OperationId, install.ActiveOperationId);
        Assert.Equal(expectedAction, install.NextAction?.Action);
        Assert.Equal("install", workflow.NextAction?.StepId);
        Assert.Equal(expectedAction, workflow.NextAction?.Action);
        Assert.Equal("not-started", Step(workflow, "ready").State);
    }

    [Fact]
    public void Build_OffersFinalizationRetryOnlyForMatchingVerifiedInstallEvidence()
    {
        OperationRecordDto verified = InstallOperation(
            "op_install_finalize",
            "recovery-required",
            Now.AddMinutes(-1),
            verified: true);
        OnboardingWorkflowDto verifiedWorkflow = OnboardingWorkflowBuilder.Build(Context(
            registrations: [PendingRegistration()],
            operations: [verified]));
        Assert.Equal("retry-finalization", Step(verifiedWorkflow, "install").NextAction?.Action);

        OperationRecordDto mismatched = verified with
        {
            OperationId = "op_install_mismatched",
            Result = verified.Result! with { BundleId = "com.example.other" },
        };
        OnboardingWorkflowDto mismatchedWorkflow = OnboardingWorkflowBuilder.Build(Context(
            registrations: [PendingRegistration()],
            operations: [mismatched]));
        Assert.Equal("blocked", Step(mismatchedWorkflow, "install").State);
        Assert.Equal("review-install", Step(mismatchedWorkflow, "install").NextAction?.Action);

        OperationRecordDto missingVerifyStage = verified with
        {
            OperationId = "op_install_missing_verify_stage",
            Stages = verified.Stages.Where(stage => stage.Id != "verify").ToArray(),
        };
        OnboardingWorkflowDto missingStageWorkflow = OnboardingWorkflowBuilder.Build(Context(
            registrations: [PendingRegistration()],
            operations: [missingVerifyStage]));
        Assert.Equal("review-install", Step(missingStageWorkflow, "install").NextAction?.Action);
    }

    private static OnboardingWorkflowContext Context(
        SystemStatusDto? system = null,
        PersonalAppleStatusDto? apple = null,
        IReadOnlyList<DeviceInfo>? devices = null,
        int acceptedDeviceCount = 1,
        IReadOnlyList<AppRegistration>? registrations = null,
        IReadOnlyList<OperationRecordDto>? operations = null,
        OnboardingCompletionReceipt? receipt = null,
        SchedulerSettingsState? scheduler = null,
        SigningIdentityInspection? signingIdentity = null) =>
        new(
            system ?? new SystemStatusDto(Operational: true, Now, []),
            apple ?? HealthyApple(),
            devices ?? [HealthyDevice()],
            acceptedDeviceCount,
            [CatalogApp()],
            registrations ?? [],
            operations ?? [],
            receipt,
            scheduler ?? Scheduler(),
            Now,
            signingIdentity ?? new SigningIdentityInspection("reusable", Now.AddDays(30), "TEST"));

    private static PersonalAppleStatusDto HealthyApple() =>
        new(
            Connector: "personal-apple-id",
            State: "validated-recently",
            SecretCustody: "managed",
            AppleIdHint: "o***@example.com",
            Message: "Apple account is ready.",
            PendingChallengeId: null,
            PendingChallengeKind: null,
            Teams: [new PersonalAppleTeamDto(TeamId, "Personal Team", "Individual")],
            CredentialSource: "managed",
            AccountProfileId,
            CredentialEntry: null,
            SelectedTeamId: TeamId,
            TeamValidatedAt: Now,
            LastAuthenticatedAt: Now,
            AuthValidatedAt: Now,
            PendingChallengeExpiresAt: null);

    private static DeviceInfo HealthyDevice() =>
        new(
            DeviceUdid,
            "iPhone",
            "iPhone17,1",
            "26.0",
            DeviceConnection.Usb,
            TrustState: "trusted",
            TrustReason: null,
            LockdownCheckedAt: Now,
            UsableForInstall: true);

    private static CatalogAppDto CatalogApp() =>
        new(
            CatalogAppId,
            "Sample App",
            "Test app",
            BundleId,
            "/private/not-returned/sample.ipa",
            "1",
            "1.0",
            1,
            "sha256",
            HasEmbeddedProfile: false,
            SignatureExpiresAt: null,
            Source: "live",
            Status: "ready",
            LastInspectedAt: Now,
            Notes: []);

    private static AppRegistration PendingRegistration() =>
        new(
            BundleId,
            "owner@example.com",
            TeamId,
            DeviceUdid,
            "/private/not-returned/sample.ipa",
            Lifecycle: "pending-install",
            CatalogAppId,
            CreatedAt: Now.AddMinutes(-3));

    private static AppRegistration ActiveRegistration(string operationId) =>
        new(
            BundleId,
            "owner@example.com",
            TeamId,
            DeviceUdid,
            "/private/not-returned/sample.ipa",
            Lifecycle: "active",
            CatalogAppId,
            CreatedAt: Now.AddMinutes(-3),
            ActivatedAt: Now.AddMinutes(-1),
            LastVerifiedOperationId: operationId,
            CatalogVersion: 1,
            CatalogSha256: "sha256");

    private static SchedulerSettingsState Scheduler() =>
        new(
            SchedulerSettingsStore.CurrentSchemaVersion,
            SettingsVersion: 1,
            Enabled: true,
            RequestedEnabled: true,
            UpdatedAt: Now,
            NextEvaluationAt: Now.AddHours(1),
            Evaluations: []);

    private static OnboardingCompletionReceipt Receipt(string operationId) =>
        new(
            OnboardingCompletionStore.CurrentSchemaVersion,
            Now,
            new OperationActorDto("api-token", "api-token-client"),
            AccountProfileId,
            TeamId,
            DeviceUdid,
            CatalogAppId,
            CatalogVersion: 1,
            CatalogSha256: "sha256",
            BundleId,
            operationId,
            SchedulerSettingsVersion: "settings_1",
            OperationalCheckedAt: Now);

    private static OperationRecordDto EnrollmentOperation(
        string operationId,
        string status,
        DateTimeOffset createdAt) =>
        new(
            operationId,
            DeviceEnrollmentService.OperationType,
            status,
            createdAt,
            createdAt,
            createdAt,
            CompletedAt: null,
            new OperationActorDto("api-token", "api-token-client"),
            IdempotencyKey: operationId,
            Attempt: 1,
            new OperationTargetDto(DeviceUdid: null, BundleId: null, Kind: "device-enrollment"),
            [new OperationStageDto(
                "wait-for-usb",
                "Connect iPhone",
                "running",
                createdAt,
                CompletedAt: null,
                "Waiting for iPhone over USB.")],
            Result: null,
            Error: null,
            Cancelable: true,
            Retryable: false,
            Rerunnable: false,
            CorrelationId: operationId);

    private static OperationRecordDto InstallOperation(
        string operationId,
        string status,
        DateTimeOffset createdAt,
        bool verified = false)
    {
        var intent = new InstallOperationIntentDto(
            DeviceUdid,
            CatalogAppId,
            AccountProfileId,
            BundleId,
            FinishOnboarding: true,
            RegistrationKey: $"{DeviceUdid}:{BundleId}",
            CatalogVersion: 1,
            CatalogSha256: "sha256");
        OperationIssueDto? error = status is "failed" or "unknown" or "recovery-required"
            ? new OperationIssueDto($"install-{status}", "The install needs attention.")
            : null;
        string stageStatus = status is "queued" or "waiting" or "running" ? "running" : status;
        return new OperationRecordDto(
            operationId,
            "install",
            status,
            createdAt,
            createdAt,
            createdAt,
            status is "queued" or "waiting" or "running" ? null : createdAt,
            new OperationActorDto("api-token", "api-token-client"),
            IdempotencyKey: operationId,
            Attempt: 1,
            new OperationTargetDto(
                DeviceUdid,
                BundleId,
                TeamId: TeamId,
                Kind: "first-install",
                CatalogAppId: CatalogAppId,
                AccountProfileId: AccountProfileId,
                CatalogVersion: 1,
                Version: "1.0",
                CatalogSha256: "sha256"),
            verified
                ?
                [
                    new OperationStageDto(
                        "install",
                        "Install app",
                        "succeeded",
                        createdAt,
                        createdAt,
                        "The app was installed."),
                    new OperationStageDto(
                        "verify",
                        "Verify on iPhone",
                        "succeeded",
                        createdAt,
                        createdAt,
                        "The installed signature was verified."),
                ]
                :
                [
                    new OperationStageDto(
                        "install",
                        "Install app",
                        stageStatus,
                        createdAt,
                        status is "queued" or "waiting" or "running" ? null : createdAt,
                        error?.Message ?? "Installing the app.",
                        error),
                ],
            verified
                ? new OperationResultDto(true, BundleId, Now.AddDays(7), Error: null, Version: "1.0")
                : new OperationResultDto(false, BundleId, ExpiresAt: null, error?.Message),
            error,
            Cancelable: status is "queued" or "waiting" or "running",
            Retryable: status == "failed",
            Rerunnable: status == "failed",
            CorrelationId: operationId,
            InstallIntent: intent);
    }

    private static OnboardingWorkflowStepDto Step(OnboardingWorkflowDto workflow, string id) =>
        Assert.Single(workflow.Steps, step => string.Equals(step.Id, id, StringComparison.Ordinal));
}
