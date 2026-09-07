namespace Sideport.Api.Operations;

public sealed record FirstInstallOptions(bool SchedulerEnabled);

public sealed record OperationActorDto(string Kind, string DisplayName, string? Id = null);

public sealed record OperationTargetDto(
    string? DeviceUdid,
    string? BundleId,
    string? AppleId = null,
    string? TeamId = null,
    string? Kind = null,
    string? CatalogAppId = null,
    string? AccountProfileId = null,
    int? CatalogVersion = null,
    string? Version = null,
    string? CatalogSha256 = null);

public sealed record OperationIssueDto(string Code, string Message, string Source = "live", string? Detail = null);

public sealed record OperationLimitDto(string Code, string Label, int Used, int Limit, string Source = "derived");

public sealed record OperationPreflightRequest(
    string Type,
    string DeviceUdid,
    string BundleId,
    bool FinishOnboarding = false,
    string? CatalogAppId = null,
    string? AccountProfileId = null,
    bool AllowWifiFirstInstall = false);

public sealed record OperationPreflightCheckDto(
    string Code,
    string Label,
    string Status,
    string Source = "live",
    string? Detail = null);

public sealed record OperationPreflightCheckGroupDto(
    string Id,
    string Label,
    IReadOnlyList<OperationPreflightCheckDto> Checks);

public sealed record OperationSigningReadinessDto(
    string LocalIdentityState,
    DateTimeOffset? LocalIdentityExpiresAt,
    int AppleCertificateCount,
    string Impact,
    bool RequiresCutover);

public sealed record OperationPreflightDto(
    bool Ready,
    OperationTargetDto Target,
    IReadOnlyList<OperationIssueDto> Blockers,
    IReadOnlyList<OperationIssueDto> Warnings,
    IReadOnlyList<string> PlannedMutations,
    IReadOnlyList<OperationLimitDto> ScarceLimits,
    bool RequiresConfirmation,
    string Source = "live",
    string? PreflightId = null,
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<OperationPreflightCheckGroupDto>? CheckGroups = null,
    string? InventoryVersion = null,
    string? PlanVersion = null,
    OperationSigningReadinessDto? Signing = null);

public sealed record RefreshOperationRequest(string DeviceUdid, string BundleId, string? IdempotencyKey = null);

public sealed record VerifyExistingRegistrationRequest(string IdempotencyKey);

public sealed record VerifyExistingRegistrationSubmissionResult(
    OperationRecordDto? Record,
    bool Created,
    string? Error = null,
    string? Message = null);

public sealed record OperationReconcileRequest(string IdempotencyKey, string? Note = null);

public sealed record OperationReconciliationSubmissionResult(
    OperationRecordDto? Record,
    bool Created,
    string? Error = null,
    string? Message = null);

public sealed record FirstInstallRequest(
    string DeviceUdid,
    string? CatalogAppId,
    string? AccountProfileId,
    bool FinishOnboarding,
    string IdempotencyKey,
    string? BundleId = null,
    string? PreflightId = null,
    string? PlanVersion = null,
    bool ConfirmedPlannedMutations = false,
    bool AllowWifiFirstInstall = false);

public sealed record InstallOperationIntentDto(
    string DeviceUdid,
    string CatalogAppId,
    string AccountProfileId,
    string BundleId,
    bool FinishOnboarding,
    string RegistrationKey,
    string? PreflightId = null,
    string? PlanVersion = null,
    string? InventoryVersion = null,
    bool ConfirmedPlannedMutations = false,
    int? CatalogVersion = null,
    string? CatalogSha256 = null,
    bool AllowWifiFirstInstall = false,
    string? InstallConnection = null);

public sealed record SigningCutoverIntentDto(
    string CurrentAccountProfileId,
    string CurrentTeamId,
    string AccountProfileId,
    string TeamId,
    string PreflightId,
    string InventoryVersion,
    IReadOnlyList<string> AcknowledgedCertificateIds,
    IReadOnlyList<string> AcknowledgedImpactCodes,
    string OriginalLocalIdentityState,
    string? OriginalLocalIdentitySerialSuffix,
    bool ReplacesAccount);

public sealed record InstallSubmissionResult(
    OperationRecordDto? Record,
    bool Created,
    string? Error = null,
    string? Message = null,
    OperationPreflightDto? ReplacementPreflight = null);

