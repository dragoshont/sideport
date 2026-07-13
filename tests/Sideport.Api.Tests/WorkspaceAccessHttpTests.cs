using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sideport.Api.Catalog;
using Sideport.Api.DeviceInventory;
using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Orchestrator;

namespace Sideport.Api.Tests;

/// <summary>
/// Exercises the family-access boundary through the real Program pipeline.
/// The header authentication scheme stands in only for a validated OIDC ticket:
/// it emits Sideport's internal validated-issuer claim and the current durable
/// workspace epoch. Authority, CSRF, handoff, projection, and persistence all
/// remain production implementations.
/// </summary>
public sealed class WorkspaceAccessHttpTests
{
    private static readonly TestIdentity Owner = new(
        "https://identity.example/application/o/sideport/",
        "owner-subject",
        "Home Owner",
        "owner@example.test");

    private static readonly TestIdentity Family = new(
        "https://identity.example/application/o/sideport/",
        "family-subject",
        "Family Person",
        "family@example.test");

    [Fact]
    public async Task AuthenticationOptions_ProjectConfiguredOidcPresentationWithoutClaimingGenericEnrollment()
    {
        using var app = new WorkspaceTestApp(settings: new Dictionary<string, string?>
        {
            ["Sideport:Oidc:ProviderId"] = "company-sso",
            ["Sideport:Oidc:ProviderLabel"] = "Company account",
            ["Sideport:Oidc:LoginLabel"] = "Continue with Company SSO",
        });
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/authentication/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonObject body = await JsonAsync(response);
        Assert.Equal("company-sso", String(body, "provider"));
        Assert.Equal("Company account", String(body, "providerLabel"));
        Assert.Equal("Continue with Company SSO", String(body, "loginLabel"));
        Assert.True(Bool(body, "oidcEnabled"));
        Assert.False(Bool(body, "enrollmentEnabled"));
        Assert.Null(body["passkeyOwner"]);
    }

    [Fact]
    public async Task OwnerBootstrap_UsesRecoveryBearer_OneTimeLinkAndExplicitOidcConsent()
    {
        using var app = new WorkspaceTestApp();
        using HttpClient recovery = app.CreateRecoveryClient();
        object initialRequest = new
        {
            expectedOwnerMemberId = (string?)null,
            impactVersion = (string?)null,
            confirmReplacement = false,
            expiresInMinutes = 15,
            idempotencyKey = "owner-claim-create-0001",
        };

        HttpResponseMessage created = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            initialRequest);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        AssertNoStore(created);
        JsonObject createdBody = await JsonAsync(created);
        string firstClaimId = String(createdBody["claim"], "claimId");
        long firstClaimVersion = Long(createdBody["claim"], "version");
        string firstClaimToken = FragmentToken(String(createdBody, "shareUrl"));
        Assert.StartsWith("spown1_", firstClaimToken, StringComparison.Ordinal);

