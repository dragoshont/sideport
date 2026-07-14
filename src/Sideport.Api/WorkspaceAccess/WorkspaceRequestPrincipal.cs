namespace Sideport.Api.WorkspaceAccess;

internal enum WorkspaceRequestPrincipalKind
{
    RecoveryBearer,
    Owner,
    Family,
    BootstrapRequired,
    UnknownOidc,
    SuspendedOidc,
    OffboardedOidc,
    StoreUnavailable,
    Unverified,
}

internal sealed record WorkspaceRequestPrincipal(
    WorkspaceRequestPrincipalKind Kind,
    WorkspaceIdentityKey? Identity,
    WorkspaceMemberRecord? Member,
    IdentityPresentationValue? Presentation,
    WorkspaceRecord? Workspace = null,
    string? AuthenticationMethod = null)
{
    public bool IsInteractive => Identity is not null &&
        Kind is not WorkspaceRequestPrincipalKind.RecoveryBearer and
        not WorkspaceRequestPrincipalKind.StoreUnavailable and
        not WorkspaceRequestPrincipalKind.Unverified;

    public bool IsOidc => IsInteractive &&
        string.Equals(AuthenticationMethod, "oidc", StringComparison.Ordinal);

    public bool IsActiveMember => Kind is WorkspaceRequestPrincipalKind.Owner or
        WorkspaceRequestPrincipalKind.Family;

    public bool IsOwnerEquivalent => Kind is WorkspaceRequestPrincipalKind.Owner or
        WorkspaceRequestPrincipalKind.RecoveryBearer;

    public string AuditActorKey => Kind switch
    {
        WorkspaceRequestPrincipalKind.RecoveryBearer => "recovery-bearer",
        WorkspaceRequestPrincipalKind.Owner or WorkspaceRequestPrincipalKind.Family =>
            $"member:{Member!.MemberId}",
        WorkspaceRequestPrincipalKind.UnknownOidc => "unknown-oidc",
        WorkspaceRequestPrincipalKind.SuspendedOidc => "suspended-oidc",
        WorkspaceRequestPrincipalKind.OffboardedOidc => "offboarded-oidc",
        WorkspaceRequestPrincipalKind.BootstrapRequired => "bootstrap-required-oidc",
        WorkspaceRequestPrincipalKind.StoreUnavailable => "workspace-store-unavailable",
        _ => "unverified",
    };

    public WorkspaceActorRecord ToWorkspaceActor() => Kind switch
    {
        WorkspaceRequestPrincipalKind.RecoveryBearer => WorkspaceActorRecord.RecoveryBearer,
        WorkspaceRequestPrincipalKind.Owner or WorkspaceRequestPrincipalKind.Family =>
            WorkspaceActorRecord.ForMember(Member!.MemberId),
        _ => throw new InvalidOperationException("The request principal cannot perform an audited workspace mutation."),
    };
}
