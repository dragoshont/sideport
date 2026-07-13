using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.DeveloperApi.Packaging;

namespace Sideport.Api.Catalog;

public sealed record AppCatalogOptions(
    string CatalogPath,
    string ImportDirectory,
    long MaxUploadBytes,
    IReadOnlyList<AppCatalogSeed> Seeds,
    IReadOnlyList<AppCatalogImportRoot>? ImportRoots = null);

public sealed record AppCatalogImportRoot(string Id, string Label, string Path);

public sealed record AppCatalogSeed(
    string Id,
    string Name,
    string IpaPath,
    string ExpectedBundleId,
    string Purpose);

public sealed record CatalogInspectRequest(
    string IpaPath,
    string? Id = null,
    string? Name = null,
    string? Purpose = null);

public sealed record CatalogUploadRequest(
    string TemporaryIpaPath,
    string? Id = null,
    string? Name = null,
    string? Purpose = null,
    bool Replace = false);

public sealed record CatalogAppDto(
    string Id,
    string Name,
    string Purpose,
    string BundleId,
    string IpaPath,
    string? Version,
    string? ShortVersion,
    long? SizeBytes,
    string? Sha256,
    bool HasEmbeddedProfile,
    DateTimeOffset? SignatureExpiresAt,
    string Source,
    string Status,
    DateTimeOffset? LastInspectedAt,
    IReadOnlyList<string> Notes,
    int CatalogVersion = 1,
    IReadOnlyList<CatalogArtifactSourceDto>? ArtifactSources = null);

public interface IAppCatalog
{
    Task<IReadOnlyList<CatalogAppDto>> ListAsync(CancellationToken ct = default);

    Task<CatalogAppDto> InspectAndStoreAsync(CatalogInspectRequest request, CancellationToken ct = default);

    Task<(CatalogAppDto Entry, bool Created)> ImportUploadedIpaAsync(CatalogUploadRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogAppV2Dto>> ListV2Async(CancellationToken ct = default);

    Task<IReadOnlyList<CatalogImportRootDto>> ListImportRootsAsync(CancellationToken ct = default);

    Task<CatalogV2MutationResult> ImportFromRootV2Async(
        CatalogRootImportRequest request,
        string actor,
        CancellationToken ct = default);

    Task<CatalogV2MutationResult> ImportUploadedIpaV2Async(
        CatalogUploadV2Request request,
        string actor,
        CancellationToken ct = default);

    Task<CatalogV2MutationResult> ImportDownloadedGitHubIpaV2Async(
        CatalogGitHubImportRequest request,
        string actor,
        CancellationToken ct = default);

    Task<CatalogV2MutationResult?> TryReplayDownloadedGitHubIpaV2Async(
        CatalogGitHubImportReplayRequest request,
        string actor,
        CancellationToken ct = default);
}

public sealed partial class FileAppCatalog : IAppCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly AppCatalogOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, CatalogAppDto> _entries = new(StringComparer.OrdinalIgnoreCase);

    public FileAppCatalog(AppCatalogOptions options)
    {
        _options = options;
        InitializeV2();
        LoadFromDisk();
    }

    public async Task<IReadOnlyList<CatalogAppDto>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool changed = false;
            foreach (AppCatalogSeed seed in _options.Seeds)
            {
                CatalogAppDto seedEntry = BuildSeedEntry(seed);
                if (!_entries.TryGetValue(seed.Id, out CatalogAppDto? existing) || ShouldReplaceSeed(existing, seedEntry))
                {
                    _entries[seed.Id] = seedEntry;
                    changed = true;
                }
            }

            if (changed)
                await SaveAsync(ct).ConfigureAwait(false);

            return [.. _entries.Values.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CatalogAppDto> InspectAndStoreAsync(CatalogInspectRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = NormalizeIpaPath(request.IpaPath);
        if (!File.Exists(path))
            throw new FileNotFoundException("IPA path does not exist on the Sideport host.", path);

        CatalogAppDto entry = Inspect(path, request.Id, request.Name, request.Purpose, source: "operator");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool exists = _entries.TryGetValue(entry.Id, out CatalogAppDto? previous);
            int nextVersion = exists ? EffectiveVersion(previous!) + 1 : 1;
            entry = entry with
            {
                CatalogVersion = nextVersion,
                ArtifactSources = [new CatalogArtifactSourceDto("server", "On this server")],
            };
            _entries[entry.Id] = entry;
            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (exists)
                    _entries[entry.Id] = previous!;
                else
                    _entries.Remove(entry.Id);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }

        return entry;
    }