public sealed record InstallPreflightStaleDto(
    string Error,
    string Message,
    OperationPreflightDto ReplacementPreflight);

public sealed record OperationActionRequest(
    string? IdempotencyKey = null,
    string? Reason = null,
    SupersedingRenewalRequest? SupersedingRenewal = null);

/// <summary>
/// Typed, explicit superseding-renewal intent carried on an unknown operation's
/// rerun. The Owner names the exact renewal-eligible reconciliation receipt and
/// echoes the lineage it observed so Sideport can reject any drift before it
/// prepares a device mutation. Authority is always re-resolved from the workspace
/// authorization services, never inferred from a caller-supplied string.
/// </summary>
public sealed record SupersedingRenewalRequest(
    string ReceiptOperationId,
    bool Confirm,
    string? DeviceUdid = null,
    string? BundleId = null,
    string? Version = null,
    string? TeamId = null,
    string? AccountProfileId = null,
    string? CatalogSha256 = null,
    DateTimeOffset? ExpectedExpiresAt = null);

/// <summary>
/// The immutable recovery intent stamped on the single refresh child a receipt
/// authorizes. It identifies the named predecessor and receipt plus the exact
/// lineage the child must keep agreeing with at execution. It is never rewritten,
/// so an identical resubmission replays the same child and any change conflicts.
/// </summary>
public sealed record OperationRecoveryIntentDto(
    string Kind,
    string ReceiptOperationId,
    string PredecessorOperationId,
    string DeviceUdid,
    string BundleId,
    string TeamId,
    string AccountProfileId,
    string Version,
    string CatalogSha256,
    DateTimeOffset PredecessorExpectedExpiresAt,
    int? CatalogVersion = null,
    string? CatalogAppId = null);

/// <summary>
/// Durable mutation-start evidence written through the orchestrator seam BEFORE
/// the recovery install touches the iPhone. Its presence marks the child as
/// having (possibly) mutated a device, so restart recovery never replays it.
/// </summary>
public sealed record OperationRecoveryCheckpointDto(
    DateTimeOffset PreparedExpiresAt,
    string PinnedArtifactSha256,
    DateTimeOffset MutationStartedAt,
    string? ArtifactSnapshotId = null);

public sealed record OperationStageDto(
    string Id,
    string Label,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string Message,
    OperationIssueDto? Error = null);

public sealed record DeviceEnrollmentCandidateDto(
    string UdidSuffix,
    string Name,
    string? ProductType,
    string? OsVersion,
    string Connection);

public sealed record DeviceEnrollmentResultDto(
    string? SelectedDeviceUdid,
    string InventoryState,
    DateTimeOffset? AcceptedAt,
    string? Reason = null);

public sealed record OperationResultDto(
    bool Success,
    string? BundleId,
    DateTimeOffset? ExpiresAt,
    string? Error,
    DeviceEnrollmentResultDto? DeviceEnrollment = null,
    DateTimeOffset? NextEvaluationAt = null,
    string? SchedulerSettingsVersion = null,
    string? Version = null,
    bool? SafeToRerun = null,
    string? ReconciledOperationId = null,
    bool? RenewalEligible = null);

public sealed record OperationRecordDto(
    string OperationId,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt,
    OperationActorDto Actor,
    string? IdempotencyKey,
    int Attempt,
    OperationTargetDto Target,
    IReadOnlyList<OperationStageDto> Stages,
    OperationResultDto? Result,
    OperationIssueDto? Error,
    bool Cancelable,
    bool Retryable,
    bool Rerunnable,
    string CorrelationId,
    string? ParentOperationId = null,
    string Source = "live",
    DateTimeOffset? ExpiresAt = null,
    IReadOnlyList<DeviceEnrollmentCandidateDto>? CandidateDevices = null,
    DateTimeOffset? DevicePairingRequestedAt = null,
    InstallOperationIntentDto? InstallIntent = null,
    string? ActorMemberId = null,
    string? OwnerMemberId = null,
    SigningCutoverIntentDto? SigningCutoverIntent = null,
    OperationRecoveryIntentDto? RecoveryIntent = null,
    OperationRecoveryCheckpointDto? RecoveryCheckpoint = null);

public sealed record RenewalItemDto(
    string Id,
    string DeviceUdid,
    string BundleId,
    string TeamId,
    string Risk,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? Blocker,
    string? OperationId,
    string Source = "live");

public sealed record OperationErrorDto(string Error, string Message, string? Detail = null);
