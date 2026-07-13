using System.Text.Json.Serialization;
using Sideport.Api.Catalog;

namespace Sideport.Api.GitHubCatalog;

public sealed record GitHubCatalogOptions(
    string StatePath,
    string StagingDirectory,
    long MaxAssetBytes,
    Uri UiBaseUri)
{
    public long? AppId { get; init; }

    public string? AppSlug { get; init; }

    public string? AppPrivateKeyPath { get; init; }

    public string UiStatusPath { get; init; } = "/settings/apps";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan DownloadTimeout { get; init; } = TimeSpan.FromMinutes(2);

    public IReadOnlyList<string> AllowedDownloadHosts { get; init; } =
    [
        "api.github.com",
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com",
        "release-assets.githubusercontent.com",
    ];

    public IReadOnlyList<GitHubConfiguredSource> ConfiguredSources { get; init; } = [];
}

public sealed record GitHubConfiguredSource(
    string Id,
    string Repository,
    string Visibility,
    long? RepositoryId = null,
    long? InstallationId = null,
    bool AllowPrereleases = false,
    string? AccessTokenEnvironmentVariable = null);

public interface IGitHubCredentialProvider
{
    ValueTask<string?> GetAccessTokenAsync(string environmentVariable, CancellationToken ct = default);
}

public sealed class EnvironmentGitHubCredentialProvider : IGitHubCredentialProvider
{
    public ValueTask<string?> GetAccessTokenAsync(string environmentVariable, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(environmentVariable))
            return ValueTask.FromResult<string?>(null);
        return ValueTask.FromResult(Environment.GetEnvironmentVariable(environmentVariable.Trim()));
    }
}

public sealed record GitHubPermissionSummaryDto(string Metadata, string Contents);

public sealed record GitHubProviderCapabilityDto(
    string Kind,
    bool Supported,
    bool AllowedNow,
    string? BlockedReason,
    GitHubPermissionSummaryDto Permissions);

public sealed record GitHubSourceDto(
    string Id,
    string Repository,
    string Visibility,
    string Provider,
    bool AllowPrereleases,
    GitHubPermissionSummaryDto Permissions,
    string Status = "connected");

public sealed record GitHubSourcesDto(
    GitHubProviderCapabilityDto Capability,
    IReadOnlyList<GitHubSourceDto> Sources);

public sealed record GitHubConnectionRequest(
    string Repository,
    string Visibility,
    string IdempotencyKey);

public sealed record GitHubConnectionDto(
    string Id,
    string Repository,
    string Visibility,
    string Status,
    GitHubPermissionSummaryDto Permissions,
    DateTimeOffset? ExpiresAt,
    string? SourceId,
    string? AuthorizationUrl,
    string? Error);

public sealed record GitHubConnectionResult(GitHubConnectionDto Connection, bool Created);

public sealed record GitHubSetupCallbackResult(
    string ConnectionId,
    string SourceId,
    Uri RedirectUri);

public sealed record GitHubReleaseAssetDto(
    long AssetId,
    string Name,
    long SizeBytes,
    DateTimeOffset? UpdatedAt,
    string? Digest,
    bool Importable);

public sealed record GitHubReleaseDto(
    long ReleaseId,
    string Tag,
    string Name,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt,
    bool Prerelease,
    IReadOnlyList<GitHubReleaseAssetDto> Assets);

public sealed record GitHubReleasePageDto(
    string SourceId,
    string Repository,
    int Page,
    IReadOnlyList<GitHubReleaseDto> Releases);

public sealed record GitHubCatalogImportRequest(
    string SourceId,
    long ReleaseId,
    long AssetId,
    string IdempotencyKey,
    string? ExpectedDigest = null,
    string? CatalogId = null,
    int? ExpectedCatalogVersion = null);

public sealed class GitHubCatalogException(
    string code,
    string message,
    long? limit = null,
    TimeSpan? retryAfter = null) : Exception(message)
{
    public string Code { get; } = code;

    public long? Limit { get; } = limit;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public interface IGitHubSetupActorAuthorizer
{
    Task<bool> IsAuthorizedAsync(
        string actor,
        CancellationToken ct = default);
}

public interface IGitHubCatalogService
{
    Task<GitHubSourcesDto> ListSourcesAsync(CancellationToken ct = default);

    Task<GitHubConnectionResult> ConnectAsync(
        GitHubConnectionRequest request,
        string actor,
        CancellationToken ct = default);

    Task<GitHubConnectionDto?> GetConnectionAsync(
        string connectionId,
        string actor,
        bool owner = false,
        CancellationToken ct = default);

    Task<GitHubSetupCallbackResult> CompleteInstallationAsync(
        string state,
        long installationId,
        string setupAction,
        CancellationToken ct = default);

    Task<GitHubReleasePageDto> ListReleasesAsync(
        string sourceId,
        int page = 1,
        CancellationToken ct = default);

    // Local replay seam: never performs an upstream request.
    Task<long?> GetKnownRepositoryIdAsync(
        string sourceId,
        CancellationToken ct = default);

    Task<GitHubPreparedImport> PrepareImportAsync(
        GitHubCatalogImportRequest request,
        CancellationToken ct = default);
}

public interface IGitHubCatalogImportService
{
    Task<CatalogV2MutationResult> ImportAsync(
        GitHubCatalogImportRequest request,
        string actor,
        CancellationToken ct = default);
}

public sealed class GitHubPreparedImport : IAsyncDisposable
{
    private int _disposed;

    internal GitHubPreparedImport(
        string temporaryIpaPath,
        string sourceId,
        string repository,
        long repositoryId,
        long releaseId,
        long assetId,
        string releaseTag,
        string assetName,
        string digest,
        string immutableSourceFingerprint)
    {
        TemporaryIpaPath = temporaryIpaPath;
        SourceId = sourceId;
        Repository = repository;
        RepositoryId = repositoryId;
        ReleaseId = releaseId;
        AssetId = assetId;
        ReleaseTag = releaseTag;
        AssetName = assetName;
        Digest = digest;
        ImmutableSourceFingerprint = immutableSourceFingerprint;
    }

    // This path is an internal service hand-off and must never be serialized into an API DTO.
    [JsonIgnore]
    public string TemporaryIpaPath { get; }

    public string SourceId { get; }

    public string Repository { get; }

    public long RepositoryId { get; }

    public long ReleaseId { get; }

    public long AssetId { get; }

    public string ReleaseTag { get; }

    public string AssetName { get; }

    public string Digest { get; }

    public string ImmutableSourceFingerprint { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            try
            {
                File.Delete(TemporaryIpaPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return ValueTask.CompletedTask;
    }
}
