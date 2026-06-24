using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.DeveloperApi.Packaging;

namespace Sideport.Api.Catalog;

public sealed record AppCatalogOptions(
    string CatalogPath,
    string ImportDirectory,
    long MaxUploadBytes,
    IReadOnlyList<AppCatalogSeed> Seeds);

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
    IReadOnlyList<string> Notes);

public interface IAppCatalog
{
    Task<IReadOnlyList<CatalogAppDto>> ListAsync(CancellationToken ct = default);

    Task<CatalogAppDto> InspectAndStoreAsync(CatalogInspectRequest request, CancellationToken ct = default);

    Task<(CatalogAppDto Entry, bool Created)> ImportUploadedIpaAsync(CatalogUploadRequest request, CancellationToken ct = default);
}

public sealed class FileAppCatalog : IAppCatalog
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
            _entries[entry.Id] = entry;
            await SaveAsync(ct).ConfigureAwait(false);
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
        string durablePath = UploadPathFor(inspected.Id);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool exists = _entries.TryGetValue(inspected.Id, out CatalogAppDto? previous);
            if (exists && !request.Replace)
                throw new CatalogConflictException(inspected.Id);

            Directory.CreateDirectory(Path.GetDirectoryName(durablePath)!);
            string tempPath = $"{durablePath}.{Guid.NewGuid():N}.tmp";
            string? backupPath = null;
            await using (FileStream source = File.OpenRead(path))
            await using (FileStream sink = File.Create(tempPath))
            {
                await source.CopyToAsync(sink, ct).ConfigureAwait(false);
            }

            CatalogAppDto entry = inspected with { IpaPath = durablePath };
            _entries[entry.Id] = entry;
            try
            {
                if (File.Exists(durablePath))
                {
                    backupPath = $"{durablePath}.{Guid.NewGuid():N}.bak";
                    File.Move(durablePath, backupPath);
                }
                File.Move(tempPath, durablePath);
                await SaveAsync(ct).ConfigureAwait(false);
                if (backupPath is not null)
                    TryDelete(backupPath);
            }
            catch
            {
                if (exists)
                    _entries[inspected.Id] = previous!;
                else
                    _entries.Remove(inspected.Id);
                TryDelete(durablePath);
                if (backupPath is not null && File.Exists(backupPath))
                    File.Move(backupPath, durablePath, overwrite: true);
                else
                    TryDelete(tempPath);
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
            return Inspect(path, seed.Id, seed.Name, seed.Purpose, source: "seed");
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
        CatalogAppDto[]? entries = JsonSerializer.Deserialize<CatalogAppDto[]>(stream, JsonOptions);
        if (entries is null)
            return;

        foreach (CatalogAppDto entry in entries)
            _entries[entry.Id] = entry;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_options.CatalogPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = $"{_options.CatalogPath}.{Guid.NewGuid():N}.tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _entries.Values.OrderBy(app => app.Name).ToArray(), JsonOptions, ct).ConfigureAwait(false);
        }

        try
        {
            File.Move(tempPath, _options.CatalogPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
            throw new CatalogStoreException("Catalog could not be saved.", ex);
        }
    }

    private string UploadPathFor(string id)
    {
        string path = Path.GetFullPath(Path.Combine(_options.ImportDirectory, $"{CatalogId(id)}.ipa"));
        string root = Path.GetFullPath(_options.ImportDirectory);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Catalog upload ID resolves outside the import directory.");
        return path;
    }

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
}

public sealed class CatalogConflictException(string id) : Exception($"Catalog app '{id}' already exists.")
{
    public string Id { get; } = id;
}

public sealed class CatalogStoreException(string message, Exception innerException) : Exception(message, innerException);