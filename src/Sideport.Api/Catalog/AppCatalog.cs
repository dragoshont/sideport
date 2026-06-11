using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sideport.DeveloperApi.Packaging;

namespace Sideport.Api.Catalog;

public sealed record AppCatalogOptions(
    string CatalogPath,
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
        string entryId = FirstNonBlank(id, Slug(displayName), Slug(info.BundleIdentifier));
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

        File.Move(tempPath, _options.CatalogPath, overwrite: true);
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