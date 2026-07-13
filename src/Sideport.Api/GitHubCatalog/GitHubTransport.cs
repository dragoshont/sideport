using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sideport.Api.GitHubCatalog;

public interface IGitHubDnsResolver
{
    ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default);
}

public sealed class SystemGitHubDnsResolver : IGitHubDnsResolver
{
    public async ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct = default) =>
        await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
}

internal sealed record GitHubRepositoryIdentity(long Id, string FullName, bool Private);

internal sealed record GitHubInstallationIdentity(long Id, string RepositorySelection);

internal sealed record GitHubInstallationToken(string Value, DateTimeOffset ExpiresAt);

internal sealed record GitHubAssetMetadata(
    long RepositoryId,
    long ReleaseId,
    string ReleaseTag,
    bool Prerelease,
    long AssetId,
    string AssetName,
    long SizeBytes,
    string? UpstreamDigest);

internal sealed record GitHubDownloadedFile(string Path, long SizeBytes, string Digest);

internal sealed class GitHubTransport
{
    private const int MaxMetadataBytes = 1_048_576;
    private static readonly Uri ApiBaseUri = new("https://api.github.com/");
    private readonly HttpClient _http;
    private readonly GitHubCatalogOptions _options;
    private readonly IGitHubDnsResolver _dns;
    private readonly TimeProvider _time;
    private readonly HashSet<string> _allowedHosts;

