using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Api.WorkspaceAccess;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed class OperationScheduler(
    IAppRegistry registry,
    OperationService operations,
    OperationStore operationStore,
    SchedulerSettingsStore schedulerSettings,
    OrchestratorOptions options,
    TimeProvider? timeProvider = null,
    ILogger<OperationScheduler>? logger = null,
    WorkspaceExecutionAuthorizer? executionAuthorization = null) : BackgroundService
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ILogger<OperationScheduler> _logger = logger ?? NullLogger<OperationScheduler>.Instance;
    private static readonly OperationActorDto SchedulerActor = new("system", "system:scheduler");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(options.ScheduleInterval, _time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "operation scheduler tick failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken).ConfigureAwait(false));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        SchedulerSettingsState? settings = await schedulerSettings.ReadAsync(ct).ConfigureAwait(false);
        if (settings is null)
        {
            _logger.LogWarning("scheduler: skipping tick because durable settings are not initialized");
            return;
        }
        if (!settings.Enabled)
            return;

        DateTimeOffset startedAt = _time.GetUtcNow();
        string evaluationId = $"sched_{startedAt:yyyyMMddHHmmss}_{Guid.NewGuid():N}";
        int dueCount = 0;
        int queuedCount = 0;
        int blockedCount = 0;
        int skippedCount = 0;

        try
        {
            IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
            IReadOnlyList<OperationRecordDto> operationRecords = await operationStore.ListAsync(
                limit: null,
                ct: ct).ConfigureAwait(false);

            var due = new List<(AppRegistration App, string? OwnerMemberId)>();
            foreach (AppRegistration app in apps
                         .OrderBy(item => item.DeviceUdid, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.BundleId, StringComparer.Ordinal))
            {
                OperationRecordDto? verified = FindVerifiedRegistrationEvidence(operationRecords, app);
                if (!string.Equals(app.Lifecycle, "active", StringComparison.Ordinal) || verified is null)
                {
                    // Missing lifecycle deserializes to "active" for compatibility,
                    // but a legacy registration without a durable verification ID
                    // remains scheduler-ineligible.
                    skippedCount++;
                    continue;
                }

                RefreshState state = LatestDurableRefreshState(operationRecords, app, verified);
                if (!state.IsDue(startedAt, options.RefreshLeadTime, options.ResignInterval))
                    continue;

                dueCount++;
                if (HasUnresolvedDeviceOperation(operationRecords, app.DeviceUdid))
                {
                    blockedCount++;
                    continue;
                }
                if (HasActiveInstall(operationRecords, app))
                {
                    skippedCount++;
                    continue;
                }
                if (executionAuthorization is not null)
                {
                    WorkspaceExecutionDecision authorization = await executionAuthorization
                        .AuthorizeSchedulerTargetAsync(app.DeviceUdid, ct)
                        .ConfigureAwait(false);
                    if (!authorization.IsAllowed)
                    {
                        skippedCount++;
                        continue;
                    }
                    due.Add((app, authorization.OwnerMemberId));
                }
                else
                {
                    due.Add((app, null));
                }
            }

            if (due.Count > 0)
                _logger.LogInformation("scheduler: enqueueing {Count} app refresh operation(s)", due.Count);
            foreach ((AppRegistration app, string? queuedOwnerMemberId) in due)
            {
                ct.ThrowIfCancellationRequested();
                string? ownerMemberId = queuedOwnerMemberId;
                if (executionAuthorization is not null)
                {
                    WorkspaceExecutionDecision authorization = await executionAuthorization
                        .AuthorizeSchedulerTargetAsync(app.DeviceUdid, ct)
                        .ConfigureAwait(false);
                    if (!authorization.IsAllowed)
                    {
                        skippedCount++;
                        continue;
                    }
                    ownerMemberId = authorization.OwnerMemberId;
                }
                string idempotencyKey = $"scheduler:{app.DeviceUdid}:{app.BundleId}:{startedAt:yyyyMMddHH}";
                (OperationRecordDto record, bool created) = await operations.RefreshAsync(
                    app.DeviceUdid,
                    app.BundleId,
                    SchedulerActor,
                    idempotencyKey,
                    actorMemberId: null,
                    ownerMemberId,
                    parentOperationId: null,
                    attempt: 1,
                    ct: ct).ConfigureAwait(false);
                if (string.Equals(record.Status, "blocked", StringComparison.Ordinal))
                    blockedCount++;
                else if (created && record.Status is "queued" or "waiting" or "running")
                    queuedCount++;
                else
                    skippedCount++;
            }

            DateTimeOffset completedAt = _time.GetUtcNow();
            string outcome = blockedCount > 0 ? "completed-with-blockers" : "succeeded";
            await schedulerSettings.RecordEvaluationAsync(
                new SchedulerEvaluationReceipt(
                    evaluationId,
                    startedAt,
                    completedAt,
                    outcome,
                    dueCount,
                    queuedCount,
                    blockedCount,
                    skippedCount),
                completedAt + options.ScheduleInterval,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            DateTimeOffset completedAt = _time.GetUtcNow();
            try
            {
                await schedulerSettings.RecordEvaluationAsync(
                    new SchedulerEvaluationReceipt(
                        evaluationId,
                        startedAt,
                        completedAt,
                        "failed",
                        dueCount,
                        queuedCount,
                        blockedCount,
                        skippedCount),
                    completedAt + options.ScheduleInterval,
                    ct).ConfigureAwait(false);
            }
            catch (Exception evidenceError)
            {
                _logger.LogError(evidenceError, "scheduler: failed to persist evaluation evidence");
            }
            throw;
        }
    }

    private static bool HasActiveInstall(
        IReadOnlyList<OperationRecordDto> operations,
        AppRegistration app) =>
        operations.Any(operation =>
            string.Equals(operation.Type, "install", StringComparison.Ordinal) &&
            (operation.Status is "queued" or "waiting" or "running") &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));

    private static bool HasUnresolvedDeviceOperation(
        IReadOnlyList<OperationRecordDto> operations,
        string deviceUdid) =>
        operations.Any(operation =>
            OperationReconciliationEvidence.IsUnresolvedMutation(operation, operations) &&
            string.Equals(operation.Target.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));

    private static OperationRecordDto? FindVerifiedRegistrationEvidence(
        IReadOnlyList<OperationRecordDto> operations,
        AppRegistration app)
    {
        if (string.IsNullOrWhiteSpace(app.LastVerifiedOperationId))
            return null;

        return operations.FirstOrDefault(operation =>
            string.Equals(operation.OperationId, app.LastVerifiedOperationId, StringComparison.Ordinal) &&
            operation.Type is "install" or "refresh" or "verify-existing-registration" or "reconcile" &&
            string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
            operation.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));
    }

    private static RefreshState LatestDurableRefreshState(
        IReadOnlyList<OperationRecordDto> operations,
        AppRegistration app,
        OperationRecordDto verified)
    {
        OperationRecordDto latest = operations.FirstOrDefault(operation =>
            operation.Type is "install" or "refresh" or "verify-existing-registration" or "reconcile" &&
            string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
            operation.Result is { Success: true, ExpiresAt: not null, Version.Length: > 0 } &&
            operation.CreatedAt >= verified.CreatedAt &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal))
            ?? verified;

        DateTimeOffset expiresAt = latest.Result!.ExpiresAt!.Value;
        DateTimeOffset succeededAt = latest.CompletedAt ?? latest.UpdatedAt;
        return new RefreshState(
            app.DeviceUdid,
            app.BundleId,
            expiresAt,
            succeededAt,
            LastSucceeded: true,
            LastError: null,
            LastSucceededUtc: succeededAt);
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
