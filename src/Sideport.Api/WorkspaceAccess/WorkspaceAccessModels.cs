using System.Text.Json.Serialization;

namespace Sideport.Api.WorkspaceAccess;

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceLifecycleState>))]
internal enum WorkspaceLifecycleState
{
    BootstrapRequired,
    Active,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceMemberRole>))]
internal enum WorkspaceMemberRole
{
    Owner,
    Family,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceMemberStatus>))]
internal enum WorkspaceMemberStatus
{
    Active,
    Suspended,
    Offboarded,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceAuthorityStatus>))]
internal enum WorkspaceAuthorityStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceOwnerClaimKind>))]
internal enum WorkspaceOwnerClaimKind
{
    Bootstrap,
    Recovery,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceHandoffKind>))]
internal enum WorkspaceHandoffKind
{
    Invitation,
    OwnerClaim,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceHandoffStatus>))]
internal enum WorkspaceHandoffStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceActorKind>))]
internal enum WorkspaceActorKind
{
    Member,
    RecoveryBearer,
    System,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceAuditAction>))]
internal enum WorkspaceAuditAction
{
    OwnerClaimCreated,
    OwnerClaimRevoked,
    OwnerClaimExpired,
    OwnerClaimAccepted,
    InvitationCreated,
    InvitationRevoked,
    InvitationExpired,
    InvitationAccepted,
    HandoffCreated,
    MemberStatusChanged,
    MemberOffboarded,
    WorkspaceRecoveredAfterRestore,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceAuditTargetType>))]
internal enum WorkspaceAuditTargetType
{
    Workspace,
    Member,
    Invitation,
    OwnerClaim,
    Handoff,
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkspaceReceiptKind>))]
internal enum WorkspaceReceiptKind
{
    OwnerBootstrap,
    OwnerRecovery,
    InvitationAccepted,
    AuthorityRevoked,
    MemberStatusChanged,
    MemberOffboarded,
    RestoreRecovery,
}

internal sealed record WorkspaceIdentityKey(string Issuer, string Subject);

internal sealed record WorkspaceActorRecord(WorkspaceActorKind Kind, string? MemberId)
{
    public static WorkspaceActorRecord RecoveryBearer { get; } = new(WorkspaceActorKind.RecoveryBearer, null);

    public static WorkspaceActorRecord System { get; } = new(WorkspaceActorKind.System, null);

    public static WorkspaceActorRecord ForMember(string memberId) =>
        new(WorkspaceActorKind.Member, memberId);
}

