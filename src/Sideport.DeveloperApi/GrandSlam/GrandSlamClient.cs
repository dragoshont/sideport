using System.Net.Http.Headers;
using System.Diagnostics;
using System.Text;
using Claunia.PropertyList;
using Microsoft.Extensions.Logging;
using Sideport.Core;
using Sideport.DeveloperApi.Plist;
using Sideport.GrandSlam;
using Sideport.GrandSlam.Crypto;
using Sideport.GrandSlam.Srp;

namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// The GrandSlam (GSA) authentication client: the two-round SRP-6a
/// plist-over-HTTPS handshake against <c>gsa.apple.com/grandslam/GsService2</c>,
/// plus the trusted-device / SMS 2FA continuation.
///
/// Clean-room from the documented GsService2 protocol (the pypush spec) — never
/// translated from AGPL AltSign source. The SRP math and the SPD decryption keys
/// come from the proven managed <c>Sideport.GrandSlam</c> primitives.
/// </summary>
internal sealed class GrandSlamClient
{
    private readonly HttpClient _http;
    private readonly IAnisetteProvider _anisette;
    private readonly GrandSlamClientOptions _options;
    private readonly ILogger<GrandSlamClient> _logger;

    public GrandSlamClient(
        HttpClient http,
        IAnisetteProvider anisette,
        GrandSlamClientOptions options,
        ILogger<GrandSlamClient> logger)
    {
        _http = http;
        _anisette = anisette;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Run the SRP handshake. Returns either a completed
    /// <see cref="AppleLoginResult.Success"/> or, when Apple demands a second
    /// factor, <see cref="AppleLoginResult.TwoFactorRequired"/> (after triggering
    /// the trusted-device prompt).
    /// </summary>
    public async Task<AppleLoginResult> AuthenticateAsync(
        string username, string password, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        using var authenticationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        authenticationCts.CancelAfter(_options.AuthenticationTimeout);
        try
        {
            return await AuthenticateCoreAsync(username, password, authenticationCts.Token);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GrandSlamException("GrandSlam authentication exceeded the overall login budget", ex);
        }
    }

    private async Task<AppleLoginResult> AuthenticateCoreAsync(
        string username, string password, CancellationToken ct)
    {
        var srp = new AppleSrpClient();
        byte[] a = srp.StartAuthentication();

        // --- Round 1: init -------------------------------------------------
        NSDictionary initResponse = await SendAsync(new Dictionary<string, object>
        {
            ["A2k"] = a,
            ["ps"] = new[] { "s2k", "s2k_fo" },
            ["u"] = username,
            ["o"] = "init",
        }, ct);
        ThrowOnError(initResponse);

        string protocol = PlistCodec.GetString(initResponse, "sp");
        bool hexExpand = protocol switch
        {
            "s2k" => false,
            "s2k_fo" => true,
            _ => throw new GrandSlamException("GrandSlam returned an unsupported SRP protocol"),
        };
        byte[] salt = PlistCodec.GetData(initResponse, "s");
        int iterations = checked((int)PlistCodec.GetLong(initResponse, "i"));
        byte[] serverB = PlistCodec.GetData(initResponse, "B");
        // The cookie is opaque and round-tripped verbatim. Real GSA sends it as a
        // plist string (not data), so keep the raw node and echo it back unchanged
        // rather than coercing its type.
        NSObject cookie = PlistCodec.GetNode(initResponse, "c");

        // --- Derive password key + client evidence -------------------------
        byte[] passwordKey = GrandSlamCrypto.DerivePasswordKey(password, salt, iterations, hexExpand);
        byte[] m1 = srp.ProcessChallenge(username, passwordKey, salt, serverB);

        // --- Round 2: complete ---------------------------------------------
        NSDictionary completeResponse = await SendAsync(new Dictionary<string, object>
        {
            ["c"] = cookie,
            ["M1"] = m1,
            ["u"] = username,
            ["o"] = "complete",
        }, ct);
        ThrowOnError(completeResponse);

        byte[] serverM2 = PlistCodec.GetData(completeResponse, "M2");
        if (!srp.VerifyServerEvidence(serverM2))
            throw new GrandSlamException("server evidence M2 did not verify — possible MITM or wrong server");

        // --- Decrypt the SPD blob with the negotiated session keys ---------
        var keys = new GrandSlamSessionKeys(srp.SessionKey);
        byte[] spdCipher = PlistCodec.GetData(completeResponse, "spd");
        byte[] spdPlain = GrandSlamCipher.DecryptCbc(keys.ExtraDataKey, keys.ExtraDataIv, spdCipher);
        NSDictionary spd;
        try
        {
            spd = PlistCodec.ParseDictionary(spdPlain);
        }
        catch (FormatException ex)
        {
            // The decrypted SPD contains Apple identity and token material. Keep
            // diagnostics to framing-independent lengths; never include plaintext
            // byte fragments in an exception that may be logged or returned.
            throw new GrandSlamException(
                $"SPD did not decrypt to a plist (cipher={spdCipher.Length}B " +
                $"plain={spdPlain.Length}B)", ex);
        }

        string adsid = PlistCodec.GetString(spd, "adsid");
        string idmsToken = PlistCodec.GetString(spd, "GsIdmsToken");
        string accountName = PlistCodec.GetStringOrNull(spd, "acname")
                             ?? PlistCodec.GetStringOrNull(spd, "fn")
                             ?? username;

        // --- 2FA decision --------------------------------------------------
        TwoFactorKind? twoFactor = DetectTwoFactor(completeResponse);
        if (twoFactor is { } kind)
        {
            _logger.LogInformation("GrandSlam login for {User} requires {Kind} 2FA", Redact(username), kind);
            if (kind == TwoFactorKind.TrustedDevice)
                await TriggerTrustedDevicePromptAsync(adsid, idmsToken, ct);

            return new AppleLoginResult.TwoFactorRequired(
                new AppleLoginChallenge(adsid, idmsToken, kind));
        }

        var session = new AppleSession(username, adsid, accountName, srp.SessionKey)
        {
            // The developer-services endpoints reject the login GsIdmsToken (1100
            // "session expired"); they need an app-specific token. Fetch it via the
            // GSA app-tokens flow using the session key + cookie delivered in the
            // SPD, and carry THAT as the dev-API X-Apple-GS-Token.
            IdmsToken = await FetchAppTokenAsync(adsid, idmsToken, spd, ct),
        };
        _logger.LogInformation("GrandSlam login for {User} succeeded (adsid {Adsid})",
            Redact(username), Redact(adsid));
        return new AppleLoginResult.Success(session);
    }

    /// <summary>
    /// Submit a 2FA code for a pending challenge. On success the trusted-device
    /// state is recorded by Apple and a subsequent
    /// <see cref="AuthenticateAsync"/> completes without a second factor.
    /// </summary>
    public async Task SubmitTwoFactorCodeAsync(
        AppleLoginChallenge challenge, string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        AnisetteHeaders anisette = await _anisette.GetHeadersAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, GrandSlamEndpoints.ValidateCode);
        ApplyIdentityHeaders(request, challenge, anisette);
        request.Headers.TryAddWithoutValidation("security-code", code);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        byte[] body = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new GrandSlamException(
                $"2FA validation HTTP {(int)response.StatusCode}",
                statusCode: response.StatusCode);

        NSDictionary parsed = PlistCodec.ParseDictionary(body);
        ThrowOnError(parsed);
    }

