using Sideport.Api.AppleAccess;
using Sideport.Api.Identity;

namespace Sideport.Api.WorkspaceAccess;

internal static partial class WorkspaceAccessEndpoints
{
    private static void MapNativePasskeyEndpoints(WebApplication app, WorkspaceHttpOptions options)
    {
        app.MapGet("/api/workspace/owner-claims/native-passkey/status", async (
            WorkspaceAccessStore store,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                EnsureLinkRateAllowed(rateLimiter, "owner-native-passkey-status", "presentation", context);
                string state = await store.GetNativeOwnerBootstrapStateAsync(ct).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(new { mode = "passkey", state });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims/native-passkey/options", async (
            NativePasskeyProfileHttpRequest request,
            WorkspaceAccessStore store,
            NativePasskeyService passkeys,
            NativeOwnerBootstrapCoordinator coordinator,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                EnsureLinkRateAllowed(rateLimiter, "owner-native-passkey-bootstrap", "unclaimed", context);
                await coordinator.Gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    string handoff = await RequireOrCreateNativeOwnerHandoffAsync(
                        store,
                        context,
                        ct).ConfigureAwait(false);
                    EnsureLinkRateAllowed(rateLimiter, "owner-native-passkey", handoff, context);
                    NativePasskeyCreationResult result = await passkeys.CreateOptionsAsync(
                        new NativePasskeyProfile(request.DisplayName, request.Email)).ConfigureAwait(false);
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.Json(new { mode = "passkey", creationOptions = result.CreationOptions });
                }
                finally
                {
                    coordinator.Gate.Release();
                }
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims/native-passkey/complete", async (
            NativePasskeyCompleteHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            NativePasskeyService passkeys,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                string handoff = RequireCookie(context, OwnerClaimHandoffCookie, "owner-claim-unavailable");
                EnsureLinkRateAllowed(rateLimiter, "owner-native-passkey-complete", handoff, context);
                await store.ResolvePendingOwnerClaimForEnrollmentAsync(handoff, ct).ConfigureAwait(false);
                NativePasskeyCompletionResult result = await passkeys.CompleteEnrollmentAsync(
                    new NativePasskeyProfile(request.DisplayName, request.Email),
                    request.CredentialJson).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                if (!result.Succeeded || result.User is null)
                    return NativePasskeyResult(result);

                try
                {
                    var identity = new WorkspaceIdentityKey(
                        SideportIdentityConstants.NativeIssuer,
                        result.User.Id);
                    WorkspaceAcceptanceResult acceptance = await store.AcceptOwnerClaimAsync(
                        handoff,
                        new WorkspaceAcceptanceRequest(
                            identity,
                            result.User.DisplayName,
                            result.User.Email,
                            request.IdempotencyKey,
                            RequestId(context)),
                        ct,
                        (current, claim, token) => impacts.VerifyOwnerReplacementAsync(
                            current,
                            claim.ExpectedOwnerMemberId!,
                            claim.ImpactVersion!,
                            token)).ConfigureAwait(false);
                    await passkeys.SignInUserAsync(result.User).ConfigureAwait(false);
                    DeleteHandoffCookie(context, OwnerClaimHandoffCookie);
                    return NativePasskeyAcceptanceResult(acceptance);
                }
                catch
                {
                    await passkeys.DeleteUserAsync(result.User).ConfigureAwait(false);
                    throw;
                }
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/native-passkey/options", async (
            WorkspaceAccessStore store,
            NativePasskeyService passkeys,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                string handoff = RequireCookie(context, InvitationHandoffCookie, "invitation-unavailable");
                EnsureLinkRateAllowed(rateLimiter, "invitation-native-passkey", handoff, context);
                WorkspaceInvitationRecord invitation = await store
                    .ResolvePendingInvitationForEnrollmentAsync(handoff, ct)
                    .ConfigureAwait(false);
                NativePasskeyCreationResult result = await passkeys.CreateOptionsAsync(
                    InvitationProfile(invitation)).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(new
                {
                    mode = "passkey",
                    creationOptions = result.CreationOptions,
                    profile = new { displayName = invitation.DisplayName, email = invitation.ContactEmail },
                });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/native-passkey/complete", async (
            NativePasskeyCredentialHttpRequest request,
            WorkspaceAccessStore store,
            NativePasskeyService passkeys,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                string handoff = RequireCookie(context, InvitationHandoffCookie, "invitation-unavailable");
                EnsureLinkRateAllowed(rateLimiter, "invitation-native-passkey-complete", handoff, context);
                WorkspaceInvitationRecord invitation = await store
                    .ResolvePendingInvitationForEnrollmentAsync(handoff, ct)
                    .ConfigureAwait(false);
                NativePasskeyCompletionResult result = await passkeys.CompleteEnrollmentAsync(
                    InvitationProfile(invitation),
                    request.CredentialJson).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                if (!result.Succeeded || result.User is null)
                    return NativePasskeyResult(result);

                try
                {
                    var identity = new WorkspaceIdentityKey(
                        SideportIdentityConstants.NativeIssuer,
                        result.User.Id);
                    WorkspaceAcceptanceResult acceptance = await store.AcceptInvitationAsync(
                        handoff,
                        new WorkspaceAcceptanceRequest(
                            identity,
                            result.User.DisplayName,
                            result.User.Email,
                            request.IdempotencyKey,
                            RequestId(context)),
                        ct).ConfigureAwait(false);
                    await passkeys.SignInUserAsync(result.User).ConfigureAwait(false);
                    DeleteHandoffCookie(context, InvitationHandoffCookie);
                    return NativePasskeyAcceptanceResult(acceptance);
                }
                catch
                {
                    await passkeys.DeleteUserAsync(result.User).ConfigureAwait(false);
                    throw;
                }
            }).ConfigureAwait(false));

        app.MapPost("/api/authentication/native-passkey/options", async (
            NativePasskeyService passkeys,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                EnsureLinkRateAllowed(rateLimiter, "native-passkey-login", "discoverable", context);
                string optionsJson = await passkeys.CreateRequestOptionsAsync().ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Json(new { mode = "passkey", requestOptions = optionsJson });
            }).ConfigureAwait(false));

        app.MapPost("/api/authentication/native-passkey/complete", async (
            NativePasskeyCredentialHttpRequest request,
            NativePasskeyService passkeys,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context) =>
            await ExecuteAsync(async () =>
            {
                RequireExactOrigin(context);
                EnsureLinkRateAllowed(rateLimiter, "native-passkey-login-complete", "discoverable", context);
                NativePasskeyCompletionResult result = await passkeys.CompleteSignInAsync(request.CredentialJson).ConfigureAwait(false);
                context.Response.Headers.CacheControl = "no-store";
                return NativePasskeyResult(result);
            }).ConfigureAwait(false));
    }

    private static NativePasskeyProfile InvitationProfile(WorkspaceInvitationRecord invitation) =>
        new(
            invitation.DisplayName ?? invitation.ContactEmail ?? "Sideport member",
            invitation.ContactEmail ?? throw new WorkspaceAccessException(
                "invitation-unavailable",
                "The invitation is unavailable."));

    private static async Task<string> RequireOrCreateNativeOwnerHandoffAsync(
        WorkspaceAccessStore store,
        HttpContext context,
        CancellationToken ct)
    {
        if (context.Request.Cookies.TryGetValue(OwnerClaimHandoffCookie, out string? existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            try
            {
                await store.ResolvePendingOwnerClaimForEnrollmentAsync(existing, ct).ConfigureAwait(false);
                return existing;
            }
            catch (WorkspaceAccessException error) when (error.Code == "owner-claim-unavailable")
            {
                DeleteHandoffCookie(context, OwnerClaimHandoffCookie);
            }
        }

        WorkspaceAccessDocument? document = await store.ReadAsync(ct).ConfigureAwait(false);
        if (document?.Workspace.State == WorkspaceLifecycleState.Active)
            throw new WorkspaceAccessException("owner-claim-unavailable", "The Owner claim is unavailable.");
        foreach (WorkspaceOwnerClaimRecord pending in document?.OwnerClaims.Where(claim =>
                     claim.Status == WorkspaceAuthorityStatus.Pending &&
                     claim.Kind == WorkspaceOwnerClaimKind.Bootstrap &&
                     claim.CreatedByActor.Kind == WorkspaceActorKind.System).ToArray() ?? [])
        {
            await store.RevokeOwnerClaimAsync(
                pending.ClaimId,
                new WorkspaceAuthorityRevokeRequest(
                    WorkspaceActorRecord.System,
                    pending.Version,
                    $"native-bootstrap-retry-{pending.ClaimId}",
                    RequestId(context)),
                ct).ConfigureAwait(false);
        }

        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(
            new WorkspaceOwnerClaimCreateRequest(
                ExpectedOwnerMemberId: null,
                ImpactVersion: null,
                Lifetime: TimeSpan.FromMinutes(15),
                IdempotencyKey: $"native-owner-bootstrap-{Guid.NewGuid():N}",
                RequestId: RequestId(context),
                Actor: WorkspaceActorRecord.System),
            ct).ConfigureAwait(false);
        if (!claim.Created || string.IsNullOrWhiteSpace(claim.Token))
            throw new WorkspaceAccessException("owner-claim-unavailable", "The Owner claim is unavailable.");

        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(
            claim.Token,
            RequestId(context),
            ct: ct).ConfigureAwait(false);
        SetHandoffCookie(context, OwnerClaimHandoffCookie, handoff.Token, handoff.Handoff.ExpiresAt);
        return handoff.Token;
    }

    private static IResult NativePasskeyResult(NativePasskeyCompletionResult result) =>
        result.Succeeded
            ? Results.Json(new { signedIn = true, method = "passkey" })
            : Results.Json(
                new { error = result.Error, message = result.Message },
                statusCode: result.Error == "passkey-ceremony-expired"
                    ? StatusCodes.Status409Conflict
                    : StatusCodes.Status422UnprocessableEntity);

    private static IResult NativePasskeyAcceptanceResult(WorkspaceAcceptanceResult acceptance) =>
        Results.Json(new
        {
            signedIn = true,
            method = "passkey",
            acceptance = ProjectAcceptance(acceptance),
        });

    private static void RequireExactOrigin(HttpContext context)
    {
        if (!AppleCredentialOriginPolicy.IsSameOrigin(context.Request) ||
            AppleCredentialOriginPolicy.IsExplicitCrossSite(context.Request))
        {
            throw new WorkspaceAccessException(
                "origin-or-antiforgery",
                "Refresh Sideport and retry this protected request.");
        }
    }

    private static void EnsureLinkRateAllowed(
        WorkspaceLinkRateLimiter limiter,
        string action,
        string authority,
        HttpContext context)
    {
        WorkspaceLinkRateLimitDecision decision = limiter.Acquire(
            action,
            context.Connection.RemoteIpAddress?.ToString(),
            authority);
        if (!decision.Allowed)
            throw new WorkspaceAccessException("passkey-rate-limited", "Wait briefly, then try again.");
    }

    internal sealed record NativePasskeyProfileHttpRequest(string DisplayName, string Email);
    internal sealed record NativePasskeyCompleteHttpRequest(
        string DisplayName,
        string Email,
        string CredentialJson,
        string IdempotencyKey);
    internal sealed record NativePasskeyCredentialHttpRequest(
        string CredentialJson,
        string IdempotencyKey = "");
}

internal sealed class NativeOwnerBootstrapCoordinator
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
