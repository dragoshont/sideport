using Sideport.Api.Operations;
using Sideport.Api.WorkspaceAccess;
using Sideport.Core;

namespace Sideport.Api.DeviceInventory;

public sealed class DeviceEnrollmentService
{
    public const string OperationType = "enroll-device";

    private static readonly string[] ActiveStatuses = ["queued", "waiting", "running"];
    private readonly OperationStore _operations;
    private readonly KnownDeviceStore _knownDevices;
    private readonly KnownDeviceService _inventory;
    private readonly IDeviceController _devices;
    private readonly DeviceEnrollmentQueue _queue;
    private readonly DeviceEnrollmentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly WorkspaceExecutionAuthorizer? _executionAuthorization;
    private readonly SemaphoreSlim _submissionGate = new(1, 1);
    private readonly SemaphoreSlim _processingGate = new(1, 1);

    public DeviceEnrollmentService(
        OperationStore operations,
        KnownDeviceStore knownDevices,
        KnownDeviceService inventory,
        IDeviceController devices,
        DeviceEnrollmentQueue queue,
        DeviceEnrollmentOptions? options = null,
        TimeProvider? timeProvider = null,
        WorkspaceExecutionAuthorizer? executionAuthorization = null)
    {
        _operations = operations;
        _knownDevices = knownDevices;
        _inventory = inventory;
        _devices = devices;
        _queue = queue;
        _options = options ?? new DeviceEnrollmentOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _executionAuthorization = executionAuthorization;
    }

    public Task<DeviceEnrollmentSubmissionResult> StartAsync(
        DeviceEnrollmentRequest request,
        OperationActorDto actor,
        CancellationToken ct = default) =>
        StartAsync(request, actor, actorMemberId: null, ct: ct);

    public async Task<DeviceEnrollmentSubmissionResult> StartAsync(
        DeviceEnrollmentRequest request,
        OperationActorDto actor,
        string? actorMemberId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actor);
        string idempotencyKey = request.IdempotencyKey?.Trim() ?? "";
        if (idempotencyKey.Length == 0)
        {
            return new DeviceEnrollmentSubmissionResult(
                null,
                false,
                "idempotency-key-required",
                "An idempotency key is required to start device enrollment.");
        }

        string? requestedUdid = NormalizeOptional(request.DeviceUdid);
        string? targetMemberId = NormalizeOptional(request.TargetMemberId);
        actorMemberId = NormalizeOptional(actorMemberId);
        if (_executionAuthorization is not null)
        {
            WorkspaceExecutionDecision authorization = await _executionAuthorization
                .AuthorizeSubmissionAsync(
                    actor,
                    actorMemberId,
                    targetMemberId,
                    requestedUdid,
                    enrollmentTarget: true,
                    assignDefaultOwner: true,
                    ct: ct)
                .ConfigureAwait(false);
            if (!authorization.IsAllowed)
            {
                return new DeviceEnrollmentSubmissionResult(
                    null,
                    false,
                    authorization.ErrorCode,
                    authorization.Message);
            }
            targetMemberId = authorization.OwnerMemberId;
        }
        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? replay = await _operations.FindByActorAndIdempotencyAsync(
                OperationType,
                actor,
                idempotencyKey,
                ct).ConfigureAwait(false);
            if (replay is not null)
            {
                if (!SameOptionalMemberId(replay.OwnerMemberId, targetMemberId) ||
                    !SameOptionalMemberId(replay.ActorMemberId, actorMemberId))
                {
                    return new DeviceEnrollmentSubmissionResult(
                        replay,
                        false,
                        "idempotency-member-conflict",
                        "This idempotency key was already used for a different Sideport member.");
                }
                if (requestedUdid is not null &&
                    (string.IsNullOrWhiteSpace(replay.Target.DeviceUdid) ||
                     !SameUdid(replay.Target.DeviceUdid, requestedUdid)))
                {
                    return new DeviceEnrollmentSubmissionResult(
                        replay,
                        false,
                        "idempotency-target-conflict",
                        "This idempotency key was already used for a different iPhone enrollment target.");
                }

                return new DeviceEnrollmentSubmissionResult(replay, false, null);
            }

            IReadOnlyList<OperationRecordDto> records = await _operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            OperationRecordDto? active = records.FirstOrDefault(IsActiveEnrollment);
            if (active is not null)
            {
                return new DeviceEnrollmentSubmissionResult(
                    active,
                    false,
                    "device-enrollment-active",
                    "Another iPhone enrollment is already in progress.");
            }

