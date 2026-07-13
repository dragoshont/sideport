using Sideport.Api.GitHubCatalog;

namespace Sideport.Api.WorkspaceAccess;

internal sealed class WorkspaceGitHubSetupActorAuthorizer(
    WorkspaceAccessStore store,
    bool recoveryBearerConfigured) : IGitHubSetupActorAuthorizer
{
    private const string MemberActorPrefix = "member:";
    private const string RecoveryBearerActor = "recovery-bearer";

    public async Task<bool> IsAuthorizedAsync(
        string actor,
        CancellationToken ct = default)
    {
        if (string.Equals(actor, RecoveryBearerActor, StringComparison.Ordinal))
            return recoveryBearerConfigured;
        if (string.IsNullOrWhiteSpace(actor) ||
            !actor.StartsWith(MemberActorPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        string memberId = actor[MemberActorPrefix.Length..];
        if (string.IsNullOrWhiteSpace(memberId))
            return false;

        try
        {
            WorkspaceAccessDocument? document = await store.ReadAsync(ct).ConfigureAwait(false);
            if (document?.Workspace.State != WorkspaceLifecycleState.Active ||
                !string.Equals(document.Workspace.OwnerMemberId, memberId, StringComparison.Ordinal))
            {
                return false;
            }

            WorkspaceMemberRecord? member = document.Members.FirstOrDefault(candidate =>
                string.Equals(candidate.MemberId, memberId, StringComparison.Ordinal));
            return member is
            {
                Role: WorkspaceMemberRole.Owner,
                Status: WorkspaceMemberStatus.Active,
            };
        }
        catch (WorkspaceAccessException)
        {
            return false;
        }
    }
}
