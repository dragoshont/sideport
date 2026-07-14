using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Sideport.Api.Identity;

namespace Sideport.Api.WorkspaceAccess;

internal sealed class WorkspaceRequestPrincipalResolver
{
    internal const string ValidatedIssuerClaimType = "sideport:validated-oidc-issuer";
    internal const string SecurityEpochClaimType = "sideport:workspace-security-epoch";

    private readonly WorkspaceAccessStore _store;
    private readonly string? _recoveryBearer;
    private readonly bool _interactiveIdentityEnabled;

    internal WorkspaceRequestPrincipalResolver(
        WorkspaceAccessStore store,
        string? recoveryBearer,
        bool interactiveIdentityEnabled)
    {
        _store = store;
        _recoveryBearer = string.IsNullOrWhiteSpace(recoveryBearer) ? null : recoveryBearer;
        _interactiveIdentityEnabled = interactiveIdentityEnabled;
    }

    internal bool AuthenticationConfigured => _recoveryBearer is not null || _interactiveIdentityEnabled;

    internal async Task<WorkspaceRequestPrincipal> ResolveAsync(
        HttpContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_recoveryBearer is not null && HasMatchingBearer(context, _recoveryBearer))
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.RecoveryBearer,
                Identity: null,
                Member: null,
                Presentation: null);
        }

        if (!_interactiveIdentityEnabled || context.User?.Identity?.IsAuthenticated != true)
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.Unverified,
                Identity: null,
                Member: null,
                Presentation: null);
        }

        string? issuer = SingleClaimValue(context.User, ValidatedIssuerClaimType);
        string? subject = SingleClaimValue(context.User, "sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.Unverified,
                Identity: null,
                Member: null,
                Presentation: null);
        }

        var identity = new WorkspaceIdentityKey(issuer, subject);
        string? authenticationMethod = SingleClaimValue(
            context.User,
            SideportIdentityConstants.AuthenticationMethodClaimType);
        if (string.IsNullOrWhiteSpace(authenticationMethod) &&
            !string.Equals(issuer, SideportIdentityConstants.NativeIssuer, StringComparison.Ordinal))
        {
            authenticationMethod = SideportIdentityConstants.OidcMethod;
        }
        if (authenticationMethod is not (SideportIdentityConstants.NativeMethod or SideportIdentityConstants.OidcMethod))
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.Unverified,
                Identity: null,
                Member: null,
                Presentation: null);
        }
        try
        {
            WorkspaceAccessValidation.ValidateIdentity(identity);
        }
        catch (ArgumentException)
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.Unverified,
                Identity: null,
                Member: null,
                Presentation: null);
        }

        IdentityPresentationValue presentation = IdentityPresentation.FromClaims(
            FirstClaimValue(context.User, "name", ClaimTypes.Name, "preferred_username"),
            FirstClaimValue(context.User, "email", ClaimTypes.Email));

        WorkspaceAccessDocument? document;
        try
        {
            document = await _store.ReadAsync(ct).ConfigureAwait(false);
        }
        catch (WorkspaceAccessException)
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.StoreUnavailable,
                identity,
                Member: null,
                presentation,
                AuthenticationMethod: authenticationMethod);
        }

        if (document is null || document.Workspace.State == WorkspaceLifecycleState.BootstrapRequired)
        {
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.BootstrapRequired,
                identity,
                Member: null,
                presentation,
                document?.Workspace,
                authenticationMethod);
        }

        string? ticketEpoch = SingleClaimValue(context.User, SecurityEpochClaimType);
        if (!string.Equals(ticketEpoch, document.Workspace.SecurityEpoch, StringComparison.Ordinal))
        {
            // Cookies issued before bootstrap or restored from an older backup
            // never inherit authority from the current workspace epoch.
            return new WorkspaceRequestPrincipal(
                WorkspaceRequestPrincipalKind.Unverified,
                Identity: null,
                Member: null,
                Presentation: null,
                document.Workspace);
        }

        WorkspaceMemberRecord? member = document.Members.FirstOrDefault(item =>
            WorkspaceAccessValidation.SameIdentity(item, identity));
        WorkspaceRequestPrincipalKind kind = member switch
        {
            null => WorkspaceRequestPrincipalKind.UnknownOidc,
            { Status: WorkspaceMemberStatus.Suspended } => WorkspaceRequestPrincipalKind.SuspendedOidc,
            { Status: WorkspaceMemberStatus.Offboarded } => WorkspaceRequestPrincipalKind.OffboardedOidc,
            { Role: WorkspaceMemberRole.Owner, Status: WorkspaceMemberStatus.Active } => WorkspaceRequestPrincipalKind.Owner,
            { Role: WorkspaceMemberRole.Family, Status: WorkspaceMemberStatus.Active } => WorkspaceRequestPrincipalKind.Family,
            _ => WorkspaceRequestPrincipalKind.Unverified,
        };
        return new WorkspaceRequestPrincipal(kind, identity, member, presentation, document.Workspace, authenticationMethod);
    }

    private static string? SingleClaimValue(ClaimsPrincipal principal, string claimType)
    {
        string[] values = principal.FindAll(claimType)
            .Select(claim => claim.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (string claimType in claimTypes)
        {
            string? value = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static bool HasMatchingBearer(HttpContext context, string expected)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var values) || values.Count != 1)
            return false;

        string raw = values[0] ?? string.Empty;
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        string presented = raw["Bearer ".Length..].Trim();
        if (presented.Length == 0)
            return false;

        byte[] presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(presentedHash, expectedHash);
    }
}