            if (requestedUdid is not null)
            {
                IReadOnlyList<DeviceInfo> candidates;
                try
                {
                    candidates = await RunBoundedAsync(
                        token => EligibleUsbCandidatesAsync(token),
                        _timeProvider.GetUtcNow().Add(_options.SessionTimeout),
                        ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return new DeviceEnrollmentSubmissionResult(
                        null,
                        false,
                        "device-discovery-unavailable",
                        "Sideport could not check the selected iPhone.");
                }

                if (!candidates.Any(candidate => SameUdid(candidate.Udid, requestedUdid)))
                {
                    return new DeviceEnrollmentSubmissionResult(
                        null,
                        false,
                        "selected-device-ineligible",
                        "The selected iPhone must be unaccepted and currently connected over USB.");
                }
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            string operationId = $"op_enroll_{Guid.NewGuid():N}";
            var record = new OperationRecordDto(
                operationId,
                OperationType,
                "waiting",
                now,
                now,
                now,
                null,
                actor,
                idempotencyKey,
                1,
                new OperationTargetDto(requestedUdid, null, Kind: "device-enrollment"),
                CreateStages(now),
                null,
                null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: operationId,
                ExpiresAt: now.Add(_options.SessionTimeout),
                ActorMemberId: actorMemberId,
                OwnerMemberId: targetMemberId);

            (OperationRecordDto saved, bool created) = await _operations.AddIfIdempotentMissingAsync(record, ct).ConfigureAwait(false);
            if (created)
                _queue.Enqueue(saved.OperationId);
            return new DeviceEnrollmentSubmissionResult(saved, created, null);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public Task<DeviceEnrollmentSubmissionResult> RetryAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        CancellationToken ct = default) =>
        RetryAsync(operationId, actor, idempotencyKey, actorMemberId: null, ct: ct);

    public async Task<DeviceEnrollmentSubmissionResult> RetryAsync(
        string operationId,
        OperationActorDto actor,
        string? idempotencyKey,
        string? actorMemberId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentNullException.ThrowIfNull(actor);
        string? trimmedKey = NormalizeOptional(idempotencyKey);
        actorMemberId = NormalizeOptional(actorMemberId);

        await _submissionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            OperationRecordDto? source = await _operations.FindAsync(operationId, ct).ConfigureAwait(false);
            if (source is null)
            {
                return new DeviceEnrollmentSubmissionResult(
                    null,
                    false,
                    "operation-not-found",
                    "Device enrollment operation not found.");
            }

            if (_executionAuthorization is not null)
            {
                WorkspaceExecutionDecision authorization = await _executionAuthorization
                    .AuthorizeSubmissionAsync(
                        actor,
                        actorMemberId,
                        source.OwnerMemberId,
                        source.Target.DeviceUdid,
                        enrollmentTarget: true,
                        assignDefaultOwner: true,
                        ct: ct)
                    .ConfigureAwait(false);
                if (!authorization.IsAllowed)
                {
                    return new DeviceEnrollmentSubmissionResult(
                        source,
                        false,
                        authorization.ErrorCode,
                        authorization.Message);
                }
                source = source with { OwnerMemberId = authorization.OwnerMemberId };
            }

            if (trimmedKey is not null)
            {
                OperationRecordDto? replay = await _operations.FindByActorAndIdempotencyAsync(
                    OperationType,
                    actor,
                    trimmedKey,
                    ct).ConfigureAwait(false);
                if (replay is not null)
                {
                    if (!SameOptionalMemberId(replay.OwnerMemberId, source.OwnerMemberId) ||
                        !SameOptionalMemberId(replay.ActorMemberId, actorMemberId))
                    {
                        return new DeviceEnrollmentSubmissionResult(
                            replay,
                            false,
                            "idempotency-member-conflict",
                            "This idempotency key was already used for a different Sideport member.");
                    }
                    if (!SameOptionalUdid(replay.Target.DeviceUdid, source.Target.DeviceUdid))
                    {
                        return new DeviceEnrollmentSubmissionResult(
                            replay,
                            false,
                            "idempotency-target-conflict",
                            "This idempotency key was already used for a different iPhone enrollment retry.");
                    }

                    return new DeviceEnrollmentSubmissionResult(replay, false, null);
                }
            }

            if (!string.Equals(source.Type, OperationType, StringComparison.Ordinal) ||
                !source.Retryable ||
                IsActiveEnrollment(source))
            {
                return new DeviceEnrollmentSubmissionResult(
                    source,
                    false,
                    "operation-not-retryable",
                    "This device enrollment is not retryable.");
            }

            if (!string.IsNullOrWhiteSpace(source.Target.DeviceUdid))
            {
                KnownDeviceRecord? accepted = await _knownDevices.FindAsync(source.Target.DeviceUdid, ct).ConfigureAwait(false);
                if (string.Equals(accepted?.InventoryState, "accepted", StringComparison.Ordinal))
                {
                    return new DeviceEnrollmentSubmissionResult(
                        source,
                        false,
                        "operation-not-retryable",
                        "This iPhone is already accepted by Sideport.");
                }
            }

            IReadOnlyList<OperationRecordDto> records = await _operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
            OperationRecordDto? active = records.FirstOrDefault(IsActiveEnrollment);
            if (active is not null)
            {
                return new DeviceEnrollmentSubmissionResult(
                    active,
                    false,
                    "device-enrollment-active",
                    "Another iPhone enrollment is already in progress.");
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            bool recoverWithoutPairing = PairingWasRequested(source);
            IReadOnlyList<OperationStageDto> stages = CreateStages(now);
            if (recoverWithoutPairing)
            {
                stages = UpdateStage(
                    stages,
                    "request-pairing",
                    "succeeded",
                    now,
                    "Trust was requested in an earlier attempt and will not be requested again.");
            }

            string retryOperationId = $"op_enroll_{Guid.NewGuid():N}";
            var retry = new OperationRecordDto(
                retryOperationId,
                OperationType,
                "waiting",
                now,
                now,
                now,
                null,
                actor,
                trimmedKey,
                source.Attempt + 1,
                source.Target with { Kind = "device-enrollment", BundleId = null },
                stages,
                null,
                null,
                Cancelable: true,
                Retryable: false,
                Rerunnable: false,
                CorrelationId: source.CorrelationId,
                ParentOperationId: source.OperationId,
                ExpiresAt: now.Add(_options.SessionTimeout),
                DevicePairingRequestedAt: recoverWithoutPairing
                    ? source.DevicePairingRequestedAt ?? source.UpdatedAt
                    : null,
                ActorMemberId: actorMemberId,
                OwnerMemberId: source.OwnerMemberId);

            (OperationRecordDto saved, bool created) = await _operations.AddIfIdempotentMissingAsync(retry, ct).ConfigureAwait(false);
            if (created)
                _queue.Enqueue(saved.OperationId);
            return new DeviceEnrollmentSubmissionResult(saved, created, null);
        }
        finally
        {
            _submissionGate.Release();
        }
    }

    public async Task RequeuePendingAsync(CancellationToken ct = default)
    {
        IReadOnlyList<OperationRecordDto> records = await _operations.ListAsync(limit: null, ct: ct).ConfigureAwait(false);
        foreach (OperationRecordDto record in records.Where(IsActiveEnrollment).OrderBy(record => record.CreatedAt))
            _queue.Enqueue(record.OperationId);
    }

    public async Task ProcessAsync(string operationId, CancellationToken ct = default)
    {
        await _processingGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            while (true)
            {
                OperationRecordDto? record = await _operations.FindAsync(operationId, ct).ConfigureAwait(false);
                if (record is null || !IsActiveEnrollment(record))
                    return;

                DateTimeOffset expiresAt = record.ExpiresAt ?? record.CreatedAt.Add(_options.SessionTimeout);
                if (await TryCompleteFromDurableAcceptanceAsync(record, ct).ConfigureAwait(false))
                    return;

                if (!await EnsureExecutionAuthorizedAsync(record, CurrentStageId(record), ct).ConfigureAwait(false))
                    return;

                if (_timeProvider.GetUtcNow() >= expiresAt)
                {
                    bool pairingWasRequested = PairingWasRequested(record);
                    await FailAsync(
                        record,
                        pairingWasRequested ? "recovery-required" : "failed",
                        pairingWasRequested ? "verify-lockdown" : CurrentStageId(record),
                        pairingWasRequested
                            ? RecoveryIssue("The enrollment window expired after a Trust request, so Sideport will not repeat pairing automatically.")
                            : new OperationIssueDto("device-enrollment-timeout", "No eligible iPhone completed enrollment within five minutes."),
                        retryable: true,
                        ct: ct).ConfigureAwait(false);
                    return;
                }

                if (PairingWasRequested(record))
                {
                    await ContinueRecoveryUntilTerminalAsync(record, expiresAt, ct).ConfigureAwait(false);
                    return;
                }

                IReadOnlyList<DeviceInfo> candidates;
                try
                {
                    if (!await EnsureExecutionAuthorizedAsync(record, "wait-for-usb", ct).ConfigureAwait(false))
                        return;
                    candidates = await RunBoundedAsync(
                        token => EligibleUsbCandidatesAsync(token),
                        expiresAt,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    await DelayUntilNextPollAsync(expiresAt, ct).ConfigureAwait(false);
                    continue;
                }

                DeviceInfo? selected;
                if (!string.IsNullOrWhiteSpace(record.Target.DeviceUdid))
                {
                    selected = candidates.FirstOrDefault(candidate => SameUdid(candidate.Udid, record.Target.DeviceUdid!));
                    if (selected is null)
                    {
                        await FailAsync(
                            record,
                            "failed",
                            "wait-for-usb",
                            new OperationIssueDto("device-enrollment-disconnected", "The selected iPhone is no longer connected over USB."),
                            retryable: true,
                            ct: ct).ConfigureAwait(false);
                        return;
                    }
                }
                else if (candidates.Count == 0)
                {
                    await DelayUntilNextPollAsync(expiresAt, ct).ConfigureAwait(false);
                    continue;
                }
                else if (candidates.Count > 1)
                {
                    DeviceEnrollmentCandidateDto[] safeCandidates = candidates.Select(ToSafeCandidate).ToArray();
                    await FailAsync(
                        record,
                        "blocked",
                        "wait-for-usb",
                        new OperationIssueDto("device-selection-required", "More than one unaccepted iPhone is connected. Choose one and start again."),
                        retryable: false,
                        candidates: safeCandidates,
                        ct: ct).ConfigureAwait(false);
                    return;
                }
                else
                {
                    selected = candidates[0];
                }

                OperationRecordDto? selectedRecord = await _operations.TransitionAsync(record.OperationId, existing =>
                {
                    if (!IsActiveEnrollment(existing))
                        return null;
                    DateTimeOffset now = _timeProvider.GetUtcNow();
                    return existing with
                    {
                        Target = existing.Target with { DeviceUdid = selected.Udid, Kind = "device-enrollment" },
                        UpdatedAt = now,
                        Stages = UpdateStage(existing.Stages, "wait-for-usb", "succeeded", now, "iPhone connected over USB."),
                    };
                }, ct).ConfigureAwait(false);
                if (selectedRecord is null || !IsActiveEnrollment(selectedRecord))
                    return;

                await ProcessSelectedAsync(selectedRecord, selected, expiresAt, ct).ConfigureAwait(false);
                return;
            }
        }
        finally
        {
            _processingGate.Release();
        }
    }

    private async Task ProcessSelectedAsync(
        OperationRecordDto record,
        DeviceInfo selected,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        if (!await EnsureExecutionAuthorizedAsync(record, "wait-for-usb", ct).ConfigureAwait(false))
            return;

        DeviceTrustProbe probe;
        try
        {
            probe = await RunBoundedAsync(token => _devices.ProbeTrustAsync(selected.Udid, token), expiresAt, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await FailAsync(record, "failed", "wait-for-usb", TimeoutIssue(), retryable: true, ct: ct).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await FailAsync(
                record,
                "failed",
                "wait-for-usb",
                new OperationIssueDto("device-trust-check-unavailable", "Sideport could not check Trust on the selected iPhone."),
                retryable: true,
                ct: ct).ConfigureAwait(false);
            return;
        }

        if (!SameUdid(probe.Udid, selected.Udid) || probe.Connection != DeviceConnection.Usb)
        {
            await FailAsync(record, "failed", "wait-for-usb", UsbRequiredIssue(), retryable: true, ct: ct).ConfigureAwait(false);
            return;
        }

        switch (NormalizeTrustState(probe.TrustState))
        {
            case "trusted" when probe.UsableForInstall:
                record = await MarkAlreadyTrustedAsync(record, ct).ConfigureAwait(false) ?? record;
                await VerifyAndAcceptAsync(record, selected, expiresAt, ct).ConfigureAwait(false);
                return;
            case "locked":
                await FailAsync(record, "failed", "await-user-trust", LockedIssue(), retryable: true, ct: ct).ConfigureAwait(false);
                return;
            case "untrusted":
                await RequestPairingAsync(record, selected, expiresAt, ct).ConfigureAwait(false);
                return;
            default:
                await FailAsync(
                    record,
                    "failed",
                    "request-pairing",
                    new OperationIssueDto("device-trust-check-unavailable", probe.TrustReason ?? "Sideport could not determine the iPhone's Trust state."),
                    retryable: true,
                    ct: ct).ConfigureAwait(false);
                return;
        }
    }

    private async Task RequestPairingAsync(
        OperationRecordDto record,
        DeviceInfo selected,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        if (!await EnsureExecutionAuthorizedAsync(record, "request-pairing", ct).ConfigureAwait(false))
            return;

        DateTimeOffset startedAt = _timeProvider.GetUtcNow();
        OperationRecordDto? pairingRecord = await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing) || PairingWasRequested(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(existing.Stages, "request-pairing", "running", startedAt, "Requesting Trust on the iPhone.");
            stages = UpdateStage(stages, "await-user-trust", "running", startedAt, "Unlock the iPhone and tap Trust.");
            return existing with
            {
                Status = "running",
                UpdatedAt = startedAt,
                Stages = stages,
                Cancelable = false,
                DevicePairingRequestedAt = startedAt,
            };
        }, ct).ConfigureAwait(false);
        if (pairingRecord is null || !IsActiveEnrollment(pairingRecord))
            return;

        if (!await EnsureExecutionAuthorizedAsync(pairingRecord, "request-pairing", ct).ConfigureAwait(false))
            return;

        DevicePairingResult result;
        try
        {
            result = await RunBoundedAsync(
                token => _devices.PairAsync(selected.Udid, progress: null, ct: token),
                expiresAt,
                ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await FailAsync(
                pairingRecord,
                "recovery-required",
                "await-user-trust",
                RecoveryIssue("The session expired after pairing was requested, so Sideport will check Trust before any retry."),
                retryable: true,
                ct: ct).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await MarkRecoveryWaitingAsync(
                pairingRecord,
                "Sideport is checking the Trust request automatically. Keep the iPhone connected and unlocked.",
                ct).ConfigureAwait(false);
            await ContinueRecoveryUntilTerminalAsync(pairingRecord, expiresAt, ct).ConfigureAwait(false);
            return;
        }

        string trustState = NormalizeTrustState(result.TrustState);
        if (result.Connection != DeviceConnection.Usb)
        {
            await MarkRecoveryWaitingAsync(
                pairingRecord,
                "Reconnect the iPhone over USB. Sideport will continue automatically without requesting pairing again.",
                ct).ConfigureAwait(false);
            await ContinueRecoveryUntilTerminalAsync(pairingRecord, expiresAt, ct).ConfigureAwait(false);
            return;
        }
        if (trustState == "locked")
        {
            await MarkRecoveryWaitingAsync(
                pairingRecord,
                "Unlock the iPhone. Sideport will continue automatically.",
                ct).ConfigureAwait(false);
            await ContinueRecoveryUntilTerminalAsync(pairingRecord, expiresAt, ct).ConfigureAwait(false);
            return;
        }
        if (trustState == "untrusted")
        {
            if (IsExplicitTrustDenial(result.TrustReason))
            {
                await FailAsync(
                    pairingRecord,
                    "failed",
                    "await-user-trust",
                    new OperationIssueDto("device-lockdown-untrusted", result.TrustReason ?? "Trust was declined on the iPhone."),
                    retryable: true,
                    ct: ct).ConfigureAwait(false);
                return;
            }
            await MarkRecoveryWaitingAsync(
                pairingRecord,
                "Tap Trust on the iPhone if asked. Sideport will continue automatically.",
                ct).ConfigureAwait(false);
            await ContinueRecoveryUntilTerminalAsync(pairingRecord, expiresAt, ct).ConfigureAwait(false);
            return;
        }
        if (trustState != "trusted" || !result.UsableForInstall)
        {
            await MarkRecoveryWaitingAsync(
                pairingRecord,
                result.TrustReason ?? "Sideport is checking the Trust request automatically.",
                ct).ConfigureAwait(false);
            await ContinueRecoveryUntilTerminalAsync(pairingRecord, expiresAt, ct).ConfigureAwait(false);
            return;
        }

        DateTimeOffset pairedAt = _timeProvider.GetUtcNow();
        OperationRecordDto? paired = await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(existing.Stages, "request-pairing", "succeeded", pairedAt, "Pairing request completed.");
            stages = UpdateStage(stages, "await-user-trust", "succeeded", pairedAt, "Trust confirmed on the iPhone.");
            return existing with { UpdatedAt = pairedAt, Stages = stages };
        }, ct).ConfigureAwait(false);
        if (paired is not null && IsActiveEnrollment(paired))
            await VerifyAndAcceptAsync(paired, selected, expiresAt, ct).ConfigureAwait(false);
    }

    private async Task ContinueRecoveryUntilTerminalAsync(OperationRecordDto record, DateTimeOffset expiresAt, CancellationToken ct)
    {
        while (_timeProvider.GetUtcNow() < expiresAt)
        {
            OperationRecordDto? current = await _operations.FindAsync(record.OperationId, ct).ConfigureAwait(false);
            if (current is null || !IsActiveEnrollment(current))
                return;
            if (await TryRecoverAfterPairRequestAsync(current, expiresAt, ct).ConfigureAwait(false))
                return;
            await DelayUntilNextPollAsync(expiresAt, ct).ConfigureAwait(false);
        }

        OperationRecordDto? expired = await _operations.FindAsync(record.OperationId, ct).ConfigureAwait(false);
        if (expired is not null && IsActiveEnrollment(expired))
        {
            await FailAsync(
                expired,
                "recovery-required",
                "verify-lockdown",
                RecoveryIssue("The connection window expired before Sideport could verify Trust. Start connecting again; pairing will not be repeated automatically."),
                retryable: true,
                ct: ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryRecoverAfterPairRequestAsync(OperationRecordDto record, DateTimeOffset expiresAt, CancellationToken ct)
    {
        if (!await EnsureExecutionAuthorizedAsync(record, "verify-lockdown", ct).ConfigureAwait(false))
            return true;

        string? udid = record.Target.DeviceUdid;
        if (string.IsNullOrWhiteSpace(udid))
        {
            await FailAsync(record, "recovery-required", "request-pairing", RecoveryIssue("The pairing target was not durably recorded."), retryable: true, ct: ct).ConfigureAwait(false);
            return true;
        }

        DeviceInfo? selected;
        try
        {
            IReadOnlyList<DeviceInfo> reachable = await RunBoundedAsync(token => _devices.ListDevicesAsync(token), expiresAt, ct).ConfigureAwait(false);
            selected = reachable.FirstOrDefault(device => SameUdid(device.Udid, udid) && device.Connection == DeviceConnection.Usb);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            selected = null;
        }

        if (selected is null)
        {
            await MarkRecoveryWaitingAsync(
                record,
                "Reconnect and unlock the iPhone. Sideport will continue automatically without requesting pairing again.",
                ct).ConfigureAwait(false);
            return false;
        }

        DeviceTrustProbe probe;
        try
        {
            if (!await EnsureExecutionAuthorizedAsync(record, "verify-lockdown", ct).ConfigureAwait(false))
                return true;
            probe = await RunBoundedAsync(token => _devices.ProbeTrustAsync(udid, token), expiresAt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await MarkRecoveryWaitingAsync(
                record,
                "Sideport is checking Trust automatically. Keep the iPhone connected and unlocked.",
                ct).ConfigureAwait(false);
            return false;
        }

        string trustState = NormalizeTrustState(probe.TrustState);
        if (trustState == "locked")
        {
            await MarkRecoveryWaitingAsync(record, "Unlock the iPhone. Sideport will continue automatically.", ct).ConfigureAwait(false);
            return false;
        }
        if (trustState == "untrusted")
        {
            if (IsExplicitTrustDenial(probe.TrustReason))
            {
                await FailAsync(
                    record,
                    "failed",
                    "verify-lockdown",
                    new OperationIssueDto("device-lockdown-untrusted", probe.TrustReason ?? "Trust was declined on the iPhone."),
                    retryable: true,
                    ct: ct).ConfigureAwait(false);
                return true;
            }
            await MarkRecoveryWaitingAsync(
                record,
                "Tap Trust on the iPhone if asked. Sideport will continue automatically.",
                ct).ConfigureAwait(false);
            return false;
        }
        if (trustState != "trusted" || !probe.UsableForInstall || probe.Connection != DeviceConnection.Usb)
        {
            await MarkRecoveryWaitingAsync(
                record,
                probe.Connection != DeviceConnection.Usb
                    ? "Reconnect the iPhone over USB. Sideport will continue automatically."
                    : probe.TrustReason ?? "Sideport is checking Trust automatically.",
                ct).ConfigureAwait(false);
            return false;
        }

        OperationRecordDto? recovered = await MarkRecoveredTrustAsync(record, ct).ConfigureAwait(false);
        if (recovered is not null && IsActiveEnrollment(recovered))
            await AcceptVerifiedAsync(recovered, selected, probe, ct).ConfigureAwait(false);
        return true;
    }

    private async Task VerifyAndAcceptAsync(
        OperationRecordDto record,
        DeviceInfo selected,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        if (!await EnsureExecutionAuthorizedAsync(record, "verify-lockdown", ct).ConfigureAwait(false))
            return;

        DateTimeOffset verifyStarted = _timeProvider.GetUtcNow();
        OperationRecordDto? verifying = await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            return existing with
            {
                Status = "running",
                UpdatedAt = verifyStarted,
                Cancelable = false,
                Stages = UpdateStage(existing.Stages, "verify-lockdown", "running", verifyStarted, "Checking the trusted USB connection."),
            };
        }, ct).ConfigureAwait(false);
        if (verifying is null || !IsActiveEnrollment(verifying))
            return;

        if (!await EnsureExecutionAuthorizedAsync(verifying, "verify-lockdown", ct).ConfigureAwait(false))
            return;

        DeviceTrustProbe probe;
        try
        {
            probe = await RunBoundedAsync(token => _devices.ProbeTrustAsync(selected.Udid, token), expiresAt, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            bool afterPairRequest = PairingWasRequested(verifying);
            if (afterPairRequest)
            {
                await MarkRecoveryWaitingAsync(verifying, "Sideport is checking Trust automatically. Keep the iPhone connected and unlocked.", ct).ConfigureAwait(false);
                await ContinueRecoveryUntilTerminalAsync(verifying, expiresAt, ct).ConfigureAwait(false);
                return;
            }
            await FailAsync(
                verifying,
                "failed",
                "verify-lockdown",
                TimeoutIssue(),
                retryable: true,
                ct: ct).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (PairingWasRequested(verifying))
            {
                await MarkRecoveryWaitingAsync(verifying, "Sideport is checking Trust automatically. Keep the iPhone connected and unlocked.", ct).ConfigureAwait(false);
                await ContinueRecoveryUntilTerminalAsync(verifying, expiresAt, ct).ConfigureAwait(false);
                return;
            }
            await FailAsync(verifying, "failed", "verify-lockdown", new OperationIssueDto("device-trust-check-unavailable", "Sideport could not verify the lockdown session."), retryable: true, ct: ct).ConfigureAwait(false);
            return;
        }

        string trustState = NormalizeTrustState(probe.TrustState);
        if (trustState == "locked")
        {
            if (PairingWasRequested(verifying))
            {
                await MarkRecoveryWaitingAsync(verifying, "Unlock the iPhone. Sideport will continue automatically.", ct).ConfigureAwait(false);
                await ContinueRecoveryUntilTerminalAsync(verifying, expiresAt, ct).ConfigureAwait(false);
                return;
            }
            await FailAsync(verifying, "failed", "verify-lockdown", LockedIssue(), retryable: true, ct: ct).ConfigureAwait(false);
            return;
        }
        if (trustState != "trusted" || !probe.UsableForInstall || probe.Connection != DeviceConnection.Usb)
        {
            bool afterPairRequest = PairingWasRequested(verifying);
            if (afterPairRequest &&
                (trustState != "untrusted" || !IsExplicitTrustDenial(probe.TrustReason)))
            {
                await MarkRecoveryWaitingAsync(
                    verifying,
                    probe.Connection != DeviceConnection.Usb
                        ? "Reconnect the iPhone over USB. Sideport will continue automatically."
                        : probe.TrustReason ?? "Sideport is checking Trust automatically.",
                    ct).ConfigureAwait(false);
                await ContinueRecoveryUntilTerminalAsync(verifying, expiresAt, ct).ConfigureAwait(false);
                return;
            }
            string status = afterPairRequest && trustState != "untrusted"
                ? "recovery-required"
                : "failed";
            OperationIssueDto issue = trustState == "untrusted"
                ? new OperationIssueDto("device-lockdown-untrusted", probe.TrustReason ?? "Trust was not granted on the iPhone.")
                : afterPairRequest
                    ? RecoveryIssue(probe.TrustReason ?? "The post-pairing Trust result is ambiguous.")
                    : probe.Connection != DeviceConnection.Usb
                        ? UsbRequiredIssue()
                        : new OperationIssueDto("device-trust-check-unavailable", probe.TrustReason ?? "Sideport could not verify the lockdown session.");
            await FailAsync(
                verifying,
                status,
                "verify-lockdown",
                issue,
                retryable: true,
                ct: ct).ConfigureAwait(false);
            return;
        }

        await AcceptVerifiedAsync(verifying, selected, probe, ct).ConfigureAwait(false);
    }

    private async Task AcceptVerifiedAsync(
        OperationRecordDto record,
        DeviceInfo selected,
        DeviceTrustProbe probe,
        CancellationToken ct)
    {
        DateTimeOffset verifiedAt = _timeProvider.GetUtcNow();
        OperationRecordDto? accepting = await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(existing.Stages, "verify-lockdown", "succeeded", verifiedAt, "Trusted lockdown session verified over USB.");
            stages = UpdateStage(stages, "accept-device", "running", verifiedAt, "Adding the iPhone to Sideport.");
            return existing with { UpdatedAt = verifiedAt, Stages = stages };
        }, ct).ConfigureAwait(false);
        if (accepting is null || !IsActiveEnrollment(accepting))
            return;

        if (!await EnsureExecutionAuthorizedAsync(accepting, "accept-device", ct).ConfigureAwait(false))
            return;

        KnownDeviceDto accepted;
        try
        {
            accepted = await _inventory.AcceptAsync(
                selected,
                probe,
                accepting.Actor.DisplayName,
                accepting.OperationId,
                accepting.OwnerMemberId,
                ct).ConfigureAwait(false);
        }
        catch (KnownDeviceAcceptanceException ex)
        {
            await FailAsync(accepting, "failed", "accept-device", new OperationIssueDto(ex.Code, ex.Message), retryable: true, ct: ct).ConfigureAwait(false);
            return;
        }
        catch (KnownDeviceStoreException)
        {
            await FailAsync(
                accepting,
                "failed",
                "accept-device",
                new OperationIssueDto("known-device-store-unavailable", "The trusted iPhone could not be saved to Sideport inventory."),
                retryable: true,
                ct: ct).ConfigureAwait(false);
            return;
        }

        await CompleteAsync(accepting, accepted.AcceptedAt, ct).ConfigureAwait(false);
    }

    private async Task MarkRecoveryWaitingAsync(OperationRecordDto record, string message, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(
                existing.Stages,
                "request-pairing",
                "succeeded",
                now,
                "Pairing was requested once; Sideport will not request it again.");
            stages = UpdateStage(
                stages,
                "await-user-trust",
                "succeeded",
                now,
                "The Trust response moved to automatic verification.");
            stages = UpdateStage(stages, "verify-lockdown", "waiting", now, message);
            return existing with
            {
                Status = "waiting",
                UpdatedAt = now,
                CompletedAt = null,
                Error = null,
                Retryable = false,
                Cancelable = false,
                Stages = stages,
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<OperationRecordDto?> MarkRecoveredTrustAsync(OperationRecordDto record, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(existing.Stages, "request-pairing", "succeeded", now, "Pairing was requested once.");
            stages = UpdateStage(stages, "await-user-trust", "succeeded", now, "Trust confirmed during automatic recovery.");
            return existing with { Status = "running", UpdatedAt = now, Stages = stages, Cancelable = false };
        }, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryCompleteFromDurableAcceptanceAsync(OperationRecordDto record, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(record.Target.DeviceUdid))
            return false;
        KnownDeviceRecord? known = await _knownDevices.FindAsync(record.Target.DeviceUdid, ct).ConfigureAwait(false);
        if (known is null ||
            !string.Equals(known.InventoryState, "accepted", StringComparison.Ordinal) ||
            !string.Equals(known.EnrollmentOperationId, record.OperationId, StringComparison.Ordinal) ||
            (record.OwnerMemberId is not null &&
             !string.Equals(known.OwnerMemberId, record.OwnerMemberId, StringComparison.Ordinal)))
        {
            return false;
        }

        await CompleteAsync(record, known.AcceptedAt, ct).ConfigureAwait(false);
        return true;
    }

    private async Task CompleteAsync(OperationRecordDto record, DateTimeOffset? acceptedAt, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = existing.Stages.Select(stage => stage.Status is "pending" or "waiting" or "running"
                ? stage with
                {
                    Status = "succeeded",
                    StartedAt = stage.StartedAt ?? now,
                    CompletedAt = now,
                    Message = stage.Id == "accept-device" ? "iPhone added to Sideport." : stage.Message,
                    Error = null,
                }
                : stage).ToArray();
            return existing with
            {
                Status = "succeeded",
                UpdatedAt = now,
                CompletedAt = now,
                Stages = stages,
                Result = new OperationResultDto(
                    true,
                    null,
                    null,
                    null,
                    new DeviceEnrollmentResultDto(existing.Target.DeviceUdid, "accepted", acceptedAt)),
                Error = null,
                Cancelable = false,
                Retryable = false,
                Rerunnable = false,
                CandidateDevices = null,
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<OperationRecordDto?> MarkAlreadyTrustedAsync(OperationRecordDto record, CancellationToken ct)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        return await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            OperationStageDto[] stages = UpdateStage(existing.Stages, "request-pairing", "succeeded", now, "Existing Trust was found; no pairing request was needed.");
            stages = UpdateStage(stages, "await-user-trust", "succeeded", now, "The iPhone is already trusted.");
            return existing with { Status = "running", UpdatedAt = now, Stages = stages, Cancelable = false };
        }, ct).ConfigureAwait(false);
    }

    private async Task FailAsync(
        OperationRecordDto record,
        string status,
        string stageId,
        OperationIssueDto error,
        bool retryable,
        IReadOnlyList<DeviceEnrollmentCandidateDto>? candidates = null,
        CancellationToken ct = default)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        await _operations.TransitionAsync(record.OperationId, existing =>
        {
            if (!IsActiveEnrollment(existing))
                return null;
            string stageStatus = status == "blocked" ? "blocked" : "failed";
            OperationStageDto[] stages = UpdateStage(existing.Stages, stageId, stageStatus, now, error.Message, error);
            return existing with
            {
                Status = status,
                UpdatedAt = now,
                CompletedAt = now,
                Stages = stages,
                Result = new OperationResultDto(
                    false,
                    null,
                    null,
                    error.Message,
                    new DeviceEnrollmentResultDto(existing.Target.DeviceUdid, "not-accepted", null, error.Code)),
                Error = error,
                Cancelable = false,
                Retryable = retryable,
                Rerunnable = false,
                CandidateDevices = candidates,
            };
        }, ct).ConfigureAwait(false);
    }

    private async Task<bool> EnsureExecutionAuthorizedAsync(
        OperationRecordDto record,
        string stageId,
        CancellationToken ct)
    {
        if (_executionAuthorization is null)
            return true;

        WorkspaceExecutionDecision authorization = await _executionAuthorization
            .AuthorizeOperationAsync(record, enrollmentTarget: true, ct: ct)
            .ConfigureAwait(false);
        if (authorization.IsAllowed)
            return true;

        string status = PairingWasRequested(record) ? "recovery-required" : "blocked";
        await FailAsync(
            record,
            status,
            stageId,
            new OperationIssueDto(
                authorization.ErrorCode ?? "operation-access-revoked",
                authorization.Message ?? "Sideport access changed after this operation was submitted."),
            retryable: authorization.Retryable,
            ct: ct).ConfigureAwait(false);
        return false;
    }

    private async Task<IReadOnlyList<DeviceInfo>> EligibleUsbCandidatesAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceInfo> reachable = await _devices.ListDevicesAsync(ct).ConfigureAwait(false);
        IReadOnlyList<KnownDeviceRecord> known = await _knownDevices.ListAsync(ct).ConfigureAwait(false);
        var accepted = known
            .Where(device => string.Equals(device.InventoryState, "accepted", StringComparison.Ordinal))
            .Select(device => device.Udid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return reachable
            .Where(device => device.Connection == DeviceConnection.Usb && !accepted.Contains(device.Udid))
            .GroupBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(device => device.Udid, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<T> RunBoundedAsync<T>(
        Func<CancellationToken, Task<T>> action,
        DateTimeOffset expiresAt,
        CancellationToken ct)
    {
        TimeSpan remaining = expiresAt - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // The vendored transport performs some socket opens synchronously before
        // returning its Task. Offload the invocation so the enrollment worker can
        // still enforce its durable deadline and release the single-flight gate.
        // Use a dedicated thread for the potentially synchronous socket-open
        // prefix. A blocked usbmux open must not consume the worker pool thread
        // needed by the timeout continuation itself.
        Task<T> task = Task.Factory.StartNew(
                () => action(linked.Token),
                linked.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();
        try
        {
            return await task.WaitAsync(remaining, _timeProvider, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            await linked.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task DelayUntilNextPollAsync(DateTimeOffset expiresAt, CancellationToken ct)
    {
        TimeSpan remaining = expiresAt - _timeProvider.GetUtcNow();
        if (remaining <= TimeSpan.Zero)
            return;
        TimeSpan delay = remaining < _options.PollInterval ? remaining : _options.PollInterval;
        await Task.Delay(delay, _timeProvider, ct).ConfigureAwait(false);
    }

    private static IReadOnlyList<OperationStageDto> CreateStages(DateTimeOffset now) =>
    [
        new("wait-for-usb", "Connect iPhone", "waiting", now, null, "Waiting for one unaccepted iPhone over USB."),
        new("request-pairing", "Request pairing", "pending", null, null, "Pairing starts only when needed."),
        new("await-user-trust", "Trust on iPhone", "pending", null, null, "Trust is confirmed on the iPhone."),
        new("verify-lockdown", "Check connection", "pending", null, null, "Sideport verifies the trusted USB connection."),
        new("accept-device", "Add iPhone", "pending", null, null, "The verified iPhone is added automatically."),
    ];

    private static OperationStageDto[] UpdateStage(
        IReadOnlyList<OperationStageDto> stages,
        string stageId,
        string status,
        DateTimeOffset now,
        string message,
        OperationIssueDto? error = null) =>
        stages.Select(stage => string.Equals(stage.Id, stageId, StringComparison.Ordinal)
            ? stage with
            {
                Status = status,
                StartedAt = stage.StartedAt ?? now,
                CompletedAt = status is "succeeded" or "failed" or "blocked" ? now : null,
                Message = message,
                Error = error,
            }
            : stage).ToArray();

    private static DeviceEnrollmentCandidateDto ToSafeCandidate(DeviceInfo device) => new(
        device.Udid.Length <= 8 ? device.Udid : device.Udid[^8..],
        string.IsNullOrWhiteSpace(device.Name) ? "iPhone" : device.Name,
        NormalizeOptional(device.ProductType),
        NormalizeOptional(device.OsVersion),
        "usb");

    private static bool IsActiveEnrollment(OperationRecordDto operation) =>
        string.Equals(operation.Type, OperationType, StringComparison.Ordinal) &&
        ActiveStatuses.Contains(operation.Status, StringComparer.Ordinal);

    private static bool PairingWasRequested(OperationRecordDto operation) =>
        operation.DevicePairingRequestedAt is not null || operation.Stages.Any(stage =>
            string.Equals(stage.Id, "request-pairing", StringComparison.Ordinal) &&
            stage.StartedAt is not null &&
            !stage.Message.StartsWith("Existing Trust", StringComparison.Ordinal));

    private static string CurrentStageId(OperationRecordDto operation) =>
        operation.Stages.FirstOrDefault(stage => stage.Status is "running" or "waiting" or "pending")?.Id ?? "wait-for-usb";

    private static OperationIssueDto TimeoutIssue() =>
        new("device-enrollment-timeout", "The five-minute iPhone enrollment session expired.");

    private static OperationIssueDto UsbRequiredIssue() =>
        new("device-usb-required", "Connect the iPhone over USB for pairing and first install.");

    private static OperationIssueDto LockedIssue() =>
        new("device-locked", "Unlock the iPhone before continuing.");

    private static OperationIssueDto RecoveryIssue(string message) =>
        new("device-enrollment-recovery-required", message);

    private static bool IsExplicitTrustDenial(string? reason) =>
        !string.IsNullOrWhiteSpace(reason) &&
        (reason.Contains("declined", StringComparison.OrdinalIgnoreCase) ||
         reason.Contains("denied", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeTrustState(string? state) => state?.Trim().ToLowerInvariant() switch
    {
        "trusted" => "trusted",
        "untrusted" => "untrusted",
        "locked" => "locked",
        "error" => "error",
        _ => "unknown",
    };

    private static bool SameUdid(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool SameOptionalUdid(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left)
            ? string.IsNullOrWhiteSpace(right)
            : !string.IsNullOrWhiteSpace(right) && SameUdid(left, right);

    private static bool SameOptionalMemberId(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left)
            ? string.IsNullOrWhiteSpace(right)
            : !string.IsNullOrWhiteSpace(right) && string.Equals(left, right, StringComparison.Ordinal);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
