using Sideport.Core;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed record SchedulerPolicyDto(
    string Mode,
    TimeSpan EvaluationInterval,
    TimeSpan RefreshLeadTime,
    TimeSpan? ResignInterval,
    string CatchUp,
    string MissedIntervals);

public sealed record SchedulerConcurrencyDto(
    int MaxRunning,
    string LockState,
    string? OperationId);

public sealed record SchedulerHistoryRetentionDto(int MaxEvaluations);

public sealed record SchedulerStatusDto(
    bool Enabled,
    DateTimeOffset CheckedAt,
    SchedulerPolicyDto Policy,
    DateTimeOffset? NextEvaluationAt,
    SchedulerEvaluationReceipt? LastEvaluation,
    int DueCount,
    int QueuedCount,
    SchedulerConcurrencyDto Concurrency,
    SchedulerHistoryRetentionDto HistoryRetention,
    string Source = "live");

public sealed record SchedulerSettingsRequest(bool Enabled);

public sealed record SchedulerSettingsUpdateResult(
    SchedulerStatusDto? Status,
    string? Error = null,
    string? Message = null);

/// <summary>
/// Projects durable scheduler settings and enforces the smallest safe manual
/// enable boundary. Configuration can request scheduling at bootstrap, but
/// unattended work is activated only after a registration has durable device
/// verification, a usable signer identity, and a currently trusted transport.
/// </summary>
public sealed class SchedulerStatusService(
    SchedulerSettingsStore settings,
    IAppRegistry registry,
    OperationStore operations,
    ISigningIdentityProvider signingIdentity,
    IDeviceController devices,
    OrchestratorOptions options,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SchedulerStatusDto> GetAsync(CancellationToken ct = default)
    {
        SchedulerSettingsState state = await ReadRequiredAsync(ct).ConfigureAwait(false);
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> records = await operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        DateTimeOffset checkedAt = _time.GetUtcNow();

        int dueCount = registrations.Count(registration => IsDue(registration, records, checkedAt));
        int queuedCount = records.Count(record =>
            string.Equals(record.Type, "refresh", StringComparison.Ordinal) &&
            record.Status is "queued" or "waiting");
        OperationRecordDto? active = records.FirstOrDefault(record =>
            record.Status is "running" or "waiting" &&
            record.Type is "install" or "refresh");
        OperationRecordDto? quarantined = records.FirstOrDefault(record =>
            OperationReconciliationEvidence.IsUnresolvedForManualAction(record, records));

        string lockState = quarantined is not null
            ? "held"
            : active is not null
                ? "busy"
                : "idle";
        string? operationId = quarantined?.OperationId ?? active?.OperationId;

        return new SchedulerStatusDto(
            state.Enabled,
            checkedAt,
            new SchedulerPolicyDto(
                "due-only",
                options.ScheduleInterval,
                options.RefreshLeadTime,
                options.ResignInterval,
                "evaluate-on-startup",
                "not-replayed"),
            state.NextEvaluationAt,
            state.LastEvaluation,
            dueCount,
            queuedCount,
            new SchedulerConcurrencyDto(1, lockState, operationId),
            new SchedulerHistoryRetentionDto(SchedulerSettingsStore.MaxEvaluations));
    }

    public async Task<SchedulerSettingsUpdateResult> SetEnabledAsync(
        bool enabled,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            SchedulerSettingsState current = await ReadRequiredAsync(ct).ConfigureAwait(false);
            if (current.Enabled == enabled && current.RequestedEnabled == enabled)
                return new SchedulerSettingsUpdateResult(await GetAsync(ct).ConfigureAwait(false));

            if (enabled)
            {
                IReadOnlyList<OperationIssueDto> blockers = await EnableBlockersAsync(ct).ConfigureAwait(false);
                if (blockers.Count > 0)
                {
                    return new SchedulerSettingsUpdateResult(
                        null,
                        "scheduler-prerequisites-not-met",
                        string.Join(" ", blockers.Select(blocker => blocker.Message)));
                }
            }

            (SchedulerSettingsState updated, _) = await settings.SetEnabledAsync(enabled, ct).ConfigureAwait(false);
            if (enabled && updated.NextEvaluationAt is null)
            {
                _ = await settings.SetNextEvaluationAtAsync(
                    _time.GetUtcNow() + options.ScheduleInterval,
                    ct).ConfigureAwait(false);
            }

            return new SchedulerSettingsUpdateResult(await GetAsync(ct).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<OperationIssueDto>> EnableBlockersAsync(CancellationToken ct)
    {
        IReadOnlyList<AppRegistration> registrations = await registry.ListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> records = await operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        AppRegistration[] verified = registrations
            .Where(registration =>
                !registration.IsPendingInstall &&
                FindVerifiedEvidence(registration, records) is not null)
            .ToArray();
        if (verified.Length == 0)
        {
            return
            [
                new OperationIssueDto(
                    "verified-registration-required",
                    "Install and verify at least one app before enabling automatic refresh."),
            ];
        }

        IReadOnlyList<DeviceInfo> reachable;
        try
        {
            reachable = await devices.ListDevicesAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return
            [
                new OperationIssueDto(
                    "scheduler-device-unavailable",
                    "Reconnect a verified iPhone over USB or paired Wi-Fi before enabling automatic refresh."),
            ];
        }

        bool signerReady = false;
        bool deviceReady = false;
        foreach (AppRegistration registration in verified)
        {
            bool unresolved = records.Any(record =>
                OperationReconciliationEvidence.IsUnresolvedMutation(record, records) &&
                string.Equals(record.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase));
            if (unresolved)
                continue;

            DeviceInfo? device = reachable.FirstOrDefault(candidate =>
                string.Equals(candidate.Udid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.TrustState, "trusted", StringComparison.OrdinalIgnoreCase) &&
                candidate.UsableForInstall);
            bool thisDeviceReady = device is not null;
            deviceReady |= thisDeviceReady;

            SigningIdentityInspection inspection;
            try
            {
                inspection = await signingIdentity.InspectAsync(
                    registration.AppleId,
                    registration.TeamId,
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                continue;
            }

            bool thisSignerReady = string.Equals(inspection.State, "reusable", StringComparison.Ordinal) &&
                (inspection.ExpiresAt is null || inspection.ExpiresAt > _time.GetUtcNow());
            signerReady |= thisSignerReady;
            if (thisDeviceReady && thisSignerReady)
                return [];
        }

        var blockers = new List<OperationIssueDto>(2);
        if (!signerReady)
        {
            blockers.Add(new OperationIssueDto(
                "scheduler-signer-unavailable",
                "A reusable Sideport signing identity is required before enabling automatic refresh."));
        }
        if (!deviceReady)
        {
            blockers.Add(new OperationIssueDto(
                "scheduler-device-unavailable",
                "Reconnect a verified iPhone over USB or paired Wi-Fi before enabling automatic refresh."));
        }
        if (signerReady && deviceReady)
        {
            blockers.Add(new OperationIssueDto(
                "scheduler-registration-lineage-unavailable",
                "No single verified app registration currently has both a reusable signer and a trusted iPhone connection."));
        }
        return blockers;
    }

    private async Task<SchedulerSettingsState> ReadRequiredAsync(CancellationToken ct) =>
        await settings.ReadAsync(ct).ConfigureAwait(false)
        ?? throw new SchedulerSettingsStoreException(
            "Scheduler settings have not been initialized.",
            new FileNotFoundException("The scheduler settings record does not exist."));

    private bool IsDue(
        AppRegistration registration,
        IReadOnlyList<OperationRecordDto> records,
        DateTimeOffset now)
    {
        if (registration.IsPendingInstall || FindVerifiedEvidence(registration, records) is null)
            return false;

        OperationRecordDto? latest = records.FirstOrDefault(record =>
            record.Type is "install" or "refresh" or "verify-existing-registration" or "reconcile" &&
            string.Equals(record.Status, "succeeded", StringComparison.Ordinal) &&
            record.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } &&
            string.Equals(record.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Target.BundleId, registration.BundleId, StringComparison.Ordinal));
        if (latest is null)
            return false;

        bool expiryDue = latest.Result!.ExpiresAt <= now + options.RefreshLeadTime;
        bool cadenceDue = options.ResignInterval is { } cadence &&
            (latest.CompletedAt ?? latest.UpdatedAt) <= now - cadence;
        return expiryDue || cadenceDue;
    }

    private static OperationRecordDto? FindVerifiedEvidence(
        AppRegistration registration,
        IReadOnlyList<OperationRecordDto> records)
    {
        if (string.IsNullOrWhiteSpace(registration.LastVerifiedOperationId))
            return null;

        return records.FirstOrDefault(record =>
            string.Equals(record.OperationId, registration.LastVerifiedOperationId, StringComparison.Ordinal) &&
            record.Type is "install" or "refresh" or "verify-existing-registration" or "reconcile" &&
            string.Equals(record.Status, "succeeded", StringComparison.Ordinal) &&
            record.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } &&
            string.Equals(record.Target.DeviceUdid, registration.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Target.BundleId, registration.BundleId, StringComparison.Ordinal));
    }
}
