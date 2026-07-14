using System.Net.Mail;

namespace Sideport.Api.WorkspaceAccess;

internal static class WorkspaceAccessValidation
{
    internal static void Validate(WorkspaceAccessDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != WorkspaceAccessStore.CurrentSchemaVersion ||
            document.Workspace is null ||
            document.Members is null ||
            document.OwnerClaims is null ||
            document.Invitations is null ||
            document.Handoffs is null ||
            document.Idempotency is null ||
            document.AuditEvents is null ||
            document.Receipts is null)
        {
            throw Invalid();
        }

        Validate(document.Workspace);
        if (document.OwnerClaims.Count + document.Invitations.Count > WorkspaceAccessStore.MaxAuthorityRecords ||
            document.Handoffs.Count > WorkspaceAccessStore.MaxHandoffRecords ||
            document.Idempotency.Count > WorkspaceAccessStore.MaxIdempotencyRecords ||
            document.AuditEvents.Count > WorkspaceAccessStore.MaxAuditEvents)
        {
            throw Invalid();
        }

        if (document.Members.Any(static item => item is null) ||
            document.OwnerClaims.Any(static item => item is null) ||
            document.Invitations.Any(static item => item is null) ||
            document.Handoffs.Any(static item => item is null) ||
            document.Idempotency.Any(static item => item is null) ||
            document.AuditEvents.Any(static item => item is null) ||
            document.Receipts.Any(static item => item is null))
        {
            throw Invalid();
        }

        ValidateUnique(document.Members.Select(item => item.MemberId));
        ValidateUnique(document.OwnerClaims.Select(item => item.ClaimId));
        ValidateUnique(document.Invitations.Select(item => item.InvitationId));
        ValidateUnique(document.Handoffs.Select(item => item.HandoffId));
        ValidateUnique(document.Idempotency.Select(item => item.IdempotencyId));
        ValidateUnique(document.AuditEvents.Select(item => item.EventId));
        ValidateUnique(document.Receipts.Select(item => item.ReceiptId));

        var identities = new HashSet<(string Issuer, string Subject)>();
        foreach (WorkspaceMemberRecord member in document.Members)
        {
            Validate(member);
            if (!identities.Add((member.OidcIssuer, member.OidcSubject)))
                throw Invalid();
        }

        foreach (WorkspaceOwnerClaimRecord claim in document.OwnerClaims)
            Validate(claim);
        foreach (WorkspaceInvitationRecord invitation in document.Invitations)
            Validate(invitation);
        foreach (WorkspaceHandoffRecord handoff in document.Handoffs)
            Validate(handoff);
        foreach (WorkspaceIdempotencyRecord idempotency in document.Idempotency)
            Validate(idempotency);
        foreach (WorkspaceAuditEventRecord auditEvent in document.AuditEvents)
            Validate(auditEvent);
        foreach (WorkspaceReceiptRecord receipt in document.Receipts)
            Validate(receipt);

        EnsureHashUniqueness(document.OwnerClaims.Select(item => item.TokenHash)
            .Concat(document.Invitations.Select(item => item.TokenHash)));
        EnsureHashUniqueness(document.Handoffs.Select(item => item.TokenHash));

        WorkspaceMemberRecord[] activeOwners = document.Members
            .Where(item => item.Role == WorkspaceMemberRole.Owner && item.Status == WorkspaceMemberStatus.Active)
            .ToArray();
        if (document.Workspace.State == WorkspaceLifecycleState.BootstrapRequired)
        {
            if (document.Workspace.OwnerMemberId is not null || activeOwners.Length != 0 || document.Members.Count != 0)
                throw Invalid();
        }
        else
        {
            if (activeOwners.Length != 1 ||
                !string.Equals(activeOwners[0].MemberId, document.Workspace.OwnerMemberId, StringComparison.Ordinal))
            {
                throw Invalid();
            }
        }

        var receiptIds = document.Receipts.Select(item => item.ReceiptId).ToHashSet(StringComparer.Ordinal);
        EnsureReceiptExists(document.Workspace.BootstrapReceiptId, receiptIds);
        EnsureReceiptExists(document.Workspace.LastRestoreReceiptId, receiptIds);
        foreach (WorkspaceMemberRecord member in document.Members)
            EnsureReceiptExists(member.LastReceiptId, receiptIds);
        foreach (WorkspaceOwnerClaimRecord claim in document.OwnerClaims)
            EnsureReceiptExists(claim.ReceiptId, receiptIds);
        foreach (WorkspaceInvitationRecord invitation in document.Invitations)
            EnsureReceiptExists(invitation.ReceiptId, receiptIds);
        foreach (WorkspaceHandoffRecord handoff in document.Handoffs)
            EnsureReceiptExists(handoff.ReceiptId, receiptIds);
        foreach (WorkspaceIdempotencyRecord idempotency in document.Idempotency)
            EnsureReceiptExists(idempotency.ReceiptId, receiptIds);

