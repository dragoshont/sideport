namespace Sideport.Api.WorkspaceAccess;

internal enum WorkspaceApiAccess
{
    Public,
    Identity,
    ActiveMember,
    FamilyScoped,
    Owner,
    RecoveryBearer,
    Deny,
}

/// <summary>
/// Explicit inventory for the current minimal-API surface. New API routes are
/// denied until they are assigned a capability boundary here.
/// </summary>
internal static class WorkspaceApiPolicy
{
    internal static WorkspaceApiAccess Classify(string method, PathString requestPath)
    {
        string path = requestPath.Value?.TrimEnd('/') ?? string.Empty;
        if (path.Length == 0)
            path = "/";

        if (HttpMethods.IsGet(method) && path == "/api/about")
            return WorkspaceApiAccess.ActiveMember;
        if (HttpMethods.IsGet(method) && path == "/api/me")
            return WorkspaceApiAccess.Identity;
        if (HttpMethods.IsGet(method) && path == "/api/authentication/options")
            return WorkspaceApiAccess.Public;
        if (HttpMethods.IsGet(method) && path == "/api/workspace")
            return WorkspaceApiAccess.ActiveMember;

        if (HttpMethods.IsPost(method) &&
            path is "/api/workspace/invitations/handoff" or "/api/workspace/owner-claims/handoff")
        {
            return WorkspaceApiAccess.Public;
        }

        if (HttpMethods.IsPost(method) &&
            path is "/api/workspace/invitations/enrollment" or "/api/workspace/owner-claims/enrollment")
            return WorkspaceApiAccess.Public;

        if ((HttpMethods.IsGet(method) || HttpMethods.IsPost(method)) &&
            path is "/api/workspace/invitations/handoff" or "/api/workspace/invitations/accept" or
                "/api/workspace/owner-claims/handoff" or "/api/workspace/owner-claims/accept")
        {
            return WorkspaceApiAccess.Identity;
        }

        if (HttpMethods.IsPost(method) &&
            (path == "/api/workspace/owner-claims" ||
             IsWorkspaceResourceAction(path, "/api/workspace/owner-claims/", "revoke") ||
             path == "/api/workspace/recovery/after-restore"))
        {
            return WorkspaceApiAccess.RecoveryBearer;
        }

        if ((HttpMethods.IsPost(method) &&
             (path == "/api/workspace/invitations" ||
              IsWorkspaceResourceAction(path, "/api/workspace/invitations/", "revoke") ||
              IsWorkspaceMemberOffboard(path))) ||
            (HttpMethods.IsPatch(method) && IsWorkspaceMember(path)) ||
            (HttpMethods.IsGet(method) && path == "/api/workspace/audit"))
        {
            return WorkspaceApiAccess.Owner;
        }

        if (HttpMethods.IsGet(method) && path == "/api/v2/catalog/apps")
            return WorkspaceApiAccess.FamilyScoped;
        if (HttpMethods.IsGet(method) && path.StartsWith("/api/v2/catalog/apps/", StringComparison.Ordinal) && path.EndsWith("/icon", StringComparison.Ordinal) && HasExactTailSegments(path, "/api/v2/catalog/apps/", 2))
            return WorkspaceApiAccess.FamilyScoped;
        if (HttpMethods.IsGet(method) &&
            (path == "/api/devices/known" ||
             IsDeviceInstalledApps(path) ||
             path == "/api/apps" ||
             path == "/api/operations" ||
             IsOperationResource(path) ||
             path == "/api/renewals" ||
             path == "/api/diagnostics/issues" ||
             IsDiagnosticIssueResource(path)))
        {
            return WorkspaceApiAccess.FamilyScoped;
        }

        if (HttpMethods.IsPost(method) &&
            (path == "/api/devices/enrollments" ||
             path is "/api/operations/preflight" or "/api/operations/install" or "/api/operations/refresh" ||
             IsOperationAction(path) ||
             IsRegistrationRefresh(path)))
        {
            return WorkspaceApiAccess.FamilyScoped;
        }

        if (HttpMethods.IsPatch(method) && IsKnownDeviceResource(path))
            return WorkspaceApiAccess.FamilyScoped;
        if (HttpMethods.IsDelete(method) && IsRegistrationResource(path))
            return WorkspaceApiAccess.FamilyScoped;

        return IsCurrentOwnerRoute(method, path)
            ? WorkspaceApiAccess.Owner
            : WorkspaceApiAccess.Deny;
    }

