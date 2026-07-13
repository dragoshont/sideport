using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sideport.Api.Catalog;

public sealed record CatalogArtifactSourceDto(
    string Kind,
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Repository = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ReleaseTag = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssetName = null);

public sealed record CatalogAppV2Dto(
    string Id,
    int CatalogVersion,
    string Name,
    string Purpose,
    string BundleId,
    string? Version,
    string? ShortVersion,
    string Source,
    string Status,
    long? SizeBytes,
    string? Sha256,
    bool HasEmbeddedProfile,
    DateTimeOffset? SignatureExpiresAt,
    IReadOnlyList<CatalogArtifactSourceDto> ArtifactSources,
    DateTimeOffset? LastInspectedAt,
    IReadOnlyList<string> Notes,
    string? Icon = null);

public sealed record CatalogImportRootDto(
    string Id,
    string Label,
    bool Available,
    string Source = "live");

public sealed record CatalogRootImportRequest(
    string RootId,
    string RelativePath,
    string? Id = null,
    string? Name = null,
    string? Purpose = null,
    int? ExpectedCatalogVersion = null,
    string? IdempotencyKey = null);

public sealed record CatalogUploadV2Request(
    string TemporaryIpaPath,
    string? Id = null,
    string? Name = null,
    string? Purpose = null,
    string? IdempotencyKey = null,
    int? ExpectedCatalogVersion = null);

public sealed record CatalogV2MutationResult(CatalogAppV2Dto Entry, bool Created, bool Replayed);

public sealed class CatalogV2Exception(string code, string message, string? id = null, long? limit = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Id { get; } = id;
    public long? Limit { get; } = limit;
}

public sealed partial class FileAppCatalog
{
    private const int MaxArchiveEntries = 10_000;
    private const long MaxInspectedEntryBytes = 16 * 1024 * 1024;
    private readonly Dictionary<string, ConfiguredImportRoot> _importRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CatalogIdempotencyRecord> _idempotency = [];

    public async Task<IReadOnlyList<CatalogAppV2Dto>> ListV2Async(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var projected = new Dictionary<string, CatalogAppDto>(_entries, StringComparer.OrdinalIgnoreCase);
            foreach (AppCatalogSeed seed in _options.Seeds)
            {
                CatalogAppDto seedEntry = BuildSeedEntry(seed);
                if (!projected.TryGetValue(seed.Id, out CatalogAppDto? existing) || ShouldReplaceSeed(existing, seedEntry))
                    projected[seed.Id] = seedEntry;
            }

            return projected.Values
                .OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToV2)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<IReadOnlyList<CatalogImportRootDto>> ListImportRootsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<CatalogImportRootDto> roots = _importRoots.Values
            .OrderBy(root => root.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(root => root.Id, StringComparer.Ordinal)
            .Select(root => new CatalogImportRootDto(root.Id, root.Label, RootIsAvailable(root)))
            .ToArray();
        return Task.FromResult(roots);
    }

    public async Task<CatalogV2MutationResult> ImportFromRootV2Async(
        CatalogRootImportRequest request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        ResolvedImportSource source = ResolveImportSource(request.RootId, request.RelativePath);
        string? stagingPath = null;
        try
        {
            (stagingPath, CatalogAppDto inspected) = await StageAndInspectAsync(
                source.CanonicalPath,
                request.Id,
                request.Name,
                request.Purpose,
                "server-import",
                "catalog-source-too-large",
                ct).ConfigureAwait(false);
            var artifactSource = new CatalogArtifactSourceDto("server", source.RootLabel);
            string semanticTarget = CreateSemanticTarget(
                "root",
                $"{source.RootId}/{source.CanonicalRelativePath}",
                inspected,
                request.Name,
                request.Purpose,
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
                ct).ConfigureAwait(false);
        }
        finally
        {
            if (stagingPath is not null)
                TryDelete(stagingPath);
        }
    }

    public async Task<CatalogV2MutationResult> ImportUploadedIpaV2Async(
        CatalogUploadV2Request request,
        string actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateActor(actor);
        string sourcePath = NormalizeIpaPath(request.TemporaryIpaPath);
        if (!File.Exists(sourcePath))
            throw new CatalogV2Exception("catalog-source-not-found", "The uploaded IPA temporary file no longer exists.");

        string? stagingPath = null;
        try
        {
            (stagingPath, CatalogAppDto inspected) = await StageAndInspectAsync(
                sourcePath,
                request.Id,
                request.Name,
                request.Purpose,
                "upload",
                "upload-too-large",
                ct).ConfigureAwait(false);
            var artifactSource = new CatalogArtifactSourceDto("browser-upload", "This computer");
            string semanticTarget = CreateSemanticTarget(
                "upload",
                inspected.Sha256 ?? string.Empty,
                inspected,
                request.Name,
                request.Purpose,
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
                ct).ConfigureAwait(false);
        }
        finally
        {
            if (stagingPath is not null)
                TryDelete(stagingPath);
        }
    }

