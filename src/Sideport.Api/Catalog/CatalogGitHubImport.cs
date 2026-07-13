using System.Text.Json;

namespace Sideport.Api.Catalog;

public sealed record CatalogGitHubImportRequest(
    string TemporaryIpaPath,
    string SourceId,
    string Repository,
    long ReleaseId,
    long AssetId,
    string? ReleaseTag,
    string AssetName,
    string ImmutableSourceFingerprint,
    string? ExpectedDigest = null,
    string? Id = null,
    string? Name = null,
    string? Purpose = null,
    int? ExpectedCatalogVersion = null,
    string? IdempotencyKey = null);

public sealed record CatalogGitHubImportReplayRequest(
    string SourceId,
    long RepositoryId,
    long ReleaseId,
    long AssetId,
    string? CatalogId,
    string? ExpectedDigest,
    int? ExpectedCatalogVersion,
    string IdempotencyKey);

public sealed partial class FileAppCatalog
{
    public async Task<CatalogV2MutationResult?> TryReplayDownloadedGitHubIpaV2Async(
        CatalogGitHubImportReplayRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        string sourceId = Required(request.SourceId, nameof(request.SourceId));
        string key = Required(request.IdempotencyKey, nameof(request.IdempotencyKey));
        ValidatePositiveId(request.RepositoryId, nameof(request.RepositoryId));
        ValidatePositiveId(request.ReleaseId, nameof(request.ReleaseId));
        ValidatePositiveId(request.AssetId, nameof(request.AssetId));
        string? expectedDigest = NormalizeDigest(request.ExpectedDigest);
        string? catalogId = NormalizeOptional(request.CatalogId);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CatalogIdempotencyRecord? receipt = FindIdempotency(actor, key);
            if (receipt is null)
                return null;

            CatalogGitHubReplayIdentity? identity = receipt.GitHubReplay;
            bool exactTarget = identity is not null &&
                string.Equals(identity.SourceId, sourceId, StringComparison.Ordinal) &&
                identity.RepositoryId == request.RepositoryId &&
                identity.ReleaseId == request.ReleaseId &&
                identity.AssetId == request.AssetId &&
                string.Equals(identity.RequestedCatalogId, catalogId, StringComparison.Ordinal) &&
                identity.ExpectedCatalogVersion == request.ExpectedCatalogVersion &&
                (expectedDigest is null ||
                 string.Equals(identity.Digest, expectedDigest, StringComparison.Ordinal));
            if (!exactTarget)
            {
                throw new CatalogV2Exception(
                    "idempotency-target-conflict",
                    "This idempotency key was already used for a different GitHub catalog import.",
                    receipt.EntryId);
            }

            if (!_entries.TryGetValue(receipt.EntryId, out CatalogAppDto? current) ||
                receipt.CatalogVersion < 1 ||
                EffectiveVersion(current) != receipt.CatalogVersion ||
                !string.Equals(current.Sha256, receipt.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals($"sha256:{current.Sha256}", identity!.Digest, StringComparison.Ordinal))
            {
                throw new CatalogV2Exception(
                    "idempotency-target-conflict",
                    "The original result for this idempotency key is no longer the current catalog version.",
                    receipt.EntryId);
            }

            return new CatalogV2MutationResult(ToV2(current), Created: false, Replayed: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CatalogV2MutationResult> ImportDownloadedGitHubIpaV2Async(
        CatalogGitHubImportRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        string sourcePath = NormalizeIpaPath(request.TemporaryIpaPath);
        if (!File.Exists(sourcePath))
            throw new CatalogV2Exception("catalog-source-not-found", "The downloaded IPA temporary file no longer exists.");

        string sourceId = Required(request.SourceId, nameof(request.SourceId));
        string repository = Required(request.Repository, nameof(request.Repository));
        string assetName = Required(request.AssetName, nameof(request.AssetName));
        string immutableFingerprint = Required(request.ImmutableSourceFingerprint, nameof(request.ImmutableSourceFingerprint));
        ValidatePositiveId(request.ReleaseId, nameof(request.ReleaseId));
        ValidatePositiveId(request.AssetId, nameof(request.AssetId));
        if (!string.Equals(Path.GetExtension(assetName), ".ipa", StringComparison.OrdinalIgnoreCase))
            throw new CatalogV2Exception("github-asset-not-ipa", "The selected GitHub release asset is not an IPA.");

        string? stagingPath = null;
        try
        {
            (stagingPath, CatalogAppDto inspected) = await StageAndInspectAsync(
                sourcePath,
                request.Id,
                request.Name,
                request.Purpose,
                "github-release",
                "github-asset-too-large",
                ct).ConfigureAwait(false);

            string actualDigest = $"sha256:{inspected.Sha256}";
            string? expectedDigest = NormalizeDigest(request.ExpectedDigest);
            GitHubFingerprint fingerprint = ParseFingerprint(immutableFingerprint);
            bool fingerprintMatches = fingerprint.ReleaseId == request.ReleaseId &&
                fingerprint.AssetId == request.AssetId &&
                string.Equals(fingerprint.Digest, actualDigest, StringComparison.Ordinal);
            if (!fingerprintMatches ||
                (expectedDigest is not null &&
                 !string.Equals(expectedDigest, actualDigest, StringComparison.Ordinal)))
            {
                throw new CatalogV2Exception(
                    "github-asset-changed",
                    "The downloaded GitHub release asset does not match its expected digest.",
                    inspected.Id);
            }

            var artifactSource = new CatalogArtifactSourceDto(
                "github-release",
                "GitHub release",
                repository,
                NormalizeOptional(request.ReleaseTag),
                assetName);
            string sourceIdentity = JsonSerializer.Serialize(new GitHubSourceIdentity(
                sourceId,
                request.ReleaseId,
                request.AssetId,
                immutableFingerprint,
                actualDigest));
            string semanticTarget = CreateSemanticTarget(
                "github-release",
                sourceIdentity,
                inspected,
                request.Name,
                request.Purpose,
                request.ExpectedCatalogVersion);
            var replayIdentity = new CatalogGitHubReplayIdentity(
                sourceId,
                fingerprint.RepositoryId,
                request.ReleaseId,
                request.AssetId,
                NormalizeOptional(request.Id),
                actualDigest,
                request.ExpectedCatalogVersion);
            return await CommitV2Async(
                stagingPath,
                inspected,
                artifactSource,
                request.Name,
                request.Purpose,
                request.ExpectedCatalogVersion,
                actor,
                request.IdempotencyKey,
                semanticTarget,
                ct,
                replayIdentity).ConfigureAwait(false);
        }
        finally
        {
            if (stagingPath is not null)
                TryDelete(stagingPath);
        }
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A value is required.", parameterName);
        return value.Trim();
    }

    private static void ValidatePositiveId(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static GitHubFingerprint ParseFingerprint(string value)
    {
        string[] parts = value.Split(':', StringSplitOptions.None);
        if (parts.Length != 6 ||
            !string.Equals(parts[0], "github", StringComparison.Ordinal) ||
            !long.TryParse(parts[1], out long repositoryId) || repositoryId <= 0 ||
            !long.TryParse(parts[2], out long releaseId) || releaseId <= 0 ||
            !long.TryParse(parts[3], out long assetId) || assetId <= 0 ||
            !string.Equals(parts[4], "sha256", StringComparison.Ordinal) ||
            parts[5].Length != 64 ||
            parts[5].Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new CatalogV2Exception(
                "github-asset-changed",
                "The GitHub release asset identity is invalid.");
        }

        return new GitHubFingerprint(
            repositoryId,
            releaseId,
            assetId,
            $"sha256:{parts[5].ToLowerInvariant()}");
    }

    private static string? NormalizeDigest(string? value)
    {
        string? digest = NormalizeOptional(value);
        if (digest is null)
            return null;
        if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            digest = digest[7..];
        if (digest.Length != 64 || digest.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new CatalogV2Exception(
                "github-asset-changed",
                "The expected GitHub release asset digest is invalid.");
        }
        return $"sha256:{digest.ToLowerInvariant()}";
    }

    private sealed record GitHubSourceIdentity(
        string SourceId,
        long ReleaseId,
        long AssetId,
        string ImmutableFingerprint,
        string Digest);

    private sealed record GitHubFingerprint(
        long RepositoryId,
        long ReleaseId,
        long AssetId,
        string Digest);

    private sealed record CatalogGitHubReplayIdentity(
        string SourceId,
        long RepositoryId,
        long ReleaseId,
        long AssetId,
        string? RequestedCatalogId,
        string Digest,
        int? ExpectedCatalogVersion);
}