    private static bool IsCurrentOwnerRoute(string method, string path)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal))
            return false;

        if (HttpMethods.IsGet(method))
        {
            return path is "/api/system/status" or "/api/scheduler/status" or
                    "/api/anisette/info" or "/api/logs" or
                    "/api/apple-access/status" or "/api/apple-access/personal/status" or
                    "/api/devices" or "/api/devices/diagnostics" or
                    "/api/onboarding/status" or "/api/v2/catalog/import-roots" or
                    "/api/v2/catalog/github/sources" or "/api/catalog/apps" ||
                IsGitHubConnectionOrReleaseRead(path);
        }

        if (HttpMethods.IsPut(method))
            return path is "/api/scheduler/settings" or "/api/apple-access/personal/team";
        if (HttpMethods.IsPatch(method))
            return path.StartsWith("/api/diagnostics/issues/", StringComparison.Ordinal) &&
                HasExactTailSegments(path, "/api/diagnostics/issues/", 1);
        if (HttpMethods.IsDelete(method))
            return IsKnownDeviceResource(path);
        if (!HttpMethods.IsPost(method))
            return false;

        return path is "/api/apple-access/personal/connect" or
                "/api/apple-access/personal/sign-in" or
                "/api/apple-access/personal/2fa" or
                "/api/apple-access/personal/signing-preflight" or
                "/api/apple-access/personal/cutover" or
                "/api/apple-access/personal/replacement-candidates" or
                "/api/apple-access/personal/replacement-candidates/2fa" or
                "/api/devices/known" or
                "/api/onboarding/complete" or
                "/api/v2/catalog/apps/inspect" or
                "/api/v2/catalog/apps/upload" or
                "/api/v2/catalog/github/connections" or
                "/api/v2/catalog/apps/import-github" or
                "/api/catalog/apps/inspect" or
                "/api/catalog/apps/upload" or
                "/api/apps" ||
            IsRegistrationVerify(path);
    }

    private static bool IsKnownDeviceResource(string path) =>
        HasExactTailSegments(path, "/api/devices/known/", 1);

    private static bool IsDeviceInstalledApps(string path) =>
        path.StartsWith("/api/devices/", StringComparison.Ordinal) &&
        path.EndsWith("/installed-apps", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/devices/", 2);

    private static bool IsRegistrationResource(string path) =>
        path.StartsWith("/api/apps/", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/apps/", 2);

    private static bool IsRegistrationRefresh(string path) =>
        path.StartsWith("/api/apps/", StringComparison.Ordinal) &&
        path.EndsWith("/refresh", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/apps/", 3);

    private static bool IsRegistrationVerify(string path) =>
        path.StartsWith("/api/apps/", StringComparison.Ordinal) &&
        path.EndsWith("/verify", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/apps/", 3);

    private static bool IsOperationResource(string path) =>
        HasExactTailSegments(path, "/api/operations/", 1);

    private static bool IsDiagnosticIssueResource(string path) =>
        HasExactTailSegments(path, "/api/diagnostics/issues/", 1);

    private static bool IsGitHubConnectionOrReleaseRead(string path) =>
        path.StartsWith("/api/v2/catalog/github/connections/", StringComparison.Ordinal) &&
            HasExactTailSegments(path, "/api/v2/catalog/github/connections/", 1) ||
        path.StartsWith("/api/v2/catalog/github/sources/", StringComparison.Ordinal) &&
            path.EndsWith("/releases", StringComparison.Ordinal) &&
            HasExactTailSegments(path, "/api/v2/catalog/github/sources/", 2);

    private static bool IsWorkspaceMember(string path) =>
        path.StartsWith("/api/workspace/members/", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/workspace/members/", 1);

    private static bool IsWorkspaceMemberOffboard(string path) =>
        path.StartsWith("/api/workspace/members/", StringComparison.Ordinal) &&
        path.EndsWith("/offboard", StringComparison.Ordinal) &&
        HasExactTailSegments(path, "/api/workspace/members/", 2);

    private static bool IsOperationAction(string path)
    {
        if (!path.StartsWith("/api/operations/", StringComparison.Ordinal) ||
            !HasExactTailSegments(path, "/api/operations/", 2))
        {
            return false;
        }

        string action = path[(path.LastIndexOf('/') + 1)..];
        return action is "reconcile" or "cancel" or "retry" or "rerun";
    }

    private static bool IsWorkspaceResourceAction(string path, string prefix, string action) =>
        path.StartsWith(prefix, StringComparison.Ordinal) &&
        path.EndsWith($"/{action}", StringComparison.Ordinal) &&
        HasExactTailSegments(path, prefix, 2);

    private static bool HasExactTailSegments(string path, string prefix, int expected)
    {
        if (!path.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        string[] segments = path[prefix.Length..].Split('/', StringSplitOptions.None);
        return segments.Length == expected && segments.All(segment => segment.Length > 0);
    }
}
