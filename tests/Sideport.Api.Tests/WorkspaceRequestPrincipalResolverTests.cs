using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class WorkspaceRequestPrincipalResolverTests
{
    private const string OwnerIssuer = "https://owner-idp.example/application/o/sideport/";
    private const string FamilyIssuer = "https://family-idp.example/application/o/sideport/";
    private const string SharedSubject = "same-subject-at-different-issuers";
    private const string RecoverySecret = "recovery-secret-value";

    [Fact]
    public async Task RecoveryBearer_TakesPrecedenceOverOidcAndCorruptStore()
    {
        string directory = StoreDirectory();
        Directory.CreateDirectory(directory);
        var store = new WorkspaceAccessStore(directory);
        await File.WriteAllTextAsync(store.StatePath, "{ corrupt-json");
        DefaultHttpContext context = AuthenticatedContext(
            OwnerIssuer,
            SharedSubject,
            securityEpoch: "stale-epoch",
            displayName: "Owner",
            email: "owner@example.test");
        context.Request.Headers.Authorization = $"Bearer {RecoverySecret}";

        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            store,
            RecoverySecret,
            oidcEnabled: true).ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.RecoveryBearer, principal.Kind);
        Assert.False(principal.IsOidc);
        Assert.False(principal.IsActiveMember);
        Assert.True(principal.IsOwnerEquivalent);
        Assert.Equal("recovery-bearer", principal.AuditActorKey);
        Assert.Equal(WorkspaceActorRecord.RecoveryBearer, principal.ToWorkspaceActor());
        Assert.Null(principal.Identity);
        Assert.Null(principal.Member);
        Assert.Null(principal.Presentation);
    }

    [Theory]
    [InlineData("recovery-secret-value", true)]
    [InlineData("Recovery-secret-value", false)]
    [InlineData("recovery-secret-valu", false)]
    [InlineData("recovery-secret-value-extra", false)]
    [InlineData("x", false)]
    public async Task RecoveryBearer_MatchesOnlyTheExactSecretAcrossCandidateLengths(
        string candidate,
        bool expectedMatch)
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {candidate}";

        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            store,
            RecoverySecret,
            oidcEnabled: true).ResolveAsync(context);

        Assert.Equal(
            expectedMatch
                ? WorkspaceRequestPrincipalKind.RecoveryBearer
                : WorkspaceRequestPrincipalKind.Unverified,
            principal.Kind);
    }

    [Fact]
    public async Task RecoveryBearer_RejectsAmbiguousAuthorizationHeaders()
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        var context = new DefaultHttpContext();
        context.Request.Headers.Append("Authorization", $"Bearer {RecoverySecret}");
        context.Request.Headers.Append("Authorization", $"Bearer {RecoverySecret}");

        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            store,
            RecoverySecret,
            oidcEnabled: true).ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.Unverified, principal.Kind);
    }

    [Fact]
    public async Task EmptyAndPendingWorkspace_ResolveAsBootstrapWithoutAnEpochCookie()
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        DefaultHttpContext context = AuthenticatedContext(
            OwnerIssuer,
            SharedSubject,
            securityEpoch: null,
            displayName: "  Owner  ",
            email: "owner@example.test");
        var resolver = new WorkspaceRequestPrincipalResolver(store, recoveryBearer: null, oidcEnabled: true);

        WorkspaceRequestPrincipal empty = await resolver.ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.BootstrapRequired, empty.Kind);
        Assert.True(empty.IsOidc);
        Assert.Equal(new WorkspaceIdentityKey(OwnerIssuer, SharedSubject), empty.Identity);
        Assert.Equal(new IdentityPresentationValue("Owner", "owner@example.test"), empty.Presentation);
        Assert.Null(empty.Workspace);

        await store.CreateOwnerClaimAsync(OwnerClaimRequest());
        WorkspaceRequestPrincipal pending = await resolver.ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.BootstrapRequired, pending.Kind);
        Assert.NotNull(pending.Workspace);
        Assert.Equal(WorkspaceLifecycleState.BootstrapRequired, pending.Workspace.State);
    }

    [Fact]
    public async Task ExactValidatedIssuerAndSubject_DistinguishOwnerFamilyAndUnknown()
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        string epoch = fixture.Document.Workspace.SecurityEpoch;
        var resolver = new WorkspaceRequestPrincipalResolver(fixture.Store, null, oidcEnabled: true);

        WorkspaceRequestPrincipal owner = await resolver.ResolveAsync(AuthenticatedContext(
            OwnerIssuer,
            SharedSubject,
            epoch,
            "Signed-in owner",
            "signed-owner@example.test"));
        WorkspaceRequestPrincipal family = await resolver.ResolveAsync(AuthenticatedContext(
            FamilyIssuer,
            SharedSubject,
            epoch,
            "Signed-in family",
            "signed-family@example.test"));
        WorkspaceRequestPrincipal wrongIssuer = await resolver.ResolveAsync(AuthenticatedContext(
            "https://other-idp.example/application/o/sideport/",
            SharedSubject,
            epoch,
            "Other account",
            "other@example.test"));
        WorkspaceRequestPrincipal wrongSubject = await resolver.ResolveAsync(AuthenticatedContext(
            OwnerIssuer,
            "different-subject",
            epoch,
            "Other account",
            "other@example.test"));

        Assert.Equal(WorkspaceRequestPrincipalKind.Owner, owner.Kind);
        Assert.Equal(fixture.Owner.MemberId, owner.Member?.MemberId);
        Assert.True(owner.IsOidc);
        Assert.True(owner.IsActiveMember);
        Assert.True(owner.IsOwnerEquivalent);
        Assert.Equal($"member:{fixture.Owner.MemberId}", owner.AuditActorKey);
        Assert.Equal(WorkspaceActorRecord.ForMember(fixture.Owner.MemberId), owner.ToWorkspaceActor());

        Assert.Equal(WorkspaceRequestPrincipalKind.Family, family.Kind);
        Assert.Equal(fixture.Family.MemberId, family.Member?.MemberId);
        Assert.True(family.IsOidc);
        Assert.True(family.IsActiveMember);
        Assert.False(family.IsOwnerEquivalent);
        Assert.Equal(WorkspaceActorRecord.ForMember(fixture.Family.MemberId), family.ToWorkspaceActor());

        Assert.Equal(WorkspaceRequestPrincipalKind.UnknownOidc, wrongIssuer.Kind);
        Assert.Equal(
            new WorkspaceIdentityKey("https://other-idp.example/application/o/sideport/", SharedSubject),
            wrongIssuer.Identity);
        Assert.Null(wrongIssuer.Member);
        Assert.False(wrongIssuer.IsActiveMember);
        Assert.Throws<InvalidOperationException>(() => wrongIssuer.ToWorkspaceActor());

        Assert.Equal(WorkspaceRequestPrincipalKind.UnknownOidc, wrongSubject.Kind);
        Assert.Null(wrongSubject.Member);
    }

    [Fact]
    public async Task StandardIssuerClaim_CannotReplaceTheInternallyValidatedIssuer()
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        string epoch = fixture.Document.Workspace.SecurityEpoch;
        var resolver = new WorkspaceRequestPrincipalResolver(fixture.Store, null, oidcEnabled: true);
        var identity = new ClaimsIdentity(
            [
                new Claim("iss", OwnerIssuer),
                new Claim("sub", SharedSubject),
                new Claim(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType, epoch),
            ],
            authenticationType: "oidc");
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        WorkspaceRequestPrincipal missingInternalIssuer = await resolver.ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.Unverified, missingInternalIssuer.Kind);

        identity.AddClaim(new Claim(
            WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType,
            "https://other-idp.example/application/o/sideport/"));
        WorkspaceRequestPrincipal exactInternalIssuer = await resolver.ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.UnknownOidc, exactInternalIssuer.Kind);
        Assert.Equal("https://other-idp.example/application/o/sideport/", exactInternalIssuer.Identity?.Issuer);
    }

    [Fact]
    public async Task AmbiguousValidatedIdentityClaims_FailClosed()
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        DefaultHttpContext context = AuthenticatedContext(
            OwnerIssuer,
            SharedSubject,
            fixture.Document.Workspace.SecurityEpoch,
            "Owner",
            "owner@example.test");
        ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim(
            WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType,
            FamilyIssuer));

        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            fixture.Store,
            null,
            oidcEnabled: true).ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.Unverified, principal.Kind);
        Assert.Null(principal.Identity);
    }

    [Fact]
    public async Task SuspendedAndOffboardedMembers_RemainDisabledOidcPrincipals()
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        await fixture.Store.SetFamilyMemberStatusAsync(
            fixture.Family.MemberId,
            new WorkspaceMemberStatusRequest(
                WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
                WorkspaceMemberStatus.Suspended,
                fixture.Family.Version,
                "resolver-suspend-family-0001",
                "req-resolver-suspend"));
        WorkspaceAccessDocument suspendedDocument = (await fixture.Store.ReadAsync())!;
        var resolver = new WorkspaceRequestPrincipalResolver(fixture.Store, null, oidcEnabled: true);

        WorkspaceRequestPrincipal suspended = await resolver.ResolveAsync(AuthenticatedContext(
            FamilyIssuer,
            SharedSubject,
            suspendedDocument.Workspace.SecurityEpoch,
            "Family",
            "family@example.test"));

        Assert.Equal(WorkspaceRequestPrincipalKind.SuspendedOidc, suspended.Kind);
        Assert.False(suspended.IsActiveMember);
        Assert.False(suspended.IsOwnerEquivalent);
        Assert.Equal("suspended-oidc", suspended.AuditActorKey);
        Assert.Throws<InvalidOperationException>(() => suspended.ToWorkspaceActor());

        WorkspaceAccessDocument offboardedDocument = suspendedDocument with
        {
            Members = suspendedDocument.Members.Select(member =>
                member.MemberId == fixture.Family.MemberId
                    ? member with
                    {
                        Status = WorkspaceMemberStatus.Offboarded,
                        Version = checked(member.Version + 1),
                    }
                    : member).ToArray(),
        };
        await WriteDocumentAsync(fixture.Store.StatePath, offboardedDocument);

        WorkspaceRequestPrincipal offboarded = await resolver.ResolveAsync(AuthenticatedContext(
            FamilyIssuer,
            SharedSubject,
            offboardedDocument.Workspace.SecurityEpoch,
            "Family",
            "family@example.test"));

        Assert.Equal(WorkspaceRequestPrincipalKind.OffboardedOidc, offboarded.Kind);
        Assert.False(offboarded.IsActiveMember);
        Assert.Equal("offboarded-oidc", offboarded.AuditActorKey);
        Assert.Throws<InvalidOperationException>(() => offboarded.ToWorkspaceActor());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("stale-security-epoch")]
    public async Task MissingOrStaleSecurityEpoch_InvalidatesAnOtherwiseActiveMember(string? epoch)
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            fixture.Store,
            null,
            oidcEnabled: true).ResolveAsync(AuthenticatedContext(
                OwnerIssuer,
                SharedSubject,
                epoch,
                "Owner",
                "owner@example.test"));

        Assert.Equal(WorkspaceRequestPrincipalKind.Unverified, principal.Kind);
        Assert.False(principal.IsOidc);
        Assert.False(principal.IsActiveMember);
        Assert.Null(principal.Identity);
        Assert.Null(principal.Member);
        Assert.Null(principal.Presentation);
        Assert.Equal(fixture.Document.Workspace.WorkspaceId, principal.Workspace?.WorkspaceId);
    }

    [Fact]
    public async Task CorruptStore_FailsClosedWithoutLosingSafeIdentityPresentation()
    {
        string directory = StoreDirectory();
        Directory.CreateDirectory(directory);
        var store = new WorkspaceAccessStore(directory);
        await File.WriteAllTextAsync(store.StatePath, "{ not-json");
        DefaultHttpContext context = AuthenticatedContext(
            OwnerIssuer,
            SharedSubject,
            "epoch-does-not-matter-before-read",
            "  Mara e\u0301 😊  ",
            "mara@example.test");

        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            store,
            null,
            oidcEnabled: true).ResolveAsync(context);

        Assert.Equal(WorkspaceRequestPrincipalKind.StoreUnavailable, principal.Kind);
        Assert.False(principal.IsOidc);
        Assert.Equal(new WorkspaceIdentityKey(OwnerIssuer, SharedSubject), principal.Identity);
        Assert.Equal(new IdentityPresentationValue("Mara é 😊", "mara@example.test"), principal.Presentation);
        Assert.Null(principal.Member);
    }

    [Fact]
    public async Task OidcPresentationClaims_AreNormalizedBeforeTheyReachThePrincipal()
    {
        ActiveWorkspace fixture = await CreateActiveWorkspaceAsync();
        WorkspaceRequestPrincipal principal = await new WorkspaceRequestPrincipalResolver(
            fixture.Store,
            null,
            oidcEnabled: true).ResolveAsync(AuthenticatedContext(
                "https://unknown-idp.example/application/o/sideport/",
                "unknown-subject",
                fixture.Document.Workspace.SecurityEpoch,
                "Mara\nForged log field",
                "mara@example.test"));

        Assert.Equal(WorkspaceRequestPrincipalKind.UnknownOidc, principal.Kind);
        Assert.Equal(new IdentityPresentationValue("mara@example.test", "mara@example.test"), principal.Presentation);
    }

    private static async Task<ActiveWorkspace> CreateActiveWorkspaceAsync()
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        WorkspaceOwnerClaimCreateResult ownerClaim = await store.CreateOwnerClaimAsync(OwnerClaimRequest());
        WorkspaceHandoffCreateResult ownerHandoff = await store.ExchangeOwnerClaimAsync(
            ownerClaim.Token!,
            "req-resolver-owner-handoff");
        WorkspaceAcceptanceResult owner = await store.AcceptOwnerClaimAsync(
            ownerHandoff.Token,
            Acceptance(
                new WorkspaceIdentityKey(OwnerIssuer, SharedSubject),
                "Stored Owner",
                "stored-owner@example.test",
                "resolver-owner-accept-0001"));
        WorkspaceInvitationCreateResult invitation = await store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(owner.Member.MemberId),
            "Stored Family",
            "stored-family@example.test",
            TimeSpan.FromDays(7),
            "resolver-family-invite-0001",
            "req-resolver-family-invite"));
        WorkspaceHandoffCreateResult familyHandoff = await store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-resolver-family-handoff");
        WorkspaceAcceptanceResult family = await store.AcceptInvitationAsync(
            familyHandoff.Token,
            Acceptance(
                new WorkspaceIdentityKey(FamilyIssuer, SharedSubject),
                "Stored Family",
                "stored-family@example.test",
                "resolver-family-accept-0001"));
        return new(store, owner.Member, family.Member, (await store.ReadAsync())!);
    }

    private static WorkspaceOwnerClaimCreateRequest OwnerClaimRequest() => new(
        ExpectedOwnerMemberId: null,
        ImpactVersion: null,
        Lifetime: TimeSpan.FromMinutes(15),
        IdempotencyKey: "resolver-owner-create-0001",
        RequestId: "req-resolver-owner-create");

    private static WorkspaceAcceptanceRequest Acceptance(
        WorkspaceIdentityKey identity,
        string displayName,
        string email,
        string idempotencyKey) => new(
            identity,
            displayName,
            email,
            idempotencyKey,
            RequestId: "req-resolver-accept");

    private static DefaultHttpContext AuthenticatedContext(
        string issuer,
        string subject,
        string? securityEpoch,
        string? displayName,
        string? email)
    {
        var claims = new List<Claim>
        {
            new(WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType, issuer),
            new("sub", subject),
        };
        if (securityEpoch is not null)
            claims.Add(new Claim(WorkspaceRequestPrincipalResolver.SecurityEpochClaimType, securityEpoch));
        if (displayName is not null)
            claims.Add(new Claim("name", displayName));
        if (email is not null)
            claims.Add(new Claim("email", email));

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "oidc")),
        };
    }

    private static async Task WriteDocumentAsync(string path, WorkspaceAccessDocument document)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document, options));
    }

    private static string StoreDirectory() => Path.Combine(
        Path.GetTempPath(),
        "sideport-principal-resolver-tests",
        Guid.NewGuid().ToString("N"));

    private sealed record ActiveWorkspace(
        WorkspaceAccessStore Store,
        WorkspaceMemberRecord Owner,
        WorkspaceMemberRecord Family,
        WorkspaceAccessDocument Document);
}
