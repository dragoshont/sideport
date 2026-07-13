using System.Text.Json;
using System.Text.Json.Nodes;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class WorkspaceAccessStoreTests
{
    [Fact]
    public async Task MissingCorruptAndUnsupportedState_FailClosedWithoutRepair()
    {
        string directory = StoreDirectory();
        var store = new WorkspaceAccessStore(directory);
        Assert.Null(await store.ReadAsync());
        Assert.False(File.Exists(store.StatePath));

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(store.StatePath, "{ not-json");
        WorkspaceAccessException corrupt = await Assert.ThrowsAsync<WorkspaceAccessException>(() => store.ReadAsync());
        Assert.Equal("workspace-store-unavailable", corrupt.Code);
        await Assert.ThrowsAsync<WorkspaceAccessException>(() => store.CreateOwnerClaimAsync(OwnerClaimRequest("owner-corrupt-0001")));
        Assert.Equal("{ not-json", await File.ReadAllTextAsync(store.StatePath));

        string futureDirectory = StoreDirectory();
        var futureStore = new WorkspaceAccessStore(futureDirectory);
        WorkspaceOwnerClaimCreateResult created = await futureStore.CreateOwnerClaimAsync(OwnerClaimRequest("owner-future-0001"));
        Assert.NotNull(created.Token);
        string valid = await File.ReadAllTextAsync(futureStore.StatePath);
        string future = valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2", StringComparison.Ordinal);
        await File.WriteAllTextAsync(futureStore.StatePath, future);

        WorkspaceAccessException unsupported = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => new WorkspaceAccessStore(futureDirectory).ReadAsync());
        Assert.Equal("workspace-store-unavailable", unsupported.Code);
        await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => new WorkspaceAccessStore(futureDirectory).CreateOwnerClaimAsync(OwnerClaimRequest("owner-future-0002")));
        Assert.Equal(future, await File.ReadAllTextAsync(futureStore.StatePath));
    }

    [Fact]
    public async Task NullCollectionsAndBrokenAcceptedGraphs_FailClosed()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        string valid = await File.ReadAllTextAsync(fixture.Store.StatePath);

        string nullMembers = valid.Replace("\"members\": [", "\"members\": null, \"discardedMembers\": [", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.Store.StatePath, nullMembers);
        WorkspaceAccessException nullError = await Assert.ThrowsAsync<WorkspaceAccessException>(() => fixture.Store.ReadAsync());
        Assert.Equal("workspace-store-unavailable", nullError.Code);

        BootstrappedWorkspace graphFixture = await BootstrapAsync();
        WorkspaceInvitationCreateResult invitation = await graphFixture.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(graphFixture.Owner.MemberId),
            "Graph",
            "graph@example.test",
            TimeSpan.FromDays(1),
            "graph-invitation-create-0001",
            "req-graph-create"));
        WorkspaceHandoffCreateResult handoff = await graphFixture.Store.ExchangeInvitationAsync(invitation.Token!, "req-graph-handoff");
        await graphFixture.Store.AcceptInvitationAsync(
            handoff.Token,
            Acceptance(new WorkspaceIdentityKey("https://auth.example/", "graph-subject"), "Graph", "graph@example.test", "graph-accept-0001"));
        string graph = await File.ReadAllTextAsync(graphFixture.Store.StatePath);
        using JsonDocument parsed = JsonDocument.Parse(graph);
        string receiptId = parsed.RootElement.GetProperty("invitations")[0].GetProperty("receiptId").GetString()!;
        JsonNode graphNode = JsonNode.Parse(graph)!;
        graphNode["handoffs"]![0]!["receiptId"] = "receipt_ffffffffffffffffffffffff";
        string broken = graphNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(graphFixture.Store.StatePath, broken);
        WorkspaceAccessException graphError = await Assert.ThrowsAsync<WorkspaceAccessException>(() => graphFixture.Store.ReadAsync());
        Assert.Equal("workspace-store-unavailable", graphError.Code);
    }

    [Fact]
    public async Task MalformedLinkAuthorities_UseTheSameUnavailableErrorsAsUnknownLinks()
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        var identity = new WorkspaceIdentityKey(
            "https://auth.example/application/o/sideport/",
            "unknown-subject");
        WorkspaceAcceptanceRequest acceptance = Acceptance(
            identity,
            "Unknown",
            null,
            "malformed-link-accept-0001");

        WorkspaceAccessException ownerExchange = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.ExchangeOwnerClaimAsync("not-an-owner-link", "req-owner-malformed"));
        WorkspaceAccessException ownerPreview = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.ResolveOwnerClaimHandoffAsync("not-a-handoff", identity));
        WorkspaceAccessException ownerAccept = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.AcceptOwnerClaimAsync("not-a-handoff", acceptance));
        Assert.All(
            [ownerExchange, ownerPreview, ownerAccept],
            error => Assert.Equal("owner-claim-unavailable", error.Code));

        WorkspaceAccessException invitationExchange = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.ExchangeInvitationAsync("not-an-invitation", "req-invitation-malformed"));
        WorkspaceAccessException invitationPreview = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.ResolveInvitationHandoffAsync("not-a-handoff", identity));
        WorkspaceAccessException invitationAccept = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => store.AcceptInvitationAsync("not-a-handoff", acceptance));
        Assert.All(
            [invitationExchange, invitationPreview, invitationAccept],
            error => Assert.Equal("invitation-unavailable", error.Code));
    }

    [Fact]
    public async Task AuthorityTokens_RejectPaddingStandardBase64AndExtraSeparators()
    {
        var store = new WorkspaceAccessStore(StoreDirectory());
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(OwnerClaimRequest("owner-canonical-token-0001"));
        string token = Assert.IsType<string>(claim.Token);
        int secretStart = token.LastIndexOf('_') + 1;
        string secret = token[secretStart..];
        string[] aliases =
        [
            token + "=",
            token[..secretStart] + secret[..^1] + "+",
            token.Insert(secretStart - 1, "_alias"),
        ];

        foreach (string alias in aliases)
        {
            WorkspaceAccessException error = await Assert.ThrowsAsync<WorkspaceAccessException>(
                () => store.ExchangeOwnerClaimAsync(alias, "req-owner-canonical-alias"));
            Assert.Equal("owner-claim-unavailable", error.Code);
        }
    }

    [Fact]
    public async Task OwnerBootstrap_IsAtomicSingleOwner_AndExactIssuerSubjectIdentityIsCaseSensitive()
    {
        string directory = StoreDirectory();
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-12T10:00:00Z"));
        var store = new WorkspaceAccessStore(directory, time);

        Task<Captured<WorkspaceOwnerClaimCreateResult>> first = CaptureAsync(() =>
            store.CreateOwnerClaimAsync(OwnerClaimRequest("owner-concurrent-0001")));
        Task<Captured<WorkspaceOwnerClaimCreateResult>> second = CaptureAsync(() =>
            store.CreateOwnerClaimAsync(OwnerClaimRequest("owner-concurrent-0002")));
        Captured<WorkspaceOwnerClaimCreateResult>[] attempts = await Task.WhenAll(first, second);

        Assert.Single(attempts, item => item.Value?.Created == true);
        WorkspaceAccessException conflict = Assert.IsType<WorkspaceAccessException>(
            Assert.Single(attempts, item => item.Error is not null).Error);
        Assert.Equal("owner-claim-pending", conflict.Code);
        WorkspaceOwnerClaimCreateResult claim = Assert.Single(attempts, item => item.Value is not null).Value!;
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(claim.Token!, "req-owner-handoff");
        var ownerIdentity = new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "CaseSensitiveSubject");
        WorkspaceAcceptanceResult accepted = await store.AcceptOwnerClaimAsync(
            handoff.Token,
            Acceptance(ownerIdentity, "Owner", "owner@example.test", "owner-accept-0001"));

        WorkspaceAccessDocument document = (await new WorkspaceAccessStore(directory).ReadAsync())!;
        Assert.Equal(WorkspaceLifecycleState.Active, document.Workspace.State);
        Assert.Equal(accepted.Member.MemberId, document.Workspace.OwnerMemberId);
        Assert.Single(document.Members, item =>
            item.Role == WorkspaceMemberRole.Owner && item.Status == WorkspaceMemberStatus.Active);
        Assert.Equal(ownerIdentity.Issuer, document.Members[0].OidcIssuer);
        Assert.Equal(ownerIdentity.Subject, document.Members[0].OidcSubject);
        Assert.Equal(accepted.Member, await store.FindMemberAsync(ownerIdentity));
        Assert.Null(await store.FindMemberAsync(ownerIdentity with { Subject = "casesensitivesubject" }));
        Assert.Null(await store.FindMemberAsync(ownerIdentity with { Issuer = "https://AUTH.example/application/o/sideport/" }));
    }

    [Fact]
    public async Task InvitationCreate_ReplaysWithoutRawToken_RejectsSemanticReuse_AndAuditIsRedacted()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        var request = new WorkspaceInvitationCreateRequest(
            WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
            "Mara",
            "mara@example.test",
            TimeSpan.FromDays(7),
            "invite-create-0001",
            "req-invite-create",
            "corr-invite-create");

        WorkspaceInvitationCreateResult created = await fixture.Store.CreateInvitationAsync(request);
        WorkspaceInvitationCreateResult replayed = await fixture.Store.CreateInvitationAsync(request);

        Assert.True(created.Created);
        Assert.NotNull(created.Token);
        Assert.False(replayed.Created);
        Assert.Null(replayed.Token);
        Assert.Equal(created.Invitation.InvitationId, replayed.Invitation.InvitationId);
        string json = await File.ReadAllTextAsync(fixture.Store.StatePath);
        Assert.DoesNotContain(created.Token!, json, StringComparison.Ordinal);
        Assert.DoesNotContain(request.IdempotencyKey, json, StringComparison.Ordinal);

        WorkspaceAccessException keyReuse = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.CreateInvitationAsync(request with { ContactEmail = "other@example.test" }));
        Assert.Equal("idempotency-key-reused", keyReuse.Code);

        using JsonDocument parsed = JsonDocument.Parse(json);
        foreach (JsonElement audit in parsed.RootElement.GetProperty("auditEvents").EnumerateArray())
        {
            string auditJson = audit.GetRawText();
            Assert.DoesNotContain("mara@example.test", auditJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.Owner.OidcIssuer, auditJson, StringComparison.Ordinal);
            Assert.DoesNotContain(fixture.Owner.OidcSubject, auditJson, StringComparison.Ordinal);
            Assert.DoesNotContain("tokenHash", auditJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("idempotency", auditJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task InvitationExpiryAndRevocation_AreTerminalVersionedAndInvalidateHandoffs()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceActorRecord actor = WorkspaceActorRecord.ForMember(fixture.Owner.MemberId);
        WorkspaceInvitationCreateResult expiring = await fixture.Store.CreateInvitationAsync(new(
            actor,
            "Soon",
            "soon@example.test",
            TimeSpan.FromMinutes(10),
            "invite-expire-0001",
            "req-expire-create"));
        fixture.Time.Set(fixture.Time.GetUtcNow().AddMinutes(11));

        WorkspaceAccessException expired = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.ExchangeInvitationAsync(expiring.Token!, "req-expire-exchange"));
        Assert.Equal("invitation-expired", expired.Code);
        WorkspaceInvitationRecord expiredRecord = Assert.Single((await fixture.Store.ReadAsync())!.Invitations);
        Assert.Equal(WorkspaceAuthorityStatus.Expired, expiredRecord.Status);
        Assert.Null(expiredRecord.ContactEmail);
        Assert.Null(expiredRecord.DisplayName);

        WorkspaceInvitationCreateResult revocable = await fixture.Store.CreateInvitationAsync(new(
            actor,
            "Alex",
            "alex@example.test",
            TimeSpan.FromDays(7),
            "invite-revoke-0001",
            "req-revoke-create"));
        WorkspaceHandoffCreateResult handoff = await fixture.Store.ExchangeInvitationAsync(
            revocable.Token!,
            "req-revoke-handoff");
        WorkspaceAccessException stale = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.RevokeInvitationAsync(
                revocable.Invitation.InvitationId,
                new(actor, ExpectedVersion: 99, "invite-revoke-0002", "req-revoke-stale")));
        Assert.Equal("workspace-version-conflict", stale.Code);

        WorkspaceMutationResult<WorkspaceInvitationRecord> revoked = await fixture.Store.RevokeInvitationAsync(
            revocable.Invitation.InvitationId,
            new(actor, revocable.Invitation.Version, "invite-revoke-0003", "req-revoke"));
        Assert.False(revoked.Replayed);
        Assert.Equal(WorkspaceAuthorityStatus.Revoked, revoked.Value.Status);
        Assert.Null(revoked.Value.ContactEmail);
        WorkspaceAccessException unavailable = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.AcceptInvitationAsync(
                handoff.Token,
                Acceptance(
                    new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "alex"),
                    "Alex",
                    "alex@example.test",
                    "invite-accept-revoked")));
        Assert.Equal("invitation-unavailable", unavailable.Code);
    }

    [Fact]
    public async Task AcceptedHandoff_IsPurgedAfterRetentionAndCannotReplay()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceInvitationCreateResult invitation = await fixture.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
            "Retention",
            "retention@example.test",
            TimeSpan.FromDays(1),
            "retention-invitation-0001",
            "req-retention-create"));
        WorkspaceHandoffCreateResult handoff = await fixture.Store.ExchangeInvitationAsync(invitation.Token!, "req-retention-handoff");
        var identity = new WorkspaceIdentityKey("https://auth.example/", "retention-subject");
        await fixture.Store.AcceptInvitationAsync(
            handoff.Token,
            Acceptance(identity, "Retention", "retention@example.test", "retention-accept-0001"));

        fixture.Time.Set(fixture.Time.GetUtcNow().AddHours(25));

        WorkspaceAccessException expired = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => fixture.Store.ResolveInvitationHandoffAsync(handoff.Token, identity));
        Assert.Equal("invitation-unavailable", expired.Code);
    }

    [Fact]
    public async Task ParallelInvitationAccept_BindsExactlyOneIdentity_AndSameIdentityReplaysReceipt()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceInvitationCreateResult invitation = await fixture.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
            "Family",
            "family@example.test",
            TimeSpan.FromDays(7),
            "invite-parallel-0001",
            "req-parallel-create"));
        WorkspaceHandoffCreateResult firstHandoff = await fixture.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-parallel-handoff-1");
        WorkspaceHandoffCreateResult secondHandoff = await fixture.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-parallel-handoff-2");
        var firstIdentity = new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "family-one");
        var secondIdentity = new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "family-two");

        Task<Captured<WorkspaceAcceptanceResult>> first = CaptureAsync(() => fixture.Store.AcceptInvitationAsync(
            firstHandoff.Token,
            Acceptance(firstIdentity, "Family One", "one@example.test", "invite-parallel-accept-1")));
        Task<Captured<WorkspaceAcceptanceResult>> second = CaptureAsync(() => fixture.Store.AcceptInvitationAsync(
            secondHandoff.Token,
            Acceptance(secondIdentity, "Family Two", "two@example.test", "invite-parallel-accept-2")));
        Captured<WorkspaceAcceptanceResult>[] attempts = await Task.WhenAll(first, second);

        WorkspaceAcceptanceResult winner = Assert.Single(attempts, item => item.Value is not null).Value!;
        WorkspaceAccessException loser = Assert.IsType<WorkspaceAccessException>(
            Assert.Single(attempts, item => item.Error is not null).Error);
        Assert.Equal("invitation-unavailable", loser.Code);
        WorkspaceIdentityKey winnerIdentity = winner.Member.OidcSubject == firstIdentity.Subject ? firstIdentity : secondIdentity;
        string winnerHandoff = winner.Member.OidcSubject == firstIdentity.Subject ? firstHandoff.Token : secondHandoff.Token;
        WorkspaceAcceptanceResult replay = await fixture.Store.AcceptInvitationAsync(
            winnerHandoff,
            Acceptance(winnerIdentity, winner.Member.DisplayName, winner.Member.Email, "invite-parallel-replay"));

        Assert.True(replay.Replayed);
        Assert.Equal(winner.Receipt.ReceiptId, replay.Receipt.ReceiptId);
        WorkspaceAccessDocument document = (await fixture.Store.ReadAsync())!;
        Assert.Equal(2, document.Members.Count);
        Assert.Single(document.Members, item => item.Role == WorkspaceMemberRole.Family);
        Assert.Single(document.Invitations, item => item.Status == WorkspaceAuthorityStatus.Accepted);
        Assert.Single(document.Handoffs, item =>
            item.Kind == WorkspaceHandoffKind.Invitation && item.Status == WorkspaceHandoffStatus.Accepted);
        Assert.Single(document.Handoffs, item =>
            item.Kind == WorkspaceHandoffKind.Invitation && item.Status == WorkspaceHandoffStatus.Revoked);
    }

    [Fact]
    public async Task TerminalCreateTombstone_PreventsLinkRemintAfterGeneralIdempotencyExpires()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceActorRecord actor = WorkspaceActorRecord.ForMember(fixture.Owner.MemberId);
        var request = new WorkspaceInvitationCreateRequest(
            actor,
            "Mara",
            "mara@example.test",
            TimeSpan.FromDays(7),
            "invite-tombstone-0001",
            "req-tombstone-create");
        WorkspaceInvitationCreateResult created = await fixture.Store.CreateInvitationAsync(request);
        await fixture.Store.RevokeInvitationAsync(
            created.Invitation.InvitationId,
            new(actor, created.Invitation.Version, "invite-tombstone-revoke", "req-tombstone-revoke"));
        fixture.Time.Set(fixture.Time.GetUtcNow().AddHours(25));

        WorkspaceInvitationCreateResult replay = await fixture.Store.CreateInvitationAsync(request);

        Assert.False(replay.Created);
        Assert.Null(replay.Token);
        Assert.Equal(created.Invitation.InvitationId, replay.Invitation.InvitationId);
        Assert.Equal(WorkspaceAuthorityStatus.Revoked, replay.Invitation.Status);
        WorkspaceAccessException changed = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.CreateInvitationAsync(request with { ContactEmail = "changed@example.test" }));
        Assert.Equal("idempotency-key-reused", changed.Code);
        Assert.Single((await fixture.Store.ReadAsync())!.Invitations);
    }

    [Fact]
    public async Task OwnerRecovery_BindsImpactAndAtomicallyReplacesTheSoleActiveOwner()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceInvitationCreateResult invitation = await fixture.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
            "Successor",
            "successor@example.test",
            TimeSpan.FromDays(7),
            "invite-successor-0001",
            "req-successor-create"));
        WorkspaceHandoffCreateResult invitationHandoff = await fixture.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-successor-handoff");
        var successorIdentity = new WorkspaceIdentityKey(
            "https://second-issuer.example/application/o/sideport/",
            fixture.Owner.OidcSubject);
        WorkspaceAcceptanceResult family = await fixture.Store.AcceptInvitationAsync(
            invitationHandoff.Token,
            Acceptance(successorIdentity, "Successor", "successor@example.test", "invite-successor-accept"));
        Assert.NotEqual(fixture.Owner.MemberId, family.Member.MemberId);

        var recoveryRequest = new WorkspaceOwnerClaimCreateRequest(
            fixture.Owner.MemberId,
            "impact-version-1",
            TimeSpan.FromMinutes(15),
            "owner-recovery-0001",
            "req-owner-recovery");
        WorkspaceImpactSnapshot recoveryImpact = TestImpact(fixture.Owner, "impact-version-1");
        WorkspaceOwnerClaimCreateResult claim = await fixture.Store.CreateOwnerClaimAsync(
            recoveryRequest,
            verifyImpact: (_, _) => Task.FromResult(recoveryImpact));
        WorkspaceHandoffCreateResult claimHandoff = await fixture.Store.ExchangeOwnerClaimAsync(
            claim.Token!,
            "req-owner-recovery-handoff");
        WorkspaceAccessException stale = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.AcceptOwnerClaimAsync(
                claimHandoff.Token,
                Acceptance(successorIdentity, "Successor", "successor@example.test", "owner-recovery-stale") with
                {
                    CurrentImpactVersion = "impact-version-stale",
                },
                verifyImpact: (_, _, _) => throw new WorkspaceAccessException("owner-replacement-preflight-stale", "stale")));
        Assert.Equal("owner-replacement-preflight-stale", stale.Code);

        WorkspaceAcceptanceResult recovered = await fixture.Store.AcceptOwnerClaimAsync(
            claimHandoff.Token,
            Acceptance(successorIdentity, "Successor", "successor@example.test", "owner-recovery-accept") with
            {
                CurrentImpactVersion = "impact-version-1",
            },
            verifyImpact: (_, _, _) => Task.FromResult(recoveryImpact));
        WorkspaceAccessDocument document = (await fixture.Store.ReadAsync())!;

        Assert.Equal(family.Member.MemberId, recovered.Member.MemberId);
        Assert.Equal(recovered.Member.MemberId, document.Workspace.OwnerMemberId);
        Assert.Single(document.Members, item =>
            item.Role == WorkspaceMemberRole.Owner && item.Status == WorkspaceMemberStatus.Active);
        Assert.Equal(
            WorkspaceMemberStatus.Suspended,
            Assert.Single(document.Members, item => item.MemberId == fixture.Owner.MemberId).Status);
        Assert.Equal(
            WorkspaceMemberRole.Owner,
            Assert.Single(document.Members, item => item.MemberId == recovered.Member.MemberId).Role);
    }

    [Fact]
    public async Task OffboardingFinalization_IsAtomicAuditedAndExactlyReplaySafe()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceMemberRecord family = await AddFamilyAsync(fixture);
        WorkspaceActorRecord ownerActor = WorkspaceActorRecord.ForMember(fixture.Owner.MemberId);
        await fixture.Store.CreateInvitationAsync(new(
            ownerActor,
            "Pending",
            "pending@example.test",
            TimeSpan.FromDays(7),
            "invite-preserve-0001",
            "req-invite-preserve"));
        WorkspaceMutationResult<WorkspaceMemberRecord> suspended = await fixture.Store.SetFamilyMemberStatusAsync(
            family.MemberId,
            new(
                ownerActor,
                WorkspaceMemberStatus.Suspended,
                family.Version,
                "family-suspend-0001",
                "req-family-suspend"));
        WorkspaceAccessDocument before = (await fixture.Store.ReadAsync())!;
        var impact = new WorkspaceAuditImpact(
            DeviceCount: 2,
            RegistrationCount: 3,
            QueuedOperationCount: 1,
            RunningOperationCount: 1,
            SchedulerEffectCount: 2,
            ImpactVersion: "offboard-impact-0001");
        var request = new WorkspaceOffboardingFinalizeRequest(
            ownerActor,
            suspended.Value.Version,
            impact,
            "family-offboard-0001",
            "req-family-offboard",
            "corr-family-offboard");

        WorkspaceImpactSnapshot snapshot = TestImpact(suspended.Value, impact.ImpactVersion!, impact);
        WorkspaceOffboardingResult finalized =
            await fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                request,
                verifyImpact: (_, _) => Task.FromResult(snapshot));
        WorkspaceOffboardingResult replayed =
            await new WorkspaceAccessStore(fixture.Directory, fixture.Time)
                .FinalizeFamilyMemberOffboardingAsync(family.MemberId, request);
        WorkspaceAccessDocument after = (await fixture.Store.ReadAsync())!;

        Assert.False(finalized.Replayed);
        Assert.True(replayed.Replayed);
        Assert.Equal(finalized.Receipt, replayed.Receipt);
        Assert.Equal(finalized.Impact, replayed.Impact);
        Assert.Equal(WorkspaceReceiptKind.MemberOffboarded, finalized.Receipt.Kind);
        Assert.Equal(family.MemberId, finalized.Receipt.TargetId);
        WorkspaceMemberRecord offboarded = Assert.Single(after.Members, item => item.MemberId == family.MemberId);
        Assert.Equal(WorkspaceMemberStatus.Offboarded, offboarded.Status);
        Assert.Equal(suspended.Value.Version + 1, offboarded.Version);
        Assert.Equal(finalized.Receipt.ReceiptId, offboarded.LastReceiptId);
        Assert.Equal(fixture.Owner.MemberId, after.Workspace.OwnerMemberId);
        Assert.Single(after.Members, item =>
            item.Role == WorkspaceMemberRole.Owner && item.Status == WorkspaceMemberStatus.Active);
        Assert.Equal(before.OwnerClaims, after.OwnerClaims);
        Assert.Equal(before.Invitations, after.Invitations);
        Assert.Equal(before.Handoffs, after.Handoffs);
        Assert.Equal(before.Receipts.Count + 1, after.Receipts.Count);
        Assert.Equal(before.AuditEvents.Count + 1, after.AuditEvents.Count);
        Assert.Equal(before.Idempotency.Count + 1, after.Idempotency.Count);
        WorkspaceAuditEventRecord audit = Assert.Single(after.AuditEvents, item =>
            item.Action == WorkspaceAuditAction.MemberOffboarded);
        Assert.Equal(ownerActor, audit.Actor);
        Assert.Equal(family.MemberId, audit.TargetId);
        Assert.Equal("req-family-offboard", audit.RequestId);
        Assert.Equal("corr-family-offboard", audit.CorrelationId);
        Assert.Equal(finalized.Impact, audit.Impact);
        Assert.DoesNotContain(request.IdempotencyKey, await File.ReadAllTextAsync(fixture.Store.StatePath));
    }

    [Fact]
    public async Task OffboardingFinalization_RequiresCurrentSuspendedFamilyAndOwnerOrRecoveryAuthority()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceMemberRecord family = await AddFamilyAsync(fixture);
        WorkspaceActorRecord ownerActor = WorkspaceActorRecord.ForMember(fixture.Owner.MemberId);
        var impact = new WorkspaceAuditImpact(
            DeviceCount: 1,
            RegistrationCount: 1,
            QueuedOperationCount: 0,
            RunningOperationCount: 0,
            SchedulerEffectCount: 1,
            ImpactVersion: "offboard-impact-0002");

        WorkspaceAccessException active = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                new(ownerActor, family.Version, impact, "offboard-active-0001", "req-offboard-active")));
        Assert.Equal("offboarding-preflight-stale", active.Code);

        WorkspaceAccessException ownerTarget = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                fixture.Owner.MemberId,
                new(ownerActor, fixture.Owner.Version, impact, "offboard-owner-0001", "req-offboard-owner")));
        Assert.Equal("last-owner-required", ownerTarget.Code);

        WorkspaceAccessException familyActor = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                new(
                    WorkspaceActorRecord.ForMember(family.MemberId),
                    family.Version,
                    impact,
                    "offboard-family-0001",
                    "req-offboard-family")));
        Assert.Equal("capability-denied", familyActor.Code);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                new(
                    ownerActor,
                    family.Version,
                    impact with { ImpactVersion = null },
                    "offboard-invalid-0001",
                    "req-offboard-invalid")));

        WorkspaceMutationResult<WorkspaceMemberRecord> suspended = await fixture.Store.SetFamilyMemberStatusAsync(
            family.MemberId,
            new(
                ownerActor,
                WorkspaceMemberStatus.Suspended,
                family.Version,
                "family-suspend-0002",
                "req-family-suspend-2"));
        WorkspaceAccessException staleMember = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                new(
                    ownerActor,
                    suspended.Value.Version - 1,
                    impact,
                    "offboard-stale-0001",
                    "req-offboard-stale")));
        Assert.Equal("workspace-version-conflict", staleMember.Code);

        var recoveryRequest = new WorkspaceOffboardingFinalizeRequest(
            WorkspaceActorRecord.RecoveryBearer,
            suspended.Value.Version,
            impact,
            "offboard-recovery-0001",
            "req-offboard-recovery");
        WorkspaceOffboardingResult finalized =
            await fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                recoveryRequest,
                verifyImpact: (_, _) => Task.FromResult(TestImpact(suspended.Value, impact.ImpactVersion!, impact)));
        Assert.False(finalized.Replayed);

        WorkspaceAccessException semanticReuse = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.FinalizeFamilyMemberOffboardingAsync(
                family.MemberId,
                recoveryRequest with { ValidatedImpact = impact with { ImpactVersion = "offboard-impact-other" } }));
        Assert.Equal("idempotency-key-reused", semanticReuse.Code);
    }

    [Fact]
    public async Task AfterRestore_RotatesEpochRevokesPendingAuthorityAndRequiresReview()
    {
        BootstrappedWorkspace fixture = await BootstrapAsync();
        WorkspaceActorRecord actor = WorkspaceActorRecord.ForMember(fixture.Owner.MemberId);
        WorkspaceInvitationCreateResult invitation = await fixture.Store.CreateInvitationAsync(new(
            actor,
            "Mara",
            "mara@example.test",
            TimeSpan.FromDays(7),
            "invite-restore-0001",
            "req-restore-create"));
        WorkspaceHandoffCreateResult handoff = await fixture.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-restore-handoff");
        WorkspaceAccessDocument before = (await fixture.Store.ReadAsync())!;

        WorkspaceMutationResult<WorkspaceReceiptRecord> recovered = await fixture.Store.RecoverAfterRestoreAsync(new(
            before.Workspace.Version,
            "restore-recovery-0001",
            "req-after-restore"));
        WorkspaceAccessDocument after = (await new WorkspaceAccessStore(fixture.Directory).ReadAsync())!;

        Assert.False(recovered.Replayed);
        Assert.NotEqual(before.Workspace.SecurityEpoch, after.Workspace.SecurityEpoch);
        Assert.True(after.Workspace.RestoreReviewRequired);
        Assert.Equal(recovered.Value.ReceiptId, after.Workspace.LastRestoreReceiptId);
        Assert.Equal(WorkspaceAuthorityStatus.Revoked, Assert.Single(after.Invitations).Status);
        Assert.Equal(
            WorkspaceHandoffStatus.Revoked,
            Assert.Single(after.Handoffs, item => item.Kind == WorkspaceHandoffKind.Invitation).Status);
        Assert.Null(Assert.Single(after.Invitations).ContactEmail);
        WorkspaceAccessException oldHandoff = await Assert.ThrowsAsync<WorkspaceAccessException>(() =>
            fixture.Store.AcceptInvitationAsync(
                handoff.Token,
                Acceptance(
                    new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "mara"),
                    "Mara",
                    "mara@example.test",
                    "restore-old-handoff")));
        Assert.Equal("invitation-unavailable", oldHandoff.Code);
    }

    private static async Task<BootstrappedWorkspace> BootstrapAsync()
    {
        string directory = StoreDirectory();
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-12T10:00:00Z"));
        var store = new WorkspaceAccessStore(directory, time);
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(OwnerClaimRequest("owner-bootstrap-0001"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(claim.Token!, "req-bootstrap-handoff");
        WorkspaceAcceptanceResult owner = await store.AcceptOwnerClaimAsync(
            handoff.Token,
            Acceptance(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "owner-subject"),
                "Owner",
                "owner@example.test",
                "owner-bootstrap-accept"));
        return new(directory, store, time, owner.Member);
    }

    private static async Task<WorkspaceMemberRecord> AddFamilyAsync(BootstrappedWorkspace fixture)
    {
        WorkspaceInvitationCreateResult invitation = await fixture.Store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(fixture.Owner.MemberId),
            "Family",
            "family@example.test",
            TimeSpan.FromDays(7),
            "invite-family-add-0001",
            "req-invite-family-add"));
        WorkspaceHandoffCreateResult handoff = await fixture.Store.ExchangeInvitationAsync(
            invitation.Token!,
            "req-invite-family-handoff");
        WorkspaceAcceptanceResult accepted = await fixture.Store.AcceptInvitationAsync(
            handoff.Token,
            Acceptance(
                new WorkspaceIdentityKey("https://auth.example/application/o/sideport/", "family-subject"),
                "Family",
                "family@example.test",
                "invite-family-accept-0001"));
        return accepted.Member;
    }

    private static WorkspaceOwnerClaimCreateRequest OwnerClaimRequest(string idempotencyKey) => new(
        ExpectedOwnerMemberId: null,
        ImpactVersion: null,
        Lifetime: TimeSpan.FromMinutes(15),
        idempotencyKey,
        RequestId: "req-owner-claim");

    private static WorkspaceAcceptanceRequest Acceptance(
        WorkspaceIdentityKey identity,
        string displayName,
        string? email,
        string idempotencyKey) => new(
            identity,
            displayName,
            email,
            idempotencyKey,
        RequestId: "req-accept");

    private static WorkspaceImpactSnapshot TestImpact(
        WorkspaceMemberRecord target,
        string impactVersion,
        WorkspaceAuditImpact? impact = null) => new(
        target.MemberId,
        target.Version,
        impact?.MemberCount ?? 2,
        impact?.DeviceCount ?? 0,
        UnassignedDeviceCount: 0,
        impact?.RegistrationCount ?? 0,
        impact?.QueuedOperationCount ?? 0,
        impact?.RunningOperationCount ?? 0,
        impact?.SchedulerEffectCount ?? 0,
        impactVersion,
        DateTimeOffset.Parse("2026-07-12T11:00:00Z"));

    private static async Task<Captured<T>> CaptureAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return new(await action(), null);
        }
        catch (Exception ex)
        {
            return new(default, ex);
        }
    }

    private static string StoreDirectory() => Path.Combine(
        Path.GetTempPath(),
        "sideport-workspace-access-tests",
        Guid.NewGuid().ToString("N"));

    private sealed record BootstrappedWorkspace(
        string Directory,
        WorkspaceAccessStore Store,
        MutableTimeProvider Time,
        WorkspaceMemberRecord Owner);

    private sealed record Captured<T>(T? Value, Exception? Error);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Set(DateTimeOffset utcNow) => _utcNow = utcNow;
    }
}