    // --- app-token fetch (dev-API token) -----------------------------------

    private const string XcodeAuthApp = "com.apple.gs.xcode.auth";

    /// <summary>
    /// Exchange the login session for an app-specific token usable as the
    /// developer-services <c>X-Apple-GS-Token</c>. Uses the <c>sk</c> session key
    /// and <c>c</c> cookie carried in the decrypted SPD.
    /// </summary>
    private async Task<string> FetchAppTokenAsync(
        string adsid, string idmsToken, NSDictionary spd, CancellationToken ct)
    {
        byte[] sk = PlistCodec.GetData(spd, "sk");
        byte[] cookie = PlistCodec.GetData(spd, "c");
        byte[] checksum = AppTokensChecksum(sk, adsid, XcodeAuthApp);

        NSDictionary response = await SendAsync(new Dictionary<string, object>
        {
            ["u"] = adsid,
            ["app"] = new[] { XcodeAuthApp },
            ["c"] = cookie,
            ["t"] = idmsToken,
            ["checksum"] = checksum,
            ["o"] = "apptokens",
        }, ct);
        ThrowOnError(response);

        byte[] encryptedToken = PlistCodec.GetData(response, "et");
        byte[] tokenPlistBytes = DecryptAppTokenBlob(sk, encryptedToken);
        NSDictionary tokenPlist = PlistCodec.ParseDictionary(tokenPlistBytes);

        NSDictionary tokens = PlistCodec.GetDictionary(tokenPlist, "t");
        NSDictionary appEntry = PlistCodec.GetDictionary(tokens, XcodeAuthApp);
        _logger.LogDebug("GrandSlam app-token minted for {App}", XcodeAuthApp);
        return PlistCodec.GetString(appEntry, "token");
    }