internal sealed record WorkspaceRecord(
    string WorkspaceId,
    string Name,
    WorkspaceLifecycleState State,
    long Version,
    string SecurityEpoch,
    string? OwnerMemberId,
    bool RestoreReviewRequired,
    string? BootstrapReceiptId,
    string? LastRestoreReceiptId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record WorkspaceMemberRecord(
    string MemberId,
    string OidcIssuer,
    string OidcSubject,
    string DisplayName,
    string? Email,
    WorkspaceMemberRole Role,
    WorkspaceMemberStatus Status,
    long Version,
    DateTimeOffset JoinedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastActiveAt,
    string? LastReceiptId);

internal sealed record WorkspaceOwnerClaimRecord(
    string ClaimId,
    WorkspaceOwnerClaimKind Kind,
    WorkspaceAuthorityStatus Status,
    string TokenHash,
    string CreationIdempotencyKeyHash,
    string SemanticDigest,
    WorkspaceActorRecord CreatedByActor,
    string? ExpectedOwnerMemberId,
    string? ImpactVersion,
    string? ClaimantMemberId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? ExpiredAt,
    string? ReceiptId);

internal sealed record WorkspaceInvitationRecord(
    string InvitationId,
    WorkspaceAuthorityStatus Status,
    string TokenHash,
    string CreationIdempotencyKeyHash,
    string SemanticDigest,
    string? DisplayName,
    string? ContactEmail,
    WorkspaceActorRecord CreatedByActor,
    string? AcceptedMemberId,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? ExpiredAt,
    string? ReceiptId);

internal sealed record WorkspaceHandoffRecord(
    string HandoffId,
    WorkspaceHandoffKind Kind,
    WorkspaceHandoffStatus Status,
    string AuthorityId,
    string TokenHash,
    string? AcceptedOidcIssuer,
    string? AcceptedOidcSubject,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset PurgeAt,
    DateTimeOffset? AcceptedAt,
    string? ReceiptId);

internal sealed record WorkspaceIdempotencyRecord(
    string IdempotencyId,
    string ActorKey,
    string Action,
    string SemanticTarget,
    string KeyHash,
    string SemanticDigest,
    string ResourceId,
    string? ReceiptId,
    DateTimeOffset CreatedAt,
    DateTimeOffset CompletedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Allowlisted counts only. This deliberately cannot carry free-form details,
/// identity claims, email, network data, tokens, paths, or Apple identifiers.
/// </summary>
internal sealed record WorkspaceAuditImpact(
    int? MemberCount = null,
    int? DeviceCount = null,
    int? RegistrationCount = null,
    int? QueuedOperationCount = null,
    int? RunningOperationCount = null,
    int? SchedulerEffectCount = null,
    string? ImpactVersion = null);

internal sealed record WorkspaceAuditEventRecord(
    string EventId,
    WorkspaceAuditAction Action,
    string Outcome,
    WorkspaceActorRecord Actor,
    WorkspaceAuditTargetType TargetType,
    string TargetId,
    DateTimeOffset OccurredAt,
    string RequestId,
    string? CorrelationId,
    WorkspaceAuditImpact? Impact);

internal sealed record WorkspaceReceiptRecord(
    string ReceiptId,
    WorkspaceReceiptKind Kind,
    string TargetId,
    string Outcome,
    DateTimeOffset RecordedAt,
    long WorkspaceVersion,
    string? MemberId,
    string? PreviousOwnerMemberId);

internal sealed record WorkspaceAccessDocument(
    int SchemaVersion,
    WorkspaceRecord Workspace,
    IReadOnlyList<WorkspaceMemberRecord> Members,
    IReadOnlyList<WorkspaceOwnerClaimRecord> OwnerClaims,
    IReadOnlyList<WorkspaceInvitationRecord> Invitations,
    IReadOnlyList<WorkspaceHandoffRecord> Handoffs,
    IReadOnlyList<WorkspaceIdempotencyRecord> Idempotency,
    IReadOnlyList<WorkspaceAuditEventRecord> AuditEvents,
    IReadOnlyList<WorkspaceReceiptRecord> Receipts);

internal sealed record WorkspaceOwnerClaimCreateRequest(
    string? ExpectedOwnerMemberId,
    string? ImpactVersion,
    TimeSpan Lifetime,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceInvitationCreateRequest(
    WorkspaceActorRecord Actor,
    string? DisplayName,
    string ContactEmail,
    TimeSpan Lifetime,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceAuthorityRevokeRequest(
    WorkspaceActorRecord Actor,
    long ExpectedVersion,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceAcceptanceRequest(
    WorkspaceIdentityKey Identity,
    string DisplayName,
    string? Email,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null,
    string? CurrentImpactVersion = null);

internal sealed record WorkspaceMemberStatusRequest(
    WorkspaceActorRecord Actor,
    WorkspaceMemberStatus Status,
    long ExpectedVersion,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceOffboardingFinalizeRequest(
    WorkspaceActorRecord Actor,
    long ExpectedMemberVersion,
    WorkspaceAuditImpact ValidatedImpact,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceAfterRestoreRequest(
    long ExpectedWorkspaceVersion,
    string IdempotencyKey,
    string RequestId,
    string? CorrelationId = null);

internal sealed record WorkspaceOwnerClaimCreateResult(
    WorkspaceOwnerClaimRecord Claim,
    string? Token,
    bool Created,
    WorkspaceImpactSnapshot? Impact = null);

internal sealed record WorkspaceInvitationCreateResult(
    WorkspaceInvitationRecord Invitation,
    string? Token,
    bool Created);

internal sealed record WorkspaceHandoffCreateResult(
    WorkspaceHandoffRecord Handoff,
    string Token);

internal sealed record WorkspaceHandoffResolution(
    WorkspaceHandoffRecord Handoff,
    WorkspaceInvitationRecord? Invitation,
    WorkspaceOwnerClaimRecord? OwnerClaim,
    WorkspaceReceiptRecord? Receipt,
    WorkspaceMemberRecord? AcceptedMember);

internal sealed record WorkspaceAcceptanceResult(
    WorkspaceMemberRecord Member,
    WorkspaceReceiptRecord Receipt,
    bool Replayed);

internal sealed record WorkspaceMutationResult<T>(T Value, bool Replayed);

internal sealed record WorkspaceOffboardingResult(
    WorkspaceReceiptRecord Receipt,
    WorkspaceAuditImpact Impact,
    bool Replayed);

internal sealed class WorkspaceAccessException : Exception
{
    public WorkspaceAccessException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public WorkspaceAccessException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
