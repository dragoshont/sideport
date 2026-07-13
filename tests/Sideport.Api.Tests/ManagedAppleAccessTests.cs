using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sideport.Api.AppleAccess;
using Sideport.Core;
using Sideport.DeveloperApi.DeveloperServices;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.Orchestrator;
using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class ManagedAppleAccessTests
{
    [Fact]
    public async Task ManagedStore_EncryptsAndPersistsCredentialAcrossRestart()
    {
        string root = TestDirectory();
        const string appleId = "owner@example.com";
        const string password = "  fake-password-with-spaces  ";
        try
        {
            ManagedAppleCredentialStore first = CreateManagedStore(root);
            ManagedAppleCredentialCommit commit = await first.CommitAuthenticatedAsync(appleId, password, "api-token:test");

            Assert.True(commit.Created);
            string envelopePath = Path.Combine(root, "apple-credentials", "credential.json");
            string envelope = await File.ReadAllTextAsync(envelopePath);
            Assert.DoesNotContain(appleId, envelope, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(password, envelope, StringComparison.Ordinal);
            Assert.DoesNotContain(password, new PersonalAppleConnectRequest { AppleId = appleId, Password = password }.ToString(), StringComparison.Ordinal);

            ManagedAppleCredentialStore restarted = CreateManagedStore(root);
            Assert.Equal(password, await restarted.GetPasswordAsync(appleId));
            ManagedAppleCredentialMetadata? metadata = await restarted.ReadMetadataAsync();
            Assert.Equal(commit.Metadata.AccountProfileId, metadata?.AccountProfileId);
            Assert.Equal("api-token:test", metadata?.UpdatedByActor);

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(envelopePath));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(Path.GetDirectoryName(envelopePath)!));
                foreach (string keyPath in Directory.EnumerateFiles(Path.Combine(root, "data-protection-keys")))
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(keyPath));
            }
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ManagedStore_ReplacementCommit_ReplacesDifferentAccountOnlyWhenExplicit()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("old@example.com", "old-password", "api-token:owner");

            await Assert.ThrowsAsync<AppleAccountReplacementRequiresCutoverException>(() =>
                store.CommitAuthenticatedAsync("new@example.com", "new-password", "api-token:owner"));
            Assert.Equal("old-password", await store.GetPasswordAsync("old@example.com"));

            await store.CommitReplacementAuthenticatedAsync("new@example.com", "new-password", "api-token:owner", null, null);

            Assert.Null(await store.GetPasswordAsync("old@example.com"));
            Assert.Equal("new-password", await store.GetPasswordAsync("new@example.com"));
            string envelope = await File.ReadAllTextAsync(Path.Combine(root, "apple-credentials", "credential.json"));
            Assert.DoesNotContain("new@example.com", envelope, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("new-password", envelope, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReplacementCandidate_DifferentAccountStaysMemoryOnlyAndActorBound()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("old@example.com", "old-password", "api-token:owner");
            var portal = new FakeApplePortal { AcceptedPassword = "new-password" };
            var candidates = new AppleAccountReplacementCandidateService(portal, store);

            AppleAccountReplacementCandidateDto candidate = await candidates.ConnectAsync(
                new AppleAccountReplacementConnectRequest { AppleId = "new@example.com", Password = "new-password" },
                "api-token:owner",
                CancellationToken.None);

            Assert.Equal("validated", candidate.State);
            Assert.Equal("old-password", await store.GetPasswordAsync("old@example.com"));
            Assert.Null(await store.GetPasswordAsync("new@example.com"));
            Assert.Throws<AppleChallengeExpiredException>(() => candidates.Resolve(candidate.CandidateId, "api-token:other"));
            AppleAccountReplacementContext resolved = candidates.Resolve(candidate.CandidateId, "api-token:owner");
            Assert.Equal("new@example.com", resolved.AppleId);
            Assert.Equal("new-password", resolved.Password);
            candidates.Complete(candidate.CandidateId);
            Assert.Throws<AppleChallengeExpiredException>(() => candidates.Resolve(candidate.CandidateId, "api-token:owner"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ReplacementCandidate_TwoFactorFailureConsumesCandidateAndKeepsOldCredential()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("old@example.com", "old-password", "api-token:owner");
            var portal = new FakeApplePortal { AcceptedPassword = "new-password", RequireTwoFactor = true };
            var candidates = new AppleAccountReplacementCandidateService(portal, store);
            AppleAccountReplacementCandidateDto candidate = await candidates.ConnectAsync(
                new AppleAccountReplacementConnectRequest { AppleId = "new@example.com", Password = "new-password" },
                "api-token:owner",
                CancellationToken.None);

            await Assert.ThrowsAsync<AppleTwoFactorInvalidException>(() => candidates.CompleteTwoFactorAsync(
                new AppleAccountReplacementTwoFactorRequest(candidate.CandidateId, "000000"),
                "api-token:owner",
                CancellationToken.None));

            Assert.Equal("old-password", await store.GetPasswordAsync("old@example.com"));
            Assert.Null(await store.GetPasswordAsync("new@example.com"));
            Assert.Throws<AppleChallengeExpiredException>(() => candidates.Resolve(candidate.CandidateId, "api-token:owner"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AccountStateStore_RejectsSchemaValidStateWithNullTeams()
    {
        string root = TestDirectory();
        try
        {
            string statePath = Path.Combine(root, "apple-account.json");
            await File.WriteAllTextAsync(statePath,
                """
                {
                  "schemaVersion": 1,
                  "accountProfileId": "acct_12345678901234567890",
                  "appleIdHint": "o***@example.com",
                  "teams": null,
                  "authValidatedAt": "2026-07-12T10:00:00Z",
                  "selectedTeamId": null,
                  "teamValidatedAt": null
                }
                """);
            var store = new AppleAccountStateStore(new AppleAccountStateStoreOptions(statePath));

            await Assert.ThrowsAsync<AppleAccountStateStoreException>(() => store.ReadAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_FailedAuthenticationDoesNotCommitCandidateOrReplaceWorkingCredential()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("owner@example.com", "working-password", "api-token:test");
            var portal = new FakeApplePortal { AcceptedPassword = "working-password" };
            PersonalAppleAccess access = CreateAccess(root, store, portal);

            await Assert.ThrowsAsync<AppleAuthenticationFailedException>(() =>
                access.ConnectAsync(
                    new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "wrong-password" },
                    "api-token:test"));

            Assert.Equal("working-password", await store.GetPasswordAsync("owner@example.com"));
            await Assert.ThrowsAsync<AppleAccountReplacementRequiresCutoverException>(() =>
                access.ConnectAsync(
                    new PersonalAppleConnectRequest { AppleId = "different@example.com", Password = "working-password" },
                    "api-token:test"));
            Assert.Equal(1, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReplacementCandidate_DifferentAccountStaysIsolatedUntilCoordinatorCommit()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("owner@example.com", "old-password", "api-token:test");
            var stateStore = new AppleAccountStateStore(new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json")));
            await stateStore.RecordAuthenticationAsync("owner@example.com", [new AppleTeam("OLDTEAM", "Old", "Individual")], DateTimeOffset.UtcNow, "api-token:test");
            await stateStore.SelectTeamAsync(AppleAccountIdentity.ProfileIdFor("owner@example.com"), "OLDTEAM", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15), "api-token:test");
            IAppRegistry registry = new FileAppRegistry(Path.Combine(root, "registrations.json"));
            await registry.UpsertAsync(new AppRegistration("com.example.app", "owner@example.com", "OLDTEAM", "device", "/tmp/app.ipa"));
            var portal = new FakeApplePortal { AcceptedPassword = "new-password", Teams = [new AppleTeam("NEWTEAM", "New", "Individual")] };
            var candidates = new AppleAccountReplacementCandidateService(portal, store, TimeProvider.System);

            AppleAccountReplacementCandidateDto candidate = await candidates.ConnectAsync(
                new AppleAccountReplacementConnectRequest { AppleId = "replacement@example.com", Password = "new-password" },
                "api-token:test",
                default);

            Assert.Equal("validated", candidate.State);
            Assert.Equal("old-password", await store.GetPasswordAsync("owner@example.com"));
            Assert.Null(await store.GetPasswordAsync("replacement@example.com"));
            Assert.Equal("owner@example.com", (await registry.ListAsync()).Single().AppleId);

            var coordinator = new AppleAuthorityCutoverCoordinator(
                new AppleAuthorityCutoverCoordinatorOptions(Path.Combine(root, "apple-authority-cutover.json")),
                store,
                stateStore,
                registry,
                DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "data-protection-keys")), builder => builder.SetApplicationName("Sideport.ManagedAppleCredential")));
            AppleAccountReplacementContext context = candidates.Resolve(candidate.CandidateId, "api-token:test");
            await coordinator.CommitAsync(
                context,
                "NEWTEAM",
                "owner@example.com",
                AppleAccountIdentity.ProfileIdFor("owner@example.com"),
                "OLDTEAM",
                "api-token:test");

            Assert.Null(await store.GetPasswordAsync("owner@example.com"));
            Assert.Equal("new-password", await store.GetPasswordAsync("replacement@example.com"));
            AppleAccountState state = (await stateStore.ReadAsync())!;
            Assert.Equal(AppleAccountIdentity.ProfileIdFor("replacement@example.com"), state.AccountProfileId);
            Assert.Equal("NEWTEAM", state.SelectedTeamId);
            AppRegistration registration = (await registry.ListAsync()).Single();
            Assert.Equal("replacement@example.com", registration.AppleId);
            Assert.Equal("NEWTEAM", registration.TeamId);
            Assert.False(File.Exists(Path.Combine(root, "apple-authority-cutover.json")));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task ReplacementCandidate_TwoFactorIsActorBoundAndSingleUse()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            await store.CommitAuthenticatedAsync("owner@example.com", "old-password", "api-token:test");
            var portal = new FakeApplePortal { AcceptedPassword = "new-password", RequireTwoFactor = true };
            var candidates = new AppleAccountReplacementCandidateService(portal, store, TimeProvider.System);
            AppleAccountReplacementCandidateDto pending = await candidates.ConnectAsync(
                new AppleAccountReplacementConnectRequest { AppleId = "replacement@example.com", Password = "new-password" },
                "oidc:owner",
                default);

            await Assert.ThrowsAsync<AppleChallengeExpiredException>(() => candidates.CompleteTwoFactorAsync(
                new AppleAccountReplacementTwoFactorRequest(pending.CandidateId, "123456"), "oidc:other", default));
            portal.RequireTwoFactor = false;
            AppleAccountReplacementCandidateDto completed = await candidates.CompleteTwoFactorAsync(
                new AppleAccountReplacementTwoFactorRequest(pending.CandidateId, "123456"), "oidc:owner", default);
            Assert.Equal("validated", completed.State);
            Assert.Equal("old-password", await store.GetPasswordAsync("owner@example.com"));
        }
        finally { DeleteDirectory(root); }
    }

    [Fact]
    public async Task Connect_InternalAuthenticatedSessionToStringIsExplicitlyRedacted()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            PersonalAppleAccess access = CreateAccess(root, store, portal);

            await access.ConnectAsync(
                new PersonalAppleConnectRequest
                {
                    AppleId = "private-owner@example.com",
                    Password = "valid-password",
                },
                "api-token:test");

            object authenticated = typeof(PersonalAppleAccess)
                .GetField("_lastAuthentication", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(access)!;
            string text = authenticated.ToString()!;

            Assert.Equal("AuthenticatedSession { [REDACTED] }", text);
            Assert.DoesNotContain("private-owner@example.com", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-adsid", text, StringComparison.Ordinal);
            Assert.DoesNotContain("fake-session-token", text, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_SameAccountRotationCommitsOnlyTheAuthenticatedReplacement()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal { AcceptedPassword = "first-password" };
            PersonalAppleAccess access = CreateAccess(root, store, portal);
            PersonalAppleConnectResult created = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "first-password" },
                "api-token:test");
            string firstVersion = (await store.ReadMetadataAsync())!.CredentialVersion;

            portal.AcceptedPassword = "rotated-password";
            PersonalAppleConnectResult updated = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "OWNER@example.com", Password = "rotated-password" },
                "api-token:test");

            Assert.Equal("created", created.Outcome);
            Assert.Equal("updated", updated.Outcome);
            Assert.Equal("rotated-password", await store.GetPasswordAsync("owner@example.com"));
            Assert.NotEqual(firstVersion, (await store.ReadMetadataAsync())!.CredentialVersion);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_TwoFactorCandidateIsSingleUseAndCommitsOnlyAfterSuccess()
    {
        string root = TestDirectory();
        const string password = "candidate-password";
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal
            {
                AcceptedPassword = password,
                RequireTwoFactor = true,
            };
            PersonalAppleAccess access = CreateAccess(root, store, portal);

            PersonalAppleConnectResult pending = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = password },
                "api-token:test");

            Assert.Equal("two-factor-required", pending.Outcome);
            Assert.Null(await store.GetPasswordAsync("owner@example.com"));
            Assert.NotNull(pending.Status.PendingChallengeExpiresAt);

            portal.RequireTwoFactor = false;
            PersonalAppleTwoFactorResult completed = await access.CompleteTwoFactorAsync(
                new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                "api-token:test");

            Assert.Equal("connected-created", completed.Outcome);
            Assert.Equal(password, await store.GetPasswordAsync("owner@example.com"));
            Assert.Equal("123456", portal.LastTwoFactorCode);
            await Assert.ThrowsAsync<AppleChallengeNotFoundException>(() =>
                access.CompleteTwoFactorAsync(
                    new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                    "api-token:test"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_RejectedTwoFactorConsumesCandidateWithoutCommitting()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal
            {
                AcceptedPassword = "candidate-password",
                RequireTwoFactor = true,
            };
            PersonalAppleAccess access = CreateAccess(root, store, portal);
            PersonalAppleConnectResult pending = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "candidate-password" },
                "api-token:test");
            portal.RequireTwoFactor = false;

            await Assert.ThrowsAsync<AppleTwoFactorInvalidException>(() =>
                access.CompleteTwoFactorAsync(
                    new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "000000"),
                    "api-token:test"));

            Assert.Null(await store.GetPasswordAsync("owner@example.com"));
            await Assert.ThrowsAsync<AppleChallengeNotFoundException>(() =>
                access.CompleteTwoFactorAsync(
                    new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                    "api-token:test"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_TwoFactorChallengeCannotBeConsumedByAnotherActor()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal
            {
                AcceptedPassword = "candidate-password",
                RequireTwoFactor = true,
            };
            PersonalAppleAccess access = CreateAccess(root, store, portal);
            PersonalAppleConnectResult pending = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "candidate-password" },
                "oidc:owner");
            portal.RequireTwoFactor = false;

            await Assert.ThrowsAsync<AppleChallengeNotFoundException>(() =>
                access.CompleteTwoFactorAsync(
                    new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                    "oidc:different-user"));

            PersonalAppleTwoFactorResult completed = await access.CompleteTwoFactorAsync(
                new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                "oidc:owner");
            Assert.Equal("connected-created", completed.Outcome);
            Assert.Equal("candidate-password", await store.GetPasswordAsync("owner@example.com"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Connect_ExpiredTwoFactorCandidateIsDiscardedWithoutStatusRead()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal
            {
                AcceptedPassword = "candidate-password",
                RequireTwoFactor = true,
            };
            var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero));
            PersonalAppleAccess access = CreateAccess(root, store, portal, time);
            PersonalAppleConnectResult pending = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "candidate-password" },
                "api-token:test");

            time.Advance(TimeSpan.FromMinutes(6));

            Assert.Null(access.PendingChallengeAccountProfileId(
                pending.Status.PendingChallengeId!,
                "api-token:test"));
            await Assert.ThrowsAsync<AppleChallengeNotFoundException>(() =>
                access.CompleteTwoFactorAsync(
                    new PersonalAppleCompleteTwoFactorRequest(pending.Status.PendingChallengeId!, "123456"),
                    "api-token:test"));
            Assert.Null(await store.GetPasswordAsync("owner@example.com"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void TwoFactorRequestStringNeverContainsTheCode()
    {
        const string code = "738291";
        string text = new PersonalAppleCompleteTwoFactorRequest("challenge", code).ToString();
        Assert.DoesNotContain(code, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TeamSelection_RequiresReturnedTeamAndPersistsAcrossRestart()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            PersonalAppleAccess access = CreateAccess(root, store, portal);
            PersonalAppleConnectResult connected = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "valid-password" },
                "api-token:test");
            Assert.Equal("TEAMID1234", connected.Status.SelectedTeamId);
            Assert.NotNull(connected.Status.TeamValidatedAt);

            await Assert.ThrowsAsync<AppleTeamNotReturnedException>(() =>
                access.SelectTeamAsync(
                    new PersonalAppleTeamSelectionRequest(connected.Status.AccountProfileId!, "UNKNOWN"),
                    "api-token:test"));

            PersonalAppleStatusDto selected = await access.SelectTeamAsync(
                new PersonalAppleTeamSelectionRequest(connected.Status.AccountProfileId!, "TEAMID1234"),
                "api-token:test");
            Assert.Equal("TEAMID1234", selected.SelectedTeamId);
            Assert.NotNull(selected.TeamValidatedAt);
            AppleAccountState persisted = (await new AppleAccountStateStore(
                new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json"))).ReadAsync())!;
            Assert.Equal("api-token:test", persisted.LastAuthenticatedByActor);
            Assert.Equal("api-token:test", persisted.TeamSelectedByActor);

            ManagedAppleCredentialStore restartedStore = CreateManagedStore(root);
            PersonalAppleAccess restarted = CreateAccess(root, restartedStore, portal);
            PersonalAppleStatusDto afterRestart = await restarted.StatusAsync();
            Assert.Equal("validation-stale", afterRestart.State);
            Assert.Equal("TEAMID1234", afterRestart.SelectedTeamId);
            Assert.Contains(afterRestart.Teams, team => team.TeamId == "TEAMID1234");
            Assert.Equal(connected.Status.AccountProfileId, afterRestart.AccountProfileId);
            await Assert.ThrowsAsync<AppleTeamSelectionStaleException>(() =>
                restarted.SelectTeamAsync(
                    new PersonalAppleTeamSelectionRequest(connected.Status.AccountProfileId!, "TEAMID1234"),
                    "api-token:test"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SignIn_AfterRestartUsesManagedAccountWithoutRetypingAppleId()
    {
        string root = TestDirectory();
        const string appleId = "owner@example.com";
        const string password = "persisted-password";
        try
        {
            ManagedAppleCredentialStore firstStore = CreateManagedStore(root);
            await firstStore.CommitAuthenticatedAsync(appleId, password, "api-token:test");

            var portal = new FakeApplePortal { AcceptedPassword = password };
            ManagedAppleCredentialStore restartedStore = CreateManagedStore(root);
            PersonalAppleAccess restarted = CreateAccess(root, restartedStore, portal);

            PersonalAppleStatusDto status = await restarted.SignInAsync(new PersonalAppleSignInRequest());

            Assert.Equal("validated-recently", status.State);
            Assert.Equal(AppleAccountIdentity.Redact(appleId), status.AppleIdHint);
            Assert.DoesNotContain(appleId, System.Text.Json.JsonSerializer.Serialize(status), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(appleId, portal.LastAppleId);
            Assert.Equal(1, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SignIn_WithoutAppleIdUsesConfiguredReadOnlyAccount()
    {
        string root = TestDirectory();
        const string appleId = "configured@example.com";
        const string password = "environment-password";
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = password };
            var accountState = new AppleAccountStateStore(new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json")));
            var access = new PersonalAppleAccess(
                new SessionManager(portal, new FixedCredentialProvider(appleId, password)),
                portal,
                new PersonalAppleAccessOptions(appleId, AppleCredentialSources.Environment),
                new ReadOnlyAppleCredentialManagement(AppleCredentialSources.Environment),
                accountState);

            PersonalAppleStatusDto status = await access.SignInAsync(new PersonalAppleSignInRequest());

            Assert.Equal("validated-recently", status.State);
            Assert.Equal(appleId, portal.LastAppleId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(AppleCredentialSources.Managed, "fallback@example.com")]
    [InlineData(AppleCredentialSources.Environment, null)]
    public async Task SignIn_WithoutConfiguredAccountFailsBeforeAppleAuthentication(
        string credentialSource,
        string? defaultAppleId)
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal();
            IAppleCredentialManagement management;
            IAppleCredentialProvider provider;
            if (string.Equals(credentialSource, AppleCredentialSources.Managed, StringComparison.Ordinal))
            {
                ManagedAppleCredentialStore store = CreateManagedStore(root);
                management = store;
                provider = store;
            }
            else
            {
                management = new ReadOnlyAppleCredentialManagement(credentialSource);
                provider = new NullCredentialProvider();
            }

            var accountState = new AppleAccountStateStore(new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json")));
            var access = new PersonalAppleAccess(
                new SessionManager(portal, provider),
                portal,
                new PersonalAppleAccessOptions(defaultAppleId, credentialSource),
                management,
                accountState);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                access.SignInAsync(new PersonalAppleSignInRequest()));
            Assert.Equal(0, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TeamSelection_RejectsStaleAppleTeamEvidence()
    {
        string root = TestDirectory();
        try
        {
            ManagedAppleCredentialStore store = CreateManagedStore(root);
            var portal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            var time = new MutableTimeProvider(new DateTimeOffset(2026, 7, 12, 10, 0, 0, TimeSpan.Zero));
            PersonalAppleAccess access = CreateAccess(root, store, portal, time);
            PersonalAppleConnectResult connected = await access.ConnectAsync(
                new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "valid-password" },
                "api-token:test");

            time.Advance(TimeSpan.FromMinutes(16));

            PersonalAppleStatusDto stale = await access.StatusAsync();
            Assert.Equal("validation-stale", stale.State);
            await Assert.ThrowsAsync<AppleTeamSelectionStaleException>(() =>
                access.SelectTeamAsync(
                    new PersonalAppleTeamSelectionRequest(connected.Status.AccountProfileId!, "TEAMID1234"),
                    "api-token:test"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(AppleCredentialSources.Environment)]
    [InlineData(AppleCredentialSources.Keychain)]
    public async Task ReadOnlyCredentialSourcesRejectConnectAndUnknownSourceNeverFallsBack(string credentialSource)
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = "unused" };
            var management = new ReadOnlyAppleCredentialManagement(credentialSource);
            var accountState = new AppleAccountStateStore(new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json")));
            var provider = new NullCredentialProvider();
            var access = new PersonalAppleAccess(
                new SessionManager(portal, provider),
                portal,
                new PersonalAppleAccessOptions("owner@example.com", credentialSource),
                management,
                accountState);

            await Assert.ThrowsAsync<AppleCredentialSourceReadOnlyException>(() =>
                access.ConnectAsync(
                    new PersonalAppleConnectRequest { AppleId = "owner@example.com", Password = "unused" },
                    "api-token:test"));
            Assert.Equal(0, portal.AuthenticationCalls);
            Assert.Throws<InvalidOperationException>(() => AppleCredentialSources.Normalize("typo-provider"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(false, "127.0.0.1", "127.0.0.1", false, false)]
    [InlineData(false, "127.0.0.1", "127.0.0.1", true, true)]
    [InlineData(false, "192.0.2.10", "127.0.0.1", true, false)]
    [InlineData(false, "127.0.0.1", "192.0.2.10", true, false)]
    [InlineData(true, "192.0.2.10", "192.0.2.11", false, true)]
    public void CredentialTransportPolicy_RequiresHttpsOrExplicitActualLoopback(
        bool isHttps,
        string local,
        string remote,
        bool allowLoopback,
        bool expected) =>
        Assert.Equal(expected, AppleCredentialTransportPolicy.IsAllowed(
            isHttps,
            IPAddress.Parse(local),
            IPAddress.Parse(remote),
            allowLoopback));

    [Fact]
    public async Task ConnectApi_OpenModeAndPlainHttpAreBlockedBeforeCredentialHandling()
    {
        string openRoot = TestDirectory();
        string protectedRoot = TestDirectory();
        try
        {
            var openPortal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using (WebApplicationFactory<Program> factory = Factory(openRoot, openPortal))
            using (HttpClient client = factory.CreateClient())
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "valid-password" });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("authentication-required", await response.Content.ReadAsStringAsync());
                Assert.Equal(0, openPortal.AuthenticationCalls);
            }

            var protectedPortal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using (WebApplicationFactory<Program> factory = Factory(protectedRoot, protectedPortal, apiToken: "test-token"))
            using (HttpClient client = factory.CreateClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "valid-password" });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("credential-entry-transport-required", await response.Content.ReadAsStringAsync());
                Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(0, protectedPortal.AuthenticationCalls);
            }
        }
        finally
        {
            DeleteDirectory(openRoot);
            DeleteDirectory(protectedRoot);
        }
    }

    [Fact]
    public async Task ConnectApi_HttpsPersistsRedactedCredentialAndTeamSelection()
    {
        string root = TestDirectory();
        const string password = "fake-api-password";
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = password };
            using WebApplicationFactory<Program> factory = Factory(root, portal, apiToken: "test-token");
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

            HttpResponseMessage connected = await client.PostAsJsonAsync(
                "/api/apple-access/personal/connect",
                new { appleId = "owner@example.com", password });

            Assert.Equal(HttpStatusCode.Created, connected.StatusCode);
            Assert.Contains("no-store", connected.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            string responseJson = await connected.Content.ReadAsStringAsync();
            Assert.DoesNotContain(password, responseJson, StringComparison.Ordinal);
            Assert.DoesNotContain("owner@example.com", responseJson, StringComparison.OrdinalIgnoreCase);
            PersonalAppleStatusDto status = (await connected.Content.ReadFromJsonAsync<PersonalAppleStatusDto>())!;
            Assert.True(status.CredentialEntry?.Supported);
            Assert.Equal("managed", status.CredentialSource);

            HttpResponseMessage selected = await client.PutAsJsonAsync(
                "/api/apple-access/personal/team",
                new { accountProfileId = status.AccountProfileId, teamId = "TEAMID1234" });
            Assert.Equal(HttpStatusCode.OK, selected.StatusCode);
            PersonalAppleStatusDto selectedStatus = (await selected.Content.ReadFromJsonAsync<PersonalAppleStatusDto>())!;
            Assert.Equal("TEAMID1234", selectedStatus.SelectedTeamId);

            PersonalAppleStatusDto readStatus = (await client.GetFromJsonAsync<PersonalAppleStatusDto>("/api/apple-access/personal/status"))!;
            Assert.True(readStatus.CredentialEntry?.AllowedNow);
            Assert.Equal("TEAMID1234", readStatus.SelectedTeamId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConnectApi_ForwardedHttpsWorksForBearerIngressOnlyFromConfiguredProxy()
    {
        string trustedRoot = TestDirectory();
        string untrustedRoot = TestDirectory();
        try
        {
            var trustedPortal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using (WebApplicationFactory<Program> factory = Factory(
                       trustedRoot,
                       trustedPortal,
                       apiToken: "test-token",
                       trustedProxy: "127.0.0.1",
                       remoteIp: IPAddress.Loopback))
            using (HttpClient client = factory.CreateClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
                client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                client.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost");
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "valid-password" });

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal(1, trustedPortal.AuthenticationCalls);
            }

            var untrustedPortal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using (WebApplicationFactory<Program> factory = Factory(
                       untrustedRoot,
                       untrustedPortal,
                       apiToken: "test-token",
                       trustedProxy: "192.0.2.10",
                       remoteIp: IPAddress.Loopback))
            using (HttpClient client = factory.CreateClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
                client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "valid-password" });

                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Contains("credential-entry-transport-required", await response.Content.ReadAsStringAsync());
                Assert.Equal(0, untrustedPortal.AuthenticationCalls);
            }
        }
        finally
        {
            DeleteDirectory(trustedRoot);
            DeleteDirectory(untrustedRoot);
        }
    }

    [Fact]
    public async Task ConnectApi_RejectsCrossOriginBearerRequestBeforeCredentialHandling()
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using WebApplicationFactory<Program> factory = Factory(root, portal, apiToken: "test-token");
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/apple-access/personal/connect")
            {
                Content = JsonContent.Create(new { appleId = "owner@example.com", password = "valid-password" }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            request.Headers.Add("Origin", "https://attacker.example");

            HttpResponseMessage response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Contains("origin-or-antiforgery", await response.Content.ReadAsStringAsync());
            Assert.Equal(0, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConnectApi_OidcCookieRequiresSameOriginAntiforgeryToken()
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = "valid-password" };
            using WebApplicationFactory<Program> factory = Factory(root, portal, oidc: true);
            await BootstrapOidcOwnerAsync(factory);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
            });

            HttpResponseMessage statusResponse = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
            string csrf = Assert.Single(statusResponse.Headers.GetValues("X-Sideport-CSRF"));

            using (var missingToken = new HttpRequestMessage(HttpMethod.Post, "/api/apple-access/personal/connect")
            {
                Content = JsonContent.Create(new { appleId = "owner@example.com", password = "valid-password" }),
            })
            {
                missingToken.Headers.Add("Origin", "https://localhost");
                HttpResponseMessage rejected = await client.SendAsync(missingToken);
                Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
                Assert.Contains("origin-or-antiforgery", await rejected.Content.ReadAsStringAsync());
            }
            Assert.Equal(0, portal.AuthenticationCalls);

            using var protectedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/apple-access/personal/connect")
            {
                Content = JsonContent.Create(new { appleId = "owner@example.com", password = "valid-password" }),
            };
            protectedRequest.Headers.Add("Origin", "https://localhost");
            protectedRequest.Headers.Add("X-Sideport-CSRF", csrf);
            HttpResponseMessage accepted = await client.SendAsync(protectedRequest);

            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            Assert.Equal(1, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ConnectApi_EnforcesClientAndPerAccountRateLimitsWithRetryAfter()
    {
        string clientRoot = TestDirectory();
        string accountRoot = TestDirectory();
        try
        {
            var clientPortal = new FakeApplePortal { AcceptedPassword = "different-password" };
            using (WebApplicationFactory<Program> factory = Factory(
                       clientRoot,
                       clientPortal,
                       apiToken: "test-token",
                       clientPermitLimit: 1,
                       accountPermitLimit: 10))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage first = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "wrong" });
                HttpResponseMessage second = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "other@example.com", password = "wrong" });

                Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
                Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
                Assert.True(second.Headers.Contains("Retry-After"));
                Assert.Equal(1, clientPortal.AuthenticationCalls);
            }

            var accountPortal = new FakeApplePortal { AcceptedPassword = "different-password" };
            using (WebApplicationFactory<Program> factory = Factory(
                       accountRoot,
                       accountPortal,
                       apiToken: "test-token",
                       clientPermitLimit: 10,
                       accountPermitLimit: 1))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage first = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "wrong" });
                HttpResponseMessage sameAccount = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "OWNER@example.com", password = "wrong" });
                HttpResponseMessage differentAccount = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "other@example.com", password = "wrong" });

                Assert.Equal(HttpStatusCode.UnprocessableEntity, first.StatusCode);
                Assert.Equal(HttpStatusCode.TooManyRequests, sameAccount.StatusCode);
                Assert.True(sameAccount.Headers.Contains("Retry-After"));
                Assert.Equal(HttpStatusCode.UnprocessableEntity, differentAccount.StatusCode);
                Assert.Equal(2, accountPortal.AuthenticationCalls);
            }
        }
        finally
        {
            DeleteDirectory(clientRoot);
            DeleteDirectory(accountRoot);
        }
    }

    [Fact]
    public async Task ConnectApi_ReturnsTypedUpstreamAndSecurityHeaderResponses()
    {
        string unavailableRoot = TestDirectory();
        string limitedRoot = TestDirectory();
        try
        {
            var unavailablePortal = new FakeApplePortal
            {
                AuthenticateException = new HttpRequestException(
                    "upstream unavailable",
                    inner: null,
                    HttpStatusCode.BadGateway),
            };
            using (WebApplicationFactory<Program> factory = Factory(unavailableRoot, unavailablePortal, apiToken: "test-token"))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "password" });
                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Contains("apple-authentication-unavailable", await response.Content.ReadAsStringAsync());
                Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
                Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
            }

            var limitedPortal = new FakeApplePortal
            {
                AuthenticateException = new HttpRequestException(
                    "upstream limited",
                    inner: null,
                    HttpStatusCode.TooManyRequests),
            };
            using (WebApplicationFactory<Program> factory = Factory(limitedRoot, limitedPortal, apiToken: "test-token"))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "password" });
                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.True(response.Headers.Contains("Retry-After"));
                Assert.Contains("apple-auth-rate-limited", await response.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            DeleteDirectory(unavailableRoot);
            DeleteDirectory(limitedRoot);
        }
    }

    [Fact]
    public async Task ConnectApi_ClassifiesWrappedAppleHttpFailuresUsingContractCodes()
    {
        string limitedRoot = TestDirectory();
        string unavailableRoot = TestDirectory();
        try
        {
            var limitedPortal = new FakeApplePortal
            {
                AuthenticateException = new GrandSlamException(
                    "sanitized GrandSlam throttle",
                    statusCode: HttpStatusCode.TooManyRequests),
            };
            using (WebApplicationFactory<Program> factory = Factory(limitedRoot, limitedPortal, apiToken: "test-token"))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "password" });

                Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
                Assert.True(response.Headers.Contains("Retry-After"));
                Assert.Contains("apple-auth-rate-limited", await response.Content.ReadAsStringAsync());
            }

            var unavailablePortal = new FakeApplePortal
            {
                AcceptedPassword = "password",
                ListTeamsException = new DeveloperServicesException(
                    "sanitized developer-services outage",
                    statusCode: HttpStatusCode.ServiceUnavailable),
            };
            using (WebApplicationFactory<Program> factory = Factory(unavailableRoot, unavailablePortal, apiToken: "test-token"))
            using (HttpClient client = HttpsBearerClient(factory))
            {
                HttpResponseMessage response = await client.PostAsJsonAsync(
                    "/api/apple-access/personal/connect",
                    new { appleId = "owner@example.com", password = "password" });

                Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
                Assert.Contains("apple-authentication-unavailable", await response.Content.ReadAsStringAsync());
            }
        }
        finally
        {
            DeleteDirectory(limitedRoot);
            DeleteDirectory(unavailableRoot);
        }
    }

    [Fact]
    public async Task ConnectApi_ReadOnlyEnvironmentProviderReturnsConflict()
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = "unused" };
            using WebApplicationFactory<Program> factory = Factory(
                root,
                portal,
                apiToken: "test-token",
                credentialSource: AppleCredentialSources.Environment);
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/apple-access/personal/connect",
                new { appleId = "owner@example.com", password = "unused" });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("credential-source-read-only", await response.Content.ReadAsStringAsync());
            Assert.Equal(0, portal.AuthenticationCalls);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ReplacementCandidateApi_RequiresOwnerHttpsAndSameOrigin()
    {
        string root = TestDirectory();
        try
        {
            var portal = new FakeApplePortal { AcceptedPassword = "new-password" };
            using WebApplicationFactory<Program> factory = Factory(root, portal, apiToken: "test-token");
            ManagedAppleCredentialStore store = factory.Services.GetRequiredService<ManagedAppleCredentialStore>();
            await store.CommitAuthenticatedAsync("owner@example.com", "old-password", "api-token:test");

            using HttpClient plain = factory.CreateClient();
            plain.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            HttpResponseMessage insecure = await plain.PostAsJsonAsync("/api/apple-access/personal/replacement-candidates", new { appleId = "replacement@example.com", password = "new-password" });
            Assert.Equal(HttpStatusCode.Forbidden, insecure.StatusCode);

            using HttpClient noAuth = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            HttpResponseMessage anonymous = await noAuth.PostAsJsonAsync("/api/apple-access/personal/replacement-candidates", new { appleId = "replacement@example.com", password = "new-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            using HttpClient crossOrigin = HttpsBearerClient(factory);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/apple-access/personal/replacement-candidates") { Content = JsonContent.Create(new { appleId = "replacement@example.com", password = "new-password" }) };
            request.Headers.Add("Origin", "https://evil.example");
            HttpResponseMessage crossSite = await crossOrigin.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, crossSite.StatusCode);

            using HttpClient owner = HttpsBearerClient(factory);
            HttpResponseMessage accepted = await owner.PostAsJsonAsync("/api/apple-access/personal/replacement-candidates", new { appleId = "replacement@example.com", password = "new-password" });
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            string body = await accepted.Content.ReadAsStringAsync();
            Assert.DoesNotContain("new-password", body, StringComparison.Ordinal);
            Assert.Equal("old-password", await store.GetPasswordAsync("owner@example.com"));
        }
        finally { DeleteDirectory(root); }
    }

    private static PersonalAppleAccess CreateAccess(
        string root,
        ManagedAppleCredentialStore store,
        FakeApplePortal portal,
        TimeProvider? timeProvider = null)
    {
        var accountState = new AppleAccountStateStore(new AppleAccountStateStoreOptions(Path.Combine(root, "apple-account.json")));
        return new PersonalAppleAccess(
            new SessionManager(portal, store),
            portal,
            new PersonalAppleAccessOptions(null, AppleCredentialSources.Managed),
            store,
            accountState,
            timeProvider);
    }

    private static ManagedAppleCredentialStore CreateManagedStore(string root)
    {
        string credentialDirectory = Path.Combine(root, "apple-credentials");
        string keyRingDirectory = Path.Combine(root, "data-protection-keys");
        Directory.CreateDirectory(keyRingDirectory);
        IDataProtectionProvider provider = DataProtectionProvider.Create(
            new DirectoryInfo(keyRingDirectory),
            builder => builder.SetApplicationName("Sideport.ManagedAppleCredential"));
        return new ManagedAppleCredentialStore(
            new ManagedAppleCredentialStoreOptions(credentialDirectory, keyRingDirectory),
            provider);
    }

    private static WebApplicationFactory<Program> Factory(
        string stateDirectory,
        FakeApplePortal portal,
        string? apiToken = null,
        string credentialSource = AppleCredentialSources.Managed,
        string? trustedProxy = null,
        bool oidc = false,
        int? clientPermitLimit = null,
        int? accountPermitLimit = null,
        IPAddress? remoteIp = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sideport:Apple:DeviceId", "TEST-MANAGED-APPLE-DEVICE");
            builder.UseSetting("Sideport:Apple:CredentialSource", credentialSource);
            builder.UseSetting("Sideport:State:Directory", stateDirectory);
            builder.UseSetting("Sideport:Scheduler:Enabled", "false");
            builder.UseSetting("Sideport:Signer:BinaryPath", typeof(ManagedAppleAccessTests).Assembly.Location);
            if (apiToken is not null)
                builder.UseSetting("Sideport:Api:AuthToken", apiToken);
            if (trustedProxy is not null)
                builder.UseSetting("Sideport:ReverseProxy:KnownProxies:0", trustedProxy);
            if (clientPermitLimit is not null)
                builder.UseSetting("Sideport:Apple:CredentialRateLimit:ClientPermitLimit", clientPermitLimit.Value.ToString());
            if (accountPermitLimit is not null)
                builder.UseSetting("Sideport:Apple:CredentialRateLimit:AccountPermitLimit", accountPermitLimit.Value.ToString());
            if (oidc)
            {
                builder.UseSetting("Sideport:Oidc:Enabled", "true");
                builder.UseSetting("Sideport:Oidc:Authority", "https://identity.example");
                builder.UseSetting("Sideport:Oidc:ClientId", "sideport-tests");
                builder.UseSetting("Sideport:Oidc:ClientSecret", "test-only-secret");
            }
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAppleDeveloperPortal>();
                services.AddSingleton<IAppleDeveloperPortal>(portal);
                if (remoteIp is not null)
                    services.AddSingleton<IStartupFilter>(new TestRemoteIpStartupFilter(remoteIp));
                if (oidc)
                {
                    services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = TestOidcAuthHandler.AuthenticationSchemeName;
                            options.DefaultScheme = TestOidcAuthHandler.AuthenticationSchemeName;
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestOidcAuthHandler>(
                            TestOidcAuthHandler.AuthenticationSchemeName,
                            _ => { });
                }
            });
        });

    private static HttpClient HttpsBearerClient(WebApplicationFactory<Program> factory)
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }

    private static async Task BootstrapOidcOwnerAsync(WebApplicationFactory<Program> factory)
    {
        WorkspaceAccessStore store = factory.Services.GetRequiredService<WorkspaceAccessStore>();
        WorkspaceOwnerClaimCreateResult claim = await store.CreateOwnerClaimAsync(
            new WorkspaceOwnerClaimCreateRequest(
                ExpectedOwnerMemberId: null,
                ImpactVersion: null,
                TimeSpan.FromMinutes(15),
                "managed-apple-owner-claim",
                "managed-apple-bootstrap"));
        WorkspaceHandoffCreateResult handoff = await store.ExchangeOwnerClaimAsync(
            claim.Token!,
            "managed-apple-handoff");
        await store.AcceptOwnerClaimAsync(
            handoff.Token,
            new WorkspaceAcceptanceRequest(
                new WorkspaceIdentityKey("https://identity.example", "owner-subject"),
                "Sideport Owner",
                "owner@example.test",
                "managed-apple-owner-accept",
                "managed-apple-accept"));
    }

    private static string TestDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "sideport-managed-apple-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class NullCredentialProvider : IAppleCredentialProvider
    {
        public Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FixedCredentialProvider(string appleId, string password) : IAppleCredentialProvider
    {
        public Task<string?> GetPasswordAsync(string requestedAppleId, CancellationToken ct = default) =>
            Task.FromResult<string?>(
                string.Equals(requestedAppleId, appleId, StringComparison.OrdinalIgnoreCase)
                    ? password
                    : null);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        private readonly List<ScheduledTimer> _timers = [];

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ScheduledTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            foreach (ScheduledTimer timer in _timers.ToArray())
                timer.FireIfDue(_utcNow);
        }

        private void Remove(ScheduledTimer timer) => _timers.Remove(timer);

        private sealed class ScheduledTimer : ITimer
        {
            private readonly MutableTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private TimeSpan _period;
            private DateTimeOffset _dueAt;
            private bool _disposed;

            public ScheduledTimer(
                MutableTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                _period = period;
                _dueAt = DueAt(owner._utcNow, dueTime);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed)
                    return false;
                _period = period;
                _dueAt = DueAt(_owner._utcNow, dueTime);
                return true;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _owner.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_disposed || now < _dueAt)
                    return;
                _dueAt = _period == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : now.Add(_period);
                _callback(_state);
            }

            private static DateTimeOffset DueAt(DateTimeOffset now, TimeSpan dueTime) =>
                dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : now.Add(dueTime);
        }
    }

    private sealed class FakeApplePortal : IAppleDeveloperPortal
    {
        public string AcceptedPassword { get; set; } = "valid-password";
        public Exception? AuthenticateException { get; init; }
        public Exception? ListTeamsException { get; init; }
        public bool RequireTwoFactor { get; set; }
        public int AuthenticationCalls { get; private set; }
        public string? LastAppleId { get; private set; }
        public string? LastTwoFactorCode { get; private set; }
        public string AcceptedTwoFactorCode { get; init; } = "123456";
        public IReadOnlyList<AppleTeam> Teams { get; init; } =
            [new AppleTeam("TEAMID1234", "Personal Team", "Individual")];

        public Task<AppleLoginResult> AuthenticateAsync(string appleId, string password, CancellationToken ct = default)
        {
            AuthenticationCalls++;
            LastAppleId = appleId;
            if (AuthenticateException is not null)
                throw AuthenticateException;
            if (!string.Equals(password, AcceptedPassword, StringComparison.Ordinal))
                throw new InvalidOperationException("invalid fake credential");
            if (RequireTwoFactor)
            {
                return Task.FromResult<AppleLoginResult>(new AppleLoginResult.TwoFactorRequired(
                    new AppleLoginChallenge("fake-adsid", "fake-token", TwoFactorKind.TrustedDevice)));
            }
            return Task.FromResult<AppleLoginResult>(new AppleLoginResult.Success(
                new AppleSession(appleId, "fake-adsid", appleId, [1, 2, 3]) { IdmsToken = "fake-session-token" }));
        }

        public Task SubmitTwoFactorCodeAsync(AppleLoginChallenge challenge, string code, CancellationToken ct = default)
        {
            LastTwoFactorCode = code;
            if (!string.Equals(code, AcceptedTwoFactorCode, StringComparison.Ordinal))
                throw new InvalidOperationException("invalid fake two-factor code");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AppleTeam>> ListTeamsAsync(AppleSession session, CancellationToken ct = default)
        {
            if (ListTeamsException is not null)
                throw ListTeamsException;
            return Task.FromResult(Teams);
        }

        public Task RegisterDeviceAsync(AppleSession session, string teamId, string udid, string name, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<SigningCertificate> EnsureCertificateAsync(AppleSession session, string teamId, byte[] csrDer, CancellationToken ct = default) =>
            Task.FromResult(new SigningCertificate("fake-serial", [], DateTimeOffset.UtcNow.AddYears(1)));

        public Task<ProvisioningProfile> EnsureProfileAsync(AppleSession session, string teamId, string bundleId, CancellationToken ct = default) =>
            Task.FromResult(new ProvisioningProfile("fake-profile", bundleId, [], DateTimeOffset.UtcNow.AddDays(7)));
    }

    private sealed class TestOidcAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        WorkspaceAccessStore workspace)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string AuthenticationSchemeName = "test-oidc";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim>
            {
                new Claim("sub", "owner-subject"),
                new Claim("iss", "https://identity.example"),
                new Claim(
                    WorkspaceRequestPrincipalResolver.ValidatedIssuerClaimType,
                    "https://identity.example"),
                new Claim(ClaimTypes.Name, "Sideport Owner"),
            };
            WorkspaceAccessDocument? document = await workspace.ReadAsync(Context.RequestAborted);
            if (document?.Workspace.State == WorkspaceLifecycleState.Active)
            {
                claims.Add(new Claim(
                    WorkspaceRequestPrincipalResolver.SecurityEpochClaimType,
                    document.Workspace.SecurityEpoch));
            }
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationSchemeName));
            return AuthenticateResult.Success(
                new AuthenticationTicket(principal, AuthenticationSchemeName));
        }
    }

    private sealed class TestRemoteIpStartupFilter(IPAddress remoteIp) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) =>
            app =>
            {
                app.Use(following => async context =>
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                    await following(context);
                });
                next(app);
            };
    }
}
