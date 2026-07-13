using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.Api.AppleAccess;

namespace Sideport.Api.WorkspaceAccess;

/// <summary>
/// Single-replica durable workspace authority store. Every security mutation,
/// receipt, and allowlisted audit event is committed by one temp-file rename.
/// Raw invitation, Owner-claim, handoff, and idempotency values are never
/// persisted.
/// </summary>
internal sealed class WorkspaceAccessStore
{
    internal const int CurrentSchemaVersion = 1;
    internal const int MaxAuthorityRecords = 10_000;
    internal const int MaxHandoffRecords = 10_000;
    internal const int MaxIdempotencyRecords = 2_000;
    internal const int MaxAuditEvents = 10_000;
    internal const string FileName = "workspace-access.json";

    private static readonly TimeSpan IdempotencyRetention = TimeSpan.FromHours(24);
    private static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan HandoffRetention = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal WorkspaceAccessStore(string stateDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateDirectory);
        _path = Path.Combine(Path.GetFullPath(stateDirectory), FileName);
        _time = timeProvider ?? TimeProvider.System;
    }

    internal string StatePath => _path;

    internal async Task<WorkspaceAccessDocument?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceMemberRecord?> FindMemberAsync(
        WorkspaceIdentityKey identity,
        CancellationToken ct = default)
    {
        WorkspaceAccessValidation.ValidateIdentity(identity);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            WorkspaceAccessDocument? document = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            return document?.Members.FirstOrDefault(member => WorkspaceAccessValidation.SameIdentity(member, identity));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceOwnerClaimCreateResult> CreateOwnerClaimAsync(
        WorkspaceOwnerClaimCreateRequest request,
        CancellationToken ct = default,
        Func<WorkspaceAccessDocument, CancellationToken, Task<WorkspaceImpactSnapshot>>? verifyImpact = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(request), "Owner claims must expire within one hour.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument? loaded = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            WorkspaceAccessDocument current = loaded is null
                ? CreateBootstrapDocument(now)
                : PruneRetention(loaded, now);

            WorkspaceOwnerClaimKind kind;
            string semanticTarget;
            if (current.Workspace.State == WorkspaceLifecycleState.BootstrapRequired)
            {
                if (request.ExpectedOwnerMemberId is not null || request.ImpactVersion is not null)
                    throw Domain("owner-replacement-preflight-stale", "The Owner-claim impact is no longer current.");
                kind = WorkspaceOwnerClaimKind.Bootstrap;
                semanticTarget = current.Workspace.WorkspaceId;
            }
            else
            {
                if (request.ExpectedOwnerMemberId is null || request.ImpactVersion is null)
                    throw Domain("owner-replacement-confirmation-required", "Owner replacement requires an exact impact confirmation.");
                if (!string.Equals(request.ExpectedOwnerMemberId, current.Workspace.OwnerMemberId, StringComparison.Ordinal))
                    throw Domain("owner-replacement-preflight-stale", "The Owner-claim impact is no longer current.");
                kind = WorkspaceOwnerClaimKind.Recovery;
                semanticTarget = request.ExpectedOwnerMemberId;
            }

            string semanticDigest = SemanticDigest(
                kind.ToString(),
                request.ExpectedOwnerMemberId,
                request.ImpactVersion,
                request.Lifetime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                ActorKey(WorkspaceActorRecord.RecoveryBearer),
                "owner-claim:create",
                request.IdempotencyKey,
                semanticTarget,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceOwnerClaimRecord replayedClaim = current.OwnerClaims.FirstOrDefault(item =>
                    string.Equals(item.ClaimId, replay.ResourceId, StringComparison.Ordinal))
                    ?? throw StoreUnavailable();
                return new(replayedClaim, Token: null, Created: false);
            }

            string creationKeyHash = HashText(request.IdempotencyKey);
            WorkspaceOwnerClaimRecord? retainedCreate = current.OwnerClaims.FirstOrDefault(item =>
                string.Equals(item.CreationIdempotencyKeyHash, creationKeyHash, StringComparison.Ordinal));
            if (retainedCreate is not null)
            {
                if (!string.Equals(retainedCreate.SemanticDigest, semanticDigest, StringComparison.Ordinal))
                    throw Domain("idempotency-key-reused", "The idempotency key was reused for a different request.");
                return new(retainedCreate, Token: null, Created: false);
            }

            WorkspaceImpactSnapshot? verifiedImpact = null;
            if (kind == WorkspaceOwnerClaimKind.Recovery)
            {
                if (verifyImpact is null)
                    throw Domain("owner-replacement-preflight-stale", "The Owner-claim impact was not verified inside the workspace mutation.");
                verifiedImpact = await verifyImpact(current, ct).ConfigureAwait(false);
                if (!string.Equals(verifiedImpact.TargetMemberId, request.ExpectedOwnerMemberId, StringComparison.Ordinal) ||
                    !string.Equals(verifiedImpact.ImpactVersion, request.ImpactVersion, StringComparison.Ordinal))
                {
                    throw Domain("owner-replacement-preflight-stale", "The Owner-claim impact is no longer current.");
                }
            }

            WorkspaceOwnerClaimRecord[] expiredClaims = current.OwnerClaims.Select(item =>
                item.Status == WorkspaceAuthorityStatus.Pending && item.ExpiresAt <= now
                    ? item with
                    {
                        Status = WorkspaceAuthorityStatus.Expired,
                        Version = checked(item.Version + 1),
                        UpdatedAt = now,
                        ExpiredAt = now,
                    }
                    : item).ToArray();
            foreach (WorkspaceOwnerClaimRecord expired in expiredClaims.Where(item =>
                         item.Status == WorkspaceAuthorityStatus.Expired &&
                         current.OwnerClaims.Any(original => original.ClaimId == item.ClaimId && original.Status == WorkspaceAuthorityStatus.Pending)))
            {
                current = current with
                {
                    AuditEvents = PrependAudit(current.AuditEvents, NewAudit(
                        WorkspaceAuditAction.OwnerClaimExpired,
                        WorkspaceActorRecord.System,
                        WorkspaceAuditTargetType.OwnerClaim,
                        expired.ClaimId,
                        now,
                        request.RequestId,
                        request.CorrelationId)),
                };
            }
            current = current with
            {
                OwnerClaims = expiredClaims,
                Handoffs = current.Handoffs.Select(handoff =>
                    handoff.Kind == WorkspaceHandoffKind.OwnerClaim &&
                    handoff.Status == WorkspaceHandoffStatus.Pending &&
                    expiredClaims.Any(claim => claim.ClaimId == handoff.AuthorityId && claim.Status == WorkspaceAuthorityStatus.Expired)
                        ? handoff with { Status = WorkspaceHandoffStatus.Revoked, UpdatedAt = now }
                        : handoff).ToArray(),
            };
            if (current.OwnerClaims.Any(item => item.Status == WorkspaceAuthorityStatus.Pending))
                throw Domain("owner-claim-pending", "Revoke the existing Owner claim before creating a replacement.");

            EnsureAuthorityCapacity(current);
            EnsureIdempotencyCapacity(current);
            string claimId = NewId("owner_claim_");
            byte[] secret = RandomNumberGenerator.GetBytes(32);
            string token = BuildAuthorityToken("spown1", claimId, secret);
            var claim = new WorkspaceOwnerClaimRecord(
                claimId,
                kind,
                WorkspaceAuthorityStatus.Pending,
                HashBytes(secret),
                HashText(request.IdempotencyKey),
                semanticDigest,
                WorkspaceActorRecord.RecoveryBearer,
                request.ExpectedOwnerMemberId,
                request.ImpactVersion,
                ClaimantMemberId: null,
                Version: 1,
                CreatedAt: now,
                UpdatedAt: now,
                ExpiresAt: now.Add(request.Lifetime),
                AcceptedAt: null,
                RevokedAt: null,
                ExpiredAt: null,
                ReceiptId: null);

            WorkspaceRecord workspace = loaded is null
                ? current.Workspace
                : Advance(current.Workspace, now);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                ActorKey(WorkspaceActorRecord.RecoveryBearer),
                "owner-claim:create",
                semanticTarget,
                request.IdempotencyKey,
                semanticDigest,
                claimId,
                receiptId: null,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.OwnerClaimCreated,
                WorkspaceActorRecord.RecoveryBearer,
                WorkspaceAuditTargetType.OwnerClaim,
                claimId,
                now,
                request.RequestId,
                request.CorrelationId,
                kind == WorkspaceOwnerClaimKind.Recovery
                    ? WorkspaceImpactService.ToAuditImpact(verifiedImpact!)
                    : null);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                OwnerClaims = current.OwnerClaims.Append(claim).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(claim, token, Created: true, verifiedImpact);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceMutationResult<WorkspaceOwnerClaimRecord>> RevokeOwnerClaimAsync(
        string claimId,
        WorkspaceAuthorityRevokeRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimId);
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);
        if (request.Actor.Kind != WorkspaceActorKind.RecoveryBearer)
            throw Domain("capability-denied", "Only recovery access can revoke an Owner claim.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            string semanticDigest = SemanticDigest(claimId, request.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                ActorKey(request.Actor),
                "owner-claim:revoke",
                request.IdempotencyKey,
                claimId,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceOwnerClaimRecord replayed = current.OwnerClaims.FirstOrDefault(item => item.ClaimId == claimId)
                    ?? throw StoreUnavailable();
                return new(replayed, Replayed: true);
            }

            int index = IndexOf(current.OwnerClaims, item => item.ClaimId == claimId);
            if (index < 0)
                throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
            WorkspaceOwnerClaimRecord existing = current.OwnerClaims[index];
            EnsureExpectedVersion(existing.Version, request.ExpectedVersion);
            if (existing.Status != WorkspaceAuthorityStatus.Pending)
                throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.AuthorityRevoked,
                claimId,
                workspace.Version,
                now);
            WorkspaceOwnerClaimRecord revoked = existing with
            {
                Status = WorkspaceAuthorityStatus.Revoked,
                Version = checked(existing.Version + 1),
                UpdatedAt = now,
                RevokedAt = now,
                ReceiptId = receipt.ReceiptId,
            };
            WorkspaceOwnerClaimRecord[] claims = Replace(current.OwnerClaims, index, revoked);
            WorkspaceHandoffRecord[] handoffs = RevokeHandoffs(
                current.Handoffs,
                WorkspaceHandoffKind.OwnerClaim,
                claimId,
                now);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                ActorKey(request.Actor),
                "owner-claim:revoke",
                claimId,
                request.IdempotencyKey,
                semanticDigest,
                claimId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.OwnerClaimRevoked,
                request.Actor,
                WorkspaceAuditTargetType.OwnerClaim,
                claimId,
                now,
                request.RequestId,
                request.CorrelationId);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                OwnerClaims = claims,
                Handoffs = handoffs,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(revoked, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceHandoffCreateResult> ExchangeOwnerClaimAsync(
        string claimToken,
        string requestId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        WorkspaceAccessValidation.ValidateRequestIds(requestId, correlationId);
        ParsedAuthorityToken parsed = ParseAuthorityToken(
            claimToken,
            "spown1",
            "owner_claim_",
            "owner-claim-unavailable");
        return await ExchangeAuthorityAsync(
            parsed,
            WorkspaceHandoffKind.OwnerClaim,
            WorkspaceAuditTargetType.OwnerClaim,
            requestId,
            correlationId,
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceAcceptanceResult> AcceptOwnerClaimAsync(
        string handoffToken,
        WorkspaceAcceptanceRequest request,
        CancellationToken ct = default,
        Func<WorkspaceAccessDocument, WorkspaceOwnerClaimRecord, CancellationToken, Task<WorkspaceImpactSnapshot>>? verifyImpact = null)
    {
        ValidateAcceptanceRequest(request);
        ParsedAuthorityToken parsedHandoff = ParseAuthorityToken(
            handoffToken,
            "sphnd1",
            "handoff_",
            "owner-claim-unavailable");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            int handoffIndex = FindHandoffIndex(current.Handoffs, parsedHandoff, WorkspaceHandoffKind.OwnerClaim);
            if (handoffIndex < 0)
                throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
            WorkspaceHandoffRecord handoff = current.Handoffs[handoffIndex];
            WorkspaceAcceptanceResult? naturalReplay = TryOwnerClaimReplay(current, handoff, request.Identity);
            if (naturalReplay is not null)
                return naturalReplay;
            EnsurePendingHandoff(handoff, now, "owner-claim-unavailable");

            int claimIndex = IndexOf(current.OwnerClaims, item => item.ClaimId == handoff.AuthorityId);
            if (claimIndex < 0)
                throw StoreUnavailable();
            WorkspaceOwnerClaimRecord claim = current.OwnerClaims[claimIndex];
            if (claim.Status != WorkspaceAuthorityStatus.Pending || claim.ExpiresAt <= now)
                throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
            if (claim.Kind == WorkspaceOwnerClaimKind.Bootstrap &&
                (current.Workspace.State != WorkspaceLifecycleState.BootstrapRequired ||
                 current.Workspace.OwnerMemberId is not null ||
                 current.Members.Count != 0))
            {
                throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
            }
            if (claim.Kind == WorkspaceOwnerClaimKind.Recovery &&
                (current.Workspace.State != WorkspaceLifecycleState.Active ||
                 !string.Equals(current.Workspace.OwnerMemberId, claim.ExpectedOwnerMemberId, StringComparison.Ordinal)))
            {
                throw Domain("owner-replacement-preflight-stale", "The Owner replacement impact is no longer current.");
            }
            WorkspaceMemberRecord? existingIdentity = current.Members.FirstOrDefault(member =>
                WorkspaceAccessValidation.SameIdentity(member, request.Identity));
            if (existingIdentity is not null && existingIdentity.Status != WorkspaceMemberStatus.Active)
                throw Domain("member-access-disabled", "This Sideport membership is disabled.");
            if (claim.Kind == WorkspaceOwnerClaimKind.Recovery &&
                existingIdentity is not null &&
                string.Equals(existingIdentity.MemberId, current.Workspace.OwnerMemberId, StringComparison.Ordinal))
            {
                throw Domain("member-already-active", "This account is already the active Owner.");
            }

            string actorKey = ActorKey(request.Identity);
            string semanticDigest = SemanticDigest(claim.ClaimId, actorKey);
            WorkspaceIdempotencyRecord? idempotencyReplay = FindIdempotency(
                current,
                actorKey,
                "owner-claim:accept",
                request.IdempotencyKey,
                claim.ClaimId,
                semanticDigest);
            if (idempotencyReplay is not null)
                throw StoreUnavailable();

            WorkspaceImpactSnapshot? verifiedImpact = null;
            if (claim.Kind == WorkspaceOwnerClaimKind.Recovery)
            {
                if (verifyImpact is null)
                    throw Domain("owner-replacement-preflight-stale", "The Owner replacement impact was not verified inside the workspace mutation.");
                verifiedImpact = await verifyImpact(current, claim, ct).ConfigureAwait(false);
                if (!string.Equals(verifiedImpact.TargetMemberId, claim.ExpectedOwnerMemberId, StringComparison.Ordinal) ||
                    !string.Equals(verifiedImpact.ImpactVersion, claim.ImpactVersion, StringComparison.Ordinal))
                {
                    throw Domain("owner-replacement-preflight-stale", "The Owner replacement impact is no longer current.");
                }
            }
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            string memberId = existingIdentity?.MemberId ?? NewId("member_");
            string? previousOwnerId = claim.Kind == WorkspaceOwnerClaimKind.Recovery
                ? current.Workspace.OwnerMemberId
                : null;
            WorkspaceReceiptKind receiptKind = claim.Kind == WorkspaceOwnerClaimKind.Bootstrap
                ? WorkspaceReceiptKind.OwnerBootstrap
                : WorkspaceReceiptKind.OwnerRecovery;
            WorkspaceReceiptRecord receipt = NewReceipt(
                receiptKind,
                claim.ClaimId,
                workspace.Version,
                now,
                memberId,
                previousOwnerId);

            var members = current.Members.ToList();
            if (previousOwnerId is not null)
            {
                int previousOwnerIndex = members.FindIndex(item => item.MemberId == previousOwnerId);
                if (previousOwnerIndex < 0)
                    throw StoreUnavailable();
                WorkspaceMemberRecord previousOwner = members[previousOwnerIndex];
                if (previousOwner.Role != WorkspaceMemberRole.Owner || previousOwner.Status != WorkspaceMemberStatus.Active)
                    throw StoreUnavailable();
                members[previousOwnerIndex] = previousOwner with
                {
                    Status = WorkspaceMemberStatus.Suspended,
                    Version = checked(previousOwner.Version + 1),
                    UpdatedAt = now,
                    LastReceiptId = receipt.ReceiptId,
                };
            }

            WorkspaceMemberRecord claimant;
            if (existingIdentity is null)
            {
                claimant = new WorkspaceMemberRecord(
                    memberId,
                    request.Identity.Issuer,
                    request.Identity.Subject,
                    request.DisplayName,
                    request.Email,
                    WorkspaceMemberRole.Owner,
                    WorkspaceMemberStatus.Active,
                    Version: 1,
                    JoinedAt: now,
                    UpdatedAt: now,
                    LastActiveAt: now,
                    LastReceiptId: receipt.ReceiptId);
                members.Add(claimant);
            }
            else
            {
                int claimantIndex = members.FindIndex(item => item.MemberId == existingIdentity.MemberId);
                claimant = existingIdentity with
                {
                    DisplayName = request.DisplayName,
                    Email = request.Email,
                    Role = WorkspaceMemberRole.Owner,
                    Status = WorkspaceMemberStatus.Active,
                    Version = checked(existingIdentity.Version + 1),
                    UpdatedAt = now,
                    LastActiveAt = now,
                    LastReceiptId = receipt.ReceiptId,
                };
                members[claimantIndex] = claimant;
            }

            WorkspaceOwnerClaimRecord acceptedClaim = claim with
            {
                Status = WorkspaceAuthorityStatus.Accepted,
                ClaimantMemberId = memberId,
                Version = checked(claim.Version + 1),
                UpdatedAt = now,
                AcceptedAt = now,
                ReceiptId = receipt.ReceiptId,
            };
            WorkspaceOwnerClaimRecord[] claims = Replace(current.OwnerClaims, claimIndex, acceptedClaim);
            WorkspaceHandoffRecord acceptedHandoff = handoff with
            {
                Status = WorkspaceHandoffStatus.Accepted,
                AcceptedOidcIssuer = request.Identity.Issuer,
                AcceptedOidcSubject = request.Identity.Subject,
                UpdatedAt = now,
                AcceptedAt = now,
                ReceiptId = receipt.ReceiptId,
            };
            WorkspaceHandoffRecord[] handoffs = AcceptOneAndRevokeOtherHandoffs(
                current.Handoffs,
                handoffIndex,
                acceptedHandoff,
                now);
            workspace = workspace with
            {
                State = WorkspaceLifecycleState.Active,
                OwnerMemberId = memberId,
                BootstrapReceiptId = claim.Kind == WorkspaceOwnerClaimKind.Bootstrap
                    ? receipt.ReceiptId
                    : workspace.BootstrapReceiptId,
            };
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "owner-claim:accept",
                claim.ClaimId,
                request.IdempotencyKey,
                semanticDigest,
                memberId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.OwnerClaimAccepted,
                WorkspaceActorRecord.ForMember(memberId),
                WorkspaceAuditTargetType.OwnerClaim,
                claim.ClaimId,
                now,
                request.RequestId,
                request.CorrelationId,
                claim.Kind == WorkspaceOwnerClaimKind.Recovery
                    ? WorkspaceImpactService.ToAuditImpact(verifiedImpact!) with { MemberCount = members.Count }
                    : new WorkspaceAuditImpact(MemberCount: members.Count));
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Members = members.ToArray(),
                OwnerClaims = claims,
                Handoffs = handoffs,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(claimant, receipt, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task<WorkspaceHandoffResolution> ResolveOwnerClaimHandoffAsync(
        string handoffToken,
        WorkspaceIdentityKey identity,
        CancellationToken ct = default) =>
        ResolveHandoffAsync(handoffToken, WorkspaceHandoffKind.OwnerClaim, identity, ct);

    internal async Task<WorkspaceInvitationCreateResult> CreateInvitationAsync(
        WorkspaceInvitationCreateRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateInvitationPresentation(request.DisplayName, request.ContactEmail);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);
        if (request.Lifetime < TimeSpan.FromMinutes(10) || request.Lifetime > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(request), "Invitations must expire between ten minutes and thirty days.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            EnsureActiveOwnerActor(current, request.Actor);
            string actorKey = ActorKey(request.Actor);
            string semanticDigest = SemanticDigest(
                request.DisplayName,
                request.ContactEmail,
                request.Lifetime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                actorKey,
                "invitation:create",
                request.IdempotencyKey,
                "invitations",
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceInvitationRecord replayedInvitation = current.Invitations.FirstOrDefault(item => item.InvitationId == replay.ResourceId)
                    ?? throw StoreUnavailable();
                return new(replayedInvitation, Token: null, Created: false);
            }

            string creationKeyHash = HashText(request.IdempotencyKey);
            WorkspaceInvitationRecord? retainedCreate = current.Invitations.FirstOrDefault(item =>
                SameActor(item.CreatedByActor, request.Actor) &&
                string.Equals(item.CreationIdempotencyKeyHash, creationKeyHash, StringComparison.Ordinal));
            if (retainedCreate is not null)
            {
                if (!string.Equals(retainedCreate.SemanticDigest, semanticDigest, StringComparison.Ordinal))
                    throw Domain("idempotency-key-reused", "The idempotency key was reused for a different request.");
                return new(retainedCreate, Token: null, Created: false);
            }

            EnsureAuthorityCapacity(current);
            EnsureIdempotencyCapacity(current);
            string invitationId = NewId("invitation_");
            byte[] secret = RandomNumberGenerator.GetBytes(32);
            string token = BuildAuthorityToken("spinv1", invitationId, secret);
            var invitation = new WorkspaceInvitationRecord(
                invitationId,
                WorkspaceAuthorityStatus.Pending,
                HashBytes(secret),
                HashText(request.IdempotencyKey),
                semanticDigest,
                request.DisplayName,
                request.ContactEmail,
                request.Actor,
                AcceptedMemberId: null,
                Version: 1,
                CreatedAt: now,
                UpdatedAt: now,
                ExpiresAt: now.Add(request.Lifetime),
                AcceptedAt: null,
                RevokedAt: null,
                ExpiredAt: null,
                ReceiptId: null);
            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "invitation:create",
                "invitations",
                request.IdempotencyKey,
                semanticDigest,
                invitationId,
                receiptId: null,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.InvitationCreated,
                request.Actor,
                WorkspaceAuditTargetType.Invitation,
                invitationId,
                now,
                request.RequestId,
                request.CorrelationId);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Invitations = current.Invitations.Append(invitation).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(invitation, token, Created: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceMutationResult<WorkspaceInvitationRecord>> RevokeInvitationAsync(
        string invitationId,
        WorkspaceAuthorityRevokeRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invitationId);
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            EnsureActiveOwnerActor(current, request.Actor);
            string actorKey = ActorKey(request.Actor);
            string semanticDigest = SemanticDigest(invitationId, request.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                actorKey,
                "invitation:revoke",
                request.IdempotencyKey,
                invitationId,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceInvitationRecord replayed = current.Invitations.FirstOrDefault(item => item.InvitationId == invitationId)
                    ?? throw StoreUnavailable();
                return new(replayed, Replayed: true);
            }

            int index = IndexOf(current.Invitations, item => item.InvitationId == invitationId);
            if (index < 0)
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            WorkspaceInvitationRecord existing = current.Invitations[index];
            EnsureExpectedVersion(existing.Version, request.ExpectedVersion);
            if (existing.Status != WorkspaceAuthorityStatus.Pending)
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.AuthorityRevoked,
                invitationId,
                workspace.Version,
                now);
            WorkspaceInvitationRecord revoked = ToInvitationTombstone(existing with
            {
                Status = WorkspaceAuthorityStatus.Revoked,
                Version = checked(existing.Version + 1),
                UpdatedAt = now,
                RevokedAt = now,
                ReceiptId = receipt.ReceiptId,
            });
            WorkspaceInvitationRecord[] invitations = Replace(current.Invitations, index, revoked);
            WorkspaceHandoffRecord[] handoffs = RevokeHandoffs(
                current.Handoffs,
                WorkspaceHandoffKind.Invitation,
                invitationId,
                now);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "invitation:revoke",
                invitationId,
                request.IdempotencyKey,
                semanticDigest,
                invitationId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.InvitationRevoked,
                request.Actor,
                WorkspaceAuditTargetType.Invitation,
                invitationId,
                now,
                request.RequestId,
                request.CorrelationId);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Invitations = invitations,
                Handoffs = handoffs,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(revoked, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceHandoffCreateResult> ExchangeInvitationAsync(
        string invitationToken,
        string requestId,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        WorkspaceAccessValidation.ValidateRequestIds(requestId, correlationId);
        ParsedAuthorityToken parsed = ParseAuthorityToken(
            invitationToken,
            "spinv1",
            "invitation_",
            "invitation-unavailable");
        return await ExchangeAuthorityAsync(
            parsed,
            WorkspaceHandoffKind.Invitation,
            WorkspaceAuditTargetType.Invitation,
            requestId,
            correlationId,
            ct).ConfigureAwait(false);
    }

    internal async Task<WorkspaceAcceptanceResult> AcceptInvitationAsync(
        string handoffToken,
        WorkspaceAcceptanceRequest request,
        CancellationToken ct = default)
    {
        ValidateAcceptanceRequest(request);
        ParsedAuthorityToken parsedHandoff = ParseAuthorityToken(
            handoffToken,
            "sphnd1",
            "handoff_",
            "invitation-unavailable");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            int handoffIndex = FindHandoffIndex(current.Handoffs, parsedHandoff, WorkspaceHandoffKind.Invitation);
            if (handoffIndex < 0)
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            WorkspaceHandoffRecord handoff = current.Handoffs[handoffIndex];
            WorkspaceAcceptanceResult? naturalReplay = TryInvitationReplay(current, handoff, request.Identity);
            if (naturalReplay is not null)
                return naturalReplay;
            EnsurePendingHandoff(handoff, now, "invitation-unavailable");

            int invitationIndex = IndexOf(current.Invitations, item => item.InvitationId == handoff.AuthorityId);
            if (invitationIndex < 0)
                throw StoreUnavailable();
            WorkspaceInvitationRecord invitation = current.Invitations[invitationIndex];
            if (invitation.Status != WorkspaceAuthorityStatus.Pending)
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            if (invitation.ExpiresAt <= now)
                throw Domain("invitation-expired", "The invitation has expired.");

            WorkspaceMemberRecord? existingIdentity = current.Members.FirstOrDefault(member =>
                WorkspaceAccessValidation.SameIdentity(member, request.Identity));
            if (existingIdentity is not null)
            {
                string code = existingIdentity.Status == WorkspaceMemberStatus.Active
                    ? "member-already-active"
                    : "member-access-disabled";
                throw Domain(code, "This account cannot accept the invitation.");
            }

            string actorKey = ActorKey(request.Identity);
            string semanticDigest = SemanticDigest(invitation.InvitationId, actorKey);
            WorkspaceIdempotencyRecord? idempotencyReplay = FindIdempotency(
                current,
                actorKey,
                "invitation:accept",
                request.IdempotencyKey,
                invitation.InvitationId,
                semanticDigest);
            if (idempotencyReplay is not null)
                throw StoreUnavailable();
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            string memberId = NewId("member_");
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.InvitationAccepted,
                invitation.InvitationId,
                workspace.Version,
                now,
                memberId);
            var member = new WorkspaceMemberRecord(
                memberId,
                request.Identity.Issuer,
                request.Identity.Subject,
                request.DisplayName,
                request.Email,
                WorkspaceMemberRole.Family,
                WorkspaceMemberStatus.Active,
                Version: 1,
                JoinedAt: now,
                UpdatedAt: now,
                LastActiveAt: now,
                LastReceiptId: receipt.ReceiptId);
            WorkspaceInvitationRecord acceptedInvitation = ToInvitationTombstone(invitation with
            {
                Status = WorkspaceAuthorityStatus.Accepted,
                AcceptedMemberId = memberId,
                Version = checked(invitation.Version + 1),
                UpdatedAt = now,
                AcceptedAt = now,
                ReceiptId = receipt.ReceiptId,
            });
            WorkspaceInvitationRecord[] invitations = Replace(current.Invitations, invitationIndex, acceptedInvitation);
            WorkspaceHandoffRecord acceptedHandoff = handoff with
            {
                Status = WorkspaceHandoffStatus.Accepted,
                AcceptedOidcIssuer = request.Identity.Issuer,
                AcceptedOidcSubject = request.Identity.Subject,
                UpdatedAt = now,
                AcceptedAt = now,
                ReceiptId = receipt.ReceiptId,
            };
            WorkspaceHandoffRecord[] handoffs = AcceptOneAndRevokeOtherHandoffs(
                current.Handoffs,
                handoffIndex,
                acceptedHandoff,
                now);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "invitation:accept",
                invitation.InvitationId,
                request.IdempotencyKey,
                semanticDigest,
                memberId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.InvitationAccepted,
                WorkspaceActorRecord.ForMember(memberId),
                WorkspaceAuditTargetType.Invitation,
                invitation.InvitationId,
                now,
                request.RequestId,
                request.CorrelationId,
                new WorkspaceAuditImpact(MemberCount: current.Members.Count + 1));
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Members = current.Members.Append(member).ToArray(),
                Invitations = invitations,
                Handoffs = handoffs,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(member, receipt, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task<WorkspaceHandoffResolution> ResolveInvitationHandoffAsync(
        string handoffToken,
        WorkspaceIdentityKey identity,
        CancellationToken ct = default) =>
        ResolveHandoffAsync(handoffToken, WorkspaceHandoffKind.Invitation, identity, ct);

    internal async Task<WorkspaceInvitationRecord> ResolvePendingInvitationForEnrollmentAsync(
        string handoffToken,
        CancellationToken ct = default)
    {
        ParsedAuthorityToken parsed = ParseAuthorityToken(
            handoffToken,
            "sphnd1",
            "handoff_",
            "invitation-unavailable");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            int handoffIndex = FindHandoffIndex(current.Handoffs, parsed, WorkspaceHandoffKind.Invitation);
            if (handoffIndex < 0)
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            WorkspaceHandoffRecord handoff = current.Handoffs[handoffIndex];
            EnsurePendingHandoff(handoff, now, "invitation-unavailable");
            WorkspaceInvitationRecord invitation = current.Invitations.FirstOrDefault(item =>
                item.InvitationId == handoff.AuthorityId) ?? throw StoreUnavailable();
            if (invitation.Status != WorkspaceAuthorityStatus.Pending || invitation.ExpiresAt <= now ||
                invitation.ContactEmail is null)
            {
                throw Domain("invitation-unavailable", "The invitation is unavailable.");
            }
            return invitation;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceMutationResult<WorkspaceMemberRecord>> SetFamilyMemberStatusAsync(
        string memberId,
        WorkspaceMemberStatusRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);
        if (request.Status == WorkspaceMemberStatus.Offboarded)
            throw Domain("offboarding-confirmation-required", "Offboarding requires a separately validated impact preflight.");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            EnsureActiveOwnerActor(current, request.Actor);
            string actorKey = ActorKey(request.Actor);
            string semanticDigest = SemanticDigest(
                memberId,
                request.Status.ToString(),
                request.ExpectedVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                actorKey,
                "member:status",
                request.IdempotencyKey,
                memberId,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceMemberRecord replayed = current.Members.FirstOrDefault(item => item.MemberId == memberId)
                    ?? throw StoreUnavailable();
                return new(replayed, Replayed: true);
            }

            int memberIndex = IndexOf(current.Members, item => item.MemberId == memberId);
            if (memberIndex < 0)
                throw Domain("resource-not-found", "The member was not found.");
            WorkspaceMemberRecord existing = current.Members[memberIndex];
            if (existing.Role != WorkspaceMemberRole.Family)
                throw Domain("last-owner-required", "The active Owner cannot be suspended or offboarded.");
            EnsureExpectedVersion(existing.Version, request.ExpectedVersion);
            if (existing.Status == request.Status)
                return new(existing, Replayed: true);
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.MemberStatusChanged,
                memberId,
                workspace.Version,
                now,
                memberId);
            WorkspaceMemberRecord updatedMember = existing with
            {
                Status = request.Status,
                Version = checked(existing.Version + 1),
                UpdatedAt = now,
                LastReceiptId = receipt.ReceiptId,
            };
            WorkspaceMemberRecord[] members = Replace(current.Members, memberIndex, updatedMember);
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "member:status",
                memberId,
                request.IdempotencyKey,
                semanticDigest,
                memberId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.MemberStatusChanged,
                request.Actor,
                WorkspaceAuditTargetType.Member,
                memberId,
                now,
                request.RequestId,
                request.CorrelationId);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Members = members,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(updatedMember, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceOffboardingResult> FinalizeFamilyMemberOffboardingAsync(
        string memberId,
        WorkspaceOffboardingFinalizeRequest request,
        CancellationToken ct = default,
        Func<WorkspaceAccessDocument, CancellationToken, Task<WorkspaceImpactSnapshot>>? verifyImpact = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberId);
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateOffboardingImpact(request.ValidatedImpact);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            EnsureActiveOwnerActor(current, request.Actor);
            string actorKey = ActorKey(request.Actor);
            string semanticDigest = SemanticDigest(
                memberId,
                request.ExpectedMemberVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                request.ValidatedImpact.ImpactVersion);
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                actorKey,
                "member:offboard",
                request.IdempotencyKey,
                memberId,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceReceiptRecord receiptReplay = current.Receipts.FirstOrDefault(item =>
                    item.ReceiptId == replay.ReceiptId &&
                    item.Kind == WorkspaceReceiptKind.MemberOffboarded &&
                    string.Equals(item.TargetId, memberId, StringComparison.Ordinal))
                    ?? throw StoreUnavailable();
                WorkspaceAuditImpact replayedImpact = current.AuditEvents.FirstOrDefault(item =>
                    item.Action == WorkspaceAuditAction.MemberOffboarded &&
                    string.Equals(item.TargetId, memberId, StringComparison.Ordinal) &&
                    string.Equals(item.Impact?.ImpactVersion, request.ValidatedImpact.ImpactVersion, StringComparison.Ordinal))?.Impact
                    ?? throw StoreUnavailable();
                return new(receiptReplay, replayedImpact, Replayed: true);
            }

            int memberIndex = IndexOf(current.Members, item => item.MemberId == memberId);
            if (memberIndex < 0)
                throw Domain("resource-not-found", "The member was not found.");
            WorkspaceMemberRecord existing = current.Members[memberIndex];
            if (existing.Role != WorkspaceMemberRole.Family)
                throw Domain("last-owner-required", "The active Owner cannot be suspended or offboarded.");
            EnsureExpectedVersion(existing.Version, request.ExpectedMemberVersion);
            if (existing.Status != WorkspaceMemberStatus.Suspended)
                throw Domain("offboarding-preflight-stale", "The member must still be suspended before offboarding can finish.");
            if (verifyImpact is null)
                throw Domain("offboarding-preflight-stale", "The offboarding impact was not verified inside the workspace mutation.");
            WorkspaceImpactSnapshot verifiedImpact = await verifyImpact(current, ct).ConfigureAwait(false);
            if (!string.Equals(verifiedImpact.TargetMemberId, memberId, StringComparison.Ordinal) ||
                !string.Equals(verifiedImpact.ImpactVersion, request.ValidatedImpact.ImpactVersion, StringComparison.Ordinal))
            {
                throw Domain("offboarding-preflight-stale", "The offboarding impact is no longer current.");
            }
            WorkspaceAuditImpact auditImpact = WorkspaceImpactService.ToAuditImpact(verifiedImpact);
            EnsureIdempotencyCapacity(current);

            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.MemberOffboarded,
                memberId,
                workspace.Version,
                now,
                memberId);
            WorkspaceMemberRecord updatedMember = existing with
            {
                Status = WorkspaceMemberStatus.Offboarded,
                Version = checked(existing.Version + 1),
                UpdatedAt = now,
                LastReceiptId = receipt.ReceiptId,
            };
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                actorKey,
                "member:offboard",
                memberId,
                request.IdempotencyKey,
                semanticDigest,
                memberId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.MemberOffboarded,
                request.Actor,
                WorkspaceAuditTargetType.Member,
                memberId,
                now,
                request.RequestId,
                request.CorrelationId,
                auditImpact);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Members = Replace(current.Members, memberIndex, updatedMember),
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(receipt, auditImpact, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<WorkspaceMutationResult<WorkspaceReceiptRecord>> RecoverAfterRestoreAsync(
        WorkspaceAfterRestoreRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            string semanticDigest = SemanticDigest(request.ExpectedWorkspaceVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            WorkspaceIdempotencyRecord? replay = FindIdempotency(
                current,
                ActorKey(WorkspaceActorRecord.RecoveryBearer),
                "workspace:after-restore",
                request.IdempotencyKey,
                current.Workspace.WorkspaceId,
                semanticDigest);
            if (replay is not null)
            {
                WorkspaceReceiptRecord receiptReplay = current.Receipts.FirstOrDefault(item => item.ReceiptId == replay.ReceiptId)
                    ?? throw StoreUnavailable();
                return new(receiptReplay, Replayed: true);
            }

            EnsureExpectedVersion(current.Workspace.Version, request.ExpectedWorkspaceVersion);
            EnsureIdempotencyCapacity(current);
            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceReceiptRecord receipt = NewReceipt(
                WorkspaceReceiptKind.RestoreRecovery,
                current.Workspace.WorkspaceId,
                workspace.Version,
                now,
                current.Workspace.OwnerMemberId);
            workspace = workspace with
            {
                SecurityEpoch = NewOpaqueSecret(),
                RestoreReviewRequired = true,
                LastRestoreReceiptId = receipt.ReceiptId,
            };
            WorkspaceOwnerClaimRecord[] claims = current.OwnerClaims.Select(claim =>
                claim.Status == WorkspaceAuthorityStatus.Pending
                    ? claim with
                    {
                        Status = WorkspaceAuthorityStatus.Revoked,
                        Version = checked(claim.Version + 1),
                        UpdatedAt = now,
                        RevokedAt = now,
                        ReceiptId = receipt.ReceiptId,
                    }
                    : claim).ToArray();
            WorkspaceInvitationRecord[] invitations = current.Invitations.Select(invitation =>
                invitation.Status == WorkspaceAuthorityStatus.Pending
                    ? ToInvitationTombstone(invitation with
                    {
                        Status = WorkspaceAuthorityStatus.Revoked,
                        Version = checked(invitation.Version + 1),
                        UpdatedAt = now,
                        RevokedAt = now,
                        ReceiptId = receipt.ReceiptId,
                    })
                    : invitation).ToArray();
            WorkspaceHandoffRecord[] handoffs = current.Handoffs.Select(handoff =>
                handoff.Status == WorkspaceHandoffStatus.Pending
                    ? handoff with { Status = WorkspaceHandoffStatus.Revoked, UpdatedAt = now }
                    : handoff).ToArray();
            WorkspaceIdempotencyRecord idempotency = NewIdempotency(
                ActorKey(WorkspaceActorRecord.RecoveryBearer),
                "workspace:after-restore",
                current.Workspace.WorkspaceId,
                request.IdempotencyKey,
                semanticDigest,
                current.Workspace.WorkspaceId,
                receipt.ReceiptId,
                now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.WorkspaceRecoveredAfterRestore,
                WorkspaceActorRecord.RecoveryBearer,
                WorkspaceAuditTargetType.Workspace,
                current.Workspace.WorkspaceId,
                now,
                request.RequestId,
                request.CorrelationId,
                new WorkspaceAuditImpact(MemberCount: current.Members.Count));
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                OwnerClaims = claims,
                Invitations = invitations,
                Handoffs = handoffs,
                Receipts = current.Receipts.Append(receipt).ToArray(),
                Idempotency = Prepend(current.Idempotency, idempotency),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(receipt, Replayed: false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkspaceHandoffCreateResult> ExchangeAuthorityAsync(
        ParsedAuthorityToken parsed,
        WorkspaceHandoffKind kind,
        WorkspaceAuditTargetType targetType,
        string requestId,
        string? correlationId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            DateTimeOffset authorityExpiresAt;
            if (kind == WorkspaceHandoffKind.Invitation)
            {
                int invitationIndex = IndexOf(current.Invitations, item => item.InvitationId == parsed.RecordId);
                if (invitationIndex < 0 || !FixedTimeHashEquals(current.Invitations[invitationIndex].TokenHash, parsed.Secret))
                    throw Domain("invitation-unavailable", "The invitation is unavailable.");
                WorkspaceInvitationRecord invitation = current.Invitations[invitationIndex];
                if (invitation.Status == WorkspaceAuthorityStatus.Revoked)
                    throw Domain("invitation-revoked", "The invitation was revoked.");
                if (invitation.Status == WorkspaceAuthorityStatus.Accepted)
                    throw Domain("invitation-already-used", "The invitation was already used.");
                if (invitation.Status == WorkspaceAuthorityStatus.Expired || invitation.ExpiresAt <= now)
                {
                    if (invitation.Status == WorkspaceAuthorityStatus.Pending)
                        await ExpireInvitationAsync(current, invitationIndex, now, requestId, correlationId, ct).ConfigureAwait(false);
                    else
                        await SaveUnsafeAsync(current, ct).ConfigureAwait(false);
                    throw Domain("invitation-expired", "The invitation has expired.");
                }
                authorityExpiresAt = invitation.ExpiresAt;
            }
            else
            {
                int claimIndex = IndexOf(current.OwnerClaims, item => item.ClaimId == parsed.RecordId);
                if (claimIndex < 0 || !FixedTimeHashEquals(current.OwnerClaims[claimIndex].TokenHash, parsed.Secret))
                    throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
                WorkspaceOwnerClaimRecord claim = current.OwnerClaims[claimIndex];
                if (claim.Status != WorkspaceAuthorityStatus.Pending)
                    throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
                if (claim.ExpiresAt <= now)
                {
                    await ExpireOwnerClaimAsync(current, claimIndex, now, requestId, correlationId, ct).ConfigureAwait(false);
                    throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
                }
                authorityExpiresAt = claim.ExpiresAt;
            }

            EnsureHandoffCapacity(current);
            string handoffId = NewId("handoff_");
            byte[] secret = RandomNumberGenerator.GetBytes(32);
            string token = BuildAuthorityToken("sphnd1", handoffId, secret);
            DateTimeOffset expiresAt = Min(now.Add(HandoffLifetime), authorityExpiresAt);
            var handoff = new WorkspaceHandoffRecord(
                handoffId,
                kind,
                WorkspaceHandoffStatus.Pending,
                parsed.RecordId,
                HashBytes(secret),
                AcceptedOidcIssuer: null,
                AcceptedOidcSubject: null,
                CreatedAt: now,
                UpdatedAt: now,
                ExpiresAt: expiresAt,
                PurgeAt: now.Add(HandoffRetention),
                AcceptedAt: null,
                ReceiptId: null);
            WorkspaceRecord workspace = Advance(current.Workspace, now);
            WorkspaceAuditEventRecord audit = NewAudit(
                WorkspaceAuditAction.HandoffCreated,
                WorkspaceActorRecord.System,
                targetType,
                parsed.RecordId,
                now,
                requestId,
                correlationId);
            WorkspaceAccessDocument updated = current with
            {
                Workspace = workspace,
                Handoffs = current.Handoffs.Append(handoff).ToArray(),
                AuditEvents = PrependAudit(current.AuditEvents, audit),
            };
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return new(handoff, token);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkspaceHandoffResolution> ResolveHandoffAsync(
        string handoffToken,
        WorkspaceHandoffKind kind,
        WorkspaceIdentityKey identity,
        CancellationToken ct)
    {
        WorkspaceAccessValidation.ValidateIdentity(identity);
        string unavailableCode = kind == WorkspaceHandoffKind.Invitation
            ? "invitation-unavailable"
            : "owner-claim-unavailable";
        ParsedAuthorityToken parsed = ParseAuthorityToken(
            handoffToken,
            "sphnd1",
            "handoff_",
            unavailableCode);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            WorkspaceAccessDocument current = PruneRetention(await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false), now);
            int handoffIndex = FindHandoffIndex(current.Handoffs, parsed, kind);
            if (handoffIndex < 0)
                throw Domain(unavailableCode, "The handoff is unavailable.");
            WorkspaceHandoffRecord handoff = current.Handoffs[handoffIndex];
            WorkspaceMemberRecord? currentMember = current.Members.FirstOrDefault(member =>
                WorkspaceAccessValidation.SameIdentity(member, identity));
            if (currentMember is not null && currentMember.Status != WorkspaceMemberStatus.Active)
                throw Domain("member-access-disabled", "This Sideport membership is disabled.");

            if (handoff.Status == WorkspaceHandoffStatus.Accepted)
            {
                if (!WorkspaceAccessValidation.SameIdentity(handoff, identity) || handoff.ReceiptId is null)
                    throw Domain(unavailableCode, "The handoff is unavailable.");
                WorkspaceReceiptRecord receipt = current.Receipts.FirstOrDefault(item => item.ReceiptId == handoff.ReceiptId)
                    ?? throw StoreUnavailable();
                if (kind == WorkspaceHandoffKind.Invitation)
                {
                    WorkspaceInvitationRecord invitation = current.Invitations.FirstOrDefault(item => item.InvitationId == handoff.AuthorityId)
                        ?? throw StoreUnavailable();
                    WorkspaceMemberRecord acceptedMember = current.Members.FirstOrDefault(item => item.MemberId == invitation.AcceptedMemberId)
                        ?? throw StoreUnavailable();
                    return new(handoff, invitation, OwnerClaim: null, receipt, acceptedMember);
                }
                WorkspaceOwnerClaimRecord claim = current.OwnerClaims.FirstOrDefault(item => item.ClaimId == handoff.AuthorityId)
                    ?? throw StoreUnavailable();
                WorkspaceMemberRecord claimant = current.Members.FirstOrDefault(item => item.MemberId == claim.ClaimantMemberId)
                    ?? throw StoreUnavailable();
                return new(handoff, Invitation: null, claim, receipt, claimant);
            }

            EnsurePendingHandoff(handoff, now, unavailableCode);
            if (kind == WorkspaceHandoffKind.Invitation)
            {
                WorkspaceInvitationRecord invitation = current.Invitations.FirstOrDefault(item => item.InvitationId == handoff.AuthorityId)
                    ?? throw StoreUnavailable();
                if (invitation.Status != WorkspaceAuthorityStatus.Pending || invitation.ExpiresAt <= now)
                    throw Domain(unavailableCode, "The handoff is unavailable.");
                if (currentMember is not null)
                    throw Domain("member-already-active", "This account is already a Sideport member.");
                return new(handoff, invitation, OwnerClaim: null, Receipt: null, AcceptedMember: null);
            }

            WorkspaceOwnerClaimRecord ownerClaim = current.OwnerClaims.FirstOrDefault(item => item.ClaimId == handoff.AuthorityId)
                ?? throw StoreUnavailable();
            if (ownerClaim.Status != WorkspaceAuthorityStatus.Pending || ownerClaim.ExpiresAt <= now)
                throw Domain(unavailableCode, "The handoff is unavailable.");
            if (currentMember is not null &&
                string.Equals(currentMember.MemberId, current.Workspace.OwnerMemberId, StringComparison.Ordinal))
            {
                throw Domain("member-already-active", "This account is already the active Owner.");
            }
            return new(handoff, Invitation: null, ownerClaim, Receipt: null, AcceptedMember: null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExpireInvitationAsync(
        WorkspaceAccessDocument current,
        int index,
        DateTimeOffset now,
        string requestId,
        string? correlationId,
        CancellationToken ct)
    {
        WorkspaceInvitationRecord invitation = current.Invitations[index];
        WorkspaceInvitationRecord expired = ToInvitationTombstone(invitation with
        {
            Status = WorkspaceAuthorityStatus.Expired,
            Version = checked(invitation.Version + 1),
            UpdatedAt = now,
            ExpiredAt = now,
        });
        WorkspaceAuditEventRecord audit = NewAudit(
            WorkspaceAuditAction.InvitationExpired,
            WorkspaceActorRecord.System,
            WorkspaceAuditTargetType.Invitation,
            invitation.InvitationId,
            now,
            requestId,
            correlationId);
        WorkspaceAccessDocument updated = current with
        {
            Workspace = Advance(current.Workspace, now),
            Invitations = Replace(current.Invitations, index, expired),
            Handoffs = RevokeHandoffs(current.Handoffs, WorkspaceHandoffKind.Invitation, invitation.InvitationId, now),
            AuditEvents = PrependAudit(current.AuditEvents, audit),
        };
        await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
    }

    private async Task ExpireOwnerClaimAsync(
        WorkspaceAccessDocument current,
        int index,
        DateTimeOffset now,
        string requestId,
        string? correlationId,
        CancellationToken ct)
    {
        WorkspaceOwnerClaimRecord claim = current.OwnerClaims[index];
        WorkspaceOwnerClaimRecord expired = claim with
        {
            Status = WorkspaceAuthorityStatus.Expired,
            Version = checked(claim.Version + 1),
            UpdatedAt = now,
            ExpiredAt = now,
        };
        WorkspaceAuditEventRecord audit = NewAudit(
            WorkspaceAuditAction.OwnerClaimExpired,
            WorkspaceActorRecord.System,
            WorkspaceAuditTargetType.OwnerClaim,
            claim.ClaimId,
            now,
            requestId,
            correlationId);
        WorkspaceAccessDocument updated = current with
        {
            Workspace = Advance(current.Workspace, now),
            OwnerClaims = Replace(current.OwnerClaims, index, expired),
            Handoffs = RevokeHandoffs(current.Handoffs, WorkspaceHandoffKind.OwnerClaim, claim.ClaimId, now),
            AuditEvents = PrependAudit(current.AuditEvents, audit),
        };
        await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
    }

    private async Task<WorkspaceAccessDocument> ReadRequiredUnsafeAsync(CancellationToken ct) =>
        await ReadUnsafeAsync(ct).ConfigureAwait(false)
        ?? throw Domain("workspace-bootstrap-required", "Sideport workspace setup is required.");

    private async Task<WorkspaceAccessDocument?> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            PrivateAppleStoreFiles.RestrictFile(_path);
            await using FileStream stream = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16_384,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            WorkspaceAccessDocument? document = await JsonSerializer.DeserializeAsync<WorkspaceAccessDocument>(
                stream,
                JsonOptions,
                ct).ConfigureAwait(false);
            if (document is null)
                throw new InvalidDataException("The workspace access record is empty.");
            WorkspaceAccessValidation.Validate(document);
            return Freeze(document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceAccessException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw StoreUnavailable(ex);
        }
    }

    private async Task SaveUnsafeAsync(WorkspaceAccessDocument document, CancellationToken ct)
    {
        WorkspaceAccessValidation.Validate(document);
        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
                PrivateAppleStoreFiles.EnsureDirectory(directory);
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            PrivateAppleStoreFiles.RestrictFile(temporaryPath);
            File.Move(temporaryPath, _path, overwrite: true);
            PrivateAppleStoreFiles.RestrictFile(_path);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            TryDelete(temporaryPath);
            throw StoreUnavailable(ex);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static WorkspaceAccessDocument CreateBootstrapDocument(DateTimeOffset now)
    {
        var workspace = new WorkspaceRecord(
            NewId("workspace_"),
            "Sideport",
            WorkspaceLifecycleState.BootstrapRequired,
            Version: 1,
            SecurityEpoch: NewOpaqueSecret(),
            OwnerMemberId: null,
            RestoreReviewRequired: false,
            BootstrapReceiptId: null,
            LastRestoreReceiptId: null,
            CreatedAt: now,
            UpdatedAt: now);
        return new WorkspaceAccessDocument(
            CurrentSchemaVersion,
            workspace,
            Members: [],
            OwnerClaims: [],
            Invitations: [],
            Handoffs: [],
            Idempotency: [],
            AuditEvents: [],
            Receipts: []);
    }

    private static WorkspaceAccessDocument Freeze(WorkspaceAccessDocument document) => document with
    {
        Members = document.Members.ToArray(),
        OwnerClaims = document.OwnerClaims.ToArray(),
        Invitations = document.Invitations.ToArray(),
        Handoffs = document.Handoffs.ToArray(),
        Idempotency = document.Idempotency.ToArray(),
        AuditEvents = document.AuditEvents.ToArray(),
        Receipts = document.Receipts.ToArray(),
    };

    private static WorkspaceAccessDocument PruneRetention(WorkspaceAccessDocument document, DateTimeOffset now)
    {
        WorkspaceInvitationRecord[] invitations = document.Invitations.Select(invitation =>
            invitation.Status == WorkspaceAuthorityStatus.Pending && invitation.ExpiresAt <= now
                ? ToInvitationTombstone(invitation with
                {
                    Status = WorkspaceAuthorityStatus.Expired,
                    Version = checked(invitation.Version + 1),
                    UpdatedAt = now,
                    ExpiredAt = now,
                })
                : invitation).ToArray();
        HashSet<string> expiredInvitationIds = invitations
            .Where(item => item.Status == WorkspaceAuthorityStatus.Expired)
            .Select(item => item.InvitationId)
            .ToHashSet(StringComparer.Ordinal);
        return document with
        {
            Invitations = invitations,
            Handoffs = document.Handoffs
                .Where(item => item.PurgeAt > now)
                .Select(item => item.Status == WorkspaceHandoffStatus.Pending && item.ExpiresAt <= now
                    ? item with { Status = WorkspaceHandoffStatus.Expired, UpdatedAt = now }
                    : item.Kind == WorkspaceHandoffKind.Invitation &&
                      item.Status == WorkspaceHandoffStatus.Pending &&
                      expiredInvitationIds.Contains(item.AuthorityId)
                        ? item with { Status = WorkspaceHandoffStatus.Revoked, UpdatedAt = now }
                        : item)
                .ToArray(),
            Idempotency = document.Idempotency.Where(item => item.ExpiresAt > now).ToArray(),
        };
    }

    private static void EnsureAuthorityCapacity(WorkspaceAccessDocument document)
    {
        if (document.OwnerClaims.Count + document.Invitations.Count >= MaxAuthorityRecords)
            throw Domain("workspace-security-history-full", "The retained authority history is full.");
    }

    private static void EnsureIdempotencyCapacity(WorkspaceAccessDocument document)
    {
        if (document.Idempotency.Count >= MaxIdempotencyRecords)
            throw Domain("workspace-idempotency-history-full", "The idempotency history is temporarily full.");
    }

    private static void EnsureHandoffCapacity(WorkspaceAccessDocument document)
    {
        if (document.Handoffs.Count >= MaxHandoffRecords)
            throw Domain("workspace-handoff-history-full", "The retained sign-in handoff history is temporarily full.");
    }

    private static void EnsureActiveOwnerActor(WorkspaceAccessDocument document, WorkspaceActorRecord actor)
    {
        if (actor.Kind == WorkspaceActorKind.RecoveryBearer)
            return;
        if (actor.Kind != WorkspaceActorKind.Member || actor.MemberId is null)
            throw Domain("capability-denied", "Owner access is required.");
        WorkspaceMemberRecord? member = document.Members.FirstOrDefault(item => item.MemberId == actor.MemberId);
        if (member is null ||
            member.Role != WorkspaceMemberRole.Owner ||
            member.Status != WorkspaceMemberStatus.Active ||
            !string.Equals(document.Workspace.OwnerMemberId, member.MemberId, StringComparison.Ordinal))
        {
            throw Domain("capability-denied", "Owner access is required.");
        }
    }

    private static void ValidateAcceptanceRequest(WorkspaceAcceptanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceAccessValidation.ValidateIdentity(request.Identity);
        WorkspaceAccessValidation.ValidatePresentation(request.DisplayName, request.Email);
        WorkspaceAccessValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        WorkspaceAccessValidation.ValidateRequestIds(request.RequestId, request.CorrelationId);
        if (request.CurrentImpactVersion is not null && request.CurrentImpactVersion.Length > 160)
            throw new ArgumentException("The impact version is invalid.", nameof(request));
    }

    private static WorkspaceAcceptanceResult? TryInvitationReplay(
        WorkspaceAccessDocument document,
        WorkspaceHandoffRecord handoff,
        WorkspaceIdentityKey identity)
    {
        if (handoff.Status != WorkspaceHandoffStatus.Accepted)
            return null;
        if (!WorkspaceAccessValidation.SameIdentity(handoff, identity) || handoff.ReceiptId is null)
            throw Domain("invitation-unavailable", "The invitation is unavailable.");
        WorkspaceInvitationRecord invitation = document.Invitations.FirstOrDefault(item => item.InvitationId == handoff.AuthorityId)
            ?? throw StoreUnavailable();
        WorkspaceMemberRecord member = document.Members.FirstOrDefault(item => item.MemberId == invitation.AcceptedMemberId)
            ?? throw StoreUnavailable();
        if (member.Status != WorkspaceMemberStatus.Active)
            throw Domain("member-access-disabled", "This Sideport membership is disabled.");
        WorkspaceReceiptRecord receipt = document.Receipts.FirstOrDefault(item => item.ReceiptId == handoff.ReceiptId)
            ?? throw StoreUnavailable();
        return new(member, receipt, Replayed: true);
    }

    private static WorkspaceAcceptanceResult? TryOwnerClaimReplay(
        WorkspaceAccessDocument document,
        WorkspaceHandoffRecord handoff,
        WorkspaceIdentityKey identity)
    {
        if (handoff.Status != WorkspaceHandoffStatus.Accepted)
            return null;
        if (!WorkspaceAccessValidation.SameIdentity(handoff, identity) || handoff.ReceiptId is null)
            throw Domain("owner-claim-unavailable", "The Owner claim is unavailable.");
        WorkspaceOwnerClaimRecord claim = document.OwnerClaims.FirstOrDefault(item => item.ClaimId == handoff.AuthorityId)
            ?? throw StoreUnavailable();
        WorkspaceMemberRecord member = document.Members.FirstOrDefault(item => item.MemberId == claim.ClaimantMemberId)
            ?? throw StoreUnavailable();
        if (member.Status != WorkspaceMemberStatus.Active)
            throw Domain("member-access-disabled", "This Sideport membership is disabled.");
        WorkspaceReceiptRecord receipt = document.Receipts.FirstOrDefault(item => item.ReceiptId == handoff.ReceiptId)
            ?? throw StoreUnavailable();
        return new(member, receipt, Replayed: true);
    }

    private static void EnsurePendingHandoff(
        WorkspaceHandoffRecord handoff,
        DateTimeOffset now,
        string unavailableCode)
    {
        if (handoff.Status != WorkspaceHandoffStatus.Pending || handoff.ExpiresAt <= now)
            throw Domain(unavailableCode, "The handoff is unavailable.");
    }

    private static WorkspaceInvitationRecord ToInvitationTombstone(WorkspaceInvitationRecord invitation) =>
        invitation with { DisplayName = null, ContactEmail = null };

    private static WorkspaceHandoffRecord[] AcceptOneAndRevokeOtherHandoffs(
        IReadOnlyList<WorkspaceHandoffRecord> handoffs,
        int acceptedIndex,
        WorkspaceHandoffRecord accepted,
        DateTimeOffset now)
    {
        WorkspaceHandoffRecord[] updated = handoffs.ToArray();
        updated[acceptedIndex] = accepted;
        for (int index = 0; index < updated.Length; index++)
        {
            if (index != acceptedIndex &&
                updated[index].Kind == accepted.Kind &&
                string.Equals(updated[index].AuthorityId, accepted.AuthorityId, StringComparison.Ordinal) &&
                updated[index].Status == WorkspaceHandoffStatus.Pending)
            {
                updated[index] = updated[index] with { Status = WorkspaceHandoffStatus.Revoked, UpdatedAt = now };
            }
        }
        return updated;
    }

    private static WorkspaceHandoffRecord[] RevokeHandoffs(
        IReadOnlyList<WorkspaceHandoffRecord> handoffs,
        WorkspaceHandoffKind kind,
        string authorityId,
        DateTimeOffset now) => handoffs.Select(handoff =>
            handoff.Kind == kind &&
            handoff.Status == WorkspaceHandoffStatus.Pending &&
            string.Equals(handoff.AuthorityId, authorityId, StringComparison.Ordinal)
                ? handoff with { Status = WorkspaceHandoffStatus.Revoked, UpdatedAt = now }
                : handoff).ToArray();

    private static int FindHandoffIndex(
        IReadOnlyList<WorkspaceHandoffRecord> handoffs,
        ParsedAuthorityToken parsed,
        WorkspaceHandoffKind kind)
    {
        for (int index = 0; index < handoffs.Count; index++)
        {
            WorkspaceHandoffRecord handoff = handoffs[index];
            if (handoff.Kind == kind &&
                string.Equals(handoff.HandoffId, parsed.RecordId, StringComparison.Ordinal) &&
                FixedTimeHashEquals(handoff.TokenHash, parsed.Secret))
            {
                return index;
            }
        }
        return -1;
    }

    private static WorkspaceIdempotencyRecord? FindIdempotency(
        WorkspaceAccessDocument document,
        string actorKey,
        string action,
        string rawKey,
        string semanticTarget,
        string semanticDigest)
    {
        string keyHash = HashText(rawKey);
        WorkspaceIdempotencyRecord? existing = document.Idempotency.FirstOrDefault(item =>
            string.Equals(item.ActorKey, actorKey, StringComparison.Ordinal) &&
            string.Equals(item.Action, action, StringComparison.Ordinal) &&
            string.Equals(item.KeyHash, keyHash, StringComparison.Ordinal));
        if (existing is not null &&
            (!string.Equals(existing.SemanticTarget, semanticTarget, StringComparison.Ordinal) ||
             !string.Equals(existing.SemanticDigest, semanticDigest, StringComparison.Ordinal)))
        {
            throw Domain("idempotency-key-reused", "The idempotency key was reused for a different request.");
        }
        return existing;
    }

    private static WorkspaceIdempotencyRecord NewIdempotency(
        string actorKey,
        string action,
        string semanticTarget,
        string rawKey,
        string semanticDigest,
        string resourceId,
        string? receiptId,
        DateTimeOffset now) => new(
            NewId("idempotency_"),
            actorKey,
            action,
            semanticTarget,
            HashText(rawKey),
            semanticDigest,
            resourceId,
            receiptId,
            now,
            now,
            now.Add(IdempotencyRetention));

    private static WorkspaceAuditEventRecord NewAudit(
        WorkspaceAuditAction action,
        WorkspaceActorRecord actor,
        WorkspaceAuditTargetType targetType,
        string targetId,
        DateTimeOffset now,
        string requestId,
        string? correlationId,
        WorkspaceAuditImpact? impact = null) => new(
            NewId("event_"),
            action,
            "succeeded",
            actor,
            targetType,
            targetId,
            now,
            requestId,
            correlationId,
            impact);

    private static WorkspaceReceiptRecord NewReceipt(
        WorkspaceReceiptKind kind,
        string targetId,
        long workspaceVersion,
        DateTimeOffset now,
        string? memberId = null,
        string? previousOwnerMemberId = null) => new(
            NewId("receipt_"),
            kind,
            targetId,
            "succeeded",
            now,
            workspaceVersion,
            memberId,
            previousOwnerMemberId);

    private static WorkspaceRecord Advance(WorkspaceRecord workspace, DateTimeOffset now) => workspace with
    {
        Version = checked(workspace.Version + 1),
        UpdatedAt = now,
    };

    private static void EnsureExpectedVersion(long actual, long expected)
    {
        if (actual != expected)
            throw Domain("workspace-version-conflict", "The workspace record changed. Refresh and try again.");
    }

    private static string ActorKey(WorkspaceActorRecord actor) => actor.Kind switch
    {
        WorkspaceActorKind.Member when actor.MemberId is not null => $"member:{actor.MemberId}",
        WorkspaceActorKind.RecoveryBearer => "recovery-bearer",
        WorkspaceActorKind.System => "system",
        _ => throw new ArgumentException("The workspace actor is invalid.", nameof(actor)),
    };

    private static string ActorKey(WorkspaceIdentityKey identity) =>
        $"identity:{SemanticDigest(identity.Issuer, identity.Subject)[..32]}";

    private static bool SameActor(WorkspaceActorRecord left, WorkspaceActorRecord right) =>
        left.Kind == right.Kind && string.Equals(left.MemberId, right.MemberId, StringComparison.Ordinal);

    private static string SemanticDigest(params string?[] values) =>
        HashBytes(JsonSerializer.SerializeToUtf8Bytes(values, JsonOptions));

    private static string HashText(string value) =>
        HashBytes(Encoding.UTF8.GetBytes(value));

    private static string HashBytes(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static bool FixedTimeHashEquals(string storedHash, ReadOnlySpan<byte> secret)
    {
        byte[] stored;
        try
        {
            stored = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            return false;
        }
        byte[] candidate = SHA256.HashData(secret);
        return stored.Length == candidate.Length && CryptographicOperations.FixedTimeEquals(stored, candidate);
    }

    private static string BuildAuthorityToken(string prefix, string recordId, ReadOnlySpan<byte> secret) =>
        $"{prefix}_{recordId}_{Base64Url(secret)}";

    private static ParsedAuthorityToken ParseAuthorityToken(
        string token,
        string prefix,
        string recordPrefix,
        string unavailableCode)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw Domain(unavailableCode, "The link is unavailable.");
        string marker = prefix + "_";
        if (!token.StartsWith(marker, StringComparison.Ordinal))
            throw Domain(unavailableCode, "The link is unavailable.");
        int recordIdLength = recordPrefix.Length + 24;
        int secretSeparator = marker.Length + recordIdLength;
        if (token.Length != secretSeparator + 1 + 43 || token[secretSeparator] != '_')
            throw Domain(unavailableCode, "The link is unavailable.");
        string recordId = token.Substring(marker.Length, recordIdLength);
        if (!recordId.StartsWith(recordPrefix, StringComparison.Ordinal) ||
            !recordId.AsSpan(recordPrefix.Length).ToString().All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw Domain(unavailableCode, "The link is unavailable.");
        byte[] secret;
        try
        {
            secret = FromBase64Url(token[(secretSeparator + 1)..]);
        }
        catch (FormatException)
        {
            throw Domain(unavailableCode, "The link is unavailable.");
        }
        if (secret.Length != 32)
            throw Domain(unavailableCode, "The link is unavailable.");
        return new(recordId, secret);
    }

    private static string NewId(string prefix) =>
        prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static string NewOpaqueSecret() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        if (value.Length != 43 || value.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new FormatException("The value is not canonical Base64url.");
        }
        string padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        byte[] decoded = Convert.FromBase64String(padded);
        if (!string.Equals(Base64Url(decoded), value, StringComparison.Ordinal))
            throw new FormatException("The value is not canonical Base64url.");
        return decoded;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static int IndexOf<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (predicate(values[index]))
                return index;
        }
        return -1;
    }

    private static T[] Replace<T>(IReadOnlyList<T> values, int index, T replacement)
    {
        T[] updated = values.ToArray();
        updated[index] = replacement;
        return updated;
    }

    private static T[] Prepend<T>(IReadOnlyList<T> values, T value) =>
        values.Prepend(value).ToArray();

    private static WorkspaceAuditEventRecord[] PrependAudit(
        IReadOnlyList<WorkspaceAuditEventRecord> values,
        WorkspaceAuditEventRecord value) =>
        values.Prepend(value).Take(MaxAuditEvents).ToArray();

    private static WorkspaceAccessException Domain(string code, string message) => new(code, message);

    private static WorkspaceAccessException StoreUnavailable(Exception? inner = null) => inner is null
        ? new WorkspaceAccessException("workspace-store-unavailable", "Workspace access state is unavailable.")
        : new WorkspaceAccessException("workspace-store-unavailable", "Workspace access state is unavailable.", inner);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ParsedAuthorityToken(string RecordId, byte[] Secret);
}
