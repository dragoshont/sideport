using System.Text.Json;

namespace Sideport.Api.DeviceInventory;

public sealed class KnownDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<KnownDeviceRecord>? _records;

    public KnownDeviceStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public async Task<IReadOnlyList<KnownDeviceRecord>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _records!
                .OrderBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<KnownDeviceRecord?> FindAsync(string udid, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return _records!.FirstOrDefault(device => SameUdid(device.Udid, udid));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(KnownDeviceRecord Record, bool Created)> UpsertAsync(KnownDeviceRecord record, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            record = NormalizeForWrite(record);
            int index = _records!.FindIndex(device => SameUdid(device.Udid, record.Udid));
            bool created = index < 0;
            KnownDeviceRecord? previous = created ? null : _records[index];
            if (created)
                _records.Add(record);
            else
                _records[index] = record;

            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                if (created)
                    _records.Remove(record);
                else
                    _records[index] = previous!;
                throw;
            }
            return (record, created);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string udid, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            int index = _records!.FindIndex(device => SameUdid(device.Udid, udid));
            if (index < 0)
                return false;

            KnownDeviceRecord previous = _records[index];
            _records.RemoveAt(index);
            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _records.Insert(index, previous);
                throw;
            }
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoaded()
    {
        if (_records is not null)
            return;

        if (!File.Exists(_path))
        {
            _records = [];
            return;
        }

        try
        {
            using FileStream stream = File.OpenRead(_path);
            _records = (JsonSerializer.Deserialize<List<KnownDeviceRecord>>(stream, JsonOptions) ?? [])
                .Select(MigrateLoadedRecord)
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new KnownDeviceStoreException("Known-device inventory could not be loaded.", ex);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, _records!.OrderBy(device => device.DisplayName).ThenBy(device => device.Udid).ToArray(), JsonOptions, ct).ConfigureAwait(false);
            }
            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new KnownDeviceStoreException("Known-device inventory could not be saved.", ex);
        }
    }

    private static bool SameUdid(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static KnownDeviceRecord MigrateLoadedRecord(KnownDeviceRecord record)
    {
        string inventoryState = NormalizeInventoryState(record.InventoryState, legacyFallback: true);
        bool hasCompleteAcceptanceEvidence =
            inventoryState == "accepted" &&
            record.AcceptedAt is not null &&
            !string.IsNullOrWhiteSpace(record.AcceptedBy) &&
            !string.IsNullOrWhiteSpace(record.EnrollmentOperationId);

        if (!hasCompleteAcceptanceEvidence && inventoryState == "accepted")
            inventoryState = "legacy-unverified";

        return record with
        {
            InventoryState = inventoryState,
            AcceptedAt = hasCompleteAcceptanceEvidence ? record.AcceptedAt : null,
            AcceptedBy = hasCompleteAcceptanceEvidence ? record.AcceptedBy : null,
            EnrollmentOperationId = hasCompleteAcceptanceEvidence ? record.EnrollmentOperationId : null,
            OwnerMemberId = NormalizeOptional(record.OwnerMemberId),
            TrustState = NormalizeTrustState(record.TrustState),
            UsableForInstall = record.UsableForInstall && NormalizeTrustState(record.TrustState) == "trusted",
        };
    }

    private static KnownDeviceRecord NormalizeForWrite(KnownDeviceRecord record)
    {
        string inventoryState = NormalizeInventoryState(record.InventoryState, legacyFallback: true);
        if (inventoryState == "accepted" &&
            (record.AcceptedAt is null ||
             string.IsNullOrWhiteSpace(record.AcceptedBy) ||
             string.IsNullOrWhiteSpace(record.EnrollmentOperationId)))
        {
            throw new ArgumentException("Accepted devices require timestamp, actor, and enrollment-operation evidence.", nameof(record));
        }

        return record with
        {
            InventoryState = inventoryState,
            AcceptedAt = inventoryState == "accepted" ? record.AcceptedAt : null,
            AcceptedBy = inventoryState == "accepted" ? record.AcceptedBy?.Trim() : null,
            EnrollmentOperationId = inventoryState == "accepted" ? record.EnrollmentOperationId?.Trim() : null,
            OwnerMemberId = NormalizeOptional(record.OwnerMemberId),
            TrustState = NormalizeTrustState(record.TrustState),
            UsableForInstall = record.UsableForInstall && NormalizeTrustState(record.TrustState) == "trusted",
        };
    }

    private static string NormalizeInventoryState(string? state, bool legacyFallback) => state?.Trim().ToLowerInvariant() switch
    {
        "discovered" => "discovered",
        "legacy-unverified" => "legacy-unverified",
        "accepted" => "accepted",
        _ => legacyFallback ? "legacy-unverified" : "discovered",
    };

    private static string NormalizeTrustState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "trusted" => "trusted",
        "untrusted" => "untrusted",
        "locked" => "locked",
        "error" => "error",
        _ => "unknown",
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record KnownDeviceRecord(
    string Udid,
    string DisplayName,
    string? ProductType,
    string? OsVersion,
    string Connection,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset? LastSeenAt,
    string LastSeenSource,
    DateTimeOffset? CurrentPollAt,
    string TrustState,
    string? Owner,
    string? Notes,
    DateTimeOffset UpdatedAt,
    string? InventoryState = null,
    DateTimeOffset? AcceptedAt = null,
    string? AcceptedBy = null,
    string? EnrollmentOperationId = null,
    string? TrustReason = null,
    DateTimeOffset? LockdownCheckedAt = null,
    bool UsableForInstall = false,
    string? OwnerMemberId = null);

public sealed class KnownDeviceStoreException(string message, Exception innerException) : Exception(message, innerException);
