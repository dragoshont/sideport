using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Tests.Support;
using Sideport.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

/// <summary>
/// Tests the <see cref="IAppleDeveloperPortal"/> facade end-to-end through the
/// fake transport, validating the seam-level result types and the 2FA + retry
/// contract the orchestrator (P6) depends on.
/// </summary>
public class AppleDeveloperPortalTests
{
    private const string Username = "dev@example.com";
    private const string Password = "swordfish-1234";
    private static readonly byte[] Salt = Enumerable.Range(10, 32).Select(i => (byte)i).ToArray();
    private const int Iterations = 1500;

    private static (IAppleDeveloperPortal portal, FakeGrandSlamHandler handler) Build(
        FakeGrandSlamHandler.TwoFactorMode twoFactor = FakeGrandSlamHandler.TwoFactorMode.None)
    {
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey(Password, Salt, Iterations);
        var handler = new FakeGrandSlamHandler(Username, passwordKey, Salt, Iterations, twoFactor);
        var grandSlam = new GrandSlamClient(
            new HttpClient(handler),
            new StubAnisetteProvider(),
            new GrandSlamClientOptions { DeviceId = "portal-device-uuid" },
            NullLogger<GrandSlamClient>.Instance);
        var portal = new AppleDeveloperPortal(grandSlam);
        return (portal, handler);
    }

    [Fact]
    public async Task Authenticate_HappyPath_YieldsSession()
    {
        (IAppleDeveloperPortal portal, _) = Build();

        AppleLoginResult result = await portal.AuthenticateAsync(Username, Password);

        var success = Assert.IsType<AppleLoginResult.Success>(result);
        Assert.Equal(Username, success.Session.AppleId);
        Assert.NotEmpty(success.Session.Adsid);
    }

    [Fact]
    public async Task Authenticate_TwoFactorThenSubmitThenRetry_FullFlow()
    {
        (IAppleDeveloperPortal portal, FakeGrandSlamHandler handler) =
            Build(FakeGrandSlamHandler.TwoFactorMode.TrustedDevice);

        var challenge = Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await portal.AuthenticateAsync(Username, Password)).Challenge;

        await portal.SubmitTwoFactorCodeAsync(challenge, "123456");
        Assert.True(handler.CodeValidated);

        AppleLoginResult retry = await portal.AuthenticateAsync(Username, Password);
        Assert.IsType<AppleLoginResult.Success>(retry);
    }

    [Fact]
    public async Task ResourceMethods_NotYetImplemented_ThrowUntilP4()
    {
        (IAppleDeveloperPortal portal, _) = Build();
        var session = new AppleSession(Username, "adsid", "name", new byte[32]);

        await Assert.ThrowsAsync<NotImplementedException>(() => portal.ListTeamsAsync(session));
    }
}
