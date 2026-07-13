using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sideport.Api.GitHubCatalog;

public sealed class GitHubCatalogService : IGitHubCatalogService
{
    private static readonly GitHubPermissionSummaryDto RequiredPermissions = new("read", "read");

    private readonly GitHubCatalogStore _store;
    private readonly GitHubCatalogOptions _options;
    private readonly GitHubTransport _transport;
    private readonly IGitHubCredentialProvider _credentials;
    private readonly IGitHubSetupActorAuthorizer _setupActorAuthorizer;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _configuredGate = new(1, 1);
    private readonly ConcurrentDictionary<string, CachedInstallationToken> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _tokenGates = new(StringComparer.Ordinal);
    private bool _configuredLoaded;

    public GitHubCatalogService(
        GitHubCatalogStore store,
        HttpClient http,
        GitHubCatalogOptions options,
        IGitHubSetupActorAuthorizer setupActorAuthorizer,
        IGitHubDnsResolver? dnsResolver = null,
        IGitHubCredentialProvider? credentialProvider = null,
        TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _setupActorAuthorizer = setupActorAuthorizer ?? throw new ArgumentNullException(nameof(setupActorAuthorizer));
        _time = timeProvider ?? TimeProvider.System;
        _credentials = credentialProvider ?? new EnvironmentGitHubCredentialProvider();
        ValidateOptions(options);
        _transport = new GitHubTransport(http, options, dnsResolver ?? new SystemGitHubDnsResolver(), _time);
    }

    public async Task<GitHubSourcesDto> ListSourcesAsync(CancellationToken ct = default)
    {
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        IReadOnlyList<GitHubStoredSource> sources = await _store.ListSourcesAsync(ct).ConfigureAwait(false);
        return new GitHubSourcesDto(
            new GitHubProviderCapabilityDto(
                "github-release",
                Supported: true,
                AllowedNow: true,
                BlockedReason: null,
                RequiredPermissions),
            sources
                .OrderBy(source => source.Repository, StringComparer.OrdinalIgnoreCase)
                .ThenBy(source => source.Id, StringComparer.Ordinal)
                .Select(ToDto)
                .ToArray());
    }

    public async Task<GitHubConnectionResult> ConnectAsync(
        GitHubConnectionRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        ValidateIdempotencyKey(request.IdempotencyKey);
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        _ = GitHubRepositoryName.Parse(request.Repository);
        string visibility = request.Visibility switch
        {
            "public" => "public",
            "private" => "private",
            _ => throw new GitHubCatalogException("github-repository-invalid", "Visibility must be public or private."),
        };
        string semanticTarget = $"{visibility}:{request.Repository.ToLowerInvariant()}";
        string keyHash = GitHubCatalogStore.HashOpaque($"{actor}\n{request.IdempotencyKey}");
        DateTimeOffset now = _time.GetUtcNow();

        if (visibility == "public")
        {
            GitHubRepositoryIdentity identity = await _transport.GetRepositoryAsync(request.Repository, null, ct).ConfigureAwait(false);
            if (identity.Private)
                throw new GitHubCatalogException("github-repository-not-public", "The selected GitHub repository is not public.");
            var source = new GitHubStoredSource(
                NewId("ghsrc_"), identity.FullName, "public", "public", identity.Id, null,
                AllowPrereleases: false, Configured: false, now);
            (GitHubStoredConnection connection, bool created) = await _store.ConnectPublicAsync(
                actor, keyHash, semanticTarget, identity.FullName, source, now, ct).ConfigureAwait(false);
            return new(ToDto(connection, null), created);
        }

        EnsureAppConfigured();
        string rawState = Base64Url(RandomNumberGenerator.GetBytes(32));
        string stateHash = GitHubCatalogStore.HashOpaque(rawState);
        DateTimeOffset expiresAt = now.AddMinutes(5);
        string origin = OriginOf(_options.UiBaseUri);
        var state = new GitHubStoredSetupState(
            stateHash,
            ConnectionId: string.Empty,
            actor,
            request.Repository,
            Intent: "selected-repository",
            AllowedOrigin: origin,
            now,
            expiresAt,
            ConsumedAt: null);
        (GitHubStoredConnection privateConnection, bool privateCreated) = await _store.StartPrivateConnectionAsync(
            actor, keyHash, semanticTarget, request.Repository, state, now, ct).ConfigureAwait(false);
        string? authorizationUrl = string.Equals(privateConnection.Status, "connected", StringComparison.Ordinal)
            ? null
            : $"https://github.com/apps/{_options.AppSlug}/installations/new?state={Uri.EscapeDataString(rawState)}";
        return new(ToDto(privateConnection, authorizationUrl), privateCreated);
    }

