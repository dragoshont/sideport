using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Sideport.Api.AppleAccess;

namespace Sideport.Api.WorkspaceAccess;

internal static class WorkspaceApiSecurity
{
    internal const string PrincipalItemKey = "sideport.workspace-principal";
    internal const long LinkExchangeBodyLimit = 8 * 1024;

    internal static async Task InvokeAsync(
        HttpContext context,
        RequestDelegate next,
        WorkspaceRequestPrincipalResolver resolver,
        IAntiforgery antiforgery,
        bool allowInsecureLoopbackLinkExchange)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        WorkspaceApiAccess access = WorkspaceApiPolicy.Classify(
            context.Request.Method,
            context.Request.Path);
        bool linkExchange = IsLinkExchange(context.Request);
        bool linkAuthorityMutation = linkExchange || IsLinkMint(context.Request);
        if (linkAuthorityMutation &&
            !ValidateLinkAuthorityBoundary(context, allowInsecureLoopbackLinkExchange))
            return;

        WorkspaceRequestPrincipal principal = await resolver.ResolveAsync(
            context,
            context.RequestAborted).ConfigureAwait(false);
        context.Items[PrincipalItemKey] = principal;

        if (IsNoStorePath(context.Request.Path))
            context.Response.Headers.CacheControl = "no-store";

        if (!await AuthorizeAsync(
                context,
                principal,
                access,
                resolver.AuthenticationConfigured).ConfigureAwait(false))
            return;

        if (!linkExchange && IsUnsafe(context.Request.Method) && principal.IsOidc)
        {
            if (AppleCredentialOriginPolicy.IsExplicitCrossSite(context.Request) ||
                !AppleCredentialOriginPolicy.IsSameOrigin(context.Request))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "origin-or-antiforgery",
                    "Refresh Sideport and retry this protected request.").ConfigureAwait(false);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status403Forbidden,
                    "origin-or-antiforgery",
                    "Refresh Sideport and retry this protected request.").ConfigureAwait(false);
                return;
            }
        }

        await next(context);
    }

    internal static WorkspaceRequestPrincipal PrincipalFrom(HttpContext context) =>
        context.Items.TryGetValue(PrincipalItemKey, out object? value) &&
        value is WorkspaceRequestPrincipal principal
            ? principal
            : throw new InvalidOperationException("The workspace request principal was not resolved.");

    private static async Task<bool> AuthorizeAsync(
        HttpContext context,
        WorkspaceRequestPrincipal principal,
        WorkspaceApiAccess access,
        bool authenticationConfigured)
    {
        if (access == WorkspaceApiAccess.Public)
            return true;
        if (access == WorkspaceApiAccess.Deny)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "capability-denied",
                "This Sideport action is not available to this account.").ConfigureAwait(false);
            return false;
        }

        if (principal.Kind == WorkspaceRequestPrincipalKind.StoreUnavailable)
        {
            await WriteErrorAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "workspace-store-unavailable",
                "Workspace access is temporarily unavailable.").ConfigureAwait(false);
            return false;
        }

        if (principal.Kind == WorkspaceRequestPrincipalKind.Unverified)
        {
            bool configurationMissing = !authenticationConfigured;
            await WriteErrorAsync(
                context,
                configurationMissing ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
                configurationMissing ? "authentication-required" : "unauthorized",
                configurationMissing
                    ? "Configure Sideport authentication before using the private API."
                    : "Sign in to continue.").ConfigureAwait(false);
            return false;
        }

        bool isMe = HttpMethods.IsGet(context.Request.Method) &&
            context.Request.Path.Equals("/api/me", StringComparison.OrdinalIgnoreCase);
        if (principal.Kind is WorkspaceRequestPrincipalKind.SuspendedOidc or
            WorkspaceRequestPrincipalKind.OffboardedOidc)
        {
            if (isMe)
                return true;
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "member-access-disabled",
                "This Sideport membership is disabled.").ConfigureAwait(false);
            return false;
        }

        if (principal.Kind == WorkspaceRequestPrincipalKind.BootstrapRequired)
        {
            if (access == WorkspaceApiAccess.Identity)
                return true;
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "workspace-bootstrap-required",
                "Finish Owner setup before using Sideport.").ConfigureAwait(false);
            return false;
        }

        if (principal.Kind == WorkspaceRequestPrincipalKind.UnknownOidc)
        {
            if (access == WorkspaceApiAccess.Identity)
                return true;
            await WriteErrorAsync(
                context,
                StatusCodes.Status403Forbidden,
                "workspace-membership-required",
                "Use a private Sideport invitation to join this home.").ConfigureAwait(false);
            return false;
        }

        bool allowed = access switch
        {
            WorkspaceApiAccess.Identity => true,
            WorkspaceApiAccess.ActiveMember => principal.IsActiveMember || principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer,
            WorkspaceApiAccess.FamilyScoped => principal.IsActiveMember || principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer,
            WorkspaceApiAccess.Owner => principal.IsOwnerEquivalent,
            WorkspaceApiAccess.RecoveryBearer => principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer,
            _ => false,
        };
        if (allowed)
            return true;

        string error = principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer && access == WorkspaceApiAccess.Identity
            ? "capability-denied"
            : "capability-denied";
        await WriteErrorAsync(
            context,
            StatusCodes.Status403Forbidden,
            error,
            "This Sideport action is available only to the home Owner.").ConfigureAwait(false);
        return false;
    }

    private static bool ValidateLinkAuthorityBoundary(
        HttpContext context,
        bool allowInsecureLoopbackLinkExchange)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.HasJsonContentType() ||
            context.Request.ContentLength is > LinkExchangeBodyLimit)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return false;
        }

        IHttpMaxRequestBodySizeFeature? bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
            bodySize.MaxRequestBodySize = LinkExchangeBodyLimit;

        if (!AppleCredentialTransportPolicy.IsAllowed(
                context.Request.IsHttps,
                context.Connection.LocalIpAddress,
                context.Connection.RemoteIpAddress,
                allowInsecureLoopbackLinkExchange))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        if (AppleCredentialOriginPolicy.IsExplicitCrossSite(context.Request) ||
            context.Request.Headers.ContainsKey("Origin") &&
            !AppleCredentialOriginPolicy.IsSameOrigin(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        if ((context.Request.Path.Equals("/api/workspace/invitations/enrollment", StringComparison.OrdinalIgnoreCase) ||
             context.Request.Path.Equals("/api/workspace/owner-claims/enrollment", StringComparison.OrdinalIgnoreCase)) &&
            !context.Request.Headers.ContainsKey("Origin"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return false;
        }

        return true;
    }

    private static bool IsLinkExchange(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        (request.Path.Equals("/api/workspace/invitations/handoff", StringComparison.OrdinalIgnoreCase) ||
         request.Path.Equals("/api/workspace/owner-claims/handoff", StringComparison.OrdinalIgnoreCase) ||
         request.Path.Equals("/api/workspace/invitations/enrollment", StringComparison.OrdinalIgnoreCase) ||
         request.Path.Equals("/api/workspace/owner-claims/enrollment", StringComparison.OrdinalIgnoreCase));

    private static bool IsLinkMint(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) &&
        (request.Path.Equals("/api/workspace/invitations", StringComparison.OrdinalIgnoreCase) ||
         request.Path.Equals("/api/workspace/owner-claims", StringComparison.OrdinalIgnoreCase));

    private static bool IsNoStorePath(PathString path) =>
        path.Equals("/api/me", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/workspace");

    private static bool IsUnsafe(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string error,
        string message)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new { error, message });
    }
}
