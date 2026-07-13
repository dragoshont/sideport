using Sideport.Api.WorkspaceAccess;

namespace Sideport.Api.Tests;

public sealed class WorkspaceLinkRateLimiterTests
{
    [Fact]
    public void Acquire_BoundsClientAndAuthorityWithoutRetainingTheSecret()
    {
        var limiter = new WorkspaceLinkRateLimiter();
        const string token = "spinv1_invitation_0123456789abcdef01234567_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // gitleaks:allow test fixture

        for (int index = 0; index < 10; index++)
            Assert.True(limiter.Acquire("invitation", $"client-{index}", token).Allowed);

        WorkspaceLinkRateLimitDecision authorityBlocked = limiter.Acquire(
            "invitation",
            "client-next",
            token);
        Assert.False(authorityBlocked.Allowed);
        Assert.True(authorityBlocked.RetryAfter > TimeSpan.Zero);

        var clientLimiter = new WorkspaceLinkRateLimiter();
        for (int index = 0; index < 20; index++)
        {
            Assert.True(clientLimiter.Acquire(
                "owner-claim",
                "one-client",
                $"spown1_owner_claim_{index:D24}_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG").Allowed);
        }
        Assert.False(clientLimiter.Acquire(
            "owner-claim",
            "one-client",
            "spown1_owner_claim_ffffffffffffffffffffffff_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG").Allowed);
    }

    [Fact]
    public void Acquire_MalformedTokenAliasesShareTheInvalidAuthorityBucket()
    {
        var limiter = new WorkspaceLinkRateLimiter();
        string[] aliases =
        [
            "spinv1_invitation_0123456789abcdef01234567_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG=",
            "spinv1_invitation_0123456789abcdef01234567_abcdefghijklmnopqrstuvwxyz0123456789ABCDE+",
            "spinv1_invitation_0123456789abcdef01234567_extra_abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG",
        ];

        for (int index = 0; index < 10; index++)
            Assert.True(limiter.Acquire("invitation", $"client-{index}", aliases[index % aliases.Length]).Allowed);

        Assert.False(limiter.Acquire("invitation", "blocked", aliases[0]).Allowed);
    }

    [Fact]
    public void Acquire_ResetsOnlyAfterTheWindow()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-12T12:00:00Z"));
        var limiter = new WorkspaceLinkRateLimiter(time);
        for (int index = 0; index < 10; index++)
            Assert.True(limiter.Acquire("invitation", $"client-{index}", "malformed").Allowed);
        Assert.False(limiter.Acquire("invitation", "blocked", "malformed").Allowed);

        time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(limiter.Acquire("invitation", "unblocked", "malformed").Allowed);
    }

    [Fact]
    public void AcquireMint_BoundsEachAuthenticatedActorAndClientIndependently()
    {
        var limiter = new WorkspaceLinkRateLimiter();

        for (int index = 0; index < 10; index++)
        {
            Assert.True(limiter.AcquireMint(
                "invitation",
                $"client-{index}",
                "member:owner").Allowed);
        }

        WorkspaceLinkRateLimitDecision actorBlocked = limiter.AcquireMint(
            "invitation",
            "new-client",
            "member:owner");
        Assert.False(actorBlocked.Allowed);
        Assert.True(actorBlocked.RetryAfter > TimeSpan.Zero);

        for (int index = 0; index < 20; index++)
        {
            Assert.True(limiter.AcquireMint(
                "owner-claim",
                "one-client",
                $"recovery-bearer-{index}").Allowed);
        }

        Assert.False(limiter.AcquireMint(
            "owner-claim",
            "one-client",
            "another-recovery-bearer").Allowed);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        internal void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }
}