    private void InitializeV2()
    {
        if (_options.MaxUploadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options.MaxUploadBytes));

        foreach (AppCatalogImportRoot configured in _options.ImportRoots ?? [])
        {
            string id = NormalizeConfiguredId(configured.Id);
            if (_importRoots.ContainsKey(id))
                throw new InvalidOperationException($"Duplicate catalog import-root ID '{id}'.");
            if (string.IsNullOrWhiteSpace(configured.Label))
                throw new InvalidOperationException($"Catalog import root '{id}' requires a label.");
            if (string.IsNullOrWhiteSpace(configured.Path))
                throw new InvalidOperationException($"Catalog import root '{id}' requires a host path.");
            _importRoots[id] = new ConfiguredImportRoot(id, configured.Label.Trim(), Path.GetFullPath(configured.Path.Trim()));
        }
    }

    private async Task<(string StagingPath, CatalogAppDto Inspected)> StageAndInspectAsync(
        string sourcePath,
        string? id,
        string? name,
        string? purpose,
        string source,
        string tooLargeCode,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.Combine(_options.ImportDirectory, ".staging"));
        string stagingPath = Path.Combine(_options.ImportDirectory, ".staging", $"{Guid.NewGuid():N}.ipa");
        try
        {
            await CopyBoundedAsync(sourcePath, stagingPath, tooLargeCode, ct).ConfigureAwait(false);
            try
            {
                ValidateArchiveForInspection(stagingPath);
                CatalogAppDto inspected = Inspect(stagingPath, id, name, purpose, source);
                return (stagingPath, inspected);
            }
            catch (Exception ex) when (ex is InvalidDataException or FormatException)
            {
                throw new CatalogV2Exception("ipa-inspection-failed", "The selected file is not a valid, inspectable IPA.");
            }
        }
        catch
        {
            TryDelete(stagingPath);
            throw;
        }
    }

    private async Task CopyBoundedAsync(string sourcePath, string destinationPath, string tooLargeCode, CancellationToken ct)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
            throw new CatalogV2Exception("catalog-source-not-found", "The selected IPA does not exist.");
        if (sourceInfo.Length > _options.MaxUploadBytes)
            throw new CatalogV2Exception(tooLargeCode, "The IPA exceeds the configured catalog size limit.", limit: _options.MaxUploadBytes);

        await using FileStream source = new(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using FileStream destination = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[81_920];
        long copied = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            copied += read;
            if (copied > _options.MaxUploadBytes)
                throw new CatalogV2Exception(tooLargeCode, "The IPA exceeds the configured catalog size limit.", limit: _options.MaxUploadBytes);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        await destination.FlushAsync(ct).ConfigureAwait(false);
    }

    private static void ValidateArchiveForInspection(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaxArchiveEntries)
            throw new InvalidDataException($"IPA contains more than {MaxArchiveEntries} ZIP entries.");

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            bool inspectedEntry = name.EndsWith(".app/Info.plist", StringComparison.Ordinal) ||
                                  name.EndsWith(".app/embedded.mobileprovision", StringComparison.Ordinal);
            if (inspectedEntry && entry.Length > MaxInspectedEntryBytes)
                throw new InvalidDataException("IPA metadata entry exceeds the safe inspection limit.");
        }
    }

    private async Task<CatalogV2MutationResult> CommitV2Async(
        string stagingPath,
        CatalogAppDto inspected,
        CatalogArtifactSourceDto artifactSource,
        string? requestedName,
        string? requestedPurpose,
        int? expectedCatalogVersion,
        string actor,
        string? idempotencyKey,
        string semanticTarget,
        CancellationToken ct,
        CatalogGitHubReplayIdentity? githubReplay = null)
    {
        string? key = NormalizeOptional(idempotencyKey);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (key is not null)
            {
                CatalogIdempotencyRecord? replay = FindIdempotency(actor, key);
                if (replay is not null)
                {
                    if (!string.Equals(replay.SemanticTarget, semanticTarget, StringComparison.Ordinal))
                        throw new CatalogV2Exception("idempotency-target-conflict", "This idempotency key was already used for a different catalog import.", inspected.Id);
                    if (!_entries.TryGetValue(replay.EntryId, out CatalogAppDto? replayEntry) ||
                        replay.CatalogVersion < 1 ||
                        EffectiveVersion(replayEntry) != replay.CatalogVersion ||
                        !string.Equals(replayEntry.Sha256, replay.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new CatalogV2Exception(
                            "idempotency-target-conflict",
                            "The original result for this idempotency key is no longer the current catalog version.",
                            replay.EntryId);
                    }
                    return new CatalogV2MutationResult(ToV2(replayEntry), Created: false, Replayed: true);
                }
            }

            bool exists = _entries.TryGetValue(inspected.Id, out CatalogAppDto? previous);
            if (exists && string.Equals(previous!.Sha256, inspected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<CatalogArtifactSourceDto> currentSources = EffectiveArtifactSources(previous);
                bool hasSource = currentSources.Any(source => SameArtifactSource(source, artifactSource));
                string? normalizedName = NormalizeOptional(requestedName);
                string? normalizedPurpose = NormalizeOptional(requestedPurpose);
                bool changesName = normalizedName is not null &&
                    !string.Equals(previous.Name, normalizedName, StringComparison.Ordinal);
                bool changesPurpose = normalizedPurpose is not null &&
                    !string.Equals(previous.Purpose, normalizedPurpose, StringComparison.Ordinal);

                if (hasSource && !changesName && !changesPurpose)
                {
                    if (key is not null)
                    {
                        await AddIdempotencyAndSaveAsync(
                            actor,
                            key,
                            semanticTarget,
                            previous.Id,
                            EffectiveVersion(previous),
                            previous.Sha256,
                            githubReplay,
                            ct).ConfigureAwait(false);
                    }
                    return new CatalogV2MutationResult(ToV2(previous), Created: false, Replayed: true);
                }

                int currentVersion = EffectiveVersion(previous);
                if (expectedCatalogVersion != currentVersion)
                    throw new CatalogV2Exception("catalog-version-conflict", $"Catalog app '{inspected.Id}' changed; refresh and try again.", inspected.Id);

                CatalogArtifactSourceDto[] mergedSources = hasSource
                    ? currentSources.ToArray()
                    : [.. currentSources, artifactSource];
                CatalogAppDto merged = previous with
                {
                    CatalogVersion = currentVersion + 1,
                    Name = normalizedName ?? previous.Name,
                    Purpose = normalizedPurpose ?? previous.Purpose,
                    ArtifactSources = mergedSources,
                    LastInspectedAt = inspected.LastInspectedAt,
                };
                int receiptCountBeforeMerge = _idempotency.Count;
                _entries[merged.Id] = merged;
                if (key is not null)
                {
                    _idempotency.Add(new CatalogIdempotencyRecord(
                        actor,
                        key,
                        semanticTarget,
                        merged.Id,
                        merged.Sha256,
                        DateTimeOffset.UtcNow,
                        EffectiveVersion(merged),
                        githubReplay));
                }

                try
                {
                    await SaveAsync(ct).ConfigureAwait(false);
                    return new CatalogV2MutationResult(ToV2(merged), Created: false, Replayed: false);
                }
                catch
                {
                    _entries[previous.Id] = previous;
                    if (_idempotency.Count > receiptCountBeforeMerge)
                        _idempotency.RemoveRange(
                            receiptCountBeforeMerge,
                            _idempotency.Count - receiptCountBeforeMerge);
                    throw;
                }
            }

            if (exists)
            {
                int currentVersion = EffectiveVersion(previous!);
                if (expectedCatalogVersion != currentVersion)
                    throw new CatalogV2Exception("catalog-version-conflict", $"Catalog app '{inspected.Id}' changed; refresh and try again.", inspected.Id);
            }
            else if (expectedCatalogVersion is not null)
            {
                throw new CatalogV2Exception("catalog-version-conflict", $"Catalog app '{inspected.Id}' does not exist at the expected version.", inspected.Id);
            }

            int nextVersion = exists ? EffectiveVersion(previous!) + 1 : 1;
            string durablePath = ManagedArtifactPathFor(inspected.Id, nextVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
            string publishPath = $"{durablePath}.publishing";
            bool published = false;
            int receiptCount = _idempotency.Count;
            CatalogAppDto entry = inspected with
            {
                IpaPath = durablePath,
                CatalogVersion = nextVersion,
                ArtifactSources = [artifactSource],
            };

            try
            {
                File.Copy(stagingPath, publishPath, overwrite: false);
                File.Move(publishPath, durablePath);
                published = true;
                _entries[entry.Id] = entry;
                if (key is not null)
                {
                    _idempotency.Add(new CatalogIdempotencyRecord(
                        actor,
                        key,
                        semanticTarget,
                        entry.Id,
                        entry.Sha256,
                        DateTimeOffset.UtcNow,
                        EffectiveVersion(entry),
                        githubReplay));
                }
                await SaveAsync(ct).ConfigureAwait(false);
                if (exists)
                    TryDeleteSupersededManagedArtifact(previous!.IpaPath, durablePath);
                return new CatalogV2MutationResult(ToV2(entry), Created: !exists, Replayed: false);
            }
            catch
            {
                if (exists)
                    _entries[inspected.Id] = previous!;
                else
                    _entries.Remove(inspected.Id);
                if (_idempotency.Count > receiptCount)
                    _idempotency.RemoveRange(receiptCount, _idempotency.Count - receiptCount);
                TryDelete(publishPath);
                if (published)
                    TryDelete(durablePath);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task AddIdempotencyAndSaveAsync(
        string actor,
        string key,
        string semanticTarget,
        string entryId,
        int catalogVersion,
        string? sha256,
        CatalogGitHubReplayIdentity? githubReplay,
        CancellationToken ct)
    {
        int count = _idempotency.Count;
        _idempotency.Add(new CatalogIdempotencyRecord(
            actor,
            key,
            semanticTarget,
            entryId,
            sha256,
            DateTimeOffset.UtcNow,
            catalogVersion,
            githubReplay));
        try
        {
            await SaveAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            _idempotency.RemoveRange(count, _idempotency.Count - count);
            throw;
        }
    }

    private CatalogIdempotencyRecord? FindIdempotency(string actor, string key) =>
        _idempotency.FirstOrDefault(item =>
            string.Equals(item.Actor, actor, StringComparison.Ordinal) &&
            string.Equals(item.Key, key, StringComparison.Ordinal));

    private ResolvedImportSource ResolveImportSource(string rootId, string relativePath)
    {
        string normalizedRootId = NormalizeConfiguredId(rootId);
        if (!_importRoots.TryGetValue(normalizedRootId, out ConfiguredImportRoot? root))
            throw new CatalogV2Exception("catalog-root-not-found", "The configured catalog import root was not found.");
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new CatalogV2Exception("catalog-path-invalid", "Catalog import paths must be relative to a configured root.");

        string normalized = relativePath.Trim().Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.None);
        if (segments.Length == 0 || segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
        {
            throw new CatalogV2Exception("catalog-path-invalid", "Catalog import paths cannot contain empty, current, or parent segments.");
        }
        if (!string.Equals(Path.GetExtension(segments[^1]), ".ipa", StringComparison.OrdinalIgnoreCase))
            throw new CatalogV2Exception("catalog-path-invalid", "Catalog imports must select an .ipa file.");

        string canonicalRoot;
        try
        {
            canonicalRoot = CanonicalizeConfiguredRoot(root.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CatalogV2Exception("catalog-root-not-found", "The configured catalog import root is unavailable.");
        }

        string current = canonicalRoot;
        try
        {
            foreach (string segment in segments)
            {
                current = ResolveExactChild(current, segment);
                EnsurePathWithinRoot(canonicalRoot, current);
            }
        }
        catch (CatalogV2Exception)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CatalogV2Exception("catalog-source-not-found", "The selected IPA was not found under the configured root.");
        }

        string relative = EnsurePathWithinRoot(canonicalRoot, current);

        var file = new FileInfo(current);
        if (!file.Exists)
            throw new CatalogV2Exception("catalog-source-not-found", "The selected IPA does not exist.");
        if ((file.Attributes & (FileAttributes.Directory | FileAttributes.Device)) != 0)
            throw new CatalogV2Exception("catalog-path-invalid", "The selected catalog source is not a regular file.");

        return new ResolvedImportSource(root.Id, root.Label, current, relative.Replace(Path.DirectorySeparatorChar, '/'));
    }

    private static string ResolveExactChild(string directory, string segment)
    {
        if (!Directory.Exists(directory))
            throw new CatalogV2Exception("catalog-source-not-found", "A catalog import path component is not a directory.");

        string[] matches = Directory.EnumerateFileSystemEntries(directory)
            .Where(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        string? exact = matches.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.Ordinal));
        if (exact is null)
        {
            if (matches.Length > 0)
                throw new CatalogV2Exception("catalog-path-invalid", "The catalog path casing does not match the configured filesystem entry.");
            throw new CatalogV2Exception("catalog-source-not-found", "The selected catalog path does not exist.");
        }
        if (matches.Length > 1)
            throw new CatalogV2Exception("catalog-path-invalid", "The catalog path is ambiguous on this filesystem.");

        FileSystemInfo info = Directory.Exists(exact) ? new DirectoryInfo(exact) : new FileInfo(exact);
        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        return Path.GetFullPath(target?.FullName ?? exact);
    }

    private static string CanonicalizeConfiguredRoot(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException();
        DirectoryInfo info = new(full);
        FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(target?.FullName ?? full));
    }

    private static string EnsurePathWithinRoot(string canonicalRoot, string candidate)
    {
        string relative = Path.GetRelativePath(canonicalRoot, candidate);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison))
        {
            throw new CatalogV2Exception("catalog-path-outside-root", "The selected IPA resolves outside the configured import root.");
        }

        return relative;
    }

    private static bool RootIsAvailable(ConfiguredImportRoot root)
    {
        try
        {
            return Directory.Exists(CanonicalizeConfiguredRoot(root.Path));
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeConfiguredId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new CatalogV2Exception("catalog-root-not-found", "A catalog import-root ID is required.");
        string id = value.Trim().ToLowerInvariant();
        if (id.Length > 64 || !char.IsLetterOrDigit(id[0]) || id.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-'))
            throw new CatalogV2Exception("catalog-root-not-found", "The catalog import-root ID is invalid.");
        return id;
    }

    private static void ValidateActor(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("A catalog mutation actor is required.", nameof(actor));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateSemanticTarget(
        string kind,
        string sourceIdentity,
        CatalogAppDto inspected,
        string? requestedName,
        string? requestedPurpose,
        int? expectedCatalogVersion) =>
        JsonSerializer.Serialize(new CatalogImportFingerprint(
            kind,
            sourceIdentity,
            inspected.Id,
            NormalizeOptional(requestedName),
            NormalizeOptional(requestedPurpose),
            expectedCatalogVersion,
            inspected.Sha256));

    private static bool SameArtifactSource(CatalogArtifactSourceDto left, CatalogArtifactSourceDto right) =>
        string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) &&
        string.Equals(left.Label, right.Label, StringComparison.Ordinal) &&
        string.Equals(left.Repository, right.Repository, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.ReleaseTag, right.ReleaseTag, StringComparison.Ordinal) &&
        string.Equals(left.AssetName, right.AssetName, StringComparison.Ordinal);

    private static CatalogAppV2Dto ToV2(CatalogAppDto entry) => new(
        entry.Id,
        EffectiveVersion(entry),
        entry.Name,
        entry.Purpose,
        entry.BundleId,
        entry.Version,
        entry.ShortVersion,
        "live",
        entry.Status,
        entry.SizeBytes,
        entry.Sha256,
        entry.HasEmbeddedProfile,
        entry.SignatureExpiresAt,
        EffectiveArtifactSources(entry),
        entry.LastInspectedAt,
        entry.Notes,
        string.Equals(entry.Status, "ready", StringComparison.Ordinal) ? $"/api/v2/catalog/apps/{Uri.EscapeDataString(entry.Id)}/icon" : null);

    private sealed record ConfiguredImportRoot(string Id, string Label, string Path);
    private sealed record ResolvedImportSource(string RootId, string RootLabel, string CanonicalPath, string CanonicalRelativePath);
    private sealed record CatalogIdempotencyRecord(
        string Actor,
        string Key,
        string SemanticTarget,
        string EntryId,
        string? Sha256,
        DateTimeOffset CreatedAt,
        int CatalogVersion = 0,
        CatalogGitHubReplayIdentity? GitHubReplay = null);
    private sealed record CatalogImportFingerprint(
        string Kind,
        string SourceIdentity,
        string CatalogId,
        string? Name,
        string? Purpose,
        int? ExpectedCatalogVersion,
        string? Sha256);
    private sealed record CatalogStoreDocument(
        int SchemaVersion,
        CatalogAppDto[] Apps,
        CatalogIdempotencyRecord[] Idempotency);
}
