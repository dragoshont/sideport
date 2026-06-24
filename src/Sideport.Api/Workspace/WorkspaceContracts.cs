namespace Sideport.Api.Workspace;

public sealed record WorkspaceMemberDto(
    string Id,
    string Name,
    string? Email,
    string Role,
    string Status,
    DateTimeOffset? LastActiveAt,
    DateTimeOffset? InvitedAt,
    string Source = "live");

public sealed record WorkspaceRoleDto(string Id, string Label, IReadOnlyList<string> Capabilities);

public sealed record WorkspaceDto(
    string Name,
    string AuthMode,
    bool AuthDelegated,
    string RoleEnforcement,
    bool SupportsUserAdministration,
    WorkspaceMemberDto CurrentMember,
    IReadOnlyList<WorkspaceMemberDto> Members,
    IReadOnlyList<WorkspaceRoleDto> Roles,
    IReadOnlyDictionary<string, bool> Capabilities,
    string Source = "live");