    /// <summary>HMAC-SHA256(sk, "apptokens" || adsid || appId).</summary>
    private static byte[] AppTokensChecksum(byte[] sk, string adsid, string appId)
    {
        byte[] message = System.Text.Encoding.UTF8.GetBytes("apptokens" + adsid + appId);
        return System.Security.Cryptography.HMACSHA256.HashData(sk, message);
    }

    /// <summary>
    /// Decrypt the GrandSlam app-token blob: layout
    /// <c>[3B "XYZ" AAD][16B IV][ciphertext][16B tag]</c>, AES-256-GCM under the
    /// session key, with the 3-byte version prefix as the AAD.
    /// </summary>
    private static byte[] DecryptAppTokenBlob(byte[] sk, byte[] et)
    {
        const int aadLength = 3, ivLength = 16, tagLength = 16;
        if (et.Length < aadLength + ivLength + tagLength)
            throw new GrandSlamException("app-token blob is too short");

        ReadOnlySpan<byte> span = et;
        ReadOnlySpan<byte> aad = span[..aadLength];
        if (!aad.SequenceEqual("XYZ"u8))
            throw new GrandSlamException("app-token blob has an unexpected version prefix");

        ReadOnlySpan<byte> iv = span.Slice(aadLength, ivLength);
        ReadOnlySpan<byte> tag = span[^tagLength..];
        ReadOnlySpan<byte> ciphertext = span[(aadLength + ivLength)..^tagLength];
        return GrandSlamCipher.DecryptGcm(sk, iv, ciphertext, tag, aad);
    }

    // --- request plumbing --------------------------------------------------

    private async Task<NSDictionary> SendAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        string operation = parameters["o"] as string
            ?? throw new InvalidOperationException("GrandSlam operation is missing");
        bool retryableOperation = operation is "init" or "apptokens";
        int maximumAttempts = retryableOperation ? 3 : 1;
        var exchangeTimer = Stopwatch.StartNew();
        using var exchangeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        exchangeCts.CancelAfter(_options.ExchangeTimeout);