        HttpResponseMessage createReplay = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            initialRequest);
        Assert.Equal(HttpStatusCode.OK, createReplay.StatusCode);
        JsonObject replayBody = await JsonAsync(createReplay);
        Assert.Equal(firstClaimId, String(replayBody["claim"], "claimId"));
        Assert.False(Bool(replayBody, "linkAvailable"));
        Assert.Null(replayBody["shareUrl"]);

        HttpResponseMessage revoked = await recovery.PostAsJsonAsync(
            $"/api/workspace/owner-claims/{firstClaimId}/revoke",
            new { expectedVersion = firstClaimVersion, idempotencyKey = "owner-claim-revoke-0001" }); // gitleaks:allow test fixture
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal("revoked", String((await JsonAsync(revoked))["claim"], "status"));

        using HttpClient revokedLinkClient = app.CreateClient();
        HttpResponseMessage revokedLink = await SendJsonAsync(
            revokedLinkClient,
            HttpMethod.Post,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = firstClaimToken },
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(revokedLink, HttpStatusCode.NotFound, "owner-claim-unavailable");

        HttpResponseMessage replacement = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            new
            {
                expectedOwnerMemberId = (string?)null,
                impactVersion = (string?)null,
                confirmReplacement = false,
                expiresInMinutes = 15,
                idempotencyKey = "owner-claim-create-0002",
            });
        Assert.Equal(HttpStatusCode.Created, replacement.StatusCode);
        string ownerToken = FragmentToken(String(await JsonAsync(replacement), "shareUrl"));

        using HttpClient insecureClient = app.CreateClient(new Uri("http://sideport.test"));
        HttpResponseMessage insecureExchange = await SendJsonAsync(
            insecureClient,
            HttpMethod.Post,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = ownerToken },
            origin: "http://sideport.test");
        Assert.Equal(HttpStatusCode.Forbidden, insecureExchange.StatusCode);
        Assert.DoesNotContain(ownerToken, await insecureExchange.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using HttpClient browser = app.CreateClient();
        HttpResponseMessage missingOwnerHandoff = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/enrollment",
            new { idempotencyKey = "owner-passkey-no-handoff-0001" }, // gitleaks:allow test fixture
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(missingOwnerHandoff, HttpStatusCode.NotFound, "owner-claim-unavailable");

        HttpResponseMessage exchanged = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/handoff",
            new { claimToken = ownerToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        AssertNoStore(exchanged);
        string exchangeBody = await exchanged.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ownerToken, exchangeBody, StringComparison.Ordinal);
        Assert.DoesNotContain("spown1_", exchangeBody, StringComparison.Ordinal);
        string handoffCookie = AssertHandoffCookie(exchanged, "__Host-sideport.owner-claim-handoff");
        Assert.DoesNotContain(ownerToken, handoffCookie, StringComparison.Ordinal);

        HttpResponseMessage ownerEnrollment = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/enrollment",
            new { idempotencyKey = "owner-passkey-enrollment-0001" }, // gitleaks:allow test fixture
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, ownerEnrollment.StatusCode);
        JsonObject ownerEnrollmentBody = await JsonAsync(ownerEnrollment);
        Assert.False(Bool(ownerEnrollmentBody, "available"));
        Assert.Null(ownerEnrollmentBody["enrollmentUrl"]);
        Assert.Equal("/login?returnUrl=%2Fowner-claim", String(ownerEnrollmentBody, "existingAccountUrl"));

        app.SetIdentity(browser, Owner);
        MeResult beforeAcceptance = await GetMeAsync(browser);
        Assert.Equal("bootstrap-required", String(beforeAcceptance.Body["membership"], "state"));
        Assert.Equal("oidc", String(beforeAcceptance.Body, "via"));

        HttpResponseMessage preview = await browser.GetAsync("/api/workspace/owner-claims/handoff");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        JsonObject previewBody = await JsonAsync(preview);
        Assert.Equal(Owner.DisplayName, String(previewBody["account"], "displayName"));
        Assert.Equal("bootstrap", String(previewBody["claim"], "kind"));
        Assert.Null(previewBody["accepted"]);

        HttpResponseMessage accepted = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/accept",
            new { idempotencyKey = "owner-claim-accept-0001" }, // gitleaks:allow test fixture
            beforeAcceptance.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        JsonObject acceptedBody = await JsonAsync(accepted);
        Assert.False(Bool(acceptedBody, "replayed"));
        Assert.Equal("owner", String(acceptedBody["member"], "role"));
        Assert.Equal(Owner.Email, String(acceptedBody["member"], "email"));

        MeResult afterAcceptance = await GetMeAsync(browser);
        Assert.Equal("active", String(afterAcceptance.Body["membership"], "state"));
        Assert.Equal("owner", String(afterAcceptance.Body["membership"], "role"));
        HttpResponseMessage workspace = await browser.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, workspace.StatusCode);
        Assert.Single((await JsonAsync(workspace))["members"]!.AsArray());
    }

    [Fact]
    public async Task Invitation_CreateAcceptAndSameIdentityReplay_NeverEchoesLinkAuthority()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        object createRequest = new
        {
            displayName = "Family Person",
            contactEmail = "delivery-only@example.test",
            expiresInDays = 7,
            idempotencyKey = "invitation-create-0001",
        };

        HttpResponseMessage created = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            "/api/workspace/invitations",
            createRequest,
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        JsonObject createdBody = await JsonAsync(created);
        string invitationId = String(createdBody["invitation"], "invitationId");
        string invitationToken = FragmentToken(String(createdBody, "shareUrl"));

        HttpResponseMessage replayedCreate = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            "/api/workspace/invitations",
            createRequest,
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, replayedCreate.StatusCode);
        JsonObject replayedCreateBody = await JsonAsync(replayedCreate);
        Assert.Equal(invitationId, String(replayedCreateBody["invitation"], "invitationId"));
        Assert.False(Bool(replayedCreateBody, "linkAvailable"));
        Assert.Null(replayedCreateBody["shareUrl"]);

        using HttpClient familyBrowser = app.CreateClient();
        HttpResponseMessage exchange = await SendJsonAsync(
            familyBrowser,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        AssertNoStore(exchange);
        string exchangeJson = await exchange.Content.ReadAsStringAsync();
        Assert.DoesNotContain(invitationToken, exchangeJson, StringComparison.Ordinal);
        Assert.DoesNotContain("spinv1_", exchangeJson, StringComparison.Ordinal);
        string handoffCookie = AssertHandoffCookie(exchange, "__Host-sideport.invitation-handoff");

        app.SetIdentity(familyBrowser, Family);
        MeResult unknown = await GetMeAsync(familyBrowser);
        Assert.Equal("none", String(unknown.Body["membership"], "state"));
        Assert.False(string.IsNullOrWhiteSpace(unknown.Csrf));

        HttpResponseMessage preview = await familyBrowser.GetAsync("/api/workspace/invitations/handoff");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        JsonObject previewBody = await JsonAsync(preview);
        Assert.Equal(Family.DisplayName, String(previewBody["account"], "displayName"));
        Assert.Equal("family", String(previewBody["invitation"], "role"));
        Assert.Equal(3, previewBody["invitation"]!["permissions"]!.AsArray().Count);
        Assert.DoesNotContain(invitationToken, previewBody.ToJsonString(), StringComparison.Ordinal);

        HttpResponseMessage accepted = await SendJsonAsync(
            familyBrowser,
            HttpMethod.Post,
            "/api/workspace/invitations/accept",
            new { idempotencyKey = "invitation-accept-0001" },
            unknown.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        JsonObject acceptedBody = await JsonAsync(accepted);
        Assert.False(Bool(acceptedBody, "replayed"));
        string receiptId = String(acceptedBody["receipt"], "receiptId");
        Assert.Equal("family", String(acceptedBody["member"], "role"));
        Assert.DoesNotContain(invitationToken, acceptedBody.ToJsonString(), StringComparison.Ordinal);

        using HttpClient replayBrowser = app.CreateClient(handleCookies: false);
        app.SetIdentity(replayBrowser, Family);
        HttpResponseMessage replayMe = await replayBrowser.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, replayMe.StatusCode);
        string replayCsrf = Assert.Single(replayMe.Headers.GetValues("X-Sideport-CSRF"));
        string csrfCookie = CookiePair(replayMe, "sideport.csrf");
        HttpResponseMessage acceptedReplay = await SendJsonAsync(
            replayBrowser,
            HttpMethod.Post,
            "/api/workspace/invitations/accept",
            new { idempotencyKey = "invitation-accept-retry-0001" },
            replayCsrf,
            WorkspaceTestApp.Origin,
            $"{handoffCookie}; {csrfCookie}");
        Assert.Equal(HttpStatusCode.OK, acceptedReplay.StatusCode);
        JsonObject acceptedReplayBody = await JsonAsync(acceptedReplay);
        Assert.True(Bool(acceptedReplayBody, "replayed"));
        Assert.Equal(receiptId, String(acceptedReplayBody["receipt"], "receiptId"));

        using HttpClient reusedLink = app.CreateClient();
        HttpResponseMessage rawReplay = await SendJsonAsync(
            reusedLink,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken },
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(rawReplay, HttpStatusCode.Gone, "invitation-already-used");
    }

    [Fact]
    public async Task InvitationEnrollment_RequiresOpaqueHandoffAndDoesNotGrantMembership()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        HttpResponseMessage invitation = await CreateInvitationAsync(
            owner,
            "New Person",
            "new-person@example.test",
            7,
            "enrollment-invitation-0001");
        string invitationToken = FragmentToken(String(await JsonAsync(invitation), "shareUrl"));

        using HttpClient browser = app.CreateClient();
        HttpResponseMessage missingHandoff = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/invitations/enrollment",
            new { idempotencyKey = "enrollment-missing-0001" }, // gitleaks:allow test fixture
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(missingHandoff, HttpStatusCode.NotFound, "invitation-unavailable");

        HttpResponseMessage exchange = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);

        HttpResponseMessage enrollment = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/invitations/enrollment",
            new { idempotencyKey = "enrollment-disabled-0001" },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, enrollment.StatusCode);
        JsonObject body = await JsonAsync(enrollment);
        Assert.False(Bool(body, "available"));
        Assert.Null(body["enrollmentUrl"]);
        Assert.Equal("/login?returnUrl=%2Finvite", String(body, "existingAccountUrl"));

        WorkspaceAccessDocument document = (await app.Store.ReadAsync())!;
        Assert.Single(document.Members);
        Assert.Single(document.Invitations, item => item.Status == WorkspaceAuthorityStatus.Pending);
    }

    [Fact]
    public async Task OwnerRecovery_LostResponseAcceptanceReplaysWithoutRecheckingImpact()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        using HttpClient recovery = app.CreateRecoveryClient();
        HttpResponseMessage preflight = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            new
            {
                expectedOwnerMemberId = (string?)null,
                impactVersion = (string?)null,
                confirmReplacement = false,
                expiresInMinutes = 15,
                idempotencyKey = "owner-recovery-preflight-0001",
            });
        Assert.Equal(HttpStatusCode.Conflict, preflight.StatusCode);
        JsonObject preflightBody = await JsonAsync(preflight);
        string impactVersion = String(preflightBody["impact"], "impactVersion");

        HttpResponseMessage claim = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            new
            {
                expectedOwnerMemberId = owner.MemberId,
                impactVersion,
                confirmReplacement = true,
                expiresInMinutes = 15,
                idempotencyKey = "owner-recovery-create-0001",
            });
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);
        string claimToken = FragmentToken(String(await JsonAsync(claim), "shareUrl"));

        using HttpClient browser = app.CreateClient(handleCookies: false);
        HttpResponseMessage exchange = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/handoff",
            new { claimToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        string handoffCookie = AssertHandoffCookie(exchange, "__Host-sideport.owner-claim-handoff");
        app.SetIdentity(browser, Family);
        HttpResponseMessage me = await browser.GetAsync("/api/me");
        string csrf = Assert.Single(me.Headers.GetValues("X-Sideport-CSRF"));
        string csrfCookie = CookiePair(me, "sideport.csrf");

        HttpResponseMessage accepted = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/accept",
            new { idempotencyKey = "owner-recovery-accept-0001" },
            csrf,
            WorkspaceTestApp.Origin,
            $"{handoffCookie}; {csrfCookie}");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        string receiptId = String((await JsonAsync(accepted))["receipt"], "receiptId");

        app.Time.Advance(TimeSpan.FromMinutes(11));
        HttpResponseMessage replay = await SendJsonAsync(
            browser,
            HttpMethod.Post,
            "/api/workspace/owner-claims/accept",
            new { idempotencyKey = "owner-recovery-accept-retry-0001" },
            csrf,
            WorkspaceTestApp.Origin,
            $"{handoffCookie}; {csrfCookie}");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        JsonObject replayBody = await JsonAsync(replay);
        Assert.True(Bool(replayBody, "replayed"));
        Assert.Equal(receiptId, String(replayBody["receipt"], "receiptId"));
    }

    [Fact]
    public async Task Invitation_RevocationAndExpiry_AreEnforcedAtPublicExchange()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);

        HttpResponseMessage revokeCandidate = await CreateInvitationAsync(
            owner,
            "Revoked Person",
            "revoked@example.test",
            7,
            "invitation-revoke-create-0001");
        JsonObject revokeCandidateBody = await JsonAsync(revokeCandidate);
        string revokeId = String(revokeCandidateBody["invitation"], "invitationId");
        long revokeVersion = Long(revokeCandidateBody["invitation"], "version");
        string revokedToken = FragmentToken(String(revokeCandidateBody, "shareUrl"));
        HttpResponseMessage revoke = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/invitations/{revokeId}/revoke",
            new { expectedVersion = revokeVersion, idempotencyKey = "invitation-revoke-0001" },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        using HttpClient publicClient = app.CreateClient();
        HttpResponseMessage revokedExchange = await SendJsonAsync(
            publicClient,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken = revokedToken },
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(revokedExchange, HttpStatusCode.Gone, "invitation-revoked");

        HttpResponseMessage expiryCandidate = await CreateInvitationAsync(
            owner,
            "Expired Person",
            "expired@example.test",
            1d / 24d,
            "invitation-expire-create-0001");
        string expiredToken = FragmentToken(String(await JsonAsync(expiryCandidate), "shareUrl"));
        app.Time.Advance(TimeSpan.FromMinutes(61));

        HttpResponseMessage expiredExchange = await SendJsonAsync(
            publicClient,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken = expiredToken },
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(expiredExchange, HttpStatusCode.Gone, "invitation-expired");
    }

    [Fact]
    public async Task InvitationMinting_IsBoundedPerOwnerAndReturnsRetryAfter()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        for (int index = 0; index < 10; index++)
        {
            HttpResponseMessage allowed = await CreateInvitationAsync(
                owner,
                $"Family {index}",
                $"family-{index}@example.test",
                7,
                $"invitation-rate-allowed-{index:0000}");
            Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        }

        HttpResponseMessage limited = await CreateInvitationAsync(
            owner,
            "One Too Many",
            "family-limited@example.test",
            7,
            "invitation-rate-limited-0011");
        await AssertErrorAsync(limited, HttpStatusCode.TooManyRequests, "invitation-rate-limited");
        string retryAfter = Assert.Single(limited.Headers.GetValues("Retry-After"));
        Assert.True(int.TryParse(retryAfter, out int seconds) && seconds is >= 1 and <= 60);
    }

    [Fact]
    public async Task WorkspaceProjection_IsOwnerFullAndFamilyPrivate_AndSuspensionDeniesImmediately()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        OidcSession family = await AddFamilyAsync(app, owner);
        HttpResponseMessage pending = await CreateInvitationAsync(
            owner,
            "Private Delivery",
            "owner-private-delivery@example.test",
            7,
            "invitation-private-create-0001");
        Assert.Equal(HttpStatusCode.Created, pending.StatusCode);

        HttpResponseMessage ownerWorkspaceResponse = await owner.Client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, ownerWorkspaceResponse.StatusCode);
        JsonObject ownerWorkspace = await JsonAsync(ownerWorkspaceResponse);
        Assert.Equal(2, ownerWorkspace["members"]!.AsArray().Count);
        Assert.True(ownerWorkspace["invitations"]!.AsArray().Count >= 2);
        Assert.Contains(Owner.Email, ownerWorkspace.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains("owner-private-delivery@example.test", ownerWorkspace.ToJsonString(), StringComparison.Ordinal);
        Assert.True(Bool(ownerWorkspace["capabilities"]!["members.invite"], "allowed"));

        HttpResponseMessage familyWorkspaceResponse = await family.Client.GetAsync("/api/workspace");
        Assert.Equal(HttpStatusCode.OK, familyWorkspaceResponse.StatusCode);
        JsonObject familyWorkspace = await JsonAsync(familyWorkspaceResponse);
        Assert.Empty(familyWorkspace["members"]!.AsArray());
        Assert.Empty(familyWorkspace["invitations"]!.AsArray());
        Assert.Equal(2, familyWorkspace["household"]!.AsArray().Count);
        Assert.Equal(family.MemberId, String(familyWorkspace["currentMember"], "memberId"));
        Assert.Equal(Family.Email, String(familyWorkspace["currentMember"], "email"));
        Assert.DoesNotContain(Owner.Email, familyWorkspace.ToJsonString(), StringComparison.Ordinal);
        Assert.DoesNotContain("owner-private-delivery@example.test", familyWorkspace.ToJsonString(), StringComparison.Ordinal);
        Assert.False(Bool(familyWorkspace["capabilities"]!["members.invite"], "allowed"));

        HttpResponseMessage familyInviteAttempt = await SendJsonAsync(
            family.Client,
            HttpMethod.Post,
            "/api/workspace/invitations",
            new
            {
                displayName = "Not Allowed",
                contactEmail = "not-allowed@example.test",
                expiresInDays = 7,
                idempotencyKey = "family-cannot-invite-0001",
            },
            family.Csrf,
            WorkspaceTestApp.Origin);
        await AssertErrorAsync(familyInviteAttempt, HttpStatusCode.Forbidden, "capability-denied");

        HttpResponseMessage suspended = await SendJsonAsync(
            owner.Client,
            HttpMethod.Patch,
            $"/api/workspace/members/{family.MemberId}",
            new
            {
                status = "suspended",
                expectedVersion = family.MemberVersion,
                reason = "Pause access during this security test.",
                idempotencyKey = "family-suspend-0001", // gitleaks:allow test fixture
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, suspended.StatusCode);

        MeResult suspendedMe = await GetMeAsync(family.Client);
        Assert.Equal("suspended", String(suspendedMe.Body["membership"], "state"));
        HttpResponseMessage deniedWorkspace = await family.Client.GetAsync("/api/workspace");
        await AssertErrorAsync(deniedWorkspace, HttpStatusCode.Forbidden, "member-access-disabled");
    }

    [Fact]
    public async Task AllCookieMutations_RequireCsrfAndExactOrigin_WhileBearerIsExempt()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);

        using HttpClient noCsrfOwner = app.CreateOidcClient(Owner);
        HttpResponseMessage missingCsrf = await SendJsonAsync(
            noCsrfOwner,
            HttpMethod.Post,
            "/api/devices/known",
            new { udid = "CSRF-MISSING-DEVICE" },
            csrf: null,
            origin: WorkspaceTestApp.Origin);
        await AssertErrorAsync(missingCsrf, HttpStatusCode.Forbidden, "origin-or-antiforgery");

        HttpResponseMessage wrongOrigin = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            "/api/devices/known",
            new { udid = "WRONG-ORIGIN-DEVICE" },
            owner.Csrf,
            origin: "https://attacker.example");
        await AssertErrorAsync(wrongOrigin, HttpStatusCode.Forbidden, "origin-or-antiforgery");

        HttpResponseMessage sameOrigin = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            "/api/devices/known",
            new { udid = "OIDC-PROTECTED-DEVICE" },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.Created, sameOrigin.StatusCode);

        using HttpClient recovery = app.CreateRecoveryClient();
        HttpResponseMessage bearerWithoutCsrf = await recovery.PostAsJsonAsync(
            "/api/devices/known",
            new { udid = "BEARER-CSRF-EXEMPT-DEVICE" });
        Assert.Equal(HttpStatusCode.Created, bearerWithoutCsrf.StatusCode);
    }

    [Fact]
    public async Task OffboardingRestoreAndAudit_AreDurableAndBearerRecoveryRotatesEpoch()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        OidcSession family = await AddFamilyAsync(app, owner);

        HttpResponseMessage suspend = await SendJsonAsync(
            owner.Client,
            HttpMethod.Patch,
            $"/api/workspace/members/{family.MemberId}",
            new
            {
                status = "suspended",
                expectedVersion = family.MemberVersion,
                reason = "Prepare deliberate offboarding.",
                idempotencyKey = "offboard-suspend-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        JsonObject suspendBody = await JsonAsync(suspend);
        long suspendedVersion = Long(suspendBody["member"], "version");

        HttpResponseMessage preflight = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/members/{family.MemberId}/offboard",
            new
            {
                expectedVersion = suspendedVersion,
                impactVersion = (string?)null,
                reason = "Remove portal access while retaining phone and app evidence.",
                idempotencyKey = "offboard-preflight-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.Conflict, preflight.StatusCode);
        JsonObject preflightBody = await JsonAsync(preflight);
        Assert.Equal("offboarding-confirmation-required", String(preflightBody, "error"));
        string impactVersion = String(preflightBody["impact"], "impactVersion");

        HttpResponseMessage offboard = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/members/{family.MemberId}/offboard",
            new
            {
                expectedVersion = suspendedVersion,
                impactVersion,
                reason = "Remove portal access while retaining phone and app evidence.",
                idempotencyKey = "offboard-confirm-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.True(
            offboard.StatusCode == HttpStatusCode.OK,
            $"Expected offboarding confirmation to succeed, got {(int)offboard.StatusCode}: " +
            await offboard.Content.ReadAsStringAsync());
        Assert.Equal("member-offboarded", String((await JsonAsync(offboard))["receipt"], "kind"));

        HttpResponseMessage lostResponseReplay = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/members/{family.MemberId}/offboard",
            new
            {
                expectedVersion = suspendedVersion,
                impactVersion,
                reason = "Remove portal access while retaining phone and app evidence.",
                idempotencyKey = "offboard-confirm-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, lostResponseReplay.StatusCode);
        JsonObject replayBody = await JsonAsync(lostResponseReplay);
        Assert.True(Bool(replayBody, "replayed"));
        Assert.Equal(
            String((await JsonAsync(offboard))["receipt"], "receiptId"),
            String(replayBody["receipt"], "receiptId"));
        Assert.Equal(impactVersion, String(replayBody["impact"], "impactVersion"));

        MeResult offboardedMe = await GetMeAsync(family.Client);
        Assert.Equal("offboarded", String(offboardedMe.Body["membership"], "state"));
        HttpResponseMessage familyDenied = await family.Client.GetAsync("/api/workspace");
        await AssertErrorAsync(familyDenied, HttpStatusCode.Forbidden, "member-access-disabled");

        HttpResponseMessage pendingInvitation = await CreateInvitationAsync(
            owner,
            "Restore Check",
            "restore-private@example.test",
            7,
            "restore-pending-invitation-0001");
        JsonObject pendingInvitationBody = await JsonAsync(pendingInvitation);
        string pendingInvitationId = String(pendingInvitationBody["invitation"], "invitationId");
        string pendingInvitationToken = FragmentToken(String(pendingInvitationBody, "shareUrl"));

        WorkspaceAccessDocument beforeRestore = (await app.Store.ReadAsync())!;
        using HttpClient recovery = app.CreateRecoveryClient();
        HttpResponseMessage restore = await recovery.PostAsJsonAsync(
            "/api/workspace/recovery/after-restore",
            new
            {
                expectedWorkspaceVersion = beforeRestore.Workspace.Version,
                idempotencyKey = "after-restore-recovery-0001",
            });
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        Assert.Equal("restore-recovery", String((await JsonAsync(restore))["receipt"], "kind"));

        WorkspaceAccessDocument afterRestore = (await app.Store.ReadAsync())!;
        Assert.NotEqual(beforeRestore.Workspace.SecurityEpoch, afterRestore.Workspace.SecurityEpoch);
        Assert.True(afterRestore.Workspace.RestoreReviewRequired);
        WorkspaceInvitationRecord revokedAfterRestore = Assert.Single(
            afterRestore.Invitations,
            item => item.InvitationId == pendingInvitationId);
        Assert.Equal(WorkspaceAuthorityStatus.Revoked, revokedAfterRestore.Status);
        Assert.Null(revokedAfterRestore.ContactEmail);

        HttpResponseMessage auditResponse = await owner.Client.GetAsync("/api/workspace/audit?limit=100");
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);
        JsonObject audit = await JsonAsync(auditResponse);
        string[] actions = audit["items"]!.AsArray()
            .Select(item => String(item, "action"))
            .ToArray();
        Assert.Contains("member-offboarded", actions);
        Assert.Contains("workspace-recovered-after-restore", actions);
        string auditJson = audit.ToJsonString();
        Assert.DoesNotContain(Family.Email, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-private@example.test", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(pendingInvitationToken, auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OffboardingImpact_Base64UrlDigestContainingUnderscore_RoundTrips()
    {
        using var app = new WorkspaceTestApp();
        OidcSession owner = await BootstrapOwnerAsync(app);
        OidcSession family = await AddFamilyAsync(app, owner);
        HttpResponseMessage suspend = await SendJsonAsync(
            owner.Client,
            HttpMethod.Patch,
            $"/api/workspace/members/{family.MemberId}",
            new
            {
                status = "suspended",
                expectedVersion = family.MemberVersion,
                reason = "Prepare the deterministic impact-token parser regression.",
                idempotencyKey = "impact-underscore-suspend-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
        long suspendedVersion = Long((await JsonAsync(suspend))["member"], "version");

        string expectedImpactVersion = await ForceImpactDigestWithUnderscoreAsync(
            app,
            family.MemberId);
        int digestSeparator = expectedImpactVersion.IndexOf('_', "impact_v1_".Length);
        string digest = expectedImpactVersion[(digestSeparator + 1)..];
        Assert.Contains('_', digest);

        HttpResponseMessage preflight = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/members/{family.MemberId}/offboard",
            new
            {
                expectedVersion = suspendedVersion,
                impactVersion = (string?)null,
                reason = "Confirm the parser accepts the complete Base64url digest.",
                idempotencyKey = "impact-underscore-preflight-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.Conflict, preflight.StatusCode);
        string impactVersion = String((await JsonAsync(preflight))["impact"], "impactVersion");
        Assert.Equal(expectedImpactVersion, impactVersion);

        HttpResponseMessage confirmed = await SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            $"/api/workspace/members/{family.MemberId}/offboard",
            new
            {
                expectedVersion = suspendedVersion,
                impactVersion,
                reason = "Confirm the parser accepts the complete Base64url digest.",
                idempotencyKey = "impact-underscore-confirm-0001",
            },
            owner.Csrf,
            WorkspaceTestApp.Origin);
        Assert.True(
            confirmed.StatusCode == HttpStatusCode.OK,
            $"Expected an underscore-bearing impact token to round-trip, got {(int)confirmed.StatusCode}: " +
            await confirmed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CorruptWorkspaceStore_FailsClosedForOidcAndRecoveryReads()
    {
        using var app = new WorkspaceTestApp();
        Directory.CreateDirectory(app.StateDirectory);
        const string corrupt = "{ this is not valid workspace json";
        await File.WriteAllTextAsync(app.Store.StatePath, corrupt);

        using HttpClient oidc = app.CreateOidcClient(Owner);
        HttpResponseMessage oidcMe = await oidc.GetAsync("/api/me");
        await AssertErrorAsync(oidcMe, HttpStatusCode.ServiceUnavailable, "workspace-store-unavailable");
        AssertNoStore(oidcMe);

        using HttpClient recovery = app.CreateRecoveryClient();
        HttpResponseMessage recoveryWorkspace = await recovery.GetAsync("/api/workspace");
        await AssertErrorAsync(recoveryWorkspace, HttpStatusCode.ServiceUnavailable, "workspace-store-unavailable");
        Assert.Equal(corrupt, await File.ReadAllTextAsync(app.Store.StatePath));
    }

    [Fact]
    public async Task FamilyDelete_RemovesOnlyAnOwnedApprovedV2Registration()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using var app = new WorkspaceTestApp(new StubCatalog([
            new CatalogAppV2Dto(
                "approved-app",
                1,
                "Approved App",
                "For the family",
                "com.example.approved",
                "1",
                "1.0",
                "operator",
                "ready",
                123,
                new string('a', 64),
                HasEmbeddedProfile: false,
                SignatureExpiresAt: null,
                [new CatalogArtifactSourceDto("server", "On this server")],
                now,
                []),
        ]));
        OidcSession owner = await BootstrapOwnerAsync(app);
        OidcSession family = await AddFamilyAsync(app, owner);
        await app.Services.GetRequiredService<KnownDeviceStore>().UpsertAsync(
            AcceptedDevice("FAMILY-DELETE-UDID", family.MemberId, now));
        IAppRegistry registry = app.Services.GetRequiredService<IAppRegistry>();
        await registry.UpsertAsync(new AppRegistration(
            "com.example.approved",
            "owner@apple.example",
            "TEAM-SECRET",
            "FAMILY-DELETE-UDID",
            "/private/approved.ipa",
            CatalogAppId: "approved-app"));
        await registry.UpsertAsync(new AppRegistration(
            "com.example.legacy",
            "owner@apple.example",
            "TEAM-SECRET",
            "FAMILY-DELETE-UDID",
            "/private/legacy.ipa"));

        HttpResponseMessage legacyDelete = await SendEmptyAsync(
            family.Client,
            HttpMethod.Delete,
            "/api/apps/FAMILY-DELETE-UDID/com.example.legacy",
            family.Csrf,
            WorkspaceTestApp.Origin);

        await AssertErrorAsync(legacyDelete, HttpStatusCode.NotFound, "resource-not-found");
        Assert.NotNull(await registry.FindAsync("FAMILY-DELETE-UDID", "com.example.legacy"));

        HttpResponseMessage approvedDelete = await SendEmptyAsync(
            family.Client,
            HttpMethod.Delete,
            "/api/apps/FAMILY-DELETE-UDID/com.example.approved",
            family.Csrf,
            WorkspaceTestApp.Origin);

        Assert.Equal(HttpStatusCode.NoContent, approvedDelete.StatusCode);
        Assert.Null(await registry.FindAsync("FAMILY-DELETE-UDID", "com.example.approved"));
        Assert.NotNull(await registry.FindAsync("FAMILY-DELETE-UDID", "com.example.legacy"));
    }

    private static async Task<OidcSession> BootstrapOwnerAsync(WorkspaceTestApp app)
    {
        using HttpClient recovery = app.CreateRecoveryClient();
        HttpResponseMessage claim = await recovery.PostAsJsonAsync(
            "/api/workspace/owner-claims",
            new
            {
                expectedOwnerMemberId = (string?)null,
                impactVersion = (string?)null,
                confirmReplacement = false,
                expiresInMinutes = 15,
                idempotencyKey = "bootstrap-helper-claim-0001",
            });
        Assert.Equal(HttpStatusCode.Created, claim.StatusCode);
        string claimToken = FragmentToken(String(await JsonAsync(claim), "shareUrl"));

        HttpClient ownerClient = app.CreateClient();
        HttpResponseMessage handoff = await SendJsonAsync(
            ownerClient,
            HttpMethod.Post,
            "/api/workspace/owner-claims/handoff",
            new { claimToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, handoff.StatusCode);
        app.SetIdentity(ownerClient, Owner);
        MeResult me = await GetMeAsync(ownerClient);
        HttpResponseMessage accept = await SendJsonAsync(
            ownerClient,
            HttpMethod.Post,
            "/api/workspace/owner-claims/accept",
            new { idempotencyKey = "bootstrap-helper-accept-0001" },
            me.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        JsonObject accepted = await JsonAsync(accept);
        return new(
            ownerClient,
            me.Csrf,
            String(accepted["member"], "memberId"),
            Long(accepted["member"], "version"));
    }

    private static async Task<OidcSession> AddFamilyAsync(WorkspaceTestApp app, OidcSession owner)
    {
        HttpResponseMessage invitation = await CreateInvitationAsync(
            owner,
            Family.DisplayName,
            Family.Email,
            7,
            "add-family-invitation-0001");
        string invitationToken = FragmentToken(String(await JsonAsync(invitation), "shareUrl"));
        HttpClient familyClient = app.CreateClient();
        HttpResponseMessage exchange = await SendJsonAsync(
            familyClient,
            HttpMethod.Post,
            "/api/workspace/invitations/handoff",
            new { invitationToken },
            origin: WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        app.SetIdentity(familyClient, Family);
        MeResult me = await GetMeAsync(familyClient);
        HttpResponseMessage accept = await SendJsonAsync(
            familyClient,
            HttpMethod.Post,
            "/api/workspace/invitations/accept",
            new { idempotencyKey = "add-family-accept-0001" },
            me.Csrf,
            WorkspaceTestApp.Origin);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        JsonObject accepted = await JsonAsync(accept);
        return new(
            familyClient,
            me.Csrf,
            String(accepted["member"], "memberId"),
            Long(accepted["member"], "version"));
    }

    private static Task<HttpResponseMessage> CreateInvitationAsync(
        OidcSession owner,
        string displayName,
        string contactEmail,
        double expiresInDays,
        string idempotencyKey) =>
        SendJsonAsync(
            owner.Client,
            HttpMethod.Post,
            "/api/workspace/invitations",
            new { displayName, contactEmail, expiresInDays, idempotencyKey },
            owner.Csrf,
            WorkspaceTestApp.Origin);

    private static async Task<MeResult> GetMeAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
        return new(
            await JsonAsync(response),
            Assert.Single(response.Headers.GetValues("X-Sideport-CSRF")));
    }

    private static async Task<string> ForceImpactDigestWithUnderscoreAsync(
        WorkspaceTestApp app,
        string familyMemberId)
    {
        WorkspaceAccessDocument document = (await app.Store.ReadAsync())!;
        WorkspaceMemberRecord target = Assert.Single(
            document.Members,
            item => item.MemberId == familyMemberId);
        DateTimeOffset expiresAt = app.Time.GetUtcNow().AddMinutes(10);
        for (int counter = 0; counter < 10_000; counter++)
        {
            byte[] epochBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"impact-epoch-{counter}"));
            string epoch = Base64Url(epochBytes);
            string canonical = string.Join('\n',
            [
                "member-offboarding",
                document.Workspace.WorkspaceId,
                target.MemberId,
                target.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                expiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                .. document.Members.OrderBy(item => item.MemberId, StringComparer.Ordinal)
                    .Select(item => $"m:{item.MemberId}:{item.Role}:{item.Status}:{item.Version}"),
            ]);
            string digest = Base64Url(HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(epoch),
                Encoding.UTF8.GetBytes(canonical)));
            if (!digest.Contains('_', StringComparison.Ordinal))
                continue;

            WorkspaceAccessDocument updated = document with
            {
                Workspace = document.Workspace with { SecurityEpoch = epoch },
            };
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
            await File.WriteAllTextAsync(
                app.Store.StatePath,
                JsonSerializer.Serialize(updated, options));
            return $"impact_v1_{expiresAt.ToUnixTimeSeconds()}_{digest}";
        }

        throw new InvalidOperationException("Could not construct an underscore-bearing impact digest.");
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object body,
        string? csrf = null,
        string? origin = null,
        string? cookies = null)
    {
        using var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        if (csrf is not null)
            request.Headers.TryAddWithoutValidation("X-Sideport-CSRF", csrf);
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        if (cookies is not null)
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendEmptyAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string csrf,
        string origin)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-Sideport-CSRF", csrf);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        return await client.SendAsync(request);
    }

    private static KnownDeviceRecord AcceptedDevice(
        string udid,
        string ownerMemberId,
        DateTimeOffset now) =>
        new(
            udid,
            "Family iPhone",
            "iPhone17,1",
            "18.5",
            "usb",
            now,
            now,
            "live-poll",
            now,
            "trusted",
            Owner: null,
            Notes: null,
            UpdatedAt: now,
            InventoryState: "accepted",
            AcceptedAt: now,
            AcceptedBy: "member:owner",
            EnrollmentOperationId: $"op_enroll_{udid}",
            TrustReason: null,
            LockdownCheckedAt: now,
            UsableForInstall: true,
            OwnerMemberId: ownerMemberId);

    private static async Task AssertErrorAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string error)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(error, String(await JsonAsync(response), "error"));
    }

    private static async Task<JsonObject> JsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        return Assert.IsType<JsonObject>(JsonNode.Parse(json));
    }

    private static string AssertHandoffCookie(HttpResponseMessage response, string name)
    {
        string setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith($"{name}=", StringComparison.Ordinal));
        Assert.Contains("; path=/", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        return setCookie.Split(';', 2)[0];
    }

    private static string CookiePair(HttpResponseMessage response, string name) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"), value =>
                value.StartsWith($"{name}=", StringComparison.Ordinal))
            .Split(';', 2)[0];

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string FragmentToken(string shareUrl)
    {
        string fragment = new Uri(shareUrl, UriKind.Absolute).Fragment;
        Assert.StartsWith("#", fragment, StringComparison.Ordinal);
        return fragment[1..];
    }

    private static string String(JsonNode? parent, string property) =>
        parent!.AsObject()[property]!.GetValue<string>();

    private static long Long(JsonNode? parent, string property) =>
        parent!.AsObject()[property]!.GetValue<long>();

    private static bool Bool(JsonNode? parent, string property) =>
        parent!.AsObject()[property]!.GetValue<bool>();

    private sealed record TestIdentity(string Issuer, string Subject, string DisplayName, string Email);

    private sealed record MeResult(JsonObject Body, string Csrf);

    private sealed record OidcSession(HttpClient Client, string Csrf, string MemberId, long MemberVersion);

    private sealed class WorkspaceTestApp : IDisposable
    {
        internal const string Origin = "https://sideport.test";
        private const string RecoveryToken = "test-recovery-token-with-enough-entropy";
        private readonly WebApplicationFactory<Program> _factory;

        internal WorkspaceTestApp(
            IAppCatalog? catalog = null,
            IReadOnlyDictionary<string, string?>? settings = null)
        {
            StateDirectory = Path.Combine(
                Path.GetTempPath(),
                "sideport-workspace-http-tests",
                Guid.NewGuid().ToString("N"));
            // Handoff cookies use an absolute Expires value. Anchor the fake
            // store clock to the real current instant so HttpClient's cookie
            // container does not discard an otherwise valid test handoff.
            Time = new MutableTimeProvider(DateTimeOffset.UtcNow);
            Store = new WorkspaceAccessStore(StateDirectory, Time);
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Sideport:Apple:DeviceId", "TEST-WORKSPACE-HTTP-DEVICE");
                builder.UseSetting("Sideport:Scheduler:Enabled", "false");
                builder.UseSetting("Sideport:Signer:BinaryPath", typeof(WorkspaceAccessHttpTests).Assembly.Location);
                builder.UseSetting("Sideport:State:Directory", StateDirectory);
                builder.UseSetting("Sideport:PublicOrigin", $"{Origin}/");
                builder.UseSetting("Sideport:Api:AuthToken", RecoveryToken);
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example/application/o/sideport/");
                builder.UseSetting("Sideport:Oidc:ClientId", "sideport-workspace-http-tests");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
                if (settings is not null)
                {
                    foreach ((string key, string? value) in settings)
                        builder.UseSetting(key, value);
                }
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    if (catalog is not null)
                    {
                        services.RemoveAll<IAppCatalog>();
                        services.AddSingleton(catalog);
                    }
                    services.RemoveAll<WorkspaceAccessStore>();
                    services.AddSingleton(Store);
                    services.RemoveAll<WorkspaceImpactService>();
                    services.AddSingleton(sp => new WorkspaceImpactService(
                        sp.GetRequiredService<WorkspaceAccessStore>(),
                        sp.GetRequiredService<KnownDeviceStore>(),
                        sp.GetRequiredService<IAppRegistry>(),
                        sp.GetRequiredService<OperationStore>(),
                        sp.GetRequiredService<OperationService>(),
                        Time));
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = HeaderOidcAuthenticationHandler.SchemeName;
                            options.DefaultScheme = HeaderOidcAuthenticationHandler.SchemeName;
                            options.DefaultChallengeScheme = HeaderOidcAuthenticationHandler.SchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, HeaderOidcAuthenticationHandler>(
                            HeaderOidcAuthenticationHandler.SchemeName,
                            _ => { });
                });
            });
        }

        internal string StateDirectory { get; }

        internal MutableTimeProvider Time { get; }

        internal WorkspaceAccessStore Store { get; }

        internal IServiceProvider Services => _factory.Services;

        internal HttpClient CreateClient(Uri? baseAddress = null, bool handleCookies = true) =>
            _factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = baseAddress ?? new Uri(Origin),
                HandleCookies = handleCookies,
            });

        internal HttpClient CreateRecoveryClient()
        {
            HttpClient client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", RecoveryToken);
            return client;
        }

        internal HttpClient CreateOidcClient(TestIdentity identity, bool handleCookies = true)
        {
            HttpClient client = CreateClient(handleCookies: handleCookies);
            SetIdentity(client, identity);
            return client;
        }

        internal void SetIdentity(HttpClient client, TestIdentity identity)
        {
            SetHeader(client, HeaderOidcAuthenticationHandler.IssuerHeader, identity.Issuer);
            SetHeader(client, HeaderOidcAuthenticationHandler.SubjectHeader, identity.Subject);
            SetHeader(client, HeaderOidcAuthenticationHandler.NameHeader, identity.DisplayName);
            SetHeader(client, HeaderOidcAuthenticationHandler.EmailHeader, identity.Email);
        }

        public void Dispose()
        {
            _factory.Dispose();
            try { Directory.Delete(StateDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void SetHeader(HttpClient client, string name, string value)
        {
            client.DefaultRequestHeaders.Remove(name);
            client.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
        }
    }

    private sealed class HeaderOidcAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "workspace-header-oidc-test";
        internal const string IssuerHeader = "X-Test-Oidc-Issuer";
        internal const string SubjectHeader = "X-Test-Oidc-Subject";
        internal const string NameHeader = "X-Test-Oidc-Name";
        internal const string EmailHeader = "X-Test-Oidc-Email";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            string? issuer = Request.Headers[IssuerHeader].FirstOrDefault();
            string? subject = Request.Headers[SubjectHeader].FirstOrDefault();
            string? displayName = Request.Headers[NameHeader].FirstOrDefault();
            string? email = Request.Headers[EmailHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(subject) ||
                string.IsNullOrWhiteSpace(displayName))
            {
                return AuthenticateResult.NoResult();
            }

            var claims = new List<Claim>
            {
                new(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType, issuer),
                new("sub", subject),
                new("name", displayName),
                new(ClaimTypes.NameIdentifier, $"{issuer}|{subject}"),
            };
            if (!string.IsNullOrWhiteSpace(email))
                claims.Add(new Claim("email", email));

            try
            {
                WorkspaceAccessDocument? document = await Context.RequestServices
                    .GetRequiredService<WorkspaceAccessStore>()
                    .ReadAsync(Context.RequestAborted);
                if (document?.Workspace.State == WorkspaceLifecycleState.Active)
                {
                    claims.Add(new Claim(
                        WorkspaceRequestPrincipalResolver.SecurityEpochClaimType,
                        document.Workspace.SecurityEpoch));
                }
            }
            catch (WorkspaceAccessException)
            {
                // The request-principal resolver must observe and fail closed on
                // the same corrupt store instead of the test auth shim masking it.
            }

            var identity = new ClaimsIdentity(claims, SchemeName, "name", ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
    }

    internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubCatalog(IReadOnlyList<CatalogAppV2Dto> apps) : IAppCatalog
    {
        public Task<IReadOnlyList<CatalogAppV2Dto>> ListV2Async(CancellationToken ct = default) =>
            Task.FromResult(apps);

        public Task<IReadOnlyList<CatalogAppDto>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogAppDto>>([]);

        public Task<IReadOnlyList<CatalogImportRootDto>> ListImportRootsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogImportRootDto>>([]);

        public Task<CatalogAppDto> InspectAndStoreAsync(
            CatalogInspectRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<(CatalogAppDto Entry, bool Created)> ImportUploadedIpaAsync(
            CatalogUploadRequest request,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportFromRootV2Async(
            CatalogRootImportRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportUploadedIpaV2Async(
            CatalogUploadV2Request request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult> ImportDownloadedGitHubIpaV2Async(
            CatalogGitHubImportRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<CatalogV2MutationResult?> TryReplayDownloadedGitHubIpaV2Async(
            CatalogGitHubImportReplayRequest request,
            string actor,
            CancellationToken ct = default) => throw new NotSupportedException();
    }
}