    public async Task<(CatalogAppDto Entry, bool Created)> ImportUploadedIpaAsync(CatalogUploadRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string path = NormalizeIpaPath(request.TemporaryIpaPath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Uploaded IPA temporary file does not exist.", path);

        CatalogAppDto inspected = Inspect(path, request.Id, request.Name, request.Purpose, source: "upload");

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool exists = _entries.TryGetValue(inspected.Id, out CatalogAppDto? previous);
            if (exists && !request.Replace)
                throw new CatalogConflictException(inspected.Id);

            int nextVersion = exists ? EffectiveVersion(previous!) + 1 : 1;
            string durablePath = ManagedArtifactPathFor(inspected.Id, nextVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
            string publishPath = $"{durablePath}.publishing";
            bool published = false;
            CatalogAppDto entry = inspected with
            {
                IpaPath = durablePath,
                CatalogVersion = nextVersion,
                ArtifactSources = [new CatalogArtifactSourceDto("browser-upload", "This computer")],
            };
            try
            {
                await using (FileStream source = File.OpenRead(path))
                await using (FileStream sink = new(
                    publishPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81_920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await source.CopyToAsync(sink, ct).ConfigureAwait(false);
                    await sink.FlushAsync(ct).ConfigureAwait(false);
                }

                File.Move(publishPath, durablePath);
                published = true;
                _entries[entry.Id] = entry;
                await SaveAsync(ct).ConfigureAwait(false);
                if (exists)
                    TryDeleteSupersededManagedArtifact(previous!.IpaPath, durablePath);
            }
            catch
            {
                if (exists)
                    _entries[inspected.Id] = previous!;
                else
                    _entries.Remove(inspected.Id);
                TryDelete(publishPath);
                if (published)
                    TryDelete(durablePath);
                throw;
            }

            return (entry, !exists);
        }
        finally
        {
            _gate.Release();
        }
    }

    private CatalogAppDto BuildSeedEntry(AppCatalogSeed seed)
    {
        string path = NormalizeIpaPath(seed.IpaPath);
        if (!File.Exists(path))
        {
            return new CatalogAppDto(
                seed.Id,
                seed.Name,
                seed.Purpose,
                seed.ExpectedBundleId,
                path,
                Version: null,
                ShortVersion: null,
                SizeBytes: null,
                Sha256: null,
                HasEmbeddedProfile: false,
                SignatureExpiresAt: null,
                Source: "seed",
                Status: "missing",
                LastInspectedAt: null,
                Notes:
                [
                    "The catalog seed is configured, but the IPA is not present on the Sideport host.",
                    "Set Sideport:Catalog:SeedCertClockPath or add an IPA through the inspect endpoint before registering it on a phone.",
                ]);
        }

        try
        {
            return Inspect(path, seed.Id, seed.Name, seed.Purpose, source: "seed") with
            {
                ArtifactSources = [new CatalogArtifactSourceDto("server", "Configured seed")],
            };
        }
        catch (Exception ex) when (ex is FormatException || ex is InvalidDataException)
        {
            return new CatalogAppDto(
                seed.Id,
                seed.Name,
                seed.Purpose,
                seed.ExpectedBundleId,
                path,
                Version: null,
                ShortVersion: null,
                SizeBytes: new FileInfo(path).Length,
                Sha256: null,
                HasEmbeddedProfile: false,
                SignatureExpiresAt: null,
                Source: "seed",
                Status: "invalid",
                LastInspectedAt: DateTimeOffset.UtcNow,
                Notes: [$"IPA inspection failed: {ex.Message}"]);
        }
    }

    private static bool ShouldReplaceSeed(CatalogAppDto existing, CatalogAppDto seedEntry) =>
        existing.Source == "seed" &&
        (existing.Status != seedEntry.Status || existing.IpaPath != seedEntry.IpaPath || existing.Sha256 != seedEntry.Sha256);

    private static CatalogAppDto Inspect(string path, string? id, string? name, string? purpose, string source)
    {
        IpaInfo info = IpaInspector.Inspect(path);
        FileInfo file = new(path);
        string displayName = FirstNonBlank(name, info.DisplayName, TrimAppExtension(info.AppBundleName), info.BundleIdentifier);
        string entryId = CatalogId(FirstNonBlank(id, displayName, info.BundleIdentifier));
        string version = FirstNonBlank(info.ShortVersion, info.Version, "Unknown");
        string? build = string.IsNullOrWhiteSpace(info.Version) ? null : info.Version;

        var notes = new List<string>
        {
            info.Profile is null
                ? "No embedded provisioning profile was found; Sideport must sign this IPA before install."
                : "Embedded provisioning profile was found and inspected.",
        };

        if (build is not null && !string.Equals(version, build, StringComparison.Ordinal))
            notes.Add($"Bundle version {build}.");

        return new CatalogAppDto(
            entryId,
            displayName,
            FirstNonBlank(purpose, "Server-side inspected IPA."),
            info.BundleIdentifier,
            path,
            info.Version,
            info.ShortVersion,
            file.Length,
            ComputeSha256(path),
            info.Profile is not null,
            info.SignatureExpiresAt,
            source,
            "ready",
            DateTimeOffset.UtcNow,
            notes);
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_options.CatalogPath))
            return;

        using FileStream stream = File.OpenRead(_options.CatalogPath);
        using JsonDocument document = JsonDocument.Parse(stream);
        CatalogAppDto[] entries;
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            entries = document.RootElement.Deserialize<CatalogAppDto[]>(JsonOptions) ?? [];
        }
        else
        {
            CatalogStoreDocument? stored = document.RootElement.Deserialize<CatalogStoreDocument>(JsonOptions);
            entries = stored?.Apps ?? [];
            _idempotency.AddRange(stored?.Idempotency ?? []);
        }

