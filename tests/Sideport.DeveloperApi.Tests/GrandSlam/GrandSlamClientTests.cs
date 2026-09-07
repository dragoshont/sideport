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

    private static GrandSlamClient ClientFor(
        HttpMessageHandler handler, GrandSlamClientOptions options) =>
        new(
            new HttpClient(handler),
            new StubAnisetteProvider(),
            options,
            NullLogger<GrandSlamClient>.Instance);

    [Fact]
    public async Task Authenticate_HappyPath_ReturnsUsableSession()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler);

        AppleLoginResult result = await client.AuthenticateAsync(Username, Password);

        var success = Assert.IsType<AppleLoginResult.Success>(result);
        Assert.Equal(Username, success.Session.AppleId);
        Assert.Equal("000123-04-deadbeef", success.Session.Adsid);
        // IdmsToken now carries the APP-SPECIFIC token fetched after login (the
        // dev-API X-Apple-GS-Token), not the raw login GsIdmsToken.
        Assert.Equal("fake-app-token", success.Session.IdmsToken);
        Assert.Equal("Test Person", success.Session.AccountName);
        Assert.Equal(32, success.Session.SessionKey.Length);
        Assert.NotEqual("test-idms-token", success.Session.IdmsToken);
        Assert.Equal(HttpVersion.Version11, handler.LastHttpVersion);
        Assert.Equal(
            "AuthKit/1 (Macintosh; OS X 26.6) (com.apple.dt.Xcode/26.0)",
            handler.LastUserAgent);
        Assert.Equal(
            "<iMac11,3> <macOS;26.6;25G72> <com.apple.AuthKit/1 (com.apple.dt.Xcode/26.0)>",
            handler.LastClientInfo);
    }

    [Fact]
    public async Task Authenticate_AttachesAnisetteHeadersPerRequest()
    {
        var anisette = new StubAnisetteProvider();
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(handler, anisette);

        await client.AuthenticateAsync(Username, Password);

        // init + complete + the app-tokens round each fetch fresh anisette headers.
        Assert.Equal(3, anisette.HeaderCalls);
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
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    [Fact]
    public async Task Authenticate_AppToken503Then200_RetriesOnlyAppTokenExchange()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenFailuresRemaining = 1,
        };
        GrandSlamClient client = ClientFor(handler);

        var result = Assert.IsType<AppleLoginResult.Success>(
            await client.AuthenticateAsync(Username, Password));

        Assert.Equal("fake-app-token", result.Session.IdmsToken);
        Assert.Equal(1, handler.OperationCalls("init"));
        Assert.Equal(1, handler.OperationCalls("complete"));
        Assert.Equal(2, handler.OperationCalls("apptokens"));
    }

    [Fact]
    public async Task Authenticate_AppToken503Exhaustion_IsBoundedToThreeAttempts()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenFailuresRemaining = 3,
        };
        GrandSlamClient client = ClientFor(handler);

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(3, handler.OperationCalls("apptokens"));
    }

    [Fact]
    public async Task Authenticate_Complete503_IsNeverRetried()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            CompleteHttpStatus = HttpStatusCode.ServiceUnavailable,
        };
        GrandSlamClient client = ClientFor(handler);

        await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Equal(1, handler.OperationCalls("init"));
        Assert.Equal(1, handler.OperationCalls("complete"));
        Assert.Equal(0, handler.OperationCalls("apptokens"));
    }

    [Fact]
    public async Task SubmitCode_503_IsNeverRetried()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.TrustedDevice)
        {
            CodeValidationHttpStatus = HttpStatusCode.ServiceUnavailable,
        };
        GrandSlamClient client = ClientFor(handler);
        var challenge = Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await client.AuthenticateAsync(Username, Password));

        await Assert.ThrowsAsync<GrandSlamException>(
            () => client.SubmitTwoFactorCodeAsync(challenge.Challenge, "123456"));

        Assert.Equal(1, handler.CodeValidationCalls);
    }

    [Fact]
    public async Task Authenticate_TrustedDevicePrompt503_IsNeverRetried()
    {
        var handler = new FakeGrandSlamHandler(
            Username, PasswordKey(), Salt, Iterations,
            FakeGrandSlamHandler.TwoFactorMode.TrustedDevice)
        {
            TrustedDeviceHttpStatus = HttpStatusCode.ServiceUnavailable,
        };

        Assert.IsType<AppleLoginResult.TwoFactorRequired>(
            await ClientFor(handler).AuthenticateAsync(Username, Password));

        Assert.Equal(1, handler.TrustedDevicePromptCalls);
        Assert.Equal(0, handler.OperationCalls("apptokens"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Authenticate_NonRetryableInitStatus_IsAttemptedOnce(HttpStatusCode status)
    {
        var handler = new CountingStatusHandler(status);
        GrandSlamClient client = ClientFor(handler, FastOptions());

        await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Authenticate_CallerCancellation_StopsAppTokenRetry()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenFailuresRemaining = 3,
            AppTokenRetryAfter = TimeSpan.FromSeconds(1),
        };
        GrandSlamClient client = ClientFor(handler);
        using var cts = new CancellationTokenSource();
        Task<AppleLoginResult> authentication = client.AuthenticateAsync(Username, Password, cts.Token);
        await handler.AppTokenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => authentication);

        Assert.Equal(1, handler.OperationCalls("apptokens"));
    }

    [Fact]
    public async Task Authenticate_AppTokenRetry_FetchesFreshAnisetteForEachAttempt()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenFailuresRemaining = 1,
        };
        var anisette = new StubAnisetteProvider();
        GrandSlamClient client = ClientFor(handler, anisette);

        Assert.IsType<AppleLoginResult.Success>(await client.AuthenticateAsync(Username, Password));
        Assert.Equal(2, handler.OperationCalls("apptokens"));
        Assert.Equal(4, anisette.HeaderCalls);
    }

    [Fact]
    public async Task Authenticate_OverallBudget_CoversAnisette()
    {
        var handler = new CountingStatusHandler(HttpStatusCode.OK);
        var client = new GrandSlamClient(
            new HttpClient(handler),
            new WaitingAnisetteProvider(),
            new GrandSlamClientOptions
            {
                DeviceId = "test",
                AuthenticationTimeout = TimeSpan.FromMilliseconds(50),
                AttemptTimeout = TimeSpan.FromSeconds(10),
                ExchangeTimeout = TimeSpan.FromSeconds(20),
            },
            NullLogger<GrandSlamClient>.Instance);

        GrandSlamException error = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Contains("overall login budget", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class WaitingAnisetteProvider : IAnisetteProvider
    {
        public Task<AnisetteClientInfo> GetClientInfoAsync(CancellationToken ct = default) =>
            Task.FromResult(new AnisetteClientInfo("test", "test"));

        public async Task<AnisetteHeaders> GetHeadersAsync(CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("The test wait must end by cancellation.");
        }
    }

    [Fact]
    public async Task Authenticate_AttemptTimeout_IsBoundedAndSanitized()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenDelay = TimeSpan.FromSeconds(5),
        };
        GrandSlamClient client = ClientFor(handler, new GrandSlamClientOptions
        {
            DeviceId = "test",
            AttemptTimeout = TimeSpan.FromSeconds(1),
            ExchangeTimeout = TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(5),
            MaximumRetryDelay = TimeSpan.FromMilliseconds(100),
        });

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Contains("apptokens timed out", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Username, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("test-idms-token", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authenticate_RetryAfterBeyondBudget_FailsRatherThanRetryingEarly()
    {
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            AppTokenFailuresRemaining = 1,
            AppTokenRetryAfter = TimeSpan.FromSeconds(10),
        };
        GrandSlamClient client = ClientFor(handler, FastOptions());

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Contains("retry delay exceeds budget", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.OperationCalls("apptokens"));
    }

    [Fact]
    public async Task Authenticate_TamperedAppTokenChecksum_IsRejectedByIndependentOracle()
    {
        var oracle = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations);
        GrandSlamClient client = ClientFor(
            new TamperAppTokenChecksumHandler(oracle), FastOptions());

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal(1, oracle.OperationCalls("apptokens"));
    }

    private static GrandSlamClientOptions FastOptions() => new()
    {
        DeviceId = "test",
        AttemptTimeout = TimeSpan.FromMilliseconds(250),
        ExchangeTimeout = TimeSpan.FromSeconds(1),
        RetryDelay = TimeSpan.FromMilliseconds(5),
        MaximumRetryDelay = TimeSpan.FromMilliseconds(100),
    };

    private sealed class CountingStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }

    [Fact]
    public async Task Authenticate_ServerReturns429_PreservesHttpStatus()
    {
        var http = new HttpClient(new FixedStatusHandler(HttpStatusCode.TooManyRequests));
        var client = new GrandSlamClient(
            http,
            new StubAnisetteProvider(),
            new GrandSlamClientOptions { DeviceId = "test" },
            NullLogger<GrandSlamClient>.Instance);

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Equal(HttpStatusCode.TooManyRequests, ex.StatusCode);
        Assert.Null(ex.ErrorCode);
    }

    [Fact]
    public async Task Authenticate_MalformedSpd_DoesNotExposeDecryptedByteFragments()
    {
        byte[] sensitivePlaintext = [0xDE, 0xAD, 0xBE, 0xEF, 0xFA, 0xCE, 0xCA, 0xFE];
        var handler = new FakeGrandSlamHandler(Username, PasswordKey(), Salt, Iterations)
        {
            SpdPlaintextOverride = sensitivePlaintext,
        };
        GrandSlamClient client = ClientFor(handler);

        GrandSlamException ex = await Assert.ThrowsAsync<GrandSlamException>(
            () => client.AuthenticateAsync(Username, Password));

        Assert.Contains("plain=8B", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(sensitivePlaintext), ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("head=", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("tail=", ex.Message, StringComparison.Ordinal);
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
