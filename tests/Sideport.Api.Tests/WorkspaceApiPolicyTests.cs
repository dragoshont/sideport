using Microsoft.AspNetCore.Http;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class WorkspaceApiPolicyTests
{
    public static IEnumerable<object[]> ExplicitClassifications()
    {
        yield return Case("GET", "/api/about", WorkspaceApiAccess.ActiveMember);
        yield return Case("GET", "/api/authentication/options", WorkspaceApiAccess.Public);
        yield return Case("POST", "/api/workspace/invitations/handoff", WorkspaceApiAccess.Public);
        yield return Case("POST", "/api/workspace/invitations/enrollment", WorkspaceApiAccess.Public);
        yield return Case("POST", "/api/workspace/owner-claims/handoff", WorkspaceApiAccess.Public);

        yield return Case("GET", "/api/me", WorkspaceApiAccess.Identity);
        yield return Case("GET", "/api/workspace/invitations/handoff", WorkspaceApiAccess.Identity);
        yield return Case("POST", "/api/workspace/invitations/accept", WorkspaceApiAccess.Identity);
        yield return Case("GET", "/api/workspace/owner-claims/handoff", WorkspaceApiAccess.Identity);
        yield return Case("POST", "/api/workspace/owner-claims/accept", WorkspaceApiAccess.Identity);

        yield return Case("GET", "/api/workspace", WorkspaceApiAccess.ActiveMember);

        yield return Case("POST", "/api/workspace/owner-claims", WorkspaceApiAccess.RecoveryBearer);
        yield return Case("POST", "/api/workspace/owner-claims/owner_claim_123/revoke", WorkspaceApiAccess.RecoveryBearer);
        yield return Case("POST", "/api/workspace/recovery/after-restore", WorkspaceApiAccess.RecoveryBearer);

        yield return Case("POST", "/api/workspace/invitations", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/workspace/invitations/invitation_123/revoke", WorkspaceApiAccess.Owner);
        yield return Case("PATCH", "/api/workspace/members/member_123", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/workspace/members/member_123/offboard", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/workspace/audit", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/system/status", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/scheduler/status", WorkspaceApiAccess.Owner);
        yield return Case("PUT", "/api/scheduler/settings", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/anisette/info", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/logs", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/apple-access/status", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/apple-access/personal/status", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/connect", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/sign-in", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/2fa", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/signing-preflight", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/cutover", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/replacement-candidates", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apple-access/personal/replacement-candidates/2fa", WorkspaceApiAccess.Owner);
        yield return Case("PUT", "/api/apple-access/personal/team", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/devices", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/devices/diagnostics", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/devices/known", WorkspaceApiAccess.Owner);
        yield return Case("DELETE", "/api/devices/known/device-1", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/onboarding/status", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/onboarding/complete", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/v2/catalog/import-roots", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/v2/catalog/apps/inspect", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/v2/catalog/apps/upload", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/v2/catalog/github/sources", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/v2/catalog/github/connections", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/v2/catalog/github/connections/connection-1", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/v2/catalog/github/sources/source-1/releases", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/v2/catalog/apps/import-github", WorkspaceApiAccess.Owner);
        yield return Case("GET", "/api/catalog/apps", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/catalog/apps/inspect", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/catalog/apps/upload", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apps", WorkspaceApiAccess.Owner);
        yield return Case("POST", "/api/apps/device-1/app.example/verify", WorkspaceApiAccess.Owner);
        yield return Case("PATCH", "/api/diagnostics/issues/issue-1", WorkspaceApiAccess.Owner);

        yield return Case("GET", "/api/v2/catalog/apps", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/v2/catalog/apps/cert-clock/icon", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/devices/known", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/devices/device-1/installed-apps", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/apps", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/operations", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/operations/operation-1", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/renewals", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/diagnostics/issues", WorkspaceApiAccess.FamilyScoped);
        yield return Case("GET", "/api/diagnostics/issues/issue-1", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/devices/enrollments", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/preflight", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/install", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/refresh", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/operation-1/reconcile", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/operation-1/cancel", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/operation-1/retry", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/operations/operation-1/rerun", WorkspaceApiAccess.FamilyScoped);
        yield return Case("POST", "/api/apps/device-1/app.example/refresh", WorkspaceApiAccess.FamilyScoped);
        yield return Case("PATCH", "/api/devices/known/device-1", WorkspaceApiAccess.FamilyScoped);
        yield return Case("DELETE", "/api/apps/device-1/app.example", WorkspaceApiAccess.FamilyScoped);
    }

    [Theory]
    [MemberData(nameof(ExplicitClassifications))]
    public void Classify_AssignsEveryExplicitRouteToItsNarrowestBoundary(
        string method,
        string path,
        string expected)
    {
        WorkspaceApiAccess actual = WorkspaceApiPolicy.Classify(method, new PathString(path));

        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("GET", "/")]
    [InlineData("GET", "/invite")]
    [InlineData("GET", "/api/not-registered")]
    [InlineData("GET", "/github/setup/callback")]
    [InlineData("GET", "/api/devices/a/b/installed-apps")]
    [InlineData("GET", "/api/operations/operation-1/extra")]
    [InlineData("GET", "/api/operations//operation-1")]
    [InlineData("GET", "/api/diagnostics/issues/issue-1/extra")]
    [InlineData("GET", "/api/diagnostics/issues//issue-1")]
    [InlineData("GET", "/api/v2/catalog/github/connections/connection-1/extra")]
    [InlineData("GET", "/api/v2/catalog/github/sources/source-1/releases/extra")]
    [InlineData("POST", "/api/workspace/invitations/invitation_123/revoke/extra")]
    [InlineData("POST", "/api/workspace/owner-claims/owner_claim_123/revoke/extra")]
    public void Classify_UnknownOrMalformedRoutes_DefaultToDeny(string method, string path)
    {
        Assert.Equal(
            WorkspaceApiAccess.Deny,
            WorkspaceApiPolicy.Classify(method, new PathString(path)));
    }

    [Theory]
    [InlineData("DELETE", "/api/workspace/audit")]
    [InlineData("GET", "/api/workspace/members/member_123")]
    [InlineData("GET", "/api/workspace/invitations/invitation_123/revoke")]
    [InlineData("GET", "/api/workspace/owner-claims/owner_claim_123/revoke")]
    [InlineData("DELETE", "/api/system/status")]
    public void Classify_UnassignedHttpMethods_DefaultToDeny(string method, string path)
    {
        Assert.Equal(
            WorkspaceApiAccess.Deny,
            WorkspaceApiPolicy.Classify(method, new PathString(path)));
    }

    private static object[] Case(string method, string path, WorkspaceApiAccess access) =>
        [method, path, access.ToString()];
}
