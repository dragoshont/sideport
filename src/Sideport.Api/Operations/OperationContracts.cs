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
    string? AccountProfileId = null);

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
    bool ConfirmedPlannedMutations = false);

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
    string? CatalogSha256 = null);

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

public sealed record OperationActionRequest(string? IdempotencyKey = null, string? Reason = null);

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
    string? ReconciledOperationId = null);

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
    SigningCutoverIntentDto? SigningCutoverIntent = null);

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