    public GitHubTransport(
        HttpClient http,
        GitHubCatalogOptions options,
        IGitHubDnsResolver dns,
        TimeProvider time)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options;
        _dns = dns;
        _time = time;
        _allowedHosts = new HashSet<string>(options.AllowedDownloadHosts, StringComparer.OrdinalIgnoreCase);
        GitHubNetworkSafety.ValidateAllowedHosts(options.AllowedDownloadHosts);
    }

    public async Task<GitHubRepositoryIdentity> GetRepositoryAsync(
        string repository,
        string? token,
        CancellationToken ct)
    {
        (string owner, string name) = GitHubRepositoryName.Parse(repository);
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}",
            token,
            null,
            "github-repository-not-found",
            ct).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        long id = RequiredInt64(root, "id");
        string fullName = RequiredString(root, "full_name", 200);
        bool isPrivate = RequiredBoolean(root, "private");
        if (!string.Equals(fullName, repository, StringComparison.OrdinalIgnoreCase))
            throw new GitHubCatalogException("github-installation-invalid", "GitHub returned a different repository identity.");
        return new(id, fullName, isPrivate);
    }

    public async Task<GitHubInstallationIdentity> GetInstallationAsync(
        long installationId,
        string appJwt,
        CancellationToken ct)
    {
        if (installationId <= 0)
            throw new GitHubCatalogException("github-installation-invalid", "The GitHub installation is invalid.");
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Get,
            $"app/installations/{installationId.ToString(CultureInfo.InvariantCulture)}",
            appJwt,
            null,
            "github-installation-invalid",
            ct).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        long returnedId = RequiredInt64(root, "id");
        if (returnedId != installationId)
            throw new GitHubCatalogException("github-installation-invalid", "GitHub returned a different installation identity.");
        string selection = RequiredString(root, "repository_selection", 32);
        if (!string.Equals(selection, "selected", StringComparison.Ordinal))
            throw new GitHubCatalogException("github-repository-not-selected", "The GitHub App must be limited to selected repositories.");
        ValidateExactPermissions(RequiredObject(root, "permissions"));
        return new(returnedId, selection);
    }

    public async Task<GitHubInstallationToken> MintInstallationTokenAsync(
        long installationId,
        string appJwt,
        long? repositoryId,
        string? repositoryName,
        CancellationToken ct)
    {
        object payload;
        if (repositoryId is > 0)
        {
            payload = new
            {
                repository_ids = new[] { repositoryId.Value },
                permissions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["metadata"] = "read",
                    ["contents"] = "read",
                },
            };
        }
        else if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            payload = new
            {
                repositories = new[] { repositoryName },
                permissions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["metadata"] = "read",
                    ["contents"] = "read",
                },
            };
        }
        else
        {
            throw new GitHubCatalogException("github-repository-not-selected", "A selected repository is required for the installation token.");
        }
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Post,
            $"app/installations/{installationId.ToString(CultureInfo.InvariantCulture)}/access_tokens",
            appJwt,
            payload,
            "github-installation-invalid",
            ct).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        string token = RequiredString(root, "token", 8_192);
        DateTimeOffset expiresAt = RequiredDate(root, "expires_at");
        ValidateExactPermissions(RequiredObject(root, "permissions"));
        return new(token, expiresAt);
    }

    public async Task<GitHubReleasePageDto> GetReleasesAsync(
        GitHubStoredSource source,
        string? token,
        int page,
        CancellationToken ct)
    {
        if (page < 1 || page > 10_000)
            throw new ArgumentOutOfRangeException(nameof(page), "GitHub release page must be between 1 and 10000.");
        (string owner, string name) = GitHubRepositoryName.Parse(source.Repository);
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases?per_page=20&page={page.ToString(CultureInfo.InvariantCulture)}",
            token,
            null,
            "github-source-not-found",
            ct).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw MalformedUpstream();

        var releases = new List<GitHubReleaseDto>(20);
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || OptionalBoolean(item, "draft") == true)
                continue;
            bool prerelease = OptionalBoolean(item, "prerelease") == true;
            if (prerelease && !source.AllowPrereleases)
                continue;
            long releaseId = RequiredInt64(item, "id");
            string tag = RequiredString(item, "tag_name", 256);
            string releaseName = OptionalString(item, "name", 256) ?? tag;
            var assets = new List<GitHubReleaseAssetDto>();
            JsonElement assetsElement = RequiredArray(item, "assets");
            foreach (JsonElement asset in assetsElement.EnumerateArray())
            {
                string assetName = RequiredString(asset, "name", 512);
                if (!assetName.EndsWith(".ipa", StringComparison.OrdinalIgnoreCase))
                    continue;
                long size = RequiredInt64(asset, "size");
                string? digest = NormalizeOptionalDigest(OptionalString(asset, "digest", 128));
                bool uploaded = string.Equals(OptionalString(asset, "state", 32) ?? "uploaded", "uploaded", StringComparison.Ordinal);
                assets.Add(new GitHubReleaseAssetDto(
                    RequiredInt64(asset, "id"),
                    assetName,
                    size,
                    OptionalDate(asset, "updated_at"),
                    digest,
                    uploaded && size >= 0 && size <= _options.MaxAssetBytes));
            }
            releases.Add(new GitHubReleaseDto(
                releaseId,
                tag,
                releaseName,
                OptionalDate(item, "published_at"),
                OptionalDate(item, "updated_at"),
                prerelease,
                assets));
            if (releases.Count == 20)
                break;
        }
        return new(source.Id, source.Repository, page, releases);
    }

    public async Task<GitHubAssetMetadata> GetAssetMetadataAsync(
        GitHubStoredSource source,
        string? token,
        long releaseId,
        long assetId,
        CancellationToken ct)
    {
        if (releaseId <= 0)
            throw new GitHubCatalogException("github-release-not-found", "The GitHub release was not found.");
        if (assetId <= 0)
            throw new GitHubCatalogException("github-asset-not-found", "The GitHub asset was not found.");
        (string owner, string name) = GitHubRepositoryName.Parse(source.Repository);
        using JsonDocument document = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases/{releaseId.ToString(CultureInfo.InvariantCulture)}",
            token,
            null,
            "github-release-not-found",
            ct).ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (RequiredInt64(root, "id") != releaseId || OptionalBoolean(root, "draft") == true)
            throw new GitHubCatalogException("github-release-not-found", "The GitHub release was not found.");
        bool prerelease = OptionalBoolean(root, "prerelease") == true;
        if (prerelease && !source.AllowPrereleases)
            throw new GitHubCatalogException("github-release-not-found", "The GitHub release is outside this source's policy.");

        JsonElement? selected = null;
        foreach (JsonElement asset in RequiredArray(root, "assets").EnumerateArray())
        {
            if (RequiredInt64(asset, "id") == assetId)
            {
                selected = asset;
                break;
            }
        }
        if (selected is null)
            throw new GitHubCatalogException("github-asset-not-found", "The GitHub asset was not found.");
        string assetName = RequiredString(selected.Value, "name", 512);
        if (!assetName.EndsWith(".ipa", StringComparison.OrdinalIgnoreCase))
            throw new GitHubCatalogException("github-asset-not-ipa", "The selected GitHub asset is not an IPA.");
        if (!string.Equals(OptionalString(selected.Value, "state", 32) ?? "uploaded", "uploaded", StringComparison.Ordinal))
            throw new GitHubCatalogException("github-asset-not-found", "The GitHub asset is not ready.");
        long size = RequiredInt64(selected.Value, "size");
        if (size < 0 || size > _options.MaxAssetBytes)
            throw new GitHubCatalogException("github-asset-too-large", "The GitHub asset exceeds the configured size limit.", _options.MaxAssetBytes);
        return new(
            source.RepositoryId ?? 0,
            releaseId,
            RequiredString(root, "tag_name", 256),
            prerelease,
            assetId,
            assetName,
            size,
            NormalizeOptionalDigest(OptionalString(selected.Value, "digest", 128)));
    }

    public async Task<GitHubDownloadedFile> DownloadAssetAsync(
        GitHubStoredSource source,
        string? token,
        GitHubAssetMetadata metadata,
        CancellationToken ct)
    {
        (string owner, string name) = GitHubRepositoryName.Parse(source.Repository);
        Uri current = new(ApiBaseUri,
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/releases/assets/{metadata.AssetId.ToString(CultureInfo.InvariantCulture)}");
        string? currentToken = token;
        int redirects = 0;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.DownloadTimeout);
        HttpResponseMessage? response = null;
        try
        {
            while (true)
            {
                await ValidateDestinationAsync(current, timeout.Token).ConfigureAwait(false);
                using HttpRequestMessage request = CreateRequest(HttpMethod.Get, current, currentToken, "application/octet-stream");
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                Uri? actual = response.RequestMessage?.RequestUri;
                if (actual is not null && !SameUri(actual, current))
                    throw RedirectRejected();
                if (!IsRedirect(response.StatusCode))
                    break;
                if (redirects >= 3 || response.Headers.Location is null)
                    throw RedirectRejected();
                Uri next = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(current, response.Headers.Location);
                await ValidateDestinationAsync(next, timeout.Token).ConfigureAwait(false);
                if (!string.Equals(current.Host, next.Host, StringComparison.OrdinalIgnoreCase))
                    currentToken = null;
                response.Dispose();
                response = null;
                current = next;
                redirects++;
            }

            EnsureSuccess(response, "github-asset-not-found");
            if (response.Content.Headers.ContentLength is long length && length > _options.MaxAssetBytes)
                throw new GitHubCatalogException("github-asset-too-large", "The GitHub asset exceeds the configured size limit.", _options.MaxAssetBytes);

            Directory.CreateDirectory(_options.StagingDirectory);
            string destination = Path.Combine(_options.StagingDirectory, $"github-{Guid.NewGuid():N}.ipa");
            try
            {
                await using Stream sourceStream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                await using FileStream target = new(
                    destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(81_920);
                long total = 0;
                try
                {
                    while (true)
                    {
                        int read = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeout.Token).ConfigureAwait(false);
                        if (read == 0)
                            break;
                        total += read;
                        if (total > _options.MaxAssetBytes)
                            throw new GitHubCatalogException("github-asset-too-large", "The GitHub asset exceeds the configured size limit.", _options.MaxAssetBytes);
                        hash.AppendData(buffer, 0, read);
                        await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
                await target.FlushAsync(timeout.Token).ConfigureAwait(false);
                if (total != metadata.SizeBytes)
                    throw new GitHubCatalogException("github-asset-changed", "The GitHub asset changed while it was imported.");
                string digest = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (metadata.UpstreamDigest is not null &&
                    !string.Equals(metadata.UpstreamDigest, digest, StringComparison.OrdinalIgnoreCase))
                {
                    throw new GitHubCatalogException("github-asset-changed", "The GitHub asset digest changed while it was imported.");
                }
                return new(destination, total, digest);
            }
            catch
            {
                TryDelete(destination);
                throw;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubCatalogException("github-download-timeout", "The GitHub asset download timed out.");
        }
        catch (HttpRequestException)
        {
            throw new GitHubCatalogException("github-upstream-unavailable", "GitHub is temporarily unavailable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new GitHubCatalogException("github-upstream-unavailable", "The GitHub asset could not be staged safely.");
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string relativePath,
        string? token,
        object? payload,
        string notFoundCode,
        CancellationToken ct)
    {
        Uri uri = new(ApiBaseUri, relativePath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            await ValidateDestinationAsync(uri, timeout.Token).ConfigureAwait(false);
            using HttpRequestMessage request = CreateRequest(method, uri, token, "application/vnd.github+json");
            if (payload is not null)
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            Uri? actual = response.RequestMessage?.RequestUri;
            if (actual is not null && !SameUri(actual, uri))
                throw RedirectRejected();
            EnsureSuccess(response, notFoundCode);
            byte[] bytes = await ReadBoundedAsync(response.Content, MaxMetadataBytes, timeout.Token).ConfigureAwait(false);
            try
            {
                return JsonDocument.Parse(bytes);
            }
            catch (JsonException)
            {
                throw MalformedUpstream();
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new GitHubCatalogException("github-upstream-unavailable", "GitHub did not respond in time.");
        }
        catch (HttpRequestException)
        {
            throw new GitHubCatalogException("github-upstream-unavailable", "GitHub is temporarily unavailable.");
        }
        catch (IOException)
        {
            throw new GitHubCatalogException("github-upstream-unavailable", "GitHub returned an unreadable response.");
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, Uri uri, string? token, string accept)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.ParseAdd(accept);
        request.Headers.UserAgent.ParseAdd("Sideport/1.0");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private void EnsureSuccess(HttpResponseMessage response, string notFoundCode)
    {
        if (response.IsSuccessStatusCode)
            return;
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new GitHubCatalogException(notFoundCode, SafeNotFoundMessage(notFoundCode));
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retry = response.Headers.RetryAfter?.Delta;
            throw new GitHubCatalogException("github-rate-limited", "GitHub temporarily limited this request.", retryAfter: retry);
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GitHubCatalogException("github-credential-unavailable", "The GitHub credential is unavailable or expired.");
        throw new GitHubCatalogException("github-upstream-unavailable", "GitHub is temporarily unavailable.");
    }

    private async ValueTask ValidateDestinationAsync(Uri uri, CancellationToken ct)
    {
        if (!uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.HostNameType != UriHostNameType.Dns ||
            !_allowedHosts.Contains(uri.DnsSafeHost))
        {
            throw RedirectRejected();
        }
        _ = await GitHubNetworkSafety.ResolveAllowedPublicAsync(
            uri.DnsSafeHost, uri.Port, _allowedHosts, _dns, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength is long length && length > maxBytes)
            throw MalformedUpstream();
        await using Stream source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var target = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(16_384);
        try
        {
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                total += read;
                if (total > maxBytes)
                    throw MalformedUpstream();
                target.Write(buffer, 0, read);
            }
            return target.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    internal static void ValidateExactPermissions(JsonElement permissions)
    {
        if (permissions.ValueKind != JsonValueKind.Object)
            throw PermissionInsufficient();
        bool metadata = false;
        bool contents = false;
        foreach (JsonProperty property in permissions.EnumerateObject())
        {
            string value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : string.Empty;
            if (string.Equals(property.Name, "metadata", StringComparison.Ordinal))
                metadata = string.Equals(value, "read", StringComparison.Ordinal);
            else if (string.Equals(property.Name, "contents", StringComparison.Ordinal))
                contents = string.Equals(value, "read", StringComparison.Ordinal);
            else if (!string.Equals(value, "none", StringComparison.Ordinal))
                throw PermissionInsufficient();
        }
        if (!metadata || !contents)
            throw PermissionInsufficient();
    }

    internal static string? NormalizeOptionalDigest(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 64)
            normalized = "sha256:" + normalized;
        if (normalized.Length != 71 || !normalized.StartsWith("sha256:", StringComparison.Ordinal) ||
            normalized.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") >= 0)
        {
            throw new GitHubCatalogException("github-asset-changed", "The expected GitHub asset digest is invalid.");
        }
        return normalized;
    }

    private static JsonElement RequiredObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : throw MalformedUpstream();

    private static JsonElement RequiredArray(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array
            ? value
            : throw MalformedUpstream();

    private static long RequiredInt64(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.TryGetInt64(out long result)
            ? result
            : throw MalformedUpstream();

    private static bool RequiredBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw MalformedUpstream();

    private static bool? OptionalBoolean(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string RequiredString(JsonElement parent, string name, int maxLength = 8_192) =>
        OptionalString(parent, name, maxLength) ?? throw MalformedUpstream();

    private static string? OptionalString(JsonElement parent, string name, int maxLength)
    {
        if (!parent.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw MalformedUpstream();
        string text = value.GetString() ?? string.Empty;
        if (text.Length > maxLength || text.Any(char.IsControl))
            throw MalformedUpstream();
        return text;
    }

    private static DateTimeOffset RequiredDate(JsonElement parent, string name) =>
        OptionalDate(parent, name) ?? throw MalformedUpstream();

    private static DateTimeOffset? OptionalDate(JsonElement parent, string name)
    {
        string? value = OptionalString(parent, name, 64);
        if (value is null)
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed.ToUniversalTime()
            : throw MalformedUpstream();
    }

    private static bool IsRedirect(HttpStatusCode code) => code is
        HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool SameUri(Uri left, Uri right) =>
        string.Equals(left.AbsoluteUri, right.AbsoluteUri, StringComparison.Ordinal);

    private static GitHubCatalogException RedirectRejected() =>
        new("github-redirect-rejected", "The GitHub asset redirect was rejected.");

    private static GitHubCatalogException PermissionInsufficient() =>
        new("github-permission-insufficient", "The GitHub App must grant exactly Metadata read and Contents read.");

    private static GitHubCatalogException MalformedUpstream() =>
        new("github-upstream-unavailable", "GitHub returned an invalid response.");

    private static string SafeNotFoundMessage(string code) => code switch
    {
        "github-release-not-found" => "The GitHub release was not found.",
        "github-asset-not-found" => "The GitHub asset was not found.",
        "github-installation-invalid" => "The GitHub installation is invalid.",
        "github-source-not-found" => "The GitHub source was not found.",
        _ => "The GitHub repository was not found.",
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class GitHubRepositoryName
{
    public static (string Owner, string Name) Parse(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository) || !string.Equals(repository, repository.Trim(), StringComparison.Ordinal))
            throw Invalid();
        string[] parts = repository.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || !ValidOwner(parts[0]) || !ValidRepository(parts[1]))
            throw Invalid();
        return (parts[0], parts[1]);
    }

    private static bool ValidOwner(string value) =>
        value.Length is >= 1 and <= 39 &&
        value[0] != '-' && value[^1] != '-' &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool ValidRepository(string value) =>
        value.Length is >= 1 and <= 100 &&
        value is not "." and not ".." &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static GitHubCatalogException Invalid() =>
        new("github-repository-invalid", "Repository must use the owner/repository format.");
}