        var memberIds = document.Members.Select(item => item.MemberId).ToHashSet(StringComparer.Ordinal);
        foreach (WorkspaceOwnerClaimRecord claim in document.OwnerClaims)
        {
            EnsureMemberExists(claim.ExpectedOwnerMemberId, memberIds);
            EnsureMemberExists(claim.ClaimantMemberId, memberIds);
            EnsureActorMemberExists(claim.CreatedByActor, memberIds);
        }
        foreach (WorkspaceInvitationRecord invitation in document.Invitations)
        {
            EnsureMemberExists(invitation.AcceptedMemberId, memberIds);
            EnsureActorMemberExists(invitation.CreatedByActor, memberIds);
        }
        foreach (WorkspaceAuditEventRecord auditEvent in document.AuditEvents)
            EnsureActorMemberExists(auditEvent.Actor, memberIds);
        foreach (WorkspaceReceiptRecord receipt in document.Receipts)
        {
            EnsureMemberExists(receipt.MemberId, memberIds);
            EnsureMemberExists(receipt.PreviousOwnerMemberId, memberIds);
        }

        var claimIds = document.OwnerClaims.Select(item => item.ClaimId).ToHashSet(StringComparer.Ordinal);
        var invitationIds = document.Invitations.Select(item => item.InvitationId).ToHashSet(StringComparer.Ordinal);
        var claimsById = document.OwnerClaims.ToDictionary(item => item.ClaimId, StringComparer.Ordinal);
        var invitationsById = document.Invitations.ToDictionary(item => item.InvitationId, StringComparer.Ordinal);
        var receiptsById = document.Receipts.ToDictionary(item => item.ReceiptId, StringComparer.Ordinal);
        foreach (WorkspaceHandoffRecord handoff in document.Handoffs)
        {
            bool targetExists = handoff.Kind == WorkspaceHandoffKind.OwnerClaim
                ? claimIds.Contains(handoff.AuthorityId)
                : invitationIds.Contains(handoff.AuthorityId);
            if (!targetExists)
                throw Invalid();
            if (handoff.Status == WorkspaceHandoffStatus.Accepted)
            {
                WorkspaceReceiptRecord receipt = receiptsById[handoff.ReceiptId!];
                if (handoff.Kind == WorkspaceHandoffKind.OwnerClaim)
                {
                    WorkspaceOwnerClaimRecord claim = claimsById[handoff.AuthorityId];
                    if (claim.Status != WorkspaceAuthorityStatus.Accepted ||
                        !string.Equals(claim.ReceiptId, handoff.ReceiptId, StringComparison.Ordinal) ||
                        receipt.Kind is not (WorkspaceReceiptKind.OwnerBootstrap or WorkspaceReceiptKind.OwnerRecovery) ||
                        !string.Equals(receipt.TargetId, claim.ClaimId, StringComparison.Ordinal))
                    {
                        throw Invalid();
                    }
                }
                else
                {
                    WorkspaceInvitationRecord invitation = invitationsById[handoff.AuthorityId];
                    if (invitation.Status != WorkspaceAuthorityStatus.Accepted ||
                        !string.Equals(invitation.ReceiptId, handoff.ReceiptId, StringComparison.Ordinal) ||
                        receipt.Kind != WorkspaceReceiptKind.InvitationAccepted ||
                        !string.Equals(receipt.TargetId, invitation.InvitationId, StringComparison.Ordinal))
                    {
                        throw Invalid();
                    }
                }
            }
        }