        foreach (CatalogAppDto entry in entries)
        {
            _entries[entry.Id] = entry with
            {
                CatalogVersion = EffectiveVersion(entry),
                ArtifactSources = EffectiveArtifactSources(entry),
            };
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        string tempPath = $"{_options.CatalogPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string? directory = Path.GetDirectoryName(_options.CatalogPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new CatalogStoreDocument(
                        2,
                        _entries.Values.OrderBy(app => app.Name).ToArray(),
                        _idempotency.OrderBy(item => item.CreatedAt).ToArray()),
                    JsonOptions,
                    ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _options.CatalogPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new CatalogStoreException("Catalog could not be saved.", ex);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private string ManagedArtifactPathFor(string id, int catalogVersion)
    {
        if (catalogVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(catalogVersion));

        string root = ManagedArtifactsRoot();
        string path = Path.GetFullPath(Path.Combine(
            root,
            CatalogId(id),
            $"v{catalogVersion}-{Guid.NewGuid():N}.ipa"));
        if (!IsPathWithinRoot(root, path))
            throw new ArgumentException("Catalog artifact ID resolves outside managed storage.");
        return path;
    }

    private string ManagedArtifactsRoot() =>
        Path.GetFullPath(Path.Combine(_options.ImportDirectory, ".managed"));

    private void TryDeleteSupersededManagedArtifact(string previousPath, string currentPath)
    {
        string fullPreviousPath;
        try
        {
            fullPreviousPath = Path.GetFullPath(previousPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!IsPathWithinRoot(ManagedArtifactsRoot(), fullPreviousPath) ||
            PathsEqual(fullPreviousPath, currentPath) ||
            _entries.Values.Any(entry => PathsEqual(entry.IpaPath, fullPreviousPath)))
        {
            return;
        }

        TryDelete(fullPreviousPath);
    }

    private static bool IsPathWithinRoot(string root, string candidate)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", PathComparison);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizeIpaPath(string ipaPath)
    {
        if (string.IsNullOrWhiteSpace(ipaPath))
            throw new ArgumentException("IPA path is required.", nameof(ipaPath));

        string fullPath = Path.GetFullPath(ipaPath.Trim());
        if (!string.Equals(Path.GetExtension(fullPath), ".ipa", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Catalog entries must point at an .ipa file.");

        return fullPath;
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string TrimAppExtension(string value) =>
        value.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value;

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown";

    private static string CatalogId(string value)
    {
        string slug = Slug(value);
        return string.IsNullOrWhiteSpace(slug) ? "uploaded-app" : slug;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool dash = false;
        foreach (char ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                dash = false;
            }
            else if (!dash)
            {
                builder.Append('-');
                dash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static int EffectiveVersion(CatalogAppDto entry) => Math.Max(1, entry.CatalogVersion);

    private static IReadOnlyList<CatalogArtifactSourceDto> EffectiveArtifactSources(CatalogAppDto entry) =>
        entry.ArtifactSources is { Count: > 0 }
            ? entry.ArtifactSources
            : entry.Source switch
            {
                "upload" => [new CatalogArtifactSourceDto("browser-upload", "This computer")],
                "seed" => [new CatalogArtifactSourceDto("server", "Configured seed")],
                _ => [new CatalogArtifactSourceDto("server", "On this server")],
            };
}

public sealed class CatalogConflictException(string id) : Exception($"Catalog app '{id}' already exists.")
{
    public string Id { get; } = id;
}

public sealed class CatalogStoreException(string message, Exception innerException) : Exception(message, innerException);
