using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Tests.Support;
using Sideport.GrandSlam;

namespace Sideport.DeveloperApi.Tests.GrandSlam;

/// <summary>
/// End-to-end tests for <see cref="GrandSlamClient"/> driven through the fake
/// GsService2 transport. These exercise the full login chain — SRP, plist
/// encode/decode, SPD CBC decryption, and the 2FA continuation — against an
/// independent server implementation, with no Apple contact.
/// </summary>
public class GrandSlamClientTests
{
    private const string Username = "person@example.com";
    private const string Password = "correct horse battery staple";
    private static readonly byte[] Salt = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private const int Iterations = 1000;

    private static byte[] PasswordKey() =>
        GrandSlamCrypto.DerivePasswordKey(Password, Salt, Iterations);

    private static GrandSlamClient ClientFor(FakeGrandSlamHandler handler, StubAnisetteProvider? anisette = null)
    {
        var http = new HttpClient(handler);
        return new GrandSlamClient(
            http,
            anisette ?? new StubAnisetteProvider(),
            new GrandSlamClientOptions { DeviceId = "11111111-2222-3333-4444-555555555555" },
            NullLogger<GrandSlamClient>.Instance);
    }

    [Fact]
    public async Task Authenticate_HappyPath_ReturnsUsableSession()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler);

        AppleLoginResult result = await client.AuthenticateAsync(Username, Password);

        var success = Assert.IsType<AppleLoginResult.Success>(result);
        Assert.Equal(Username, success.Session.AppleId);
        Assert.Equal("000123-04-deadbeef", success.Session.Adsid);
        Assert.Equal("test-idms-token", success.Session.IdmsToken);
        Assert.Equal("Test Person", success.Session.AccountName);
        Assert.Equal(32, success.Session.SessionKey.Length);
    }

    [Fact]
    public async Task Authenticate_AttachesAnisetteHeadersPerRequest()
    {
        var anisette = new StubAnisetteProvider();
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler, anisette);

        await client.AuthenticateAsync(Username, Password);

        // init + complete each fetch fresh anisette headers.
        Assert.Equal(2, anisette.HeaderCalls);
    }

    [Fact]
    public async Task Authenticate_S2kFoProtocol_HexExpandsPasswordKey()
    {
        // When the server advertises s2k_fo, the client must hex-expand the
        // inner SHA-256 before PBKDF2; the server's verifier uses the same key.
        byte[] foKey = GrandSlamCrypto.DerivePasswordKey(Password, Salt, Iterations, hexExpand: true);
        var handler = new FakeGrandSlamHandler(Username, foKey, Salt, Iterations)
        {
            Protocol = "s2k_fo",
        };
        GrandSlamClient client = ClientFor(handler);

        AppleLoginResult result = await client.AuthenticateAsync(Username, Password);

        Assert.IsType<AppleLoginResult.Success>(result);
    }

    [Fact]
    public async Task Authenticate_TrustedDevice_TriggersPromptAndReturnsChallenge()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.TrustedDevice);
        GrandSlamClient client = ClientFor(handler);

        AppleLoginResult result = await client.AuthenticateAsync(Username, Password);

        var twoFactor = Assert.IsType<AppleLoginResult.TwoFactorRequired>(result);
        Assert.Equal(TwoFactorKind.TrustedDevice, twoFactor.Challenge.Kind);
        Assert.Equal("000123-04-deadbeef", twoFactor.Challenge.Adsid);
        Assert.True(handler.TrustedDevicePromptTriggered);
    }

    [Fact]
    public async Task Authenticate_Sms_ReturnsSmsChallenge()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.Sms);
        GrandSlamClient client = ClientFor(handler);

        AppleLoginResult result = await client.AuthenticateAsync(Username, Password);

        var twoFactor = Assert.IsType<AppleLoginResult.TwoFactorRequired>(result);
        Assert.Equal(TwoFactorKind.Sms, twoFactor.Challenge.Kind);
    }

    [Fact]
    public async Task SubmitCode_ThenReauthenticate_Succeeds()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.TrustedDevice);
        GrandSlamClient client = ClientFor(handler);

        var twoFactor = Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await client.AuthenticateAsync(Username, Password));

        await client.SubmitTwoFactorCodeAsync(twoFactor.Challenge, "123456");
        Assert.True(handler.CodeValidated);

        // The factor is now satisfied; a fresh login completes.
        AppleLoginResult retry = await client.AuthenticateAsync(Username, Password);
        Assert.IsType<AppleLoginResult.Success>(retry);
    }

    [Fact]
    public async Task SubmitCode_WrongCode_ThrowsWithAppleErrorCode()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.TrustedDevice);
        GrandSlamClient client = ClientFor(handler);

        var twoFactor = Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await client.AuthenticateAsync(Username, Password));

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.SubmitTwoFactorCodeAsync(twoFactor.Challenge, "000000"));
        Assert.Equal(-28000, ex.ErrorCode);
    }

    [Fact]
    public async Task Authenticate_WrongPassword_ThrowsGrandSlamError()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler);

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, "the wrong password"));
        Assert.Equal(-22406, ex.ErrorCode);
    }

    [Fact]
    public async Task Authenticate_ServerReturns502_ThrowsGrandSlamException()
    {
        var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.BadGateway));
        var client = new GrandSlamClient(
            http,
            new StubAnisetteProvider(),
            new GrandSlamClientOptions { DeviceId = "test" },
            NullLogger<GrandSlamClient>.Instance);

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));
        Assert.Contains("502", ex.Message);
    }

    [Fact]
    public async Task Authenticate_RejectsEmptyCredentials()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => client.AuthenticateAsync("", Password));
        await Assert.ThrowsAsync<ArgumentException>(() => client.AuthenticateAsync(Username, ""));
    }
}
