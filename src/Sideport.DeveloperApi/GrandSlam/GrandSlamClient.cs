using System.Net.Http.Headers;
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
            _ => throw new GrandSlamException($"unsupported SRP protocol '{protocol}'"),
        };
        byte[] salt = PlistCodec.GetData(initResponse, "s");
        int iterations = checked((int)PlistCodec.GetLong(initResponse, "i"));
        byte[] serverB = PlistCodec.GetData(initResponse, "B");
        byte[] cookie = PlistCodec.GetData(initResponse, "c");

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
        NSDictionary spd = PlistCodec.ParseDictionary(spdPlain);

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
            IdmsToken = idmsToken,
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
                $"2FA validation HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        NSDictionary parsed = PlistCodec.ParseDictionary(body);
        ThrowOnError(parsed);
    }

    // --- request plumbing --------------------------------------------------

    private async Task<NSDictionary> SendAsync(Dictionary<string, object> parameters, CancellationToken ct)
    {
        AnisetteHeaders anisette = await _anisette.GetHeadersAsync(ct);
        var body = new Dictionary<string, object>
        {
            ["Header"] = new Dictionary<string, object> { ["Version"] = GrandSlamEndpoints.ProtocolVersion },
            ["Request"] = BuildRequest(parameters, anisette),
        };

        byte[] payload = PlistCodec.ToXmlBytes(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, GrandSlamEndpoints.GsService2)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(GrandSlamEndpoints.PlistContentType);
        request.Headers.UserAgent.ParseAdd(GrandSlamEndpoints.AkdUserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.TryAddWithoutValidation("X-MMe-Client-Info", GrandSlamHeaders.ClientInfo);

        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        byte[] responseBody = await response.Content.ReadAsByteArrayAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new GrandSlamException(
                $"GrandSlam HTTP {(int)response.StatusCode} {response.ReasonPhrase} " +
                $"(content-type {response.Content.Headers.ContentType?.MediaType ?? "none"})");

        NSDictionary parsed = PlistCodec.ParseDictionary(responseBody);
        return PlistCodec.GetDictionary(parsed, "Response");
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
            _ => throw new GrandSlamException($"unknown secondary-auth method '{au}'"),
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

        string message = status.ContainsKey("em") ? status["em"].ToString()! : "unknown error";
        throw new GrandSlamException($"GrandSlam error {code}: {message}", code);
    }

    /// <summary>Redact most of an identifier for logs (keep a short prefix).</summary>
    private static string Redact(string value) =>
        string.IsNullOrEmpty(value) || value.Length <= 3
            ? "***"
            : value[..3] + "***";
}
