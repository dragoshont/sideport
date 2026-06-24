using System.Text.Json;

namespace Sideport.Api.Operations;

public sealed class OperationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<OperationRecordDto>? _records;

    public OperationStore(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
    }

    public async Task<IReadOnlyList<OperationRecordDto>> ListAsync(
        string? deviceUdid = null,
        string? bundleId = null,
        int? limit = 25,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            await ReconcileStaleRunningOperationsAsync(ct).ConfigureAwait(false);
            IEnumerable<OperationRecordDto> query = _records!;
            if (!string.IsNullOrWhiteSpace(deviceUdid))
                query = query.Where(operation => string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(bundleId))
                query = query.Where(operation => string.Equals(operation.Target.BundleId, bundleId, StringComparison.Ordinal));

            query = query
                .OrderByDescending(operation => operation.CreatedAt)
                .ThenByDescending(operation => operation.OperationId, StringComparer.Ordinal);

            if (limit is { } requestedLimit)
            {
                int take = Math.Clamp(requestedLimit, 1, 100);
                query = query.Take(take);
            }

            return query.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationRecordDto?> FindAsync(string operationId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            await ReconcileStaleRunningOperationsAsync(ct).ConfigureAwait(false);
            return _records!.FirstOrDefault(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationRecordDto?> FindByIdempotencyAsync(
        string type,
        OperationTargetDto target,
        OperationActorDto actor,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            return FindByIdempotency(type, target, actor, idempotencyKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(OperationRecordDto Record, bool Created)> AddIfIdempotentMissingAsync(
        OperationRecordDto record,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
            {
                OperationRecordDto? existing = FindByIdempotency(record.Type, record.Target, record.Actor, record.IdempotencyKey);
                if (existing is not null)
                    return (existing, false);
            }

            _records!.Add(record);
            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _records.Remove(record);
                throw;
            }
            return (record, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationRecordDto> UpdateAsync(OperationRecordDto record, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            int index = _records!.FindIndex(operation => string.Equals(operation.OperationId, record.OperationId, StringComparison.Ordinal));
            if (index < 0)
                throw new OperationStoreException("Operation history could not be updated.", new KeyNotFoundException(record.OperationId));

            OperationRecordDto previous = _records[index];
            _records[index] = record;
            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _records[index] = previous;
                throw;
            }
            return record;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OperationRecordDto?> TransitionAsync(
        string operationId,
        Func<OperationRecordDto, OperationRecordDto?> transition,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureLoaded();
            int index = _records!.FindIndex(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal));
            if (index < 0)
                return null;

            OperationRecordDto previous = _records[index];
            OperationRecordDto? next = transition(previous);
            if (next is null)
                return previous;

            _records[index] = next;
            try
            {
                await SaveAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                _records[index] = previous;
                throw;
            }
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationRecordDto? FindByIdempotency(
        string type,
        OperationTargetDto target,
        OperationActorDto actor,
        string idempotencyKey) =>
        _records!.FirstOrDefault(operation =>
            string.Equals(operation.Type, type, StringComparison.Ordinal) &&
            string.Equals(operation.Target.DeviceUdid, target.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, target.BundleId, StringComparison.Ordinal) &&
            string.Equals(operation.Actor.Kind, actor.Kind, StringComparison.Ordinal) &&
            string.Equals(operation.Actor.DisplayName, actor.DisplayName, StringComparison.Ordinal) &&
            string.Equals(operation.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

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
            _records = JsonSerializer.Deserialize<List<OperationRecordDto>>(stream, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new OperationStoreException("Operation history could not be loaded.", ex);
        }
    }

    private async Task ReconcileStaleRunningOperationsAsync(CancellationToken ct)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddMinutes(-30);
        bool changed = false;
        for (int i = 0; i < _records!.Count; i++)
        {
            OperationRecordDto operation = _records[i];
            if (!string.Equals(operation.Status, "running", StringComparison.Ordinal) || operation.UpdatedAt > cutoff)
                continue;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var error = new OperationIssueDto(
                "operation-terminal-state-unknown",
                "The API restarted or could not persist the terminal state after this operation started. Review device state before retrying.");
            OperationStageDto[] stages = operation.Stages.Select(stage =>
                string.Equals(stage.Status, "running", StringComparison.Ordinal)
                    ? stage with { Status = "failed", CompletedAt = now, Message = error.Message, Error = error }
                    : stage).ToArray();
            _records[i] = operation with
            {
                Status = "failed",
                UpdatedAt = now,
                CompletedAt = now,
                Stages = stages,
                Error = error,
                Retryable = true,
            };
            changed = true;
        }

        if (changed)
            await SaveAsync(ct).ConfigureAwait(false);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            string tempPath = $"{_path}.{Guid.NewGuid():N}.tmp";
            await using (FileStream stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    _records!.OrderBy(operation => operation.CreatedAt).ThenBy(operation => operation.OperationId, StringComparer.Ordinal).ToArray(),
                    JsonOptions,
                    ct).ConfigureAwait(false);
            }

            File.Move(tempPath, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new OperationStoreException("Operation history could not be saved.", ex);
        }
    }
}

public sealed class OperationStoreException(string message, Exception innerException) : Exception(message, innerException);