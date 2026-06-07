using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
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
                $"dev-api {action} HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        NSDictionary parsed = PlistCodec.ParseDictionary(Inflate(raw));
        ThrowOnResultError(action, parsed);
        return parsed;
    }

    // --- header plumbing ---------------------------------------------------

    private void ApplyHeaders(
        HttpRequestMessage request, AppleSession session, AnisetteHeaders anisette, string accept)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "Xcode");
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-us");
        request.Headers.TryAddWithoutValidation("X-Apple-App-Info", DeveloperServicesEndpoints.AppInfo);
        request.Headers.TryAddWithoutValidation("X-Xcode-Version", DeveloperServicesEndpoints.XcodeVersion);

        // Identity: the developer-services endpoints authenticate with the raw
        // adsid + IDMS token (NOT the base64 X-Apple-Identity-Token the GSA 2FA
        // path uses).
        request.Headers.TryAddWithoutValidation("X-Apple-I-Identity-Id", session.Adsid);
        request.Headers.TryAddWithoutValidation("X-Apple-GS-Token", session.IdmsToken);

        // The anisette / device header set (shared with GrandSlam).
        foreach ((string key, object value) in
                 GrandSlamHeaders.BuildHeaders(anisette, _options.DeviceId, includeClientInfo: true))
        {
            request.Headers.TryAddWithoutValidation(key, value.ToString());
        }
    }

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
