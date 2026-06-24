namespace Sideport.Api.Operations;

public sealed record OperationActorDto(string Kind, string DisplayName);

public sealed record OperationTargetDto(string DeviceUdid, string BundleId, string? AppleId = null, string? TeamId = null);

public sealed record OperationIssueDto(string Code, string Message, string Source = "live", string? Detail = null);

public sealed record OperationLimitDto(string Code, string Label, int Used, int Limit, string Source = "derived");

public sealed record OperationPreflightRequest(string Type, string DeviceUdid, string BundleId);

public sealed record OperationPreflightDto(
    bool Ready,
    OperationTargetDto Target,
    IReadOnlyList<OperationIssueDto> Blockers,
    IReadOnlyList<OperationIssueDto> Warnings,
    IReadOnlyList<string> PlannedMutations,
    IReadOnlyList<OperationLimitDto> ScarceLimits,
    bool RequiresConfirmation,
    string Source = "live");

public sealed record RefreshOperationRequest(string DeviceUdid, string BundleId, string? IdempotencyKey = null);

public sealed record OperationActionRequest(string? IdempotencyKey = null, string? Reason = null);

public sealed record OperationStageDto(
    string Id,
    string Label,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string Message,
    OperationIssueDto? Error = null);

public sealed record OperationResultDto(bool Success, string BundleId, DateTimeOffset? ExpiresAt, string? Error);

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
    string Source = "live");

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