        foreach (WorkspaceOwnerClaimRecord claim in document.OwnerClaims.Where(item => item.Status == WorkspaceAuthorityStatus.Accepted))
        {
            WorkspaceReceiptRecord receipt = receiptsById[claim.ReceiptId!];
            if (receipt.Kind is not (WorkspaceReceiptKind.OwnerBootstrap or WorkspaceReceiptKind.OwnerRecovery) ||
                !string.Equals(receipt.TargetId, claim.ClaimId, StringComparison.Ordinal) ||
                !string.Equals(receipt.MemberId, claim.ClaimantMemberId, StringComparison.Ordinal))
            {
                throw Invalid();
            }
        }
        foreach (WorkspaceInvitationRecord invitation in document.Invitations.Where(item => item.Status == WorkspaceAuthorityStatus.Accepted))
        {
            WorkspaceReceiptRecord receipt = receiptsById[invitation.ReceiptId!];
            if (receipt.Kind != WorkspaceReceiptKind.InvitationAccepted ||
                !string.Equals(receipt.TargetId, invitation.InvitationId, StringComparison.Ordinal) ||
                !string.Equals(receipt.MemberId, invitation.AcceptedMemberId, StringComparison.Ordinal))
            {
                throw Invalid();
            }
        }
        foreach (WorkspaceIdempotencyRecord idempotency in document.Idempotency)
        {
            if (idempotency.ReceiptId is null)
                continue;
            WorkspaceReceiptRecord receipt = receiptsById[idempotency.ReceiptId];
            if (!string.Equals(receipt.ReceiptId, idempotency.ReceiptId, StringComparison.Ordinal))
                throw Invalid();
        }
    }

    internal static void ValidateIdentity(WorkspaceIdentityKey identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        bool nativePasskey = string.Equals(
            identity.Issuer,
            WorkspaceIdentityKey.NativePasskeyIssuer,
            StringComparison.Ordinal);
        bool externalIssuer = Uri.TryCreate(identity.Issuer, UriKind.Absolute, out Uri? issuer) &&
            issuer.Scheme is "https" or "http";
        if (!IsBoundedText(identity.Issuer, 1, 2048) ||
            (!nativePasskey && !externalIssuer) ||
            !IsBoundedText(identity.Subject, 1, 512))
        {
            throw new ArgumentException("A validated identity issuer and subject are required.", nameof(identity));
        }
    }

    internal static void ValidatePresentation(string displayName, string? email)
    {
        if (!IsBoundedText(displayName, 1, 160) || !IsSafeDisplayText(displayName))
            throw new ArgumentException("The member display name is invalid.", nameof(displayName));
        if (email is not null && !IsValidEmail(email))
            throw new ArgumentException("The member email is invalid.", nameof(email));
    }

    internal static void ValidateInvitationPresentation(string? displayName, string contactEmail)
    {
        if (displayName is not null && (!IsBoundedText(displayName, 1, 160) || !IsSafeDisplayText(displayName)))
            throw new ArgumentException("The invitation display name is invalid.", nameof(displayName));
        if (!IsValidEmail(contactEmail))
            throw new ArgumentException("The invitation contact email is invalid.", nameof(contactEmail));
    }

    internal static void ValidateIdempotencyKey(string key)
    {
        if (!IsBoundedText(key, 16, 128) || key.Any(char.IsControl))
            throw new ArgumentException("The idempotency key must contain 16 to 128 safe characters.", nameof(key));
    }

    internal static void ValidateRequestIds(string requestId, string? correlationId)
    {
        if (!IsOpaqueId(requestId, 128) || (correlationId is not null && !IsOpaqueId(correlationId, 128)))
            throw new ArgumentException("Request evidence IDs must be bounded opaque values.", nameof(requestId));
    }

    internal static void ValidateOffboardingImpact(WorkspaceAuditImpact impact)
    {
        ArgumentNullException.ThrowIfNull(impact);
        if (!IsValidAuditImpact(impact) || impact.ImpactVersion is null)
            throw new ArgumentException("A validated offboarding impact version and non-negative counts are required.", nameof(impact));
    }

    internal static bool SameIdentity(WorkspaceMemberRecord member, WorkspaceIdentityKey identity) =>
        string.Equals(member.OidcIssuer, identity.Issuer, StringComparison.Ordinal) &&
        string.Equals(member.OidcSubject, identity.Subject, StringComparison.Ordinal);

    internal static bool SameIdentity(WorkspaceHandoffRecord handoff, WorkspaceIdentityKey identity) =>
        string.Equals(handoff.AcceptedOidcIssuer, identity.Issuer, StringComparison.Ordinal) &&
        string.Equals(handoff.AcceptedOidcSubject, identity.Subject, StringComparison.Ordinal);

    internal static bool IsHexHash(string value) =>
        value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void Validate(WorkspaceRecord workspace)
    {
        if (!Enum.IsDefined(workspace.State) ||
            !IsStableId(workspace.WorkspaceId, "workspace_") ||
            !IsBoundedText(workspace.Name, 1, 120) ||
            workspace.Version < 1 ||
            !IsBase64UrlBytes(workspace.SecurityEpoch, 32) ||
            (workspace.OwnerMemberId is not null && !IsStableId(workspace.OwnerMemberId, "member_")) ||
            workspace.CreatedAt == default ||
            workspace.UpdatedAt < workspace.CreatedAt)
        {
            throw Invalid();
        }
    }

    private static void Validate(WorkspaceMemberRecord member)
    {
        ValidateIdentity(new WorkspaceIdentityKey(member.OidcIssuer, member.OidcSubject));
        ValidatePresentation(member.DisplayName, member.Email);
        if (!Enum.IsDefined(member.Role) ||
            !Enum.IsDefined(member.Status) ||
            !IsStableId(member.MemberId, "member_") ||
            member.Version < 1 ||
            member.JoinedAt == default ||
            member.UpdatedAt < member.JoinedAt ||
            member.LastActiveAt < member.JoinedAt ||
            (member.LastReceiptId is not null && !IsStableId(member.LastReceiptId, "receipt_")))
        {
            throw Invalid();
        }
    }

    private static void Validate(WorkspaceOwnerClaimRecord claim)
    {
        ValidateActor(claim.CreatedByActor);
        if (!Enum.IsDefined(claim.Kind) ||
            !Enum.IsDefined(claim.Status) ||
            claim.CreatedByActor.Kind != WorkspaceActorKind.RecoveryBearer ||
            !IsStableId(claim.ClaimId, "owner_claim_") ||
            !IsHexHash(claim.TokenHash) ||
            !IsHexHash(claim.CreationIdempotencyKeyHash) ||
            !IsHexHash(claim.SemanticDigest) ||
            (claim.ExpectedOwnerMemberId is not null && !IsStableId(claim.ExpectedOwnerMemberId, "member_")) ||
            (claim.ClaimantMemberId is not null && !IsStableId(claim.ClaimantMemberId, "member_")) ||
            (claim.ImpactVersion is not null && !IsOpaqueId(claim.ImpactVersion, 160)) ||
            claim.Version < 1 ||
            claim.CreatedAt == default ||
            claim.UpdatedAt < claim.CreatedAt ||
            claim.ExpiresAt <= claim.CreatedAt ||
            claim.AcceptedAt < claim.CreatedAt ||
            claim.RevokedAt < claim.CreatedAt ||
            claim.ExpiredAt < claim.CreatedAt ||
            (claim.ReceiptId is not null && !IsStableId(claim.ReceiptId, "receipt_")))
        {
            throw Invalid();
        }

        if (claim.Kind == WorkspaceOwnerClaimKind.Bootstrap &&
            (claim.ExpectedOwnerMemberId is not null || claim.ImpactVersion is not null))
        {
            throw Invalid();
        }
        if (claim.Kind == WorkspaceOwnerClaimKind.Recovery &&
            (claim.ExpectedOwnerMemberId is null || claim.ImpactVersion is null))
        {
            throw Invalid();
        }
        ValidateAuthorityTerminalState(
            claim.Status,
            claim.AcceptedAt,
            claim.RevokedAt,
            claim.ExpiredAt,
            claim.ClaimantMemberId,
            claim.ReceiptId);
    }

    private static void Validate(WorkspaceInvitationRecord invitation)
    {
        ValidateActor(invitation.CreatedByActor);
        if (!Enum.IsDefined(invitation.Status) ||
            invitation.CreatedByActor.Kind is not (WorkspaceActorKind.Member or WorkspaceActorKind.RecoveryBearer) ||
            !IsStableId(invitation.InvitationId, "invitation_") ||
            !IsHexHash(invitation.TokenHash) ||
            !IsHexHash(invitation.CreationIdempotencyKeyHash) ||
            !IsHexHash(invitation.SemanticDigest) ||
            (invitation.DisplayName is not null && (!IsBoundedText(invitation.DisplayName, 1, 160) || !IsSafeDisplayText(invitation.DisplayName))) ||
            (invitation.ContactEmail is not null && !IsValidEmail(invitation.ContactEmail)) ||
            (invitation.AcceptedMemberId is not null && !IsStableId(invitation.AcceptedMemberId, "member_")) ||
            invitation.Version < 1 ||
            invitation.CreatedAt == default ||
            invitation.UpdatedAt < invitation.CreatedAt ||
            invitation.ExpiresAt <= invitation.CreatedAt ||
            invitation.AcceptedAt < invitation.CreatedAt ||
            invitation.RevokedAt < invitation.CreatedAt ||
            invitation.ExpiredAt < invitation.CreatedAt ||
            (invitation.ReceiptId is not null && !IsStableId(invitation.ReceiptId, "receipt_")))
        {
            throw Invalid();
        }
        if (invitation.Status == WorkspaceAuthorityStatus.Pending && invitation.ContactEmail is null)
            throw Invalid();
        ValidateAuthorityTerminalState(
            invitation.Status,
            invitation.AcceptedAt,
            invitation.RevokedAt,
            invitation.ExpiredAt,
            invitation.AcceptedMemberId,
            invitation.ReceiptId);
    }

    private static void Validate(WorkspaceHandoffRecord handoff)
    {
        if (!Enum.IsDefined(handoff.Kind) ||
            !Enum.IsDefined(handoff.Status) ||
            !IsStableId(handoff.HandoffId, "handoff_") ||
            !IsBoundedText(handoff.AuthorityId, 1, 128) ||
            !IsHexHash(handoff.TokenHash) ||
            handoff.CreatedAt == default ||
            handoff.UpdatedAt < handoff.CreatedAt ||
            handoff.ExpiresAt <= handoff.CreatedAt ||
            handoff.PurgeAt < handoff.ExpiresAt ||
            handoff.AcceptedAt < handoff.CreatedAt ||
            (handoff.ReceiptId is not null && !IsStableId(handoff.ReceiptId, "receipt_")))
        {
            throw Invalid();
        }

        bool accepted = handoff.Status == WorkspaceHandoffStatus.Accepted;
        if (accepted != (handoff.AcceptedAt is not null) ||
            accepted != (handoff.AcceptedOidcIssuer is not null) ||
            accepted != (handoff.AcceptedOidcSubject is not null) ||
            accepted != (handoff.ReceiptId is not null))
        {
            throw Invalid();
        }
        if (accepted)
            ValidateIdentity(new WorkspaceIdentityKey(handoff.AcceptedOidcIssuer!, handoff.AcceptedOidcSubject!));
    }

    private static void Validate(WorkspaceIdempotencyRecord idempotency)
    {
        if (!IsStableId(idempotency.IdempotencyId, "idempotency_") ||
            !IsBoundedText(idempotency.ActorKey, 1, 96) ||
            !IsBoundedText(idempotency.Action, 1, 96) ||
            !IsBoundedText(idempotency.SemanticTarget, 1, 160) ||
            !IsHexHash(idempotency.KeyHash) ||
            !IsHexHash(idempotency.SemanticDigest) ||
            !IsBoundedText(idempotency.ResourceId, 1, 160) ||
            (idempotency.ReceiptId is not null && !IsStableId(idempotency.ReceiptId, "receipt_")) ||
            idempotency.CreatedAt == default ||
            idempotency.CompletedAt < idempotency.CreatedAt ||
            idempotency.ExpiresAt <= idempotency.CompletedAt)
        {
            throw Invalid();
        }
    }

    private static void Validate(WorkspaceAuditEventRecord auditEvent)
    {
        ValidateActor(auditEvent.Actor);
        if (!Enum.IsDefined(auditEvent.Action) ||
            !Enum.IsDefined(auditEvent.TargetType) ||
            !IsStableId(auditEvent.EventId, "event_") ||
            auditEvent.Outcome is not "succeeded" ||
            !IsBoundedText(auditEvent.TargetId, 1, 160) ||
            auditEvent.OccurredAt == default ||
            !IsOpaqueId(auditEvent.RequestId, 128) ||
            (auditEvent.CorrelationId is not null && !IsOpaqueId(auditEvent.CorrelationId, 128)))
        {
            throw Invalid();
        }

        WorkspaceAuditImpact? impact = auditEvent.Impact;
        if (impact is not null && !IsValidAuditImpact(impact))
            throw Invalid();
    }

    private static bool IsValidAuditImpact(WorkspaceAuditImpact impact) =>
        !HasNegative(impact.MemberCount) &&
        !HasNegative(impact.DeviceCount) &&
        !HasNegative(impact.RegistrationCount) &&
        !HasNegative(impact.QueuedOperationCount) &&
        !HasNegative(impact.RunningOperationCount) &&
        !HasNegative(impact.SchedulerEffectCount) &&
        (impact.ImpactVersion is null || IsOpaqueId(impact.ImpactVersion, 160));

    private static void Validate(WorkspaceReceiptRecord receipt)
    {
        if (!Enum.IsDefined(receipt.Kind) ||
            !IsStableId(receipt.ReceiptId, "receipt_") ||
            !IsBoundedText(receipt.TargetId, 1, 160) ||
            receipt.Outcome is not "succeeded" ||
            receipt.RecordedAt == default ||
            receipt.WorkspaceVersion < 1 ||
            (receipt.MemberId is not null && !IsStableId(receipt.MemberId, "member_")) ||
            (receipt.PreviousOwnerMemberId is not null && !IsStableId(receipt.PreviousOwnerMemberId, "member_")))
        {
            throw Invalid();
        }
    }

    private static void ValidateActor(WorkspaceActorRecord actor)
    {
        if (actor is null ||
            !Enum.IsDefined(actor.Kind) ||
            (actor.Kind == WorkspaceActorKind.Member) != (actor.MemberId is not null) ||
            (actor.MemberId is not null && !IsStableId(actor.MemberId, "member_")))
        {
            throw Invalid();
        }
    }

    private static void ValidateAuthorityTerminalState(
        WorkspaceAuthorityStatus status,
        DateTimeOffset? acceptedAt,
        DateTimeOffset? revokedAt,
        DateTimeOffset? expiredAt,
        string? acceptedMemberId,
        string? receiptId)
    {
        bool accepted = status == WorkspaceAuthorityStatus.Accepted;
        bool revoked = status == WorkspaceAuthorityStatus.Revoked;
        bool expired = status == WorkspaceAuthorityStatus.Expired;
        if (accepted != (acceptedAt is not null) ||
            revoked != (revokedAt is not null) ||
            expired != (expiredAt is not null) ||
            accepted != (acceptedMemberId is not null) ||
            (accepted || revoked) != (receiptId is not null))
        {
            throw Invalid();
        }
    }

    private static void ValidateUnique(IEnumerable<string> values)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string value in values)
        {
            if (!seen.Add(value))
                throw Invalid();
        }
    }

    private static void EnsureHashUniqueness(IEnumerable<string> hashes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string hash in hashes)
        {
            if (!seen.Add(hash))
                throw Invalid();
        }
    }

    private static void EnsureReceiptExists(string? receiptId, IReadOnlySet<string> receiptIds)
    {
        if (receiptId is not null && !receiptIds.Contains(receiptId))
            throw Invalid();
    }

    private static void EnsureMemberExists(string? memberId, IReadOnlySet<string> memberIds)
    {
        if (memberId is not null && !memberIds.Contains(memberId))
            throw Invalid();
    }

    private static void EnsureActorMemberExists(WorkspaceActorRecord actor, IReadOnlySet<string> memberIds)
    {
        if (actor.Kind == WorkspaceActorKind.Member &&
            (actor.MemberId is null || !memberIds.Contains(actor.MemberId)))
        {
            throw Invalid();
        }
    }

    private static bool IsValidEmail(string value)
    {
        if (!IsBoundedText(value, 3, 320) || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace))
            return false;
        return MailAddress.TryCreate(value, out MailAddress? address) &&
            string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeDisplayText(string value)
    {
        foreach (char character in value)
        {
            if (char.IsControl(character) || character is '\u202a' or '\u202b' or '\u202c' or '\u202d' or '\u202e' or
                '\u2066' or '\u2067' or '\u2068' or '\u2069')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsBase64UrlBytes(string value, int byteCount)
    {
        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded).Length == byteCount;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsStableId(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) &&
        value.Length == prefix.Length + 24 &&
        value.AsSpan(prefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsBoundedText(string? value, int minimum, int maximum) =>
        value is not null && value.Length >= minimum && value.Length <= maximum && !string.IsNullOrWhiteSpace(value);

    private static bool IsOpaqueId(string value, int maximum) =>
        IsBoundedText(value, 1, maximum) && value.All(static character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':');

    private static bool HasNegative(int? value) => value < 0;

    private static InvalidDataException Invalid() =>
        new("The workspace access state is invalid.");
}
