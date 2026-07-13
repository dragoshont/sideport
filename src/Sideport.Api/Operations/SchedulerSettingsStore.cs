using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sideport.Api.Operations;

public sealed record SchedulerEvaluationReceipt(
    string EvaluationId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string Outcome,
    int DueCount,
    int QueuedCount,
    int BlockedCount,
    int SkippedCount);

public sealed record SchedulerSettingsState(
    int SchemaVersion,
    long SettingsVersion,
    bool Enabled,
    bool RequestedEnabled,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? NextEvaluationAt,
    IReadOnlyList<SchedulerEvaluationReceipt> Evaluations)
{
    [JsonIgnore]
    public SchedulerEvaluationReceipt? LastEvaluation => Evaluations.FirstOrDefault();
}

/// <summary>
/// Durable scheduler settings and bounded evaluation evidence. Bootstrap
/// configuration is written only when the store does not exist; a legacy
/// request to enable scheduling is retained as requested state but activates
/// only when the caller has proved every V2 prerequisite.
/// </summary>
public sealed class SchedulerSettingsStore
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxEvaluations = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SchedulerSettingsStore(string path, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _time = timeProvider ?? TimeProvider.System;
    }

    public async Task<SchedulerSettingsState?> ReadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadUnsafeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Seeds bootstrap configuration once. Existing durable settings always
    /// win. The requested value is retained separately so a legacy
    /// <c>Enabled=true</c> can be reported without silently activating it.
    /// </summary>
    public async Task<(SchedulerSettingsState State, bool Created)> InitializeAsync(
        bool requestedEnabled,
        bool prerequisitesSatisfied,
        DateTimeOffset? nextEvaluationAt = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SchedulerSettingsState? existing = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (existing is not null)
                return (existing, false);

            DateTimeOffset now = _time.GetUtcNow();
            bool enabled = requestedEnabled && prerequisitesSatisfied;
            var created = new SchedulerSettingsState(
                CurrentSchemaVersion,
                SettingsVersion: 1,
                Enabled: enabled,
                RequestedEnabled: requestedEnabled,
                UpdatedAt: now,
                NextEvaluationAt: enabled ? nextEvaluationAt : null,
                Evaluations: []);
            Validate(created);
            await SaveUnsafeAsync(created, ct).ConfigureAwait(false);
            return (created, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Applies an already-authorized setting change. Identical requests are an
    /// idempotent no-op and do not advance the settings version.
    /// </summary>
    public async Task<(SchedulerSettingsState State, bool Changed)> SetEnabledAsync(
        bool enabled,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SchedulerSettingsState? current = await ReadUnsafeAsync(ct).ConfigureAwait(false);
            if (current is not null && current.Enabled == enabled && current.RequestedEnabled == enabled)
                return (current, false);

            DateTimeOffset now = _time.GetUtcNow();
            SchedulerSettingsState updated = current is null
                ? new SchedulerSettingsState(
                    CurrentSchemaVersion,
                    SettingsVersion: 1,
                    Enabled: enabled,
                    RequestedEnabled: enabled,
                    UpdatedAt: now,
                    NextEvaluationAt: null,
                    Evaluations: [])
                : current with
                {
                    SettingsVersion = checked(current.SettingsVersion + 1),
                    Enabled = enabled,
                    RequestedEnabled = enabled,
                    UpdatedAt = now,
                    NextEvaluationAt = enabled ? current.NextEvaluationAt : null,
                };
            Validate(updated);
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return (updated, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Persists the next due-only evaluation boundary without changing the
    /// settings version. Callers use this after enabling and before writing a
    /// first-run completion receipt.
    /// </summary>
    public async Task<SchedulerSettingsState> SetNextEvaluationAtAsync(
        DateTimeOffset? nextEvaluationAt,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SchedulerSettingsState current = await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false);
            DateTimeOffset? effectiveNext = current.Enabled ? nextEvaluationAt : null;
            if (current.NextEvaluationAt == effectiveNext)
                return current;

            SchedulerSettingsState updated = current with
            {
                UpdatedAt = _time.GetUtcNow(),
                NextEvaluationAt = effectiveNext,
            };
            Validate(updated);
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically appends one evaluation receipt and advances the next
    /// evaluation. The newest one hundred receipts are retained.
    /// </summary>
    public async Task<SchedulerSettingsState> RecordEvaluationAsync(
        SchedulerEvaluationReceipt receipt,
        DateTimeOffset nextEvaluationAt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Validate(receipt);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SchedulerSettingsState current = await ReadRequiredUnsafeAsync(ct).ConfigureAwait(false);
            SchedulerEvaluationReceipt? duplicate = current.Evaluations.FirstOrDefault(item =>
                string.Equals(item.EvaluationId, receipt.EvaluationId, StringComparison.Ordinal));
            if (duplicate is not null)
            {
                if (duplicate != receipt)
                    throw new InvalidDataException("A scheduler evaluation ID cannot be reused for different evidence.");
                return current;
            }

            SchedulerEvaluationReceipt[] evaluations = current.Evaluations
                .Append(receipt)
                .OrderByDescending(item => item.StartedAt)
                .ThenByDescending(item => item.EvaluationId, StringComparer.Ordinal)
                .Take(MaxEvaluations)
                .ToArray();
            SchedulerSettingsState updated = current with
            {
                UpdatedAt = receipt.CompletedAt,
                NextEvaluationAt = current.Enabled ? nextEvaluationAt : null,
                Evaluations = evaluations,
            };
            Validate(updated);
            await SaveUnsafeAsync(updated, ct).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SchedulerSettingsState> ReadRequiredUnsafeAsync(CancellationToken ct) =>
        await ReadUnsafeAsync(ct).ConfigureAwait(false)
        ?? throw new SchedulerSettingsStoreException(
            "Scheduler settings have not been initialized.",
            new FileNotFoundException("The scheduler settings record does not exist.", _path));

    private async Task<SchedulerSettingsState?> ReadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return null;

        try
        {
            await using FileStream stream = File.OpenRead(_path);
            SchedulerSettingsState? state = await JsonSerializer.DeserializeAsync<SchedulerSettingsState>(
                stream,
                JsonOptions,
                ct).ConfigureAwait(false);
            if (state is null)
                throw new InvalidDataException("The scheduler settings record is empty.");
            Validate(state);
            return state with { Evaluations = state.Evaluations.ToArray() };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SchedulerSettingsStoreException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            throw new SchedulerSettingsStoreException("Scheduler settings are unavailable.", ex);
        }
    }

    private async Task SaveUnsafeAsync(SchedulerSettingsState state, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_path);
        string temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await using (FileStream stream = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            TryDelete(temporaryPath);
            throw new SchedulerSettingsStoreException("Scheduler settings could not be saved.", ex);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static void Validate(SchedulerSettingsState state)
    {
        if (state.SchemaVersion != CurrentSchemaVersion ||
            state.SettingsVersion < 1 ||
            state.UpdatedAt == default ||
            state.Evaluations is null ||
            state.Evaluations.Count > MaxEvaluations)
        {
            throw new InvalidDataException("The scheduler settings record is invalid.");
        }

        var evaluationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (SchedulerEvaluationReceipt evaluation in state.Evaluations)
        {
            Validate(evaluation);
            if (!evaluationIds.Add(evaluation.EvaluationId))
                throw new InvalidDataException("The scheduler evaluation history contains a duplicate ID.");
        }

        for (int index = 1; index < state.Evaluations.Count; index++)
        {
            if (state.Evaluations[index - 1].StartedAt < state.Evaluations[index].StartedAt)
                throw new InvalidDataException("The scheduler evaluation history is not newest-first.");
        }
    }

    private static void Validate(SchedulerEvaluationReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.EvaluationId) ||
            receipt.StartedAt == default ||
            receipt.CompletedAt < receipt.StartedAt ||
            receipt.Outcome is not ("succeeded" or "completed-with-blockers" or "failed") ||
            receipt.DueCount < 0 ||
            receipt.QueuedCount < 0 ||
            receipt.BlockedCount < 0 ||
            receipt.SkippedCount < 0 ||
            receipt.QueuedCount + receipt.BlockedCount > receipt.DueCount)
        {
            throw new InvalidDataException("The scheduler evaluation receipt is invalid.");
        }
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
}

public sealed class SchedulerSettingsStoreException(string message, Exception innerException)
    : Exception(message, innerException);
