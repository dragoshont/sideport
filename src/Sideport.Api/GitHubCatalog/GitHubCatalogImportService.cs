using System.Security.Cryptography;
using System.Text;
using Sideport.Api.Catalog;

namespace Sideport.Api.GitHubCatalog;

public sealed class GitHubCatalogImportService(
    IGitHubCatalogService github,
    IAppCatalog catalog) : IGitHubCatalogImportService
{
    private readonly object _importGatesLock = new();
    private readonly Dictionary<string, ImportGate> _importGates = new(StringComparer.Ordinal);

    public async Task<CatalogV2MutationResult> ImportAsync(
        GitHubCatalogImportRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 256 || actor.Any(char.IsControl))
            throw new ArgumentException("An authenticated actor is required.", nameof(actor));
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) ||
            request.IdempotencyKey.Length > 256 ||
            request.IdempotencyKey.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded idempotency key is required.", nameof(request));
        }

        string gateKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{actor}\n{request.IdempotencyKey}")));
        ImportGate gate = AcquireGate(gateKey);
        bool entered = false;
        try
        {
            await gate.Semaphore.WaitAsync(ct).ConfigureAwait(false);
            entered = true;
            return await ImportCoreAsync(request, actor, ct).ConfigureAwait(false);
        }
        finally
        {
            if (entered)
                gate.Semaphore.Release();
            ReleaseGate(gateKey, gate);
        }
    }

    private async Task<CatalogV2MutationResult> ImportCoreAsync(
        GitHubCatalogImportRequest request,
        string actor,
        CancellationToken ct)
    {
        long? repositoryId = await github.GetKnownRepositoryIdAsync(request.SourceId, ct).ConfigureAwait(false);
        if (repositoryId is > 0)
        {
            CatalogV2MutationResult? replay = await catalog.TryReplayDownloadedGitHubIpaV2Async(
                new CatalogGitHubImportReplayRequest(
                    request.SourceId,
                    repositoryId.Value,
                    request.ReleaseId,
                    request.AssetId,
                    request.CatalogId,
                    request.ExpectedDigest,
                    request.ExpectedCatalogVersion,
                    request.IdempotencyKey),
                actor,
                ct).ConfigureAwait(false);
            if (replay is not null)
                return replay;
        }

        await using GitHubPreparedImport prepared = await github.PrepareImportAsync(request, ct).ConfigureAwait(false);
        return await catalog.ImportDownloadedGitHubIpaV2Async(
            new CatalogGitHubImportRequest(
                TemporaryIpaPath: prepared.TemporaryIpaPath,
                SourceId: prepared.SourceId,
                Repository: prepared.Repository,
                ReleaseId: prepared.ReleaseId,
                AssetId: prepared.AssetId,
                ReleaseTag: prepared.ReleaseTag,
                AssetName: prepared.AssetName,
                ImmutableSourceFingerprint: prepared.ImmutableSourceFingerprint,
                ExpectedDigest: prepared.Digest,
                Id: request.CatalogId,
                ExpectedCatalogVersion: request.ExpectedCatalogVersion,
                IdempotencyKey: request.IdempotencyKey),
            actor,
            ct).ConfigureAwait(false);
    }

    private ImportGate AcquireGate(string key)
    {
        lock (_importGatesLock)
        {
            if (!_importGates.TryGetValue(key, out ImportGate? gate))
            {
                gate = new ImportGate();
                _importGates.Add(key, gate);
            }
            gate.References++;
            return gate;
        }
    }

    private void ReleaseGate(string key, ImportGate gate)
    {
        lock (_importGatesLock)
        {
            gate.References--;
            if (gate.References == 0 &&
                _importGates.TryGetValue(key, out ImportGate? current) &&
                ReferenceEquals(current, gate))
            {
                _importGates.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class ImportGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int References { get; set; }
    }
}
