using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sideport.Orchestrator;

/// <summary>
/// Configuration for <see cref="VaultBackedCredentialProvider"/>.
/// </summary>
/// <param name="BaseUrl">
/// Base URL of a Bitwarden-CLI <c>bw serve</c> REST endpoint (an unlocked
/// session in front of Vaultwarden). Example: <c>http://127.0.0.1:8087</c>.
/// </param>
/// <param name="ItemNameTemplate">
/// Template used to derive the vault item's <c>name</c> (or login username) from
/// the Apple ID. <c>{appleId}</c> is substituted. Default <c>{appleId}</c> — i.e.
/// the vault item is named after the Apple ID and its login password is returned.
/// </param>
/// <param name="ApiKey">
/// Optional shared secret. When set, sent as <c>Authorization: Bearer &lt;key&gt;</c>
/// — for a reverse-proxy-guarded <c>bw serve</c>. <c>bw serve</c> itself is
/// unauthenticated and MUST stay bound to loopback / a private network.
/// </param>
public sealed record VaultCredentialOptions(
    string BaseUrl,
    string ItemNameTemplate = "{appleId}",
    string? ApiKey = null);

/// <summary>
/// <see cref="IAppleCredentialProvider"/> that resolves the Apple password from a
/// Bitwarden-compatible vault (Vaultwarden) via a <c>bw serve</c> REST endpoint,
/// so credentials are managed in the vault UI instead of <c>sops</c> on the CLI.
///
/// It does NOT implement Bitwarden's crypto: <c>bw serve</c> runs with an unlocked
/// session and handles decryption; this provider only does authenticated HTTP GETs
/// and reads the matching item's login password. The password is held transiently
/// and never logged (design invariant #6). The same thin HTTP-secret-fetch shape
/// is reused by the homelab auth-broker for other apps.
///
/// Opt-in via <c>Sideport:Apple:CredentialSource=vault</c>; the default stays
/// <see cref="EnvironmentCredentialProvider"/>.
/// </summary>
public sealed class VaultBackedCredentialProvider : IAppleCredentialProvider
{
    private readonly HttpClient _http;
    private readonly VaultCredentialOptions _options;
    private readonly Microsoft.Extensions.Logging.ILogger<VaultBackedCredentialProvider>? _logger;

    public VaultBackedCredentialProvider(
        HttpClient http,
        VaultCredentialOptions options,
        Microsoft.Extensions.Logging.ILogger<VaultBackedCredentialProvider>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(options.BaseUrl);
        _http = http;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetPasswordAsync(string appleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(appleId);

        string itemName = _options.ItemNameTemplate.Replace("{appleId}", appleId, StringComparison.Ordinal);
        string url = $"{_options.BaseUrl.TrimEnd('/')}/list/object/items?search={Uri.EscapeDataString(itemName)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(_options.ApiKey))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");

        using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // A vault outage must surface as a real failure, NOT be masked as
            // "no credential" (which would look like a misconfiguration and
            // could silently skip a refresh). `bw serve` returns 200 + empty
            // data for a genuine miss, so any non-2xx is an error.
            throw new InvalidOperationException(
                $"Vault lookup for Apple ID failed: HTTP {(int)response.StatusCode} from {_options.BaseUrl}.");
        }

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string? password = ExtractPassword(body, itemName, appleId);
        if (password is null)
            _logger?.LogWarning("Vault returned no matching item for the requested Apple ID.");
        return password;
    }

    /// <summary>
    /// Parse a <c>bw serve</c> <c>/list/object/items</c> response and return the
    /// login password of the first item whose <c>name</c> equals
    /// <paramref name="itemName"/> or whose login username equals
    /// <paramref name="appleId"/> (both case-insensitive). Null if none.
    /// </summary>
    internal static string? ExtractPassword(string json, string itemName, string appleId)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("success", out JsonElement success)
            && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException("Vault lookup returned success=false.");
        }

        if (!root.TryGetProperty("data", out JsonElement data))
            return null;
        // bw serve wraps the list in data.data; tolerate a bare data array too.
        JsonElement items = data.TryGetProperty("data", out JsonElement inner) ? inner : data;
        if (items.ValueKind != JsonValueKind.Array)
            return null;

        foreach (JsonElement item in items.EnumerateArray())
        {
            string? name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
            if (!item.TryGetProperty("login", out JsonElement login) || login.ValueKind != JsonValueKind.Object)
                continue;
            string? username = login.TryGetProperty("username", out JsonElement u) ? u.GetString() : null;

            bool matches =
                (name is not null && string.Equals(name, itemName, StringComparison.OrdinalIgnoreCase))
                || (username is not null && string.Equals(username, appleId, StringComparison.OrdinalIgnoreCase));
            if (!matches)
                continue;

            string? password = login.TryGetProperty("password", out JsonElement p) ? p.GetString() : null;
            if (!string.IsNullOrEmpty(password))
                return password;
        }

        return null;
    }
}