    public async Task<GitHubConnectionDto?> GetConnectionAsync(
        string connectionId,
        string actor,
        bool owner = false,
        CancellationToken ct = default)
    {
        ValidateActor(actor);
        ValidateOpaqueId(connectionId, "connectionId");
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        GitHubStoredConnection? connection = await _store.FindConnectionAsync(connectionId, actor, owner, ct).ConfigureAwait(false);
        return connection is null ? null : ToDto(connection, null);
    }

    public async Task<GitHubSetupCallbackResult> CompleteInstallationAsync(
        string state,
        long installationId,
        string setupAction,
        CancellationToken ct = default)
    {
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256)
            throw StateError("github-state-invalid");
        string stateHash = GitHubCatalogStore.HashOpaque(state);
        DateTimeOffset now = _time.GetUtcNow();
        string callbackIntent = setupAction is "install" or "update"
            ? "selected-repository"
            : setupAction ?? string.Empty;
        GitHubStateConsumeResult consumed = await _store.ConsumeStateAsync(
            stateHash, callbackIntent, now, ct).ConfigureAwait(false);
        if (consumed.Status != GitHubStateConsumeStatus.Accepted || consumed.State is null || consumed.Connection is null)
        {
            throw consumed.Status switch
            {
                GitHubStateConsumeStatus.Expired => StateError("github-state-expired"),
                GitHubStateConsumeStatus.Replayed => StateError("github-state-replayed"),
                _ => StateError("github-state-invalid"),
            };
        }

