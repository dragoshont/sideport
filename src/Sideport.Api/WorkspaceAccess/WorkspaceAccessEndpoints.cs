using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Authentik;
using Sideport.Api.Operations;

namespace Sideport.Api.WorkspaceAccess;

internal sealed record WorkspaceHttpOptions(
    Uri PublicOrigin,
    bool RecoveryProofConfigured);

internal static class WorkspaceAccessEndpoints
{
    private const string InvitationHandoffCookie = "__Host-sideport.invitation-handoff";
    private const string OwnerClaimHandoffCookie = "__Host-sideport.owner-claim-handoff";

    internal static void MapWorkspaceAccessEndpoints(
        this WebApplication app,
        WorkspaceHttpOptions options,
        AuthentikAuthenticationOptions authentication)
    {
        app.MapGet("/api/authentication/options", () => Results.Json(new
        {
            provider = "authentik",
            oidcEnabled = authentication.OidcEnabled,
            existingAccountUrl = authentication.ExistingAccountUrl,
            enrollmentEnabled = authentication.EnrollmentEnabled,
            recoveryUrl = authentication.RecoveryUrl,
            passkeyOwner = "authentik",
            officialSignInWithApple = false,
        }));

        app.MapGet("/api/me", async (
            HttpContext context,
            IAntiforgery antiforgery,
            CancellationToken ct) =>
        {
            WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
            if (principal.IsOidc)
            {
                AntiforgeryTokenSet tokens = antiforgery.GetAndStoreTokens(context);
                if (!string.IsNullOrWhiteSpace(tokens.RequestToken))
                    context.Response.Headers["X-Sideport-CSRF"] = tokens.RequestToken;
            }

            await Task.CompletedTask;
            return Results.Json(ProjectMe(principal, options.RecoveryProofConfigured));
        });

        app.MapGet("/api/workspace", async (
            HttpContext context,
            WorkspaceAccessStore store,
            KnownDeviceStore devices,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                IReadOnlyList<KnownDeviceRecord> knownDevices = await devices.ListAsync(ct).ConfigureAwait(false);
                return Results.Json(ProjectWorkspace(document, principal, knownDevices));
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims", async (
            OwnerClaimCreateHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                if (!options.RecoveryProofConfigured)
                    throw new WorkspaceAccessException("recovery-proof-not-configured", "Recovery access is not configured.");

                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceLinkRateLimitDecision limit = rateLimiter.AcquireMint(
                    "owner-claim",
                    context.Connection.RemoteIpAddress?.ToString(),
                    principal.AuditActorKey);
                if (!limit.Allowed)
                    return LinkRateLimited(context, "owner-claim-rate-limited", limit.RetryAfter);

                WorkspaceAccessDocument? document = await store.ReadAsync(ct).ConfigureAwait(false);
                WorkspaceImpactSnapshot? impact = null;
                if (document?.Workspace.State == WorkspaceLifecycleState.Active)
                {
                    if (!request.ConfirmReplacement ||
                        string.IsNullOrWhiteSpace(request.ExpectedOwnerMemberId) ||
                        string.IsNullOrWhiteSpace(request.ImpactVersion))
                    {
                        impact = await impacts.CreateOwnerReplacementAsync(ct).ConfigureAwait(false);
                        return OwnerReplacementConfirmation(impact);
                    }

                }

                int minutes = request.ExpiresInMinutes ?? 15;
                if (minutes is < 1 or > 60)
                    return Validation("expiresInMinutes", "Use a value from 1 to 60 minutes.");
                WorkspaceOwnerClaimCreateResult result = await store.CreateOwnerClaimAsync(
                    new WorkspaceOwnerClaimCreateRequest(
                        request.ExpectedOwnerMemberId,
                        request.ImpactVersion,
                        TimeSpan.FromMinutes(minutes),
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct,
                    document?.Workspace.State == WorkspaceLifecycleState.Active
                        ? (current, token) => impacts.VerifyOwnerReplacementAsync(
                            current,
                            request.ExpectedOwnerMemberId!,
                            request.ImpactVersion!,
                            token)
                        : null).ConfigureAwait(false);
                impact = result.Impact;
                string? shareUrl = result.Token is null
                    ? null
                    : BuildShareUrl(options.PublicOrigin, "/owner-claim", result.Token);
                object response = new
                {
                    claim = ProjectOwnerClaim(result.Claim),
                    shareUrl,
                    linkAvailable = shareUrl is not null,
                    impact = impact is null ? null : ProjectImpact(impact),
                };
                return Results.Json(
                    response,
                    statusCode: result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims/{claimId}/revoke", async (
            string claimId,
            AuthorityRevokeHttpRequest request,
            WorkspaceAccessStore store,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceMutationResult<WorkspaceOwnerClaimRecord> result = await store.RevokeOwnerClaimAsync(
                    claimId,
                    new WorkspaceAuthorityRevokeRequest(
                        WorkspaceActorRecord.RecoveryBearer,
                        request.ExpectedVersion,
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct).ConfigureAwait(false);
                return Results.Json(new { claim = ProjectOwnerClaim(result.Value), replayed = result.Replayed });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims/handoff", async (
            OwnerClaimHandoffHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceLinkRateLimitDecision limit = rateLimiter.Acquire(
                    "owner-claim",
                    context.Connection.RemoteIpAddress?.ToString(),
                    request.ClaimToken);
                if (!limit.Allowed)
                    return LinkRateLimited(context, "owner-claim-rate-limited", limit.RetryAfter);
                WorkspaceHandoffCreateResult result = await store.ExchangeOwnerClaimAsync(
                    request.ClaimToken,
                    RequestId(context),
                    ct: ct).ConfigureAwait(false);
                SetHandoffCookie(context, OwnerClaimHandoffCookie, result.Token, result.Handoff.ExpiresAt);
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                WorkspaceOwnerClaimRecord claim = document.OwnerClaims.First(item => item.ClaimId == result.Handoff.AuthorityId);
                return Results.Json(new
                {
                    workspace = new { name = document.Workspace.Name },
                    claim = new
                    {
                        kind = EnumText(claim.Kind),
                        expiresAt = claim.ExpiresAt,
                    },
                    next = "sign-in",
                });
            }).ConfigureAwait(false));

        app.MapGet("/api/workspace/owner-claims/handoff", async (
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = RequireOidcPrincipal(context);
                string handoff = RequireCookie(context, OwnerClaimHandoffCookie, "owner-claim-unavailable");
                WorkspaceHandoffResolution resolution = await store.ResolveOwnerClaimHandoffAsync(
                    handoff,
                    principal.Identity!,
                    ct).ConfigureAwait(false);
                WorkspaceImpactSnapshot? impact = null;
                if (resolution.OwnerClaim is { Kind: WorkspaceOwnerClaimKind.Recovery } claim)
                {
                    impact = await impacts.VerifyOwnerReplacementAsync(
                        claim.ExpectedOwnerMemberId!,
                        claim.ImpactVersion!,
                        ct).ConfigureAwait(false);
                }
                return Results.Json(ProjectOwnerClaimHandoff(resolution, principal, impact));
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/owner-claims/accept", async (
            AuthorityAcceptHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = RequireOidcPrincipal(context);
                string handoff = RequireCookie(context, OwnerClaimHandoffCookie, "owner-claim-unavailable");
                WorkspaceAcceptanceResult result = await store.AcceptOwnerClaimAsync(
                    handoff,
                    AcceptanceRequest(principal, request.IdempotencyKey, context),
                    ct,
                    (current, claim, token) => impacts.VerifyOwnerReplacementAsync(
                            current,
                            claim.ExpectedOwnerMemberId!,
                            claim.ImpactVersion!,
                            token)).ConfigureAwait(false);
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                await RefreshSessionEpochAsync(context, document.Workspace.SecurityEpoch).ConfigureAwait(false);
                DeleteHandoffCookie(context, OwnerClaimHandoffCookie);
                return Results.Json(ProjectAcceptance(result));
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations", async (
            InvitationCreateHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceLinkRateLimitDecision limit = rateLimiter.AcquireMint(
                    "invitation",
                    context.Connection.RemoteIpAddress?.ToString(),
                    principal.AuditActorKey);
                if (!limit.Allowed)
                    return LinkRateLimited(context, "invitation-rate-limited", limit.RetryAfter);
                double days = request.ExpiresInDays ?? 7;
                if (days < 10d / 1440d || days > 30)
                    return Validation("expiresInDays", "Use a value from ten minutes to 30 days.");
                WorkspaceInvitationCreateResult result = await store.CreateInvitationAsync(
                    new WorkspaceInvitationCreateRequest(
                        principal.ToWorkspaceActor(),
                        request.DisplayName,
                        request.ContactEmail,
                        TimeSpan.FromDays(days),
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct).ConfigureAwait(false);
                string? shareUrl = result.Token is null
                    ? null
                    : BuildShareUrl(options.PublicOrigin, "/invite", result.Token);
                return Results.Json(
                    new
                    {
                        invitation = ProjectInvitation(result.Invitation),
                        shareUrl,
                        linkAvailable = shareUrl is not null,
                    },
                    statusCode: result.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/{invitationId}/revoke", async (
            string invitationId,
            AuthorityRevokeHttpRequest request,
            WorkspaceAccessStore store,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceMutationResult<WorkspaceInvitationRecord> result = await store.RevokeInvitationAsync(
                    invitationId,
                    new WorkspaceAuthorityRevokeRequest(
                        principal.ToWorkspaceActor(),
                        request.ExpectedVersion,
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct).ConfigureAwait(false);
                return Results.Json(new { invitation = ProjectInvitation(result.Value), replayed = result.Replayed });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/handoff", async (
            InvitationHandoffHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceLinkRateLimitDecision limit = rateLimiter.Acquire(
                    "invitation",
                    context.Connection.RemoteIpAddress?.ToString(),
                    request.InvitationToken);
                if (!limit.Allowed)
                    return LinkRateLimited(context, "invitation-rate-limited", limit.RetryAfter);
                WorkspaceHandoffCreateResult result = await store.ExchangeInvitationAsync(
                    request.InvitationToken,
                    RequestId(context),
                    ct: ct).ConfigureAwait(false);
                SetHandoffCookie(context, InvitationHandoffCookie, result.Token, result.Handoff.ExpiresAt);
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                WorkspaceInvitationRecord invitation = document.Invitations.First(item => item.InvitationId == result.Handoff.AuthorityId);
                WorkspaceMemberRecord? inviter = invitation.CreatedByActor.MemberId is null
                    ? null
                    : document.Members.FirstOrDefault(item => item.MemberId == invitation.CreatedByActor.MemberId);
                return Results.Json(new
                {
                    workspace = new { name = document.Workspace.Name },
                    invitation = new
                    {
                        invitedName = invitation.DisplayName,
                        inviter = inviter?.DisplayName ?? "Sideport Owner",
                        role = "family",
                        expiresAt = invitation.ExpiresAt,
                    },
                    next = "sign-in",
                });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/enrollment", async (
            AuthentikEnrollmentHttpRequest request,
            WorkspaceAccessStore store,
            IAuthentikEnrollmentAdapter enrollment,
            WorkspaceLinkRateLimiter rateLimiter,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                string handoff = RequireCookie(context, InvitationHandoffCookie, "invitation-unavailable");
                WorkspaceLinkRateLimitDecision limit = rateLimiter.Acquire(
                    "invitation-enrollment",
                    context.Connection.RemoteIpAddress?.ToString(),
                    handoff);
                if (!limit.Allowed)
                    return LinkRateLimited(context, "invitation-rate-limited", limit.RetryAfter);

                WorkspaceInvitationRecord invitation = await store.ResolvePendingInvitationForEnrollmentAsync(
                    handoff,
                    ct).ConfigureAwait(false);
                AuthentikEnrollmentResult result;
                try
                {
                    result = await enrollment.CreateAsync(
                        new AuthentikEnrollmentRequest(
                            invitation.DisplayName ?? "Sideport member",
                            invitation.ContactEmail ?? throw new WorkspaceAccessException(
                                "invitation-unavailable",
                                "The invitation is unavailable."),
                            request.IdempotencyKey),
                        ct).ConfigureAwait(false);
                }
                catch (AuthentikEnrollmentException error)
                {
                    throw new WorkspaceAccessException(error.Code, error.Message, error);
                }
                return Results.Json(new
                {
                    available = result.Available,
                    enrollmentUrl = result.EnrollmentUrl,
                    expiresAt = result.ExpiresAt,
                    reason = result.Reason,
                    existingAccountUrl = "/login?returnUrl=%2Finvite",
                });
            }).ConfigureAwait(false));

        app.MapGet("/api/workspace/invitations/handoff", async (
            WorkspaceAccessStore store,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = RequireOidcPrincipal(context);
                string handoff = RequireCookie(context, InvitationHandoffCookie, "invitation-unavailable");
                WorkspaceHandoffResolution resolution = await store.ResolveInvitationHandoffAsync(
                    handoff,
                    principal.Identity!,
                    ct).ConfigureAwait(false);
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                return Results.Json(ProjectInvitationHandoff(document, resolution, principal));
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/invitations/accept", async (
            AuthorityAcceptHttpRequest request,
            WorkspaceAccessStore store,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceRequestPrincipal principal = RequireOidcPrincipal(context);
                string handoff = RequireCookie(context, InvitationHandoffCookie, "invitation-unavailable");
                WorkspaceAcceptanceResult result = await store.AcceptInvitationAsync(
                    handoff,
                    AcceptanceRequest(principal, request.IdempotencyKey, context),
                    ct).ConfigureAwait(false);
                DeleteHandoffCookie(context, InvitationHandoffCookie);
                return Results.Json(ProjectAcceptance(result));
            }).ConfigureAwait(false));

        app.MapPatch("/api/workspace/members/{memberId}", async (
            string memberId,
            MemberStatusHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 240)
                    return Validation("reason", "Briefly explain this access change.");
                WorkspaceMemberStatus status = request.Status.Trim().ToLowerInvariant() switch
                {
                    "active" => WorkspaceMemberStatus.Active,
                    "suspended" => WorkspaceMemberStatus.Suspended,
                    _ => throw new ArgumentException("Member status must be active or suspended."),
                };
                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceMutationResult<WorkspaceMemberRecord> result = await store.SetFamilyMemberStatusAsync(
                    memberId,
                    new WorkspaceMemberStatusRequest(
                        principal.ToWorkspaceActor(),
                        status,
                        request.ExpectedVersion,
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct).ConfigureAwait(false);
                if (status == WorkspaceMemberStatus.Suspended)
                    await impacts.CancelQueuedWorkBestEffortAsync(memberId, ct).ConfigureAwait(false);
                return Results.Json(new { member = ProjectMember(result.Value), replayed = result.Replayed });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/members/{memberId}/offboard", async (
            string memberId,
            MemberOffboardHttpRequest request,
            WorkspaceAccessStore store,
            WorkspaceImpactService impacts,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 240)
                    return Validation("reason", "Briefly explain why access should be removed.");
                if (string.IsNullOrWhiteSpace(request.ImpactVersion))
                {
                    WorkspaceImpactSnapshot preflight = await impacts.CreateOffboardingAsync(memberId, ct).ConfigureAwait(false);
                    return Results.Json(new
                    {
                        error = "offboarding-confirmation-required",
                        message = "Suspend this person, then review the retained phones, apps, and work before removing access.",
                        impact = ProjectImpact(preflight),
                    }, statusCode: StatusCodes.Status409Conflict);
                }

                WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
                WorkspaceOffboardingResult result = await store.FinalizeFamilyMemberOffboardingAsync(
                    memberId,
                    new WorkspaceOffboardingFinalizeRequest(
                        principal.ToWorkspaceActor(),
                        request.ExpectedVersion,
                        new WorkspaceAuditImpact(ImpactVersion: request.ImpactVersion),
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct,
                    (current, token) => impacts.VerifyOffboardingAsync(
                        current,
                        memberId,
                        request.ExpectedVersion,
                        request.ImpactVersion,
                        token)).ConfigureAwait(false);
                await impacts.CancelQueuedWorkBestEffortAsync(memberId, ct).ConfigureAwait(false);
                return Results.Json(new
                {
                    receipt = ProjectReceipt(result.Receipt),
                    impact = result.Impact,
                    replayed = result.Replayed,
                });
            }).ConfigureAwait(false));

        app.MapGet("/api/workspace/audit", async (
            string? cursor,
            int? limit,
            WorkspaceAccessStore store,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceAccessDocument document = await RequiredDocumentAsync(store, ct).ConfigureAwait(false);
                int requested = limit ?? 50;
                if (requested is < 1 or > 100)
                    return Validation("limit", "Use a value from 1 to 100.");
                int start = 0;
                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    if (!cursor.StartsWith("audit_", StringComparison.Ordinal))
                        return Validation("cursor", "The audit cursor is invalid.");
                    string eventId = cursor["audit_".Length..];
                    int index = document.AuditEvents.ToList().FindIndex(item => item.EventId == eventId);
                    if (index < 0)
                        return Validation("cursor", "The audit cursor is invalid.");
                    start = index + 1;
                }
                WorkspaceAuditEventRecord[] page = document.AuditEvents.Skip(start).Take(requested).ToArray();
                string? nextCursor = start + page.Length < document.AuditEvents.Count && page.Length != 0
                    ? $"audit_{page[^1].EventId}"
                    : null;
                return Results.Json(new
                {
                    items = page.Select(ProjectAudit),
                    nextCursor,
                    retentionLimit = WorkspaceAccessStore.MaxAuditEvents,
                });
            }).ConfigureAwait(false));

        app.MapPost("/api/workspace/recovery/after-restore", async (
            AfterRestoreHttpRequest request,
            WorkspaceAccessStore store,
            HttpContext context,
            CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                WorkspaceMutationResult<WorkspaceReceiptRecord> result = await store.RecoverAfterRestoreAsync(
                    new WorkspaceAfterRestoreRequest(
                        request.ExpectedWorkspaceVersion,
                        request.IdempotencyKey,
                        RequestId(context)),
                    ct).ConfigureAwait(false);
                return Results.Json(new { receipt = ProjectReceipt(result.Value), replayed = result.Replayed });
            }).ConfigureAwait(false));
    }

    private static object ProjectMe(WorkspaceRequestPrincipal principal, bool recoveryConfigured)
    {
        if (principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer)
        {
            return new
            {
                authenticated = true,
                via = "recovery-bearer",
                identity = (object?)null,
                membership = new { state = "machine", memberId = (string?)null, role = "owner" },
                source = "live",
            };
        }

        string state = principal.Kind switch
        {
            WorkspaceRequestPrincipalKind.BootstrapRequired => "bootstrap-required",
            WorkspaceRequestPrincipalKind.UnknownOidc => "none",
            WorkspaceRequestPrincipalKind.SuspendedOidc => "suspended",
            WorkspaceRequestPrincipalKind.OffboardedOidc => "offboarded",
            WorkspaceRequestPrincipalKind.Owner or WorkspaceRequestPrincipalKind.Family => "active",
            _ => "none",
        };
        return new
        {
            authenticated = true,
            via = "oidc",
            identity = new
            {
                displayName = principal.Presentation?.DisplayName ?? IdentityPresentation.FallbackDisplayName,
                email = principal.Presentation?.Email,
            },
            membership = new
            {
                state,
                memberId = principal.Member?.MemberId,
                role = principal.Member is null ? null : EnumText(principal.Member.Role),
                reason = principal.Kind == WorkspaceRequestPrincipalKind.BootstrapRequired && !recoveryConfigured
                    ? "recovery-proof-not-configured"
                    : null,
            },
            source = "live",
        };
    }

    private static object ProjectWorkspace(
        WorkspaceAccessDocument document,
        WorkspaceRequestPrincipal principal,
        IReadOnlyList<KnownDeviceRecord> devices)
    {
        bool full = principal.IsOwnerEquivalent;
        WorkspaceMemberRecord? currentMember = principal.Member;
        object[] members = full
            ? document.Members.Select(ProjectMember).ToArray()
            : [];
        object[] invitations = full
            ? document.Invitations.Select(ProjectInvitation).ToArray()
            : [];
        object[] household = document.Members
            .Where(item => item.Status == WorkspaceMemberStatus.Active)
            .Select(item => (object)new
            {
                displayName = item.DisplayName,
                role = EnumText(item.Role),
                deviceCount = devices.Count(device =>
                    string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal) &&
                    string.Equals(device.OwnerMemberId, item.MemberId, StringComparison.Ordinal)),
            })
            .ToArray();
        Dictionary<string, object> capabilities = CapabilitiesFor(principal);
        return new
        {
            schemaVersion = 2,
            workspaceId = document.Workspace.WorkspaceId,
            name = document.Workspace.Name,
            roleEnforcement = "server",
            currentMember = currentMember is null ? null : ProjectMember(currentMember),
            currentActor = principal.Kind == WorkspaceRequestPrincipalKind.RecoveryBearer
                ? new { kind = "recovery-bearer", displayName = "Recovery access" }
                : null,
            members,
            household,
            invitations,
            roles = new[]
            {
                new { id = "owner", label = "Owner" },
                new { id = "family", label = "Family" },
            },
            capabilities,
            restoreReviewRequired = document.Workspace.RestoreReviewRequired,
            version = $"workspace-version_{document.Workspace.Version}",
            source = "live",
        };
    }

    private static Dictionary<string, object> CapabilitiesFor(WorkspaceRequestPrincipal principal)
    {
        bool owner = principal.IsOwnerEquivalent;
        static object Capability(bool allowed, string scope, string? reason = null) => new { allowed, scope, reason };
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["workspace.read"] = Capability(true, owner ? "all" : "self"),
            ["members.invite"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
            ["members.manage"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
            ["audit.read"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
            ["apple.signer.manage"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
            ["devices.read"] = Capability(true, owner ? "all" : "own"),
            ["devices.enroll"] = Capability(true, owner ? "all" : "self"),
            ["catalog.read"] = Capability(true, "shared"),
            ["catalog.import"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
            ["operations.run"] = Capability(true, owner ? "all" : "own"),
            ["scheduler.manage"] = Capability(owner, owner ? "all" : "none", owner ? null : "Owner only."),
        };
    }

    private static object ProjectInvitationHandoff(
        WorkspaceAccessDocument document,
        WorkspaceHandoffResolution resolution,
        WorkspaceRequestPrincipal principal)
    {
        WorkspaceInvitationRecord invitation = resolution.Invitation!;
        WorkspaceMemberRecord? inviter = invitation.CreatedByActor.MemberId is null
            ? null
            : document.Members.FirstOrDefault(item => item.MemberId == invitation.CreatedByActor.MemberId);
        return new
        {
            workspace = new { name = document.Workspace.Name },
            account = new
            {
                displayName = principal.Presentation!.DisplayName,
                email = principal.Presentation.Email,
            },
            invitation = new
            {
                invitedName = invitation.DisplayName,
                inviter = inviter?.DisplayName ?? "Sideport Owner",
                role = "family",
                permissions = new[] { "Choose approved apps", "Use your own iPhone", "Receive home Wi-Fi refreshes" },
                status = EnumText(invitation.Status),
                expiresAt = invitation.ExpiresAt,
            },
            accepted = resolution.Receipt is null ? null : ProjectReceipt(resolution.Receipt),
        };
    }

    private static object ProjectOwnerClaimHandoff(
        WorkspaceHandoffResolution resolution,
        WorkspaceRequestPrincipal principal,
        WorkspaceImpactSnapshot? impact)
    {
        WorkspaceOwnerClaimRecord claim = resolution.OwnerClaim!;
        return new
        {
            account = new
            {
                displayName = principal.Presentation!.DisplayName,
                email = principal.Presentation.Email,
            },
            claim = new
            {
                kind = EnumText(claim.Kind),
                status = EnumText(claim.Status),
                expiresAt = claim.ExpiresAt,
                impact = impact is null ? null : ProjectImpact(impact),
            },
            accepted = resolution.Receipt is null ? null : ProjectReceipt(resolution.Receipt),
        };
    }

    private static object ProjectMember(WorkspaceMemberRecord member) => new
    {
        memberId = member.MemberId,
        displayName = member.DisplayName,
        email = member.Email,
        role = EnumText(member.Role),
        status = EnumText(member.Status),
        joinedAt = member.JoinedAt,
        lastActiveAt = member.LastActiveAt,
        version = member.Version,
        source = "live",
    };

    private static object ProjectInvitation(WorkspaceInvitationRecord invitation) => new
    {
        invitationId = invitation.InvitationId,
        displayName = invitation.DisplayName,
        contactEmail = invitation.ContactEmail,
        role = "family",
        status = EnumText(invitation.Status),
        createdAt = invitation.CreatedAt,
        expiresAt = invitation.ExpiresAt,
        createdByActor = ProjectActor(invitation.CreatedByActor),
        acceptedAt = invitation.AcceptedAt,
        acceptedMemberId = invitation.AcceptedMemberId,
        version = invitation.Version,
    };

    private static object ProjectOwnerClaim(WorkspaceOwnerClaimRecord claim) => new
    {
        claimId = claim.ClaimId,
        kind = EnumText(claim.Kind),
        status = EnumText(claim.Status),
        createdAt = claim.CreatedAt,
        expiresAt = claim.ExpiresAt,
        expectedOwnerMemberId = claim.ExpectedOwnerMemberId,
        acceptedAt = claim.AcceptedAt,
        version = claim.Version,
    };

    private static object ProjectAcceptance(WorkspaceAcceptanceResult result) => new
    {
        member = ProjectMember(result.Member),
        receipt = ProjectReceipt(result.Receipt),
        replayed = result.Replayed,
    };

    private static object ProjectReceipt(WorkspaceReceiptRecord receipt) => new
    {
        receiptId = receipt.ReceiptId,
        kind = EnumText(receipt.Kind),
        targetId = receipt.TargetId,
        outcome = receipt.Outcome,
        recordedAt = receipt.RecordedAt,
        workspaceVersion = receipt.WorkspaceVersion,
        memberId = receipt.MemberId,
        previousOwnerMemberId = receipt.PreviousOwnerMemberId,
    };

    private static object ProjectAudit(WorkspaceAuditEventRecord item) => new
    {
        eventId = item.EventId,
        action = EnumText(item.Action),
        outcome = item.Outcome,
        actor = ProjectActor(item.Actor),
        target = new { type = EnumText(item.TargetType), id = item.TargetId },
        occurredAt = item.OccurredAt,
        requestId = item.RequestId,
        correlationId = item.CorrelationId,
        impact = item.Impact,
    };

    private static object ProjectActor(WorkspaceActorRecord actor) => new
    {
        kind = EnumText(actor.Kind),
        memberId = actor.MemberId,
    };

    private static object ProjectImpact(WorkspaceImpactSnapshot impact) => new
    {
        memberId = impact.TargetMemberId,
        memberVersion = impact.TargetMemberVersion,
        memberCount = impact.MemberCount,
        ownedDeviceCount = impact.OwnedDeviceCount,
        unassignedDeviceCount = impact.UnassignedDeviceCount,
        registrationCount = impact.RegistrationCount,
        queuedOperationCount = impact.QueuedOperationCount,
        runningOperationCount = impact.RunningOperationCount,
        schedulerEffectCount = impact.SchedulerEffectCount,
        impactVersion = impact.ImpactVersion,
        expiresAt = impact.ExpiresAt,
    };

    private static IResult OwnerReplacementConfirmation(WorkspaceImpactSnapshot impact) =>
        Results.Json(new
        {
            error = "owner-replacement-confirmation-required",
            message = "Review the current Owner and affected Sideport items before replacing access.",
            impact = ProjectImpact(impact),
        }, statusCode: StatusCodes.Status409Conflict);

    private static IResult LinkRateLimited(
        HttpContext context,
        string error,
        TimeSpan retryAfter)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Results.Json(new
        {
            error,
            message = "Wait a moment, then open the private link again.",
            retryAfterSeconds = seconds,
        }, statusCode: StatusCodes.Status429TooManyRequests);
    }

    private static WorkspaceAcceptanceRequest AcceptanceRequest(
        WorkspaceRequestPrincipal principal,
        string idempotencyKey,
        HttpContext context,
        string? currentImpactVersion = null) =>
        new(
            principal.Identity!,
            principal.Presentation!.DisplayName,
            principal.Presentation.Email,
            idempotencyKey,
            RequestId(context),
            CurrentImpactVersion: currentImpactVersion);

    private static WorkspaceRequestPrincipal RequireOidcPrincipal(HttpContext context)
    {
        WorkspaceRequestPrincipal principal = WorkspaceApiSecurity.PrincipalFrom(context);
        if (!principal.IsOidc || principal.Identity is null || principal.Presentation is null)
            throw new WorkspaceAccessException("capability-denied", "A signed-in person is required.");
        return principal;
    }

    private static async Task<WorkspaceAccessDocument> RequiredDocumentAsync(
        WorkspaceAccessStore store,
        CancellationToken ct) =>
        await store.ReadAsync(ct).ConfigureAwait(false)
            ?? throw new WorkspaceAccessException("workspace-bootstrap-required", "Finish Owner setup first.");

    private static string RequireCookie(HttpContext context, string name, string error)
    {
        if (!context.Request.Cookies.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            throw new WorkspaceAccessException(error, "The private link is unavailable.");
        return value;
    }

    private static void SetHandoffCookie(
        HttpContext context,
        string name,
        string value,
        DateTimeOffset expiresAt) =>
        context.Response.Cookies.Append(name, value, HandoffCookie(expiresAt));

    private static void DeleteHandoffCookie(HttpContext context, string name) =>
        context.Response.Cookies.Delete(name, HandoffCookie(DateTimeOffset.UnixEpoch));

    private static CookieOptions HandoffCookie(DateTimeOffset expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expiresAt,
        IsEssential = true,
    };

    private static async Task RefreshSessionEpochAsync(HttpContext context, string securityEpoch)
    {
        IAuthenticationSchemeProvider schemes = context.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        if (await schemes.GetSchemeAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false) is null)
            return;

        Claim[] claims = context.User.Claims
            .Where(item => item.Type != WorkspaceRequestPrincipalResolver.SecurityEpochClaimType)
            .Append(new Claim(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType, securityEpoch))
            .ToArray();
        var currentIdentity = context.User.Identity as ClaimsIdentity;
        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            currentIdentity?.NameClaimType ?? ClaimTypes.Name,
            currentIdentity?.RoleClaimType ?? ClaimTypes.Role);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = false }).ConfigureAwait(false);
    }

    private static string BuildShareUrl(Uri publicOrigin, string path, string token)
    {
        var builder = new UriBuilder(new Uri(publicOrigin, path)) { Fragment = token };
        return builder.Uri.AbsoluteUri;
    }

    private static string RequestId(HttpContext context)
    {
        string value = context.TraceIdentifier;
        if (value.Length is > 0 and <= 128 && value.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':'))
        {
            return value;
        }
        return $"request_{Guid.NewGuid():N}";
    }

    private static string EnumText<T>(T value) where T : struct, Enum
    {
        string name = value.ToString();
        var result = new System.Text.StringBuilder(name.Length + 4);
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (index > 0 && char.IsUpper(character))
                result.Append('-');
            result.Append(char.ToLowerInvariant(character));
        }
        return result.ToString();
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (WorkspaceAccessException error)
        {
            return WorkspaceError(error);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { error = "validation-failed", message = "The request is invalid." });
        }
        catch (Exception error) when (error is KnownDeviceStoreException or OperationStoreException or
                                      IOException or UnauthorizedAccessException)
        {
            return Results.Json(new
            {
                error = "workspace-store-unavailable",
                message = "Workspace access is temporarily unavailable.",
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static IResult WorkspaceError(WorkspaceAccessException error)
    {
        int statusCode = error.Code switch
        {
            "unauthorized" => StatusCodes.Status401Unauthorized,
            "workspace-bootstrap-required" or "workspace-membership-required" or
                "member-access-disabled" or "capability-denied" or
                "recovery-proof-invalid" => StatusCodes.Status403Forbidden,
            "resource-not-found" or "invitation-unavailable" or "owner-claim-unavailable" => StatusCodes.Status404NotFound,
            "invitation-expired" or "invitation-revoked" or "invitation-already-used" => StatusCodes.Status410Gone,
            "invitation-rate-limited" or "owner-claim-rate-limited" => StatusCodes.Status429TooManyRequests,
            "workspace-store-unavailable" or "workspace-security-history-full" => StatusCodes.Status503ServiceUnavailable,
            "owner-action-required" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status409Conflict,
        };
        return Results.Json(new { error = error.Code, message = error.Message }, statusCode: statusCode);
    }

    private static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    internal sealed record OwnerClaimCreateHttpRequest(
        string? ExpectedOwnerMemberId,
        string? ImpactVersion,
        bool ConfirmReplacement,
        int? ExpiresInMinutes,
        string IdempotencyKey);

    internal sealed record InvitationCreateHttpRequest(
        string? DisplayName,
        string ContactEmail,
        double? ExpiresInDays,
        string IdempotencyKey);

    internal sealed record AuthorityRevokeHttpRequest(long ExpectedVersion, string IdempotencyKey);
    internal sealed record OwnerClaimHandoffHttpRequest(string ClaimToken);
    internal sealed record InvitationHandoffHttpRequest(string InvitationToken);
    internal sealed record AuthentikEnrollmentHttpRequest(string IdempotencyKey);
    internal sealed record AuthorityAcceptHttpRequest(string IdempotencyKey);
    internal sealed record MemberStatusHttpRequest(
        string Status,
        long ExpectedVersion,
        string Reason,
        string IdempotencyKey);
    internal sealed record MemberOffboardHttpRequest(
        long ExpectedVersion,
        string? ImpactVersion,
        string Reason,
        string IdempotencyKey);
    internal sealed record AfterRestoreHttpRequest(long ExpectedWorkspaceVersion, string IdempotencyKey);
}
