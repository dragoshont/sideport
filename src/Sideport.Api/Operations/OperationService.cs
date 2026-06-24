using Sideport.Core;
using Sideport.DeveloperApi;
using Sideport.DeveloperApi.Packaging;
using Sideport.Orchestrator;

namespace Sideport.Api.Operations;

public sealed class OperationService(
    IAppRegistry registry,
    RefreshOrchestrator orchestrator,
    SignerOptions signerOptions,
    OperationStore store,
    OperationQueue queue)
{
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private static readonly string[] PlannedRefreshMutations =
    [
        "Authenticate Apple ID from server-side custody",
        "Register device with Apple if needed",
        "Ensure App ID, certificate, and provisioning profile",
        "Re-sign IPA",
        "Install signed IPA on the device",
    ];

    public async Task<OperationPreflightDto> PreflightRefreshAsync(string deviceUdid, string bundleId, CancellationToken ct = default)
    {
        AppRegistration? registration = await registry.FindAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
        var target = registration is null
            ? new OperationTargetDto(deviceUdid, bundleId)
            : new OperationTargetDto(registration.DeviceUdid, registration.BundleId, registration.AppleId, registration.TeamId);
        var blockers = new List<OperationIssueDto>();
        var warnings = new List<OperationIssueDto>();
        IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
        int deviceRegistrations = apps.Count(app => string.Equals(app.DeviceUdid, deviceUdid, StringComparison.OrdinalIgnoreCase));

        if (registration is null)
        {
            blockers.Add(new OperationIssueDto(
                "registration-missing",
                "No Sideport registration exists for this device and bundle ID."));
        }
        else
        {
            if (!File.Exists(registration.InputIpaPath))
            {
                blockers.Add(new OperationIssueDto(
                    "ipa-missing",
                    "The registered IPA is missing from durable storage.",
                    Detail: registration.InputIpaPath));
            }
            else
            {
                try
                {
                    IpaInfo info = IpaInspector.Inspect(registration.InputIpaPath);
                    if (!string.Equals(info.BundleIdentifier, registration.BundleId, StringComparison.Ordinal))
                    {
                        blockers.Add(new OperationIssueDto(
                            "bundle-mismatch",
                            "The stored IPA bundle ID no longer matches the registration.",
                            Detail: $"expected {registration.BundleId}, inspected {info.BundleIdentifier}"));
                    }
                }
                catch (Exception ex) when (ex is FormatException || ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    blockers.Add(new OperationIssueDto(
                        "ipa-inspection-failed",
                        "The stored IPA could not be inspected before refresh.",
                        Detail: ex.Message));
                }
            }
        }

        if (!File.Exists(signerOptions.SignerBinaryPath))
        {
            blockers.Add(new OperationIssueDto(
                "signer-missing",
                "The signer binary is not available at the configured path.",
                Detail: signerOptions.SignerBinaryPath));
        }

        if (registration is not null)
        {
            warnings.Add(new OperationIssueDto(
                "device-reachability-not-verified",
                "The registration exists, but this preflight does not prove the device is currently reachable."));
            warnings.Add(new OperationIssueDto(
                "apple-session-not-prevalidated",
                "Apple authentication, device registration, App ID, certificate, and profile checks run inside the refresh operation."));
        }

        var limits = new[]
        {
            new OperationLimitDto("free-device-app-slots", "Free-account app slots", deviceRegistrations, 3),
        };

        return new OperationPreflightDto(
            Ready: blockers.Count == 0,
            Target: target,
            Blockers: blockers,
            Warnings: warnings,
            PlannedMutations: PlannedRefreshMutations,
            ScarceLimits: limits,
            RequiresConfirmation: true);
    }

    public async Task<(OperationRecordDto Record, bool Created)> RefreshAsync(
        string deviceUdid,
        string bundleId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? parentOperationId = null,
        int attempt = 1,
        CancellationToken ct = default)
    {
        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string? trimmedKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
            OperationPreflightDto preflight = await PreflightRefreshAsync(deviceUdid, bundleId, ct).ConfigureAwait(false);
            OperationTargetDto target = preflight.Target;

            if (trimmedKey is not null)
            {
                OperationRecordDto? existing = await store.FindByIdempotencyAsync("refresh", target, actor, trimmedKey, ct).ConfigureAwait(false);
                if (existing is not null)
                    return (existing, false);
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string operationId = NewOperationId(now);
            OperationRecordDto record;

            if (!preflight.Ready)
            {
                OperationIssueDto error = preflight.Blockers.FirstOrDefault()
                    ?? new OperationIssueDto("preflight-blocked", "Preflight blocked the refresh operation.");
                record = new OperationRecordDto(
                    operationId,
                    "refresh",
                    "blocked",
                    now,
                    now,
                    now,
                    now,
                    actor,
                    trimmedKey,
                    attempt,
                    target,
                    [new OperationStageDto("preflight", "Preflight", "blocked", now, now, error.Message, error)],
                    null,
                    error,
                    Cancelable: false,
                    Retryable: true,
                    Rerunnable: false,
                    CorrelationId: operationId,
                    ParentOperationId: parentOperationId);

                return await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            }

            var stages = new List<OperationStageDto>
            {
                new("preflight", "Preflight", "succeeded", now, now, "Ready to refresh.", null),
                new("refresh", "Sign and install", "pending", null, null, "Waiting for the single-flight signer.", null),
            };

            record = new OperationRecordDto(
                operationId,
                "refresh",
                "queued",
                now,
                null,
                now,
                null,
                actor,
                trimmedKey,
                attempt,
                target,
                stages,
                null,
                null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                ParentOperationId: parentOperationId);

            (OperationRecordDto initialRecord, bool created) = await store.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (!created)
                return (initialRecord, false);

            queue.Enqueue(initialRecord.OperationId);
            return (initialRecord, true);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public async Task ProcessQueuedRefreshAsync(string operationId, CancellationToken ct = default)
    {
        await _operationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DateTimeOffset started = DateTimeOffset.UtcNow;
            OperationRecordDto? record = await store.TransitionAsync(operationId, existing =>
            {
                if (!string.Equals(existing.Status, "queued", StringComparison.Ordinal))
                    return null;

                OperationStageDto[] runningStages = existing.Stages.Select(stage =>
                    string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                        ? stage with { Status = "running", StartedAt = started, Message = "Refresh is running." }
                        : stage).ToArray();

                return existing with
                {
                    Status = "running",
                    StartedAt = started,
                    UpdatedAt = started,
                    Stages = runningStages,
                    Cancelable = false,
                };
            }, ct).ConfigureAwait(false);
            if (record is null || !string.Equals(record.Status, "running", StringComparison.Ordinal))
                return;

            RefreshResult result = await orchestrator.RefreshAsync(record.Target.DeviceUdid, record.Target.BundleId, ct).ConfigureAwait(false);
            DateTimeOffset completed = DateTimeOffset.UtcNow;
            OperationIssueDto? terminalError = result.Success
                ? null
                : new OperationIssueDto("refresh-failed", result.Error ?? "Refresh failed.");
            OperationStageDto[] completedStages = record.Stages.Select(stage =>
                string.Equals(stage.Id, "refresh", StringComparison.Ordinal)
                    ? stage with
                    {
                        Status = result.Success ? "succeeded" : "failed",
                        CompletedAt = completed,
                        Message = result.Success ? "Refresh completed." : terminalError!.Message,
                        Error = terminalError,
                    }
                    : stage).ToArray();

            record = record with
            {
                Status = result.Success ? "succeeded" : "failed",
                UpdatedAt = completed,
                CompletedAt = completed,
                Stages = completedStages,
                Result = new OperationResultDto(result.Success, result.BundleId, result.NewExpiry, result.Error),
                Error = terminalError,
                Cancelable = false,
                Retryable = !result.Success,
                Rerunnable = result.Success,
            };
            await store.UpdateAsync(record, ct).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task RequeuePendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OperationRecordDto> records = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        foreach (OperationRecordDto record in records.Where(record => record.Status is "queued" or "waiting"))
            queue.Enqueue(record.OperationId);
    }

    public async Task<(OperationRecordDto? Record, string? Error)> CancelAsync(string operationId, string? reason, CancellationToken ct = default)
    {
        OperationRecordDto? record = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (record is null)
            return (null, "operation-not-found");
        if (record.Status is "canceled" or "canceling")
            return (record, null);
        if (record.Status is not ("queued" or "waiting"))
            return (record, "operation-not-cancelable");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        OperationIssueDto canceled = new("operation-canceled", string.IsNullOrWhiteSpace(reason) ? "Operation canceled before signing started." : reason.Trim());
        OperationRecordDto? updated = await store.TransitionAsync(operationId, existing =>
        {
            if (existing.Status is "canceled" or "canceling")
                return existing;
            if (existing.Status is not ("queued" or "waiting"))
                return null;
            return existing with
            {
                Status = "canceled",
                UpdatedAt = now,
                CompletedAt = now,
                Error = canceled,
                Cancelable = false,
                Retryable = false,
                Rerunnable = true,
            };
        }, ct).ConfigureAwait(false);
        if (updated is null)
            return (null, "operation-not-found");
        return updated.Status == "canceled" ? (updated, null) : (updated, "operation-not-cancelable");
    }

    public async Task<(OperationRecordDto? Record, bool Created, string? Error)> RetryAsync(string operationId, OperationActorDto actor, string? idempotencyKey, CancellationToken ct = default)
    {
        OperationRecordDto? source = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, false, "operation-not-found");
        if (!source.Retryable)
            return (source, false, "operation-not-retryable");
        return await RefreshFromSourceAsync(source, actor, idempotencyKey, source.Attempt + 1, ct).ConfigureAwait(false);
    }

    public async Task<(OperationRecordDto? Record, bool Created, string? Error)> RerunAsync(string operationId, OperationActorDto actor, string? idempotencyKey, CancellationToken ct = default)
    {
        OperationRecordDto? source = await store.FindAsync(operationId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, false, "operation-not-found");
        if (source.CompletedAt is null)
            return (source, false, "operation-not-rerunnable");
        return await RefreshFromSourceAsync(source, actor, idempotencyKey, 1, ct).ConfigureAwait(false);
    }

    private async Task<(OperationRecordDto? Record, bool Created, string? Error)> RefreshFromSourceAsync(OperationRecordDto source, OperationActorDto actor, string? idempotencyKey, int attempt, CancellationToken ct)
    {
        (OperationRecordDto record, bool created) = await RefreshAsync(source.Target.DeviceUdid, source.Target.BundleId, actor, idempotencyKey, source.OperationId, attempt, ct).ConfigureAwait(false);
        return (record, created, null);
    }

    public async Task<IReadOnlyList<RenewalItemDto>> RenewalsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AppRegistration> apps = await registry.ListAsync(ct).ConfigureAwait(false);
        IReadOnlyList<OperationRecordDto> operations = await store.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return apps.Select(app => ToRenewal(
            app,
            orchestrator.GetState(app.DeviceUdid, app.BundleId),
            LatestOperation(app, operations),
            LatestSuccessfulOperation(app, operations),
            now)).ToArray();
    }

    private static RenewalItemDto ToRenewal(
        AppRegistration app,
        RefreshState? state,
        OperationRecordDto? latestOperation,
        OperationRecordDto? latestSuccessfulOperation,
        DateTimeOffset now)
    {
        DateTimeOffset? effectiveExpiry = state?.ExpiresAt ?? DurableSuccessfulExpiry(latestSuccessfulOperation);
        string risk = RenewalRisk(state, latestOperation, effectiveExpiry, now);
        string status = latestOperation?.Status switch
        {
            "running" => "running",
            "blocked" => "blocked",
            "failed" => "failed",
            _ when state?.LastSucceeded == false => "failed",
            _ => "idle",
        };
        string? blocker = latestOperation?.Error?.Message ?? state?.LastError;
        return new RenewalItemDto(
            $"{app.DeviceUdid}:{app.BundleId}",
            app.DeviceUdid,
            app.BundleId,
            app.TeamId,
            risk,
            status,
            effectiveExpiry,
            blocker,
            latestOperation?.OperationId);
    }

    private static DateTimeOffset? DurableSuccessfulExpiry(OperationRecordDto? latestOperation) =>
        latestOperation is { Status: "succeeded", Result.Success: true }
            ? latestOperation.Result.ExpiresAt
            : null;

    private static OperationRecordDto? LatestOperation(AppRegistration app, IReadOnlyList<OperationRecordDto> operations) =>
        operations.FirstOrDefault(operation =>
            string.Equals(operation.Type, "refresh", StringComparison.Ordinal) &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));

    private static OperationRecordDto? LatestSuccessfulOperation(AppRegistration app, IReadOnlyList<OperationRecordDto> operations) =>
        operations.FirstOrDefault(operation =>
            string.Equals(operation.Type, "refresh", StringComparison.Ordinal) &&
            string.Equals(operation.Status, "succeeded", StringComparison.Ordinal) &&
            operation.Result?.Success == true &&
            string.Equals(operation.Target.DeviceUdid, app.DeviceUdid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(operation.Target.BundleId, app.BundleId, StringComparison.Ordinal));

    private static string RenewalRisk(RefreshState? state, OperationRecordDto? latestOperation, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (state?.LastSucceeded == false || latestOperation?.Status is "blocked" or "failed") return "blocked";
        if (expiresAt is not { } expiry) return "unknown";
        TimeSpan remaining = expiry - now;
        if (remaining <= TimeSpan.Zero) return "due-now";
        if (remaining <= TimeSpan.FromDays(2)) return "upcoming";
        return "healthy";
    }

    private static string NewOperationId(DateTimeOffset now) => $"op_{now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..31];
}