        GitHubStoredConnection connection = consumed.Connection;
        try
        {
            if (!string.Equals(consumed.State.AllowedOrigin, OriginOf(_options.UiBaseUri), StringComparison.Ordinal))
                throw StateError("github-state-invalid");
            if (!await _setupActorAuthorizer.IsAuthorizedAsync(consumed.State.Actor, ct).ConfigureAwait(false))
                throw StateError("github-state-invalid");
            EnsureAppConfigured();
            string appJwt = CreateAppJwt(now);
            _ = await _transport.GetInstallationAsync(installationId, appJwt, ct).ConfigureAwait(false);
            (string _, string repositoryName) = GitHubRepositoryName.Parse(connection.Repository);
            GitHubInstallationToken restricted = await _transport.MintInstallationTokenAsync(
                installationId, appJwt, repositoryId: null, repositoryName, ct).ConfigureAwait(false);
            EnsureUsableToken(restricted, now);
            GitHubRepositoryIdentity repository = await _transport.GetRepositoryAsync(
                connection.Repository, restricted.Value, ct).ConfigureAwait(false);
            if (!repository.Private)
                throw new GitHubCatalogException("github-installation-invalid", "The selected repository is not private.");

            string sourceId = NewId("ghsrc_");
            var source = new GitHubStoredSource(
                sourceId,
                repository.FullName,
                "private",
                "github-app",
                repository.Id,
                installationId,
                AllowPrereleases: false,
                Configured: false,
                now);
            await _store.CompleteConnectionAsync(connection.Id, source, now, ct).ConfigureAwait(false);
            CacheToken(TokenKey(installationId, repository.Id), restricted, now);
            return new(
                connection.Id,
                sourceId,
                BuildStatusUri(connection.Id, sourceId));
        }
        catch (GitHubCatalogException error)
        {
            try
            {
                await _store.FailConnectionAsync(connection.Id, error.Code, now, ct).ConfigureAwait(false);
            }
            catch (GitHubCatalogException storeError) when (storeError.Code == "github-store-unavailable")
            {
                throw new GitHubCatalogException(storeError.Code, storeError.Message);
            }
            throw;
        }
    }

    public async Task<GitHubReleasePageDto> ListReleasesAsync(
        string sourceId,
        int page = 1,
        CancellationToken ct = default)
    {
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        GitHubStoredSource source = await FindSourceAsync(sourceId, ct).ConfigureAwait(false);
        (source, string? token) = await ResolveAndValidateSourceAsync(source, ct).ConfigureAwait(false);
        return await _transport.GetReleasesAsync(source, token, page, ct).ConfigureAwait(false);
    }

    public async Task<long?> GetKnownRepositoryIdAsync(
        string sourceId,
        CancellationToken ct = default)
    {
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        GitHubStoredSource source = await FindSourceAsync(sourceId, ct).ConfigureAwait(false);
        return source.RepositoryId;
    }

    public async Task<GitHubPreparedImport> PrepareImportAsync(
        GitHubCatalogImportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdempotencyKey(request.IdempotencyKey);
        await EnsureConfiguredSourcesAsync(ct).ConfigureAwait(false);
        GitHubStoredSource source = await FindSourceAsync(request.SourceId, ct).ConfigureAwait(false);
        (source, string? token) = await ResolveAndValidateSourceAsync(source, ct).ConfigureAwait(false);
        GitHubAssetMetadata metadata = await _transport.GetAssetMetadataAsync(
            source, token, request.ReleaseId, request.AssetId, ct).ConfigureAwait(false);
        string? expected = GitHubTransport.NormalizeOptionalDigest(request.ExpectedDigest);
        if (expected is not null && metadata.UpstreamDigest is not null &&
            !string.Equals(expected, metadata.UpstreamDigest, StringComparison.OrdinalIgnoreCase))
        {
            throw new GitHubCatalogException("github-asset-changed", "The GitHub asset no longer matches the selected digest.");
        }

        GitHubDownloadedFile downloaded = await _transport.DownloadAssetAsync(source, token, metadata, ct).ConfigureAwait(false);
        try
        {
            if (expected is not null && !string.Equals(expected, downloaded.Digest, StringComparison.OrdinalIgnoreCase))
                throw new GitHubCatalogException("github-asset-changed", "The GitHub asset no longer matches the selected digest.");
            long repositoryId = source.RepositoryId
                ?? throw new GitHubCatalogException("github-installation-invalid", "The GitHub repository identity is unavailable.");
            string fingerprint = string.Create(
                CultureInfo.InvariantCulture,
                $"github:{repositoryId}:{metadata.ReleaseId}:{metadata.AssetId}:{downloaded.Digest}");
            return new GitHubPreparedImport(
                downloaded.Path,
                source.Id,
                source.Repository,
                repositoryId,
                metadata.ReleaseId,
                metadata.AssetId,
                metadata.ReleaseTag,
                metadata.AssetName,
                downloaded.Digest,
                fingerprint);
        }
        catch
        {
            TryDelete(downloaded.Path);
            throw;
        }
    }

    private async Task EnsureConfiguredSourcesAsync(CancellationToken ct)
    {
        if (_configuredLoaded)
            return;
        await _configuredGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_configuredLoaded)
                return;
            DateTimeOffset now = _time.GetUtcNow();
            GitHubStoredSource[] configured = _options.ConfiguredSources.Select(source =>
            {
                _ = GitHubRepositoryName.Parse(source.Repository);
                string visibility = source.Visibility switch
                {
                    "public" => "public",
                    "private" => "private",
                    _ => throw new InvalidOperationException($"Configured GitHub source '{source.Id}' has an invalid visibility."),
                };
                string provider = visibility == "public"
                    ? "public"
                    : source.InstallationId is > 0 && source.RepositoryId is > 0
                        ? "github-app"
                        : !string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable)
                            ? "deployment-token"
                            : "unavailable";
                return new GitHubStoredSource(
                    source.Id,
                    source.Repository,
                    visibility,
                    provider,
                    source.RepositoryId,
                    source.InstallationId,
                    source.AllowPrereleases,
                    Configured: true,
                    now);
            }).ToArray();
            await _store.EnsureConfiguredSourcesAsync(configured, ct).ConfigureAwait(false);
            _configuredLoaded = true;
        }
        finally
        {
            _configuredGate.Release();
        }
    }

    private async Task<GitHubStoredSource> FindSourceAsync(string id, CancellationToken ct)
    {
        ValidateOpaqueId(id, "sourceId");
        return await _store.FindSourceAsync(id, ct).ConfigureAwait(false)
            ?? throw new GitHubCatalogException("github-source-not-found", "The GitHub source was not found.");
    }

    private async Task<(GitHubStoredSource Source, string? Token)> ResolveAndValidateSourceAsync(
        GitHubStoredSource source,
        CancellationToken ct)
    {
        string? token = await ResolveTokenAsync(source, ct).ConfigureAwait(false);
        GitHubRepositoryIdentity identity = await _transport.GetRepositoryAsync(source.Repository, token, ct).ConfigureAwait(false);
        if (identity.Private != string.Equals(source.Visibility, "private", StringComparison.Ordinal))
            throw new GitHubCatalogException("github-installation-invalid", "The GitHub repository visibility changed.");
        if (source.RepositoryId is not null && source.RepositoryId != identity.Id)
            throw new GitHubCatalogException("github-installation-invalid", "The GitHub repository identity changed.");
        if (source.RepositoryId is null)
            source = await _store.UpdateSourceIdentityAsync(source.Id, identity.Id, ct).ConfigureAwait(false);
        return (source, token);
    }

    private async Task<string?> ResolveTokenAsync(GitHubStoredSource source, CancellationToken ct)
    {
        if (string.Equals(source.Provider, "public", StringComparison.Ordinal))
            return null;
        if (string.Equals(source.Provider, "deployment-token", StringComparison.Ordinal))
        {
            GitHubConfiguredSource? configured = _options.ConfiguredSources.FirstOrDefault(item => item.Id == source.Id);
            string? reference = configured?.AccessTokenEnvironmentVariable;
            if (string.IsNullOrWhiteSpace(reference))
                throw CredentialUnavailable();
            string? token = await _credentials.GetAccessTokenAsync(reference, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(token) ? throw CredentialUnavailable() : token.Trim();
        }
        if (!string.Equals(source.Provider, "github-app", StringComparison.Ordinal) ||
            source.InstallationId is not > 0 || source.RepositoryId is not > 0)
        {
            throw CredentialUnavailable();
        }

        long installationId = source.InstallationId.Value;
        long repositoryId = source.RepositoryId.Value;
        string key = TokenKey(installationId, repositoryId);
        DateTimeOffset now = _time.GetUtcNow();
        if (_tokens.TryGetValue(key, out CachedInstallationToken? cached))
        {
            if (cached.ExpiresAt > now.AddMinutes(5))
                return cached.Value;
            RemoveCachedToken(key, cached);
        }

        SemaphoreSlim gate = _tokenGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            now = _time.GetUtcNow();
            if (_tokens.TryGetValue(key, out cached))
            {
                if (cached.ExpiresAt > now.AddMinutes(5))
                    return cached.Value;
                RemoveCachedToken(key, cached);
            }
            EnsureAppConfigured();
            string jwt = CreateAppJwt(now);
            _ = await _transport.GetInstallationAsync(installationId, jwt, ct).ConfigureAwait(false);
            GitHubInstallationToken minted = await _transport.MintInstallationTokenAsync(
                installationId, jwt, repositoryId, repositoryName: null, ct).ConfigureAwait(false);
            EnsureUsableToken(minted, now);
            CacheToken(key, minted, now);
            return minted.Value;
        }
        finally
        {
            gate.Release();
        }
    }

    private string CreateAppJwt(DateTimeOffset now)
    {
        EnsureAppConfigured();
        try
        {
            string pem = File.ReadAllText(_options.AppPrivateKeyPath!);
            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            string header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            long issuedAt = now.AddSeconds(-60).ToUnixTimeSeconds();
            long expiresAt = now.AddMinutes(9).ToUnixTimeSeconds();
            string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iat = issuedAt,
                exp = expiresAt,
                iss = _options.AppId!.Value,
            }));
            byte[] signingInput = Encoding.ASCII.GetBytes($"{header}.{payload}");
            byte[] signature = rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"{header}.{payload}.{Base64Url(signature)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or ArgumentException)
        {
            throw CredentialUnavailable();
        }
    }

    private Uri BuildStatusUri(string connectionId, string sourceId)
    {
        string separator = _options.UiStatusPath.Contains('?') ? "&" : "?";
        string relative = $"{_options.UiStatusPath}{separator}githubConnection={Uri.EscapeDataString(connectionId)}&source={Uri.EscapeDataString(sourceId)}";
        Uri result = new(_options.UiBaseUri, relative);
        if (!string.Equals(OriginOf(result), OriginOf(_options.UiBaseUri), StringComparison.Ordinal))
            throw StateError("github-state-invalid");
        return result;
    }

    private void EnsureAppConfigured()
    {
        if (_options.AppId is not > 0 ||
            string.IsNullOrWhiteSpace(_options.AppSlug) ||
            string.IsNullOrWhiteSpace(_options.AppPrivateKeyPath))
        {
            throw new GitHubCatalogException("github-app-not-configured", "A selected-repository GitHub App is not configured.");
        }
    }

    private static void ValidateOptions(GitHubCatalogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StatePath) || string.IsNullOrWhiteSpace(options.StagingDirectory))
            throw new ArgumentException("GitHub catalog state and staging paths are required.");
        if (options.MaxAssetBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(options.MaxAssetBytes));
        if (options.RequestTimeout <= TimeSpan.Zero || options.DownloadTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.RequestTimeout));
        if (!options.UiBaseUri.IsAbsoluteUri ||
            !string.IsNullOrEmpty(options.UiBaseUri.UserInfo) ||
            !string.IsNullOrEmpty(options.UiBaseUri.Query) ||
            !string.IsNullOrEmpty(options.UiBaseUri.Fragment) ||
            options.UiBaseUri.AbsolutePath != "/" ||
            !SafeUiScheme(options.UiBaseUri))
        {
            throw new ArgumentException("The GitHub setup UI base URI must be a fixed HTTPS origin (HTTP loopback is allowed for development).", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.UiStatusPath) ||
            !options.UiStatusPath.StartsWith("/", StringComparison.Ordinal) ||
            options.UiStatusPath.StartsWith("//", StringComparison.Ordinal) ||
            options.UiStatusPath.Contains('#'))
        {
            throw new ArgumentException("The GitHub setup UI status path must be a fixed same-origin path.", nameof(options));
        }
        bool anyApp = options.AppId is not null || options.AppSlug is not null || options.AppPrivateKeyPath is not null;
        bool completeApp = options.AppId is > 0 && ValidSlug(options.AppSlug) && !string.IsNullOrWhiteSpace(options.AppPrivateKeyPath);
        if (anyApp && !completeApp)
            throw new ArgumentException("GitHub App ID, slug, and private-key file path must be configured together.", nameof(options));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (GitHubConfiguredSource source in options.ConfiguredSources)
        {
            if (!ValidConfiguredId(source.Id) || !ids.Add(source.Id))
                throw new ArgumentException("Configured GitHub source IDs must be unique safe identifiers.", nameof(options));
            _ = GitHubRepositoryName.Parse(source.Repository);
            if (source.Visibility is not "public" and not "private")
                throw new ArgumentException($"Configured GitHub source '{source.Id}' has an invalid visibility.", nameof(options));
            if (!string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable) &&
                !ValidEnvironmentVariable(source.AccessTokenEnvironmentVariable))
            {
                throw new ArgumentException($"Configured GitHub source '{source.Id}' has an invalid credential reference.", nameof(options));
            }
            if (source.Visibility == "public" &&
                (source.InstallationId is not null || !string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable)))
            {
                throw new ArgumentException($"Public GitHub source '{source.Id}' cannot configure a private credential.", nameof(options));
            }
            if (source.InstallationId is not null && (source.InstallationId <= 0 || source.RepositoryId is not > 0 || !completeApp))
                throw new ArgumentException($"Configured GitHub App source '{source.Id}' is incomplete.", nameof(options));
            if (source.InstallationId is not null && !string.IsNullOrWhiteSpace(source.AccessTokenEnvironmentVariable))
                throw new ArgumentException($"Configured GitHub source '{source.Id}' must use one credential provider.", nameof(options));
        }
    }

    private static bool SafeUiScheme(Uri uri)
    {
        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return uri.Port == 443;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
            return false;
        if (string.Equals(uri.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(uri.DnsSafeHost, out IPAddress? address) && IPAddress.IsLoopback(address);
    }

    private static string OriginOf(Uri uri) => uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');

    private static bool ValidSlug(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value[0] != '-' && value[^1] != '-' &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static bool ValidConfiguredId(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool ValidEnvironmentVariable(string value) =>
        value.Length <= 128 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static void ValidateActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw new ArgumentException("An authenticated actor is required.", nameof(actor));
    }

    private static void ValidateIdempotencyKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl))
            throw new ArgumentException("A bounded idempotency key is required.", nameof(key));
    }

    private static void ValidateOpaqueId(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("A valid identifier is required.", parameter);
        }
    }

    private static GitHubSourceDto ToDto(GitHubStoredSource source) => new(
        source.Id,
        source.Repository,
        source.Visibility,
        source.Provider,
        source.AllowPrereleases,
        RequiredPermissions,
        source.Provider == "unavailable" ? "credential-unavailable" : "connected");

    private static GitHubConnectionDto ToDto(GitHubStoredConnection connection, string? authorizationUrl) => new(
        connection.Id,
        connection.Repository,
        connection.Visibility,
        connection.Status,
        RequiredPermissions,
        connection.ExpiresAt,
        connection.SourceId,
        authorizationUrl,
        connection.Error);

    private static void EnsureUsableToken(GitHubInstallationToken token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token.Value) || token.ExpiresAt <= now.AddMinutes(5))
            throw CredentialUnavailable();
    }

    private void CacheToken(string key, GitHubInstallationToken token, DateTimeOffset now)
    {
        TimeSpan lifetime = token.ExpiresAt - now - TimeSpan.FromMinutes(5);
        if (lifetime <= TimeSpan.Zero)
            throw CredentialUnavailable();
        var cached = new CachedInstallationToken(token.Value, token.ExpiresAt);
        cached.EvictionTimer = _time.CreateTimer(
            _ => RemoveCachedToken(key, cached),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _tokens.AddOrUpdate(
            key,
            cached,
            (_, previous) =>
            {
                previous.Dispose();
                return cached;
            });
        try
        {
            cached.EvictionTimer.Change(lifetime, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // A concurrent refresh installed and scheduled a newer token.
        }
    }

    private void RemoveCachedToken(string key, CachedInstallationToken expected)
    {
        if (_tokens.TryGetValue(key, out CachedInstallationToken? current) &&
            ReferenceEquals(current, expected) &&
            _tokens.TryRemove(key, out CachedInstallationToken? removed))
        {
            removed.Dispose();
        }
    }

    private static string TokenKey(long installationId, long repositoryId) =>
        $"{installationId.ToString(CultureInfo.InvariantCulture)}:{repositoryId.ToString(CultureInfo.InvariantCulture)}";

    private static string NewId(string prefix) =>
        prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    private static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static GitHubCatalogException StateError(string code) =>
        new(code, code switch
        {
            "github-state-expired" => "The GitHub setup link expired. Start again.",
            "github-state-replayed" => "The GitHub setup link was already used.",
            _ => "The GitHub setup state is invalid.",
        });

    private static GitHubCatalogException CredentialUnavailable() =>
        new("github-credential-unavailable", "A GitHub credential is unavailable.");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class CachedInstallationToken(string value, DateTimeOffset expiresAt) : IDisposable
    {
        public string Value { get; } = value;

        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public ITimer? EvictionTimer { get; set; }

        public void Dispose() => EvictionTimer?.Dispose();
    }
}
