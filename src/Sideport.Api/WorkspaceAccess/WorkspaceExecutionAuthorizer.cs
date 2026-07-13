using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;

namespace Sideport.Api.WorkspaceAccess;

/// <summary>
/// Re-resolves durable member and device ownership immediately before queued
/// work crosses an Apple or device boundary. The public type exists only so it
/// can be constructor-injected into the public worker services; its contract is
/// intentionally internal to the API process.
/// </summary>
public sealed class WorkspaceExecutionAuthorizer
{
    private readonly WorkspaceAccessStore _workspace;
    private readonly KnownDeviceStore _knownDevices;

    internal WorkspaceExecutionAuthorizer(
        WorkspaceAccessStore workspace,
        KnownDeviceStore knownDevices)
    {
        _workspace = workspace;
        _knownDevices = knownDevices;
    }

    internal Task<WorkspaceExecutionDecision> AuthorizeOperationAsync(
        OperationRecordDto operation,
        bool enrollmentTarget = false,
        CancellationToken ct = default) =>
        AuthorizeAsync(
            operation.Actor,
            operation.ActorMemberId,
            operation.OwnerMemberId,
            operation.Target.DeviceUdid,
            enrollmentTarget,
            assignDefaultOwner: false,
            ct);

    internal Task<WorkspaceExecutionDecision> AuthorizeSubmissionAsync(
        OperationActorDto actor,
        string? actorMemberId,
        string? ownerMemberId,
        string? deviceUdid,
        bool enrollmentTarget,
        bool assignDefaultOwner,
        CancellationToken ct = default) =>
        AuthorizeAsync(
            actor,
            actorMemberId,
            ownerMemberId,
            deviceUdid,
            enrollmentTarget,
            assignDefaultOwner,
            ct);

    internal async Task<WorkspaceExecutionDecision> AuthorizeSchedulerTargetAsync(
        string deviceUdid,
        CancellationToken ct = default)
    {
        string? ownerMemberId;
        try
        {
            KnownDeviceRecord? known = await _knownDevices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
            ownerMemberId = Normalize(known?.OwnerMemberId);
        }
        catch (KnownDeviceStoreException)
        {
            return WorkspaceExecutionDecision.Denied(
                "resource-authorization-unavailable",
                "Sideport could not safely recheck this iPhone before automatic refresh.",
                retryable: true);
        }

        return await AuthorizeAsync(
            new OperationActorDto("system", "system:scheduler"),
            actorMemberId: null,
            ownerMemberId,
            deviceUdid,
            enrollmentTarget: false,
            assignDefaultOwner: false,
            ct).ConfigureAwait(false);
    }

