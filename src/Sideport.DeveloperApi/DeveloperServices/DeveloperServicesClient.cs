using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Claunia.PropertyList;
using Microsoft.Extensions.Logging;
using Sideport.Core;
using Sideport.DeveloperApi.GrandSlam;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi.DeveloperServices;

/// <summary>
/// Transport for <c>developerservices2.apple.com</c>: the plist "action"
/// endpoints (teams, devices, CSR, App IDs, profiles) and the JSON services
/// endpoints (certificate listing/revocation). Reimplemented clean-room from
/// Apple's documented developer-portal protocol — never translated from AGPL
/// AltSign source.
///
/// Both transports authenticate with the GrandSlam identity headers
/// (<c>X-Apple-I-Identity-Id</c> = adsid, <c>X-Apple-GS-Token</c> = the IDMS
/// token) plus the per-request anisette headers, and surface Apple's
/// <c>resultCode</c> as a <see cref="DeveloperServicesException"/>.
/// </summary>
internal sealed class DeveloperServicesClient
{
    private readonly HttpClient _http;
    private readonly IAnisetteProvider _anisette;
    private readonly GrandSlamClientOptions _options;
    private readonly ILogger<DeveloperServicesClient> _logger;

    public DeveloperServicesClient(
        HttpClient http,
        IAnisetteProvider anisette,
        GrandSlamClientOptions options,
        ILogger<DeveloperServicesClient> logger)
    {
        _http = http;
        _anisette = anisette;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// POST a plist "action" request and return the response dictionary, after
    /// verifying Apple's <c>resultCode</c>. <paramref name="teamId"/> is added to
    /// the request body when present.
    /// </summary>
    public async Task<NSDictionary> SendActionAsync(
        string action,
        AppleSession session,
        string? teamId,
        IReadOnlyDictionary<string, object>? parameters,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object>
        {
            ["clientId"] = DeveloperServicesEndpoints.ClientId,
            ["protocolVersion"] = DeveloperServicesEndpoints.ProtocolVersion,
            ["requestId"] = Guid.NewGuid().ToString().ToUpperInvariant(),
        };
        if (parameters is not null)
            foreach ((string key, object value) in parameters)
                body[key] = value;
        if (!string.IsNullOrEmpty(teamId))
            body["teamId"] = teamId;

        byte[] payload = PlistCodec.ToXmlBytes(body);
        var uri = new Uri(DeveloperServicesEndpoints.PlistServiceBase + action);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue(DeveloperServicesEndpoints.PlistContentType);
        AnisetteHeaders anisette = await _anisette.GetHeadersAsync(ct);
        ApplyHeaders(request, session, anisette, DeveloperServicesEndpoints.PlistContentType);

        _logger.LogDebug("dev-api action {Action} (team {Team})", action, teamId ?? "-");
        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        byte[] raw = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new DeveloperServicesException(
                $"dev-api {action} HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                statusCode: response.StatusCode);

        NSDictionary parsed = PlistCodec.ParseDictionary(Inflate(raw));
        long resultCode = parsed.ContainsKey("resultCode") && parsed["resultCode"] is NSNumber rc
            ? rc.ToLong() : 0;
        if (resultCode != 0)
            _logger.LogDebug("dev-api {Action} error response:\n{Xml}", action, parsed.ToXmlPropertyList());
        ThrowOnResultError(action, parsed);
        return parsed;
    }

    /// <summary>
    /// Issue a request against the JSON (<c>application/vnd.api+json</c>) services
    /// endpoints (<c>services/v1/{path}</c>) and return the parsed JSON root. The
    /// HTTP verb is tunneled via <c>X-HTTP-Method-Override</c> (GET/DELETE) over a
    /// POST, and the query (<c>teamId</c> first, then any filters) is sent in the
    /// body as a single url-encoded <c>urlEncodedQueryParams</c> string — the
    /// exact framing Apple's developer-services JSON API expects. Used for the
    /// certificate list/revoke endpoints the plist "action" surface doesn't cover.
    /// </summary>
    public async Task<JsonElement> SendServicesRequestAsync(
        string path,
        string httpMethodOverride,
        AppleSession session,
        string teamId,
        IReadOnlyDictionary<string, string>? query = null,
        CancellationToken ct = default)
    {
        var pairs = new List<string> { "teamId=" + Uri.EscapeDataString(teamId) };
        if (query is not null)
            foreach ((string key, string value) in query)
                pairs.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, string> { ["urlEncodedQueryParams"] = string.Join("&", pairs) });
        var uri = new Uri(DeveloperServicesEndpoints.JsonServiceBase + path);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(payload),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue(DeveloperServicesEndpoints.JsonContentType);
        AnisetteHeaders anisette = await _anisette.GetHeadersAsync(ct);
        ApplyHeaders(request, session, anisette, DeveloperServicesEndpoints.JsonContentType);
        request.Headers.TryAddWithoutValidation("X-HTTP-Method-Override", httpMethodOverride);

        _logger.LogDebug("dev-api services {Method} {Path} (team {Team})", httpMethodOverride, path, teamId);
        using HttpResponseMessage response = await _http.SendAsync(request, ct);
        byte[] raw = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new DeveloperServicesException(
                $"dev-api services {httpMethodOverride} {path} HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                statusCode: response.StatusCode);

        byte[] body = Inflate(raw);
        if (body.Length == 0)
            return default;
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    // --- header plumbing ---------------------------------------------------

    private void ApplyHeaders(
        HttpRequestMessage request, AppleSession session, AnisetteHeaders anisette, string accept)
    {
        // Emit the EXACT developer-services header set Xcode/AltServer send, each
        // header exactly once. Reusing GrandSlamHeaders.BuildHeaders here used to
        // DOUBLE X-Apple-App-Info / X-Xcode-Version (set explicitly *and* by
        // BuildHeaders) — the request then carried
        // "X-Apple-App-Info: com.apple.gs.xcode.auth,com.apple.gs.xcode.auth",
        // which no longer matched the app the X-Apple-GS-Token was minted for, so
        // developer-services rejected every action with resultCode 1100 "session
        // expired" even though the token + login were valid. It also leaked the
        // cpd-only "loc" / "X-Apple-I-SRL-NO" headers the transport must not send.
        string deviceId = string.IsNullOrEmpty(anisette.DeviceId) ? _options.DeviceId : anisette.DeviceId;
        string clientInfo = string.IsNullOrEmpty(anisette.ClientInfo) ? GrandSlamHeaders.ClientInfo : anisette.ClientInfo;

        request.Headers.TryAddWithoutValidation("User-Agent", "Xcode");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-us");
        request.Headers.TryAddWithoutValidation("X-Apple-App-Info", DeveloperServicesEndpoints.AppInfo);
        request.Headers.TryAddWithoutValidation("X-Xcode-Version", DeveloperServicesEndpoints.XcodeVersion);

        // Identity: the developer-services endpoints authenticate with the raw
        // adsid + app-specific GS token (NOT the base64 X-Apple-Identity-Token the
        // GSA 2FA path uses).
        request.Headers.TryAddWithoutValidation("X-Apple-I-Identity-Id", session.Adsid);
        request.Headers.TryAddWithoutValidation("X-Apple-GS-Token", session.IdmsToken);

        // Anisette / device identity — the same instant the OTP was minted for,
        // with a numeric-offset client-time (Xcode's strftime %z, not a 'Z').
        request.Headers.TryAddWithoutValidation("X-Apple-I-MD-M", anisette.MachineId);
        request.Headers.TryAddWithoutValidation("X-Apple-I-MD", anisette.OneTimePassword);
        request.Headers.TryAddWithoutValidation("X-Apple-I-MD-LU", anisette.LocalUserId);
        request.Headers.TryAddWithoutValidation(
            "X-Apple-I-MD-RINFO",
            string.IsNullOrEmpty(anisette.RoutingInfo) ? "17106176" : anisette.RoutingInfo);
        request.Headers.TryAddWithoutValidation("X-Mme-Device-Id", deviceId);
        request.Headers.TryAddWithoutValidation("X-Mme-Client-Info", clientInfo);
        request.Headers.TryAddWithoutValidation("X-Apple-I-Client-Time", FormatClientTime(anisette.ClientTime));
        request.Headers.TryAddWithoutValidation("X-Apple-Locale", "en_US");
        request.Headers.TryAddWithoutValidation("X-Apple-I-TimeZone", "UTC");
    }

    /// <summary>
    /// Format the developer-services <c>X-Apple-I-Client-Time</c> exactly like
    /// Xcode (<c>strftime "%FT%T%z"</c>): second precision with a numeric UTC
    /// offset and no colon (e.g. <c>2026-06-07T19:06:03+0000</c>), for the same
    /// instant the anisette OTP was minted for.
    /// </summary>
    private static string FormatClientTime(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss") + "+0000";

    // --- response handling -------------------------------------------------

    /// <summary>Gunzip the payload when gzip-framed; otherwise return as-is.</summary>
    private static byte[] Inflate(byte[] data)
    {
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
            return data;

        using var input = new MemoryStream(data);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Throw when Apple's <c>resultCode</c> is non-zero. The human-readable
    /// message comes from <c>userString</c>/<c>resultString</c> when present.
    /// </summary>
    private static void ThrowOnResultError(string action, NSDictionary response)
    {
        if (!response.ContainsKey("resultCode"))
            return;

        long code = response["resultCode"] is NSNumber n ? n.ToLong() : 0;
        if (code == 0)
            return;

        string message =
            (response.ContainsKey("userString") ? response["userString"].ToString() : null)
            ?? (response.ContainsKey("resultString") ? response["resultString"].ToString() : null)
            ?? "unknown error";
        throw new DeveloperServicesException($"dev-api {action} failed ({code}): {message}", code);
    }
}
