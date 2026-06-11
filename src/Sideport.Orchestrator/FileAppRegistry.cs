using System.Collections.Concurrent;
using System.Text.Json;

namespace Sideport.Orchestrator;

/// <summary>
/// Durable JSON-backed app registry. This is intentionally small and boring:
/// one file, atomic replace, and a process-local lock. It gives the admin portal
/// stable app registrations across API restarts without introducing a database
/// before the product model is proven.
/// </summary>
public sealed class FileAppRegistry : IAppRegistry
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    // Case-insensitive: the API layer compares DeviceUdid/BundleId with
    // OrdinalIgnoreCase, so the registry must match to avoid casing-only
    // duplicate registrations and Find/Remove misses on differing route casing.
    private readonly ConcurrentDictionary<string, AppRegistration> _apps = new(StringComparer.OrdinalIgnoreCase);

    public FileAppRegistry(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        LoadFromDisk();
    }

    public Task<IReadOnlyList<AppRegistration>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AppRegistration>>([.. _apps.Values.OrderBy(app => app.DeviceUdid).ThenBy(app => app.BundleId)]);

    public Task<AppRegistration?> FindAsync(string udid, string bundleId, CancellationToken ct = default) =>
        Task.FromResult(_apps.GetValueOrDefault(KeyOf(udid, bundleId)));

    public async Task UpsertAsync(AppRegistration registration, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(registration);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _apps[registration.Key] = registration;
            await SaveAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string udid, string bundleId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool removed = _apps.TryRemove(KeyOf(udid, bundleId), out _);
            if (removed)
                await SaveAsync(ct).ConfigureAwait(false);
            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_path))
            return;

        using FileStream stream = File.OpenRead(_path);
        AppRegistration[]? apps = JsonSerializer.Deserialize<AppRegistration[]>(stream, JsonOptions);
        if (apps is null)
            return;

        foreach (AppRegistration app in apps)
            _apps[app.Key] = app;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (FileStream stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _apps.Values.OrderBy(app => app.DeviceUdid).ThenBy(app => app.BundleId).ToArray(), JsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _path, overwrite: true);
    }

    private static string KeyOf(string udid, string bundleId) => $"{udid}:{bundleId}";
}