    private async Task<WorkspaceExecutionDecision> AuthorizeAsync(
        OperationActorDto actor,
        string? actorMemberId,
        string? ownerMemberId,
        string? deviceUdid,
        bool enrollmentTarget,
        bool assignDefaultOwner,
        CancellationToken ct)
    {
        WorkspaceAccessDocument? document;
        try
        {
            document = await _workspace.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (WorkspaceAccessException)
        {
            return WorkspaceExecutionDecision.Denied(
                "workspace-store-unavailable",
                "Sideport could not safely recheck workspace access before changing an iPhone.",
                retryable: true);
        }

        actorMemberId = Normalize(actorMemberId);
        ownerMemberId = Normalize(ownerMemberId);
        bool privilegedNonHuman = actorMemberId is null && IsPrivilegedNonHuman(actor.Kind);
        if (document is null && actorMemberId is null && IsRecoveryBearer(actor.Kind) && ownerMemberId is null)
        {
            if (assignDefaultOwner)
            {
                return WorkspaceExecutionDecision.Denied(
                    "workspace-bootstrap-required",
                    "Claim the Sideport Owner account before adding a new iPhone.");
            }

            // The recovery bearer remains able to service pre-family legacy
            // resources. A stable member assignment is never trusted without
            // its workspace store, and the scheduler waits for bootstrap.
            if (!string.IsNullOrWhiteSpace(deviceUdid))
            {
                try
                {
                    KnownDeviceRecord? legacyDevice = await _knownDevices
                        .FindAsync(deviceUdid, ct)
                        .ConfigureAwait(false);
                    if (Normalize(legacyDevice?.OwnerMemberId) is not null)
                    {
                        return WorkspaceExecutionDecision.Denied(
                            "workspace-bootstrap-required",
                            "Restore secure workspace membership before Sideport changes this assigned iPhone.");
                    }
                }
                catch (KnownDeviceStoreException)
                {
                    return WorkspaceExecutionDecision.Denied(
                        "resource-authorization-unavailable",
                        "Sideport could not safely recheck this iPhone before continuing.",
                        retryable: true);
                }
            }

            return WorkspaceExecutionDecision.Allowed(
                ownerMemberId: null,
                canUseOwnerManagedAppleAuthority: true);
        }

        if (document?.Workspace.State != WorkspaceLifecycleState.Active)
        {
            return WorkspaceExecutionDecision.Denied(
                "workspace-bootstrap-required",
                "Finish secure workspace setup before Sideport changes an iPhone.");
        }

        WorkspaceMemberRecord? workspaceOwner = document.Members.FirstOrDefault(member =>
            member.Role == WorkspaceMemberRole.Owner &&
            member.Status == WorkspaceMemberStatus.Active &&
            string.Equals(member.MemberId, document.Workspace.OwnerMemberId, StringComparison.Ordinal));
        if (workspaceOwner is null)
        {
            return WorkspaceExecutionDecision.Denied(
                "workspace-owner-unavailable",
                "Sideport could not verify an active workspace Owner before changing an iPhone.");
        }

        WorkspaceMemberRecord? actorMember = null;
        if (actorMemberId is not null)
        {
            actorMember = document.Members.FirstOrDefault(member =>
                string.Equals(member.MemberId, actorMemberId, StringComparison.Ordinal));
            if (actorMember is null)
            {
                return WorkspaceExecutionDecision.Denied(
                    "workspace-membership-required",
                    "The member who started this operation no longer belongs to this Sideport workspace.");
            }
            if (actorMember.Status != WorkspaceMemberStatus.Active)
            {
                return WorkspaceExecutionDecision.Denied(
                    "member-access-disabled",
                    "This operation stopped because the member who started it no longer has Sideport access.");
            }
        }
        else if (!privilegedNonHuman)
        {
            return WorkspaceExecutionDecision.Denied(
                "workspace-membership-required",
                "Sideport cannot verify the member who started this legacy operation.");
        }

        if (assignDefaultOwner && ownerMemberId is null)
            ownerMemberId = actorMember?.MemberId ?? workspaceOwner.MemberId;

        WorkspaceMemberRecord? resourceOwner = null;
        if (ownerMemberId is not null)
        {
            resourceOwner = document.Members.FirstOrDefault(member =>
                string.Equals(member.MemberId, ownerMemberId, StringComparison.Ordinal));
            if (resourceOwner is null)
            {
                return WorkspaceExecutionDecision.Denied(
                    "resource-owner-unavailable",
                    "The iPhone owner recorded for this operation no longer belongs to this workspace.");
            }
            if (resourceOwner.Status != WorkspaceMemberStatus.Active)
            {
                return WorkspaceExecutionDecision.Denied(
                    "member-access-disabled",
                    "This operation stopped because the iPhone owner no longer has Sideport access.");
            }
        }

        if (actorMember?.Role == WorkspaceMemberRole.Family &&
            !string.Equals(actorMember.MemberId, ownerMemberId, StringComparison.Ordinal))
        {
            return WorkspaceExecutionDecision.Denied(
                "resource-not-found",
                "This operation is no longer available for this member.");
        }

        if (!string.IsNullOrWhiteSpace(deviceUdid))
        {
            KnownDeviceRecord? known;
            try
            {
                known = await _knownDevices.FindAsync(deviceUdid, ct).ConfigureAwait(false);
            }
            catch (KnownDeviceStoreException)
            {
                return WorkspaceExecutionDecision.Denied(
                    "resource-authorization-unavailable",
                    "Sideport could not safely recheck this iPhone before continuing.",
                    retryable: true);
            }

            string? currentOwnerMemberId = Normalize(known?.OwnerMemberId);
            bool ownerMatches = string.Equals(
                currentOwnerMemberId,
                ownerMemberId,
                StringComparison.Ordinal);
            if (enrollmentTarget)
            {
                // A not-yet-accepted enrollment target may legitimately be
                // unassigned. Any existing stable assignment must still match.
                ownerMatches = currentOwnerMemberId is null || ownerMatches;
            }

            if (!ownerMatches)
            {
                return WorkspaceExecutionDecision.Denied(
                    "resource-ownership-changed",
                    "This operation stopped because the iPhone owner changed after it was submitted.");
            }
        }

        if (ownerMemberId is null && actorMember?.Role == WorkspaceMemberRole.Family)
        {
            return WorkspaceExecutionDecision.Denied(
                "resource-not-found",
                "This operation is no longer available for this member.");
        }

        return WorkspaceExecutionDecision.Allowed(
            ownerMemberId,
            canUseOwnerManagedAppleAuthority:
                privilegedNonHuman || actorMember?.Role == WorkspaceMemberRole.Owner);
    }

    private static bool IsPrivilegedNonHuman(string? actorKind) =>
        actorKind is not null &&
        (string.Equals(actorKind, "recovery-bearer", StringComparison.Ordinal) ||
         string.Equals(actorKind, "api-token", StringComparison.Ordinal) ||
         string.Equals(actorKind, "system", StringComparison.Ordinal));

    private static bool IsRecoveryBearer(string? actorKind) =>
        string.Equals(actorKind, "recovery-bearer", StringComparison.Ordinal) ||
        string.Equals(actorKind, "api-token", StringComparison.Ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record WorkspaceExecutionDecision(
    bool IsAllowed,
    string? OwnerMemberId,
    bool CanUseOwnerManagedAppleAuthority,
    string? ErrorCode,
    string? Message,
    bool Retryable)
{
    internal static WorkspaceExecutionDecision Allowed(
        string? ownerMemberId,
        bool canUseOwnerManagedAppleAuthority) =>
        new(true, ownerMemberId, canUseOwnerManagedAppleAuthority, null, null, false);

    internal static WorkspaceExecutionDecision Denied(
        string code,
        string message,
        bool retryable = false) =>
        new(false, null, false, code, message, retryable);
}