        for (int attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            using var attemptCts =
                CancellationTokenSource.CreateLinkedTokenSource(exchangeCts.Token);
            attemptCts.CancelAfter(_options.AttemptTimeout);
            var attemptTimer = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                // OTPs and their timestamps belong to one request, including retries.
                AnisetteHeaders anisette = await _anisette.GetHeadersAsync(attemptCts.Token);
                var body = new Dictionary<string, object>
                {
                    ["Header"] = new Dictionary<string, object> { ["Version"] = GrandSlamEndpoints.ProtocolVersion },
                    ["Request"] = BuildRequest(parameters, anisette),
                };
                using HttpRequestMessage request =
                    CreateGrandSlamRequest(PlistCodec.ToXmlBytes(body), anisette);
                response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                string budget = exchangeCts.IsCancellationRequested ? "exchange" : "attempt";
                throw new GrandSlamException(
                    $"GrandSlam {operation} timed out within the {budget} budget " +
                    $"(attempt {attempt}/{maximumAttempts})", ex);
            }

            using (response)
            {
                string contentType = response.Content.Headers.ContentType?.MediaType ?? "none";
                _logger.LogInformation(
                    "GrandSlam {Operation} attempt {Attempt}/{MaximumAttempts} returned HTTP {Status} " +
                    "content-type {ContentType} HTTP/{HttpVersion} in {ElapsedMs}ms",
                    operation, attempt, maximumAttempts, (int)response.StatusCode, contentType,
                    response.Version, attemptTimer.ElapsedMilliseconds);

                if (response.IsSuccessStatusCode)
                {
                    byte[] responseBody;
                    try
                    {
                        responseBody =
                            await response.Content.ReadAsByteArrayAsync(attemptCts.Token);
                    }
                    catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                    {
                        string budget =
                            exchangeCts.IsCancellationRequested ? "exchange" : "attempt";
                        throw new GrandSlamException(
                            $"GrandSlam {operation} timed out within the {budget} budget " +
                            $"(attempt {attempt}/{maximumAttempts})", ex);
                    }
                    NSDictionary parsed = PlistCodec.ParseDictionary(responseBody);
                    return PlistCodec.GetDictionary(parsed, "Response");
                }

                bool retryableStatus = response.StatusCode is
                    System.Net.HttpStatusCode.BadGateway or
                    System.Net.HttpStatusCode.ServiceUnavailable or
                    System.Net.HttpStatusCode.GatewayTimeout;
                if (!retryableStatus || attempt == maximumAttempts)
                    throw HttpFailure(operation, attempt, response, contentType);

                TimeSpan delay = GetRetryDelay(response, attempt);
                TimeSpan remaining = _options.ExchangeTimeout - exchangeTimer.Elapsed;
                if (delay > _options.MaximumRetryDelay || delay >= remaining)
                {
                    throw new GrandSlamException(
                        $"GrandSlam {operation} HTTP {(int)response.StatusCode} " +
                        $"(attempt {attempt}/{maximumAttempts}, retry delay exceeds budget, " +
                        $"content-type {contentType}, HTTP/{response.Version})",
                        statusCode: response.StatusCode);
                }

                _logger.LogWarning(
                    "GrandSlam {Operation} retry scheduled after {DelayMs}ms " +
                    "(attempt {Attempt}/{MaximumAttempts}, status {Status})",
                    operation, delay.TotalMilliseconds, attempt, maximumAttempts,
                    (int)response.StatusCode);
                response.Dispose();
                try
                {
                    await Task.Delay(delay, exchangeCts.Token);
                }
                catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    throw new GrandSlamException(
                        $"GrandSlam {operation} timed out within the exchange budget " +
                        $"(attempt {attempt}/{maximumAttempts})", ex);
                }
            }
        }

        throw new InvalidOperationException("GrandSlam retry loop completed unexpectedly");
    }

    private static GrandSlamException HttpFailure(
        string operation,
        int attempt,
        HttpResponseMessage response,
        string contentType) =>
        new(
            $"GrandSlam {operation} HTTP {(int)response.StatusCode} " +
            $"(attempt {attempt}, content-type {contentType}, HTTP/{response.Version})",
            statusCode: response.StatusCode);

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;

        if (response.Headers.RetryAfter?.Date is { } date)
        {
            TimeSpan dateDelay = date - DateTimeOffset.UtcNow;
            return dateDelay < TimeSpan.Zero ? TimeSpan.Zero : dateDelay;
        }

        double multiplier = Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(_options.RetryDelay.TotalMilliseconds * multiplier);
    }

    private static HttpRequestMessage CreateGrandSlamRequest(
        byte[] payload, AnisetteHeaders anisette)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GrandSlamEndpoints.GsService2)
        {
            Version = System.Net.HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue(GrandSlamEndpoints.PlistContentType);
        request.Headers.UserAgent.ParseAdd(GrandSlamEndpoints.AuthKitUserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.TryAddWithoutValidation(
            "X-MMe-Client-Info",
            string.IsNullOrEmpty(anisette.ClientInfo)
                ? GrandSlamHeaders.ClientInfo
                : anisette.ClientInfo);
        return request;
    }

    private Dictionary<string, object> BuildRequest(
        Dictionary<string, object> parameters, AnisetteHeaders anisette)
    {
        var request = new Dictionary<string, object>
        {
            ["cpd"] = GrandSlamHeaders.BuildCpd(anisette, _options.DeviceId),
        };
        foreach ((string key, object value) in parameters)
            request[key] = value;
        return request;
    }

    private async Task TriggerTrustedDevicePromptAsync(string adsid, string idmsToken, CancellationToken ct)
    {
        AnisetteHeaders anisette = await _anisette.GetHeadersAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, GrandSlamEndpoints.TrustedDeviceVerify);
        ApplyIdentityHeaders(request, new AppleLoginChallenge(adsid, idmsToken, TwoFactorKind.TrustedDevice), anisette);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        // The response is an HTML form we don't consume; a non-2xx here is not
        // fatal (the prompt may still fire), so we only log it.
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("trusted-device prompt returned HTTP {Status}", (int)response.StatusCode);
    }

    private void ApplyIdentityHeaders(
        HttpRequestMessage request, AppleLoginChallenge challenge, AnisetteHeaders anisette)
    {
        string identityToken = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{challenge.Adsid}:{challenge.IdmsToken}"));
        request.Headers.TryAddWithoutValidation("X-Apple-Identity-Token", identityToken);
        request.Headers.TryAddWithoutValidation("User-Agent", "Xcode");
        request.Headers.TryAddWithoutValidation("Accept", "text/x-xml-plist");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-us");

        foreach ((string key, object value) in
                 GrandSlamHeaders.BuildHeaders(anisette, _options.DeviceId, includeClientInfo: true))
        {
            request.Headers.TryAddWithoutValidation(key, value.ToString());
        }
    }

    private static TwoFactorKind? DetectTwoFactor(NSDictionary response)
    {
        if (!PlistCodec.TryGet(response, "Status", out NSObject statusObj) ||
            statusObj is not NSDictionary status ||
            !status.ContainsKey("au"))
        {
            return null;
        }

        string au = status["au"].ToString()!;
        return au switch
        {
            "trustedDeviceSecondaryAuth" => TwoFactorKind.TrustedDevice,
            "secondaryAuth" => TwoFactorKind.Sms,
            _ => throw new GrandSlamException("GrandSlam returned an unknown secondary-auth method"),
        };
    }

    private static void ThrowOnError(NSDictionary response)
    {
        NSDictionary status = response.ContainsKey("Status") && response["Status"] is NSDictionary s
            ? s
            : response;

        if (!status.ContainsKey("ec"))
            return;

        long code = status["ec"] is NSNumber n ? n.ToLong() : 0;
        if (code == 0)
            return;

        throw new GrandSlamException($"GrandSlam error {code}", code);
    }

    /// <summary>Redact most of an identifier for logs (keep a short prefix).</summary>
    private static string Redact(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 3
            ? "***"
            : value[..3] + "***";
}
