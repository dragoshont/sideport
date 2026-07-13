using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.Orchestrator.Tests.Support;

namespace Sideport.Orchestrator.Tests;

public class SessionManagerTests
{
    private readonly FakePortal _portal = new();
    private readonly FakeCredentialProvider _credentials = new();

    private SessionManager Build() =>
        new(_portal, _credentials, NullLogger<SessionManager>.Instance);

    [Fact]
    public async Task GetSession_FirstCall_Authenticates()
    {
        _credentials.Add("me@example.com", "pw");
        SessionManager manager = Build();

        AppleSession session = await manager.GetSessionAsync("me@example.com");

        Assert.Equal("me@example.com", session.AppleId);
        Assert.Equal(1, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task GetSession_SecondCall_UsesCache()
    {
        _credentials.Add("me@example.com", "pw");
        SessionManager manager = Build();

        await manager.GetSessionAsync("me@example.com");
        await manager.GetSessionAsync("me@example.com");

        Assert.Equal(1, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task GetSession_NoCredential_ThrowsInteractive()
    {
        SessionManager manager = Build();
        await Assert.ThrowsAsync<InteractiveLoginRequiredException>(
            () => manager.GetSessionAsync("nobody@example.com"));
    }

    [Fact]
    public async Task GetSession_TwoFactor_ThrowsInteractiveWithChallenge()
    {
        _credentials.Add("me@example.com", "pw");
        _portal.OnAuthenticate = (_, _) => new AppleLoginResult.TwoFactorRequired(
            new AppleLoginChallenge("adsid", "idms", TwoFactorKind.TrustedDevice));
        SessionManager manager = Build();

        var ex = await Assert.ThrowsAsync<InteractiveLoginRequiredException>(
            () => manager.GetSessionAsync("me@example.com"));
        Assert.NotNull(ex.Challenge);
        Assert.Equal(TwoFactorKind.TrustedDevice, ex.Challenge!.Kind);
    }

    [Fact]
    public async Task GetSession_ConcurrentCacheMiss_AuthenticatesOnce()
    {
        _credentials.Add("me@example.com", "pw");
        SessionManager manager = Build();

        Task<AppleSession>[] tasks =
        [
            .. Enumerable.Range(0, 10).Select(_ => manager.GetSessionAsync("me@example.com")),
        ];
        await Task.WhenAll(tasks);

        // The per-Apple-ID lock collapses the concurrent miss into one login.
        Assert.Equal(1, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task Invalidate_ForcesReauthentication()
    {
        _credentials.Add("me@example.com", "pw");
        SessionManager manager = Build();

        await manager.GetSessionAsync("me@example.com");
        manager.Invalidate("me@example.com");
        await manager.GetSessionAsync("me@example.com");

        Assert.Equal(2, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task CompleteTwoFactor_SubmitsCodeThenCachesSession()
    {
        _credentials.Add("me@example.com", "pw");
        SessionManager manager = Build();
        var challenge = new AppleLoginChallenge("adsid", "idms", TwoFactorKind.TrustedDevice);

        AppleSession session = await manager.CompleteTwoFactorAsync("me@example.com", challenge, "123456");

        Assert.Equal(1, _portal.SubmitCodeCalls);
        Assert.Equal("123456", _portal.LastSubmittedCode);
        Assert.Equal("me@example.com", session.AppleId);

        // Session is now cached.
        await manager.GetSessionAsync("me@example.com");
        Assert.Equal(1, _portal.AuthenticateCalls);
    }

    [Fact]
    public async Task RememberSession_SameAccountWithDifferentCasing_ReplacesCachedSession()
    {
        SessionManager manager = Build();
        var original = new AppleSession("Owner@Example.com", "old-adsid", "Owner", [1])
        {
            IdmsToken = "old-token",
        };
        var rotated = new AppleSession("owner@example.com", "new-adsid", "Owner", [2])
        {
            IdmsToken = "new-token",
        };

        manager.RememberSession(original);
        manager.RememberSession(rotated);

        AppleSession cached = await manager.GetSessionAsync("OWNER@example.com");
        Assert.Same(rotated, cached);
        Assert.Equal(0, _portal.AuthenticateCalls);
    }

    [Fact]
    public void AppleAuthenticationRecords_ToString_RedactsSecretMaterial()
    {
        const string adsid = "sensitive-adsid";
        const string loginToken = "sensitive-login-token";
        const string appleId = "private-owner@example.com";
        const string accountName = "Private Owner";
        const string sessionToken = "sensitive-session-token";
        var challenge = new AppleLoginChallenge(adsid, loginToken, TwoFactorKind.TrustedDevice);
        var session = new AppleSession(appleId, adsid, accountName, [0xDE, 0xAD, 0xBE, 0xEF])
        {
            IdmsToken = sessionToken,
        };

        string challengeText = challenge.ToString();
        string sessionText = session.ToString();

        Assert.Contains("[REDACTED]", challengeText, StringComparison.Ordinal);
        Assert.DoesNotContain(adsid, challengeText, StringComparison.Ordinal);
        Assert.DoesNotContain(loginToken, challengeText, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(appleId, sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(adsid, sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(accountName, sessionText, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionToken, sessionText, StringComparison.Ordinal);
    }
}
