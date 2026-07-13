using System.Text.Json;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class GitHubSetupActorAuthorizerTests : IDisposable
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sideport-github-actor-{Guid.NewGuid():N}");

    public GitHubSetupActorAuthorizerTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task OnlyCurrentActiveOwnerOrConfiguredRecoveryBearerIsAuthorized()
    {
        WorkspaceFixture fixture = await CreateWorkspaceAsync("active");
        var configured = new WorkspaceGitHubSetupActorAuthorizer(
            fixture.Store,
            recoveryBearerConfigured: true);
        var bearerDisabled = new WorkspaceGitHubSetupActorAuthorizer(
            fixture.Store,
            recoveryBearerConfigured: false);

        Assert.True(await configured.IsAuthorizedAsync($"member:{fixture.Owner.MemberId}"));
        Assert.True(await configured.IsAuthorizedAsync("recovery-bearer"));
        Assert.False(await bearerDisabled.IsAuthorizedAsync("recovery-bearer"));
        Assert.False(await configured.IsAuthorizedAsync($"member:{fixture.Family.MemberId}"));
        Assert.False(await configured.IsAuthorizedAsync("member:member_unknownactor"));
        Assert.False(await configured.IsAuthorizedAsync("user:legacy-owner"));
        Assert.False(await configured.IsAuthorizedAsync(string.Empty));
    }

    [Fact]
    public async Task ReplacedSuspendedAndOffboardedOwnerIsDenied()
    {
        WorkspaceFixture fixture = await CreateWorkspaceAsync("replacement");
        const string impactVersion = "github-owner-replacement-v1";
        WorkspaceImpactSnapshot impact = TestImpact(fixture.Owner, impactVersion);
        WorkspaceOwnerClaimCreateResult replacementClaim = await fixture.Store.CreateOwnerClaimAsync(
            new(
                fixture.Owner.MemberId,
                impactVersion,
                TimeSpan.FromMinutes(15),
                "github-replacement-claim-0001",
                "req-github-replacement-claim"),
            verifyImpact: (_, _) => Task.FromResult(impact));
        WorkspaceHandoffCreateResult handoff = await fixture.Store.ExchangeOwnerClaimAsync(
            replacementClaim.Token!,
            "req-github-replacement-handoff");
        WorkspaceAcceptanceResult replacement = await fixture.Store.AcceptOwnerClaimAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://id.example.test/", "recovered-owner"),
                "Replacement Owner",
                "replacement@example.test",
                "github-replacement-accept-0001",
                "req-github-replacement-accept",
                CurrentImpactVersion: impactVersion),
            verifyImpact: (_, _, _) => Task.FromResult(impact));
        WorkspaceAccessDocument replaced = (await fixture.Store.ReadAsync())!;
        WorkspaceMemberRecord previousOwner = Assert.Single(
            replaced.Members,
            member => member.MemberId == fixture.Owner.MemberId);
        Assert.Equal(WorkspaceMemberStatus.Suspended, previousOwner.Status);

        var authorizer = new WorkspaceGitHubSetupActorAuthorizer(
            fixture.Store,
            recoveryBearerConfigured: false);
        Assert.False(await authorizer.IsAuthorizedAsync($"member:{previousOwner.MemberId}"));
        Assert.True(await authorizer.IsAuthorizedAsync($"member:{replacement.Member.MemberId}"));

        DateTimeOffset updatedAt = previousOwner.UpdatedAt.AddTicks(1);
        WorkspaceAccessDocument offboarded = replaced with
        {
            Members = replaced.Members.Select(member => member.MemberId == previousOwner.MemberId
                ? member with
                {
                    Status = WorkspaceMemberStatus.Offboarded,
                    Version = checked(member.Version + 1),
                    UpdatedAt = updatedAt,
                }
                : member).ToArray(),
        };
        WorkspaceAccessValidation.Validate(offboarded);
        string offboardedDirectory = Path.Combine(_directory, "offboarded");
        Directory.CreateDirectory(offboardedDirectory);
        var offboardedStore = new WorkspaceAccessStore(offboardedDirectory);
        await File.WriteAllTextAsync(
            offboardedStore.StatePath,
            JsonSerializer.Serialize(offboarded, WebJson));
        var offboardedAuthorizer = new WorkspaceGitHubSetupActorAuthorizer(
            offboardedStore,
            recoveryBearerConfigured: false);

        Assert.False(await offboardedAuthorizer.IsAuthorizedAsync($"member:{previousOwner.MemberId}"));
        Assert.True(await offboardedAuthorizer.IsAuthorizedAsync($"member:{replacement.Member.MemberId}"));
    }

    private static WorkspaceImpactSnapshot TestImpact(WorkspaceMemberRecord target, string impactVersion) => new(
        target.MemberId,
        target.Version,
        MemberCount: 2,
        OwnedDeviceCount: 0,
        UnassignedDeviceCount: 0,
        RegistrationCount: 0,
        QueuedOperationCount: 0,
        RunningOperationCount: 0,
        SchedulerEffectCount: 0,
        impactVersion,
        DateTimeOffset.UtcNow.AddMinutes(10));

    [Fact]
    public async Task MissingOrCorruptWorkspaceFailsClosedForHumanActor()
    {
        string missingDirectory = Path.Combine(_directory, "missing");
        var missingStore = new WorkspaceAccessStore(missingDirectory);
        var missingAuthorizer = new WorkspaceGitHubSetupActorAuthorizer(
            missingStore,
            recoveryBearerConfigured: false);
        Assert.False(await missingAuthorizer.IsAuthorizedAsync("member:member_missingowner"));

        string corruptDirectory = Path.Combine(_directory, "corrupt");
        Directory.CreateDirectory(corruptDirectory);
        var corruptStore = new WorkspaceAccessStore(corruptDirectory);
        await File.WriteAllTextAsync(corruptStore.StatePath, "{ not-json");
        var corruptAuthorizer = new WorkspaceGitHubSetupActorAuthorizer(
            corruptStore,
            recoveryBearerConfigured: false);

        Assert.False(await corruptAuthorizer.IsAuthorizedAsync("member:member_corruptowner"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<WorkspaceFixture> CreateWorkspaceAsync(string name)
    {
        string directory = Path.Combine(_directory, name);
        var store = new WorkspaceAccessStore(directory);
        WorkspaceOwnerClaimCreateResult ownerClaim = await store.CreateOwnerClaimAsync(new(
            ExpectedOwnerMemberId: null,
            ImpactVersion: null,
            Lifetime: TimeSpan.FromMinutes(15),
            IdempotencyKey: $"github-{name}-owner-claim-0001",
            RequestId: $"req-github-{name}-owner-claim"));
        WorkspaceHandoffCreateResult ownerHandoff = await store.ExchangeOwnerClaimAsync(
            ownerClaim.Token!,
            $"req-github-{name}-owner-handoff");
        WorkspaceAcceptanceResult owner = await store.AcceptOwnerClaimAsync(
            ownerHandoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://id.example.test/", $"{name}-owner"),
                "Stored Owner",
                "owner@example.test",
                $"github-{name}-owner-accept-0001",
                $"req-github-{name}-owner-accept"));
        WorkspaceInvitationCreateResult invitation = await store.CreateInvitationAsync(new(
            WorkspaceActorRecord.ForMember(owner.Member.MemberId),
            "Stored Family",
            "family@example.test",
            TimeSpan.FromDays(7),
            $"github-{name}-family-invite-0001",
            $"req-github-{name}-family-invite"));
        WorkspaceHandoffCreateResult familyHandoff = await store.ExchangeInvitationAsync(
            invitation.Token!,
            $"req-github-{name}-family-handoff");
        WorkspaceAcceptanceResult family = await store.AcceptInvitationAsync(
            familyHandoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://id.example.test/", $"{name}-family"),
                "Stored Family",
                "family@example.test",
                $"github-{name}-family-accept-0001",
                $"req-github-{name}-family-accept"));
        return new WorkspaceFixture(store, owner.Member, family.Member);
    }

    private sealed record WorkspaceFixture(
        WorkspaceAccessStore Store,
        WorkspaceMemberRecord Owner,
        WorkspaceMemberRecord Family);
}
