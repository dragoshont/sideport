using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Sideport.Api.AppleAccess;

public sealed record AppStoreConnectOptions(
    string? KeyId,
    string? IssuerId,
    string? PrivateKeyPath,
    string BaseUrl);

public sealed record AppleAccessStatusDto(
    string Connector,
    string State,
    string SecretCustody,
    string? KeyIdSuffix,
    string? IssuerIdSuffix,
    string Message,
    IReadOnlyList<AppleAccessCapabilityDto> Capabilities);

public sealed record AppleAccessCapabilityDto(
    string Id,
    string Label,
    string Endpoint,
    string State,
    int? HttpStatus,
    string Detail,
    int? Count);

public interface IAppleAccessProbe
{
    Task<AppleAccessStatusDto> ProbeAsync(CancellationToken ct = default);
}

public sealed class AppStoreConnectProbe : IAppleAccessProbe
{
    private static readonly IReadOnlyList<ProbeTarget> Targets =
    [
        new("devices", "List registered devices", "/v1/devices"),
        new("certificates", "List signing certificates", "/v1/certificates"),
        new("bundle-ids", "List bundle IDs", "/v1/bundleIds"),
        new("profiles", "List provisioning profiles", "/v1/profiles"),
    ];

    private readonly HttpClient _http;
    private readonly AppStoreConnectOptions _options;

    public AppStoreConnectProbe(HttpClient http, AppStoreConnectOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<AppleAccessStatusDto> ProbeAsync(CancellationToken ct = default)
    {
        var missing = MissingConfiguration();
        if (missing.Count > 0)
        {
            return new AppleAccessStatusDto(
                "app-store-connect-jwt",
                "not-configured",
                "server-configured-key-reference",
                RedactSuffix(_options.KeyId),
                RedactSuffix(_options.IssuerId),
                $"Missing {string.Join(", ", missing)}. Configure server-side App Store Connect team key references before probing.",
                Targets.Select(target => new AppleAccessCapabilityDto(
                    target.Id, target.Label, target.Endpoint, "not-checked", null,
                    "Connector is not configured.", null)).ToArray());
        }

        string token;
        try
        {
            token = CreateJwt();
        }
        catch (Exception ex) when (ex is IOException || ex is CryptographicException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return new AppleAccessStatusDto(
                "app-store-connect-jwt",
                "invalid-configuration",
                "server-configured-key-reference",
                RedactSuffix(_options.KeyId),
                RedactSuffix(_options.IssuerId),
                $"Could not load or sign with the configured private key: {ex.GetType().Name}.",
                Targets.Select(target => new AppleAccessCapabilityDto(
                    target.Id, target.Label, target.Endpoint, "not-checked", null,
                    "Private key could not be loaded.", null)).ToArray());
        }

        var capabilities = new List<AppleAccessCapabilityDto>(Targets.Count);
        foreach (ProbeTarget target in Targets)
            capabilities.Add(await ProbeTargetAsync(target, token, ct).ConfigureAwait(false));

        string state = capabilities.All(c => c.State == "verified")
            ? "read-only-verified"
            : capabilities.Any(c => c.State == "verified")
                ? "partial"
                : "blocked";

        return new AppleAccessStatusDto(
            "app-store-connect-jwt",
            state,
            "server-configured-key-reference",
            RedactSuffix(_options.KeyId),
            RedactSuffix(_options.IssuerId),
            state == "read-only-verified"
                ? "Read-only App Store Connect provisioning probe succeeded. No mutations were performed."
                : "Read-only App Store Connect provisioning probe completed with blockers. No mutations were performed.",
            capabilities);
    }

    private async Task<AppleAccessCapabilityDto> ProbeTargetAsync(ProbeTarget target, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(_options.BaseUrl), target.Endpoint + "?limit=1"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            string detail = response.StatusCode switch
            {
                HttpStatusCode.OK => "Endpoint accepted the team API key.",
                HttpStatusCode.Unauthorized => "Unauthorized. Check key ID, issuer ID, private key, token clock, or revoked key state.",
                HttpStatusCode.Forbidden => "Forbidden. The API key role, agreements, or team access do not permit this endpoint.",
                HttpStatusCode.TooManyRequests => "Rate limited by Apple. Retry after the backoff window.",
                _ => $"Apple returned {(int)response.StatusCode} {response.ReasonPhrase}.",
            };

            int? count = response.IsSuccessStatusCode ? await TryReadCountAsync(response, ct).ConfigureAwait(false) : null;
            return new AppleAccessCapabilityDto(
                target.Id,
                target.Label,
                target.Endpoint,
                response.StatusCode == HttpStatusCode.OK ? "verified" : StatusToState(response.StatusCode),
                (int)response.StatusCode,
                detail,
                count);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new AppleAccessCapabilityDto(target.Id, target.Label, target.Endpoint, "failed", null, "Timed out contacting App Store Connect.", null);
        }
        catch (HttpRequestException ex)
        {
            return new AppleAccessCapabilityDto(target.Id, target.Label, target.Endpoint, "failed", null, $"Network error: {ex.GetType().Name}.", null);
        }
    }

    private IReadOnlyList<string> MissingConfiguration()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.KeyId)) missing.Add("Sideport:AppStoreConnect:KeyId");
        if (string.IsNullOrWhiteSpace(_options.IssuerId)) missing.Add("Sideport:AppStoreConnect:IssuerId");
        if (string.IsNullOrWhiteSpace(_options.PrivateKeyPath)) missing.Add("Sideport:AppStoreConnect:PrivateKeyPath");
        else if (!File.Exists(_options.PrivateKeyPath)) missing.Add("existing private key file");
        return missing;
    }

    private string CreateJwt()
    {
        if (string.IsNullOrWhiteSpace(_options.KeyId) || string.IsNullOrWhiteSpace(_options.IssuerId) || string.IsNullOrWhiteSpace(_options.PrivateKeyPath))
            throw new InvalidOperationException("App Store Connect options are incomplete.");

        using ECDsa key = ECDsa.Create();
        key.ImportFromPem(File.ReadAllText(_options.PrivateKeyPath));

        var credentials = new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var now = DateTimeOffset.UtcNow;
        var header = new JwtHeader(credentials)
        {
            ["kid"] = _options.KeyId,
            ["typ"] = "JWT",
        };
        var payload = new JwtPayload
        {
            ["iss"] = _options.IssuerId,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["aud"] = "appstoreconnect-v1",
        };

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }

    private static async Task<int?> TryReadCountAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var document = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("data", out System.Text.Json.JsonElement data) && data.ValueKind == System.Text.Json.JsonValueKind.Array
                ? data.GetArrayLength()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string StatusToState(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "unauthorized",
        HttpStatusCode.Forbidden => "denied",
        HttpStatusCode.TooManyRequests => "rate-limited",
        _ => "failed",
    };

    private static string? RedactSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 4 ? "****" : $"...{value[^4..]}";
    }

    private sealed record ProbeTarget(string Id, string Label, string Endpoint);
}