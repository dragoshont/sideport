using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;

namespace Sideport.Orchestrator;

/// <summary>
/// Drives the refresh loop — <c>auth → ensure cert/App-ID/profile → re-sign →
/// install</c> — and enforces the single-signer rule (design invariant #5): every
/// re-sign across the whole process is serialized through one lock, so Sideport
/// never becomes a competing signer that revokes its own (or AltStore's)
/// certificate.
/// </summary>
public sealed class RefreshOrchestrator : IRefreshOrchestrator
{
    private readonly IAppRegistry _registry;
    private readonly ISessionManager _sessions;
    private readonly ISigningIdentityProvider _identity;
    private readonly ISigner _signer;
    private readonly IDeviceController _devices;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<RefreshOrchestrator> _logger;

    // The one lock that serializes every re-sign in the process.
    private readonly SemaphoreSlim _signGate = new(1, 1);
    private readonly ConcurrentDictionary<string, RefreshState> _states = new();
    private string? _activeDeviceUdid;

    public RefreshOrchestrator(
        IAppRegistry registry,
        ISessionManager sessions,
        ISigningIdentityProvider identity,
        ISigner signer,
        IDeviceController devices,
        OrchestratorOptions options,
        ILogger<RefreshOrchestrator>? logger = null)
    {
        _registry = registry;
        _sessions = sessions;
        _identity = identity;
        _signer = signer;
        _devices = devices;
        _options = options;
        _logger = logger ?? NullLogger<RefreshOrchestrator>.Instance;
    }

    /// <summary>Current refresh state of every app that has been seen.</summary>
    public IReadOnlyCollection<RefreshState> States => [.. _states.Values];

    public RefreshState? GetState(string udid, string bundleId) =>
        _states.GetValueOrDefault(KeyOf(udid, bundleId));

    public bool IsDeviceMutationActive(string udid) =>
        !string.IsNullOrWhiteSpace(udid) &&
        string.Equals(Volatile.Read(ref _activeDeviceUdid), udid, StringComparison.OrdinalIgnoreCase);

    public Task<RefreshResult> RefreshAsync(
        string udid,
        string bundleId,
        CancellationToken ct = default) =>
        RefreshAsync(udid, bundleId, RefreshExecutionPolicy.OwnerManaged, ct);

    public bool HasCachedAppleSession(string appleId) =>
        !string.IsNullOrWhiteSpace(appleId) && _sessions.TryGetCachedSession(appleId) is not null;

    public Task<RefreshResult> RefreshAsync(
        string udid,
        string bundleId,
        RefreshExecutionPolicy executionPolicy,
        CancellationToken ct = default) =>
        RefreshCoreAsync(udid, bundleId, executionPolicy, executionPolicy.Recovery, ct);

    private async Task<RefreshResult> RefreshCoreAsync(
        string udid,
        string bundleId,
        RefreshExecutionPolicy executionPolicy,
        RefreshRecoveryPlan? recovery,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        ArgumentNullException.ThrowIfNull(executionPolicy);
        if (recovery is not null && executionPolicy.AllowCertificateCreation)
            throw new ArgumentException("Recovery cannot authorize certificate creation.", nameof(executionPolicy));

        AppRegistration? registration = await _registry.FindAsync(udid, bundleId, ct);
        if (registration is null)
            return Record(udid, bundleId, null, false, "app is not registered");

        // Single-flight: only one refresh runs at a time, process-wide.
        await _signGate.WaitAsync(ct);
        Volatile.Write(ref _activeDeviceUdid, udid);
        bool releaseLease = true;
        try
        {
            return await RunLockedAsync(registration, executionPolicy, recovery, ct);
        }
        catch (InstallOutcomeUnknownException unknown)
        {
            // An ambiguous install remains reconciliation-only even when its
            // socket was successfully aborted. The process-wide lease may be
            // released only after the actual managed transfer task terminates.
            // A transport that ignores cancellation still holds the lease until
            // its observer sees termination.
            if (!unknown.TransferTask.IsCompleted)
            {
                releaseLease = false;
                _ = ObserveUnknownInstallAsync(udid, unknown.TransferTask);
            }
            return Record(
                registration,
                unknown.ExpiresAt,
                false,
                "The iPhone stopped responding during install. Sideport cannot prove whether the app changed; reconcile the device before retrying.",
                "install-outcome-unknown");
        }
        finally
        {
            if (releaseLease)
            {
                Volatile.Write(ref _activeDeviceUdid, null);
                _signGate.Release();
            }
        }
    }

    private async Task<RefreshResult> RunLockedAsync(
        AppRegistration app,
        RefreshExecutionPolicy executionPolicy,
        RefreshRecoveryPlan? recovery,
        CancellationToken ct)
    {
        _logger.LogInformation("refreshing {Bundle} on {Udid}", app.BundleId, app.DeviceUdid);

        if (!File.Exists(app.InputIpaPath))
            return Record(app, null, false, $"input IPA not found: {app.InputIpaPath}");

        AppleSession session;
        try
        {
            session = executionPolicy.AllowAppleAuthentication
                ? await _sessions.GetSessionAsync(app.AppleId, ct)
                : _sessions.TryGetCachedSession(app.AppleId)
                    ?? throw new OwnerManagedAppleActionRequiredException();
        }
        catch (InteractiveLoginRequiredException)
        {
            return Record(app, null, false, "interactive sign-in (2FA) required");
        }
        catch (Exception ex) when (ex is IStructuredRefreshFailure structured)
        {
            return Record(app, null, false, structured.SafeMessage, structured.ErrorCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Apple authentication failed ({ErrorType})", ex.GetType().Name);
            return Record(app, null, false, "Apple authentication could not be completed.", "apple-authentication-failed");
        }

        PreparedSigningInputs inputs;
        try
        {
            inputs = await _identity.PrepareAsync(
                session,
                app.TeamId,
                app.BundleId,
                app.DeviceUdid,
                executionPolicy.AllowCertificateCreation,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("signing preparation failed ({ErrorType})", ex.GetType().Name);
            return ex is IStructuredRefreshFailure structured
                ? Record(app, null, false, structured.SafeMessage, structured.ErrorCode)
                : Record(app, null, false, "Sideport could not prepare the signing identity.", "signing-preparation-failed");
        }

        using (inputs)
        {
            string outputIpa = Path.Combine(
                _options.WorkDirectory, app.DeviceUdid, $"{app.BundleId}.ipa");

            string signInputPath = app.InputIpaPath;
            string? pinnedSha = null;
            if (recovery is not null)
            {
                (string? pinnedPath, string? pinnedHash, RefreshResult? pinFailure) =
                    await PinArtifactSnapshotAsync(app, recovery.ExpectedArtifactSha256, ct).ConfigureAwait(false);
                if (pinFailure is not null)
                    return pinFailure;
                signInputPath = pinnedPath!;
                pinnedSha = pinnedHash;
            }

            SignResult sign;
            try
            {
                sign = await _signer.SignAsync(new SignRequest(
                    signInputPath, outputIpa,
                    inputs.Pkcs12Path, inputs.ProvisioningProfilePath,
                    inputs.Pkcs12Password), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("signer execution failed ({ErrorType})", ex.GetType().Name);
                return Record(app, null, false, "The signer stopped before producing an app.", "signer-execution-failed");
            }
            finally
            {
                if (recovery is not null)
                    DeleteRecoverySnapshot(signInputPath);
            }

            if (!sign.Success)
                return Record(app, null, false, sign.Error ?? "signing failed");

            if (recovery is not null)
            {
                // The prepared expiry + pinned lineage + mutation-start marker
                // must be durable BEFORE the iPhone is changed. A store failure
                // fails the attempt closed without any device mutation.
                try
                {
                    await recovery.PersistMutationStartAsync(
                        new RefreshMutationCheckpoint(
                            app.DeviceUdid,
                            app.BundleId,
                            inputs.ExpiresAt,
                            pinnedSha!,
                            Path.GetFileName(Path.GetDirectoryName(signInputPath))),
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("recovery checkpoint store failed ({ErrorType})", ex.GetType().Name);
                    return Record(
                        app,
                        inputs.ExpiresAt,
                        false,
                        "Sideport could not durably record the recovery checkpoint before installing.",
                        "recovery-checkpoint-store-failed");
                }
            }

            try
            {
                await InstallWithWatchdogAsync(
                    app.DeviceUdid,
                    sign.OutputIpaPath,
                    inputs.ExpiresAt,
                    executionPolicy.RequiredInstallConnection,
                    ct).ConfigureAwait(false);
            }
            catch (InstallOutcomeUnknownException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("device install failed ({ErrorType})", ex.GetType().Name);
                return Record(
                    app,
                    inputs.ExpiresAt,
                    false,
                    "The iPhone install failed before Sideport could verify it.",
                    "install-failed");
            }

            _logger.LogInformation(
                "refreshed {Bundle} on {Udid}; expires {Expiry:u}",
                app.BundleId, app.DeviceUdid, inputs.ExpiresAt);
            return Record(app, inputs.ExpiresAt, true, null);
        }
    }

    private async Task<(string? PinnedPath, string? PinnedSha256, RefreshResult? Failure)> PinArtifactSnapshotAsync(
        AppRegistration app,
        string expectedArtifactSha256,
        CancellationToken ct)
    {
        string sourceSha;
        try
        {
            sourceSha = await ComputeSha256Async(app.InputIpaPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null, Record(app, null, false, "Sideport could not read the saved app to pin it for recovery.", "recovery-artifact-unreadable"));
        }
        if (!string.Equals(sourceSha, expectedArtifactSha256, StringComparison.OrdinalIgnoreCase))
            return (null, null, Record(app, null, false, "The saved app changed before Sideport could pin it for recovery.", "recovery-artifact-lineage-changed"));

        string directory = Path.Combine(_options.WorkDirectory, app.DeviceUdid, "recovery", Guid.NewGuid().ToString("N"));
        string pinnedPath = Path.Combine(directory, $"{app.BundleId}.ipa");
        string pinnedSha;
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            await using (var source = new FileStream(app.InputIpaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await using (var destination = new FileStream(pinnedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(pinnedPath, UnixFileMode.UserRead);
            pinnedSha = await ComputeSha256Async(pinnedPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeleteRecoverySnapshot(pinnedPath);
            return (null, null, Record(app, null, false, "Sideport could not create a private signing snapshot for recovery.", "recovery-artifact-unreadable"));
        }
        if (!string.Equals(pinnedSha, expectedArtifactSha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteRecoverySnapshot(pinnedPath);
            return (null, null, Record(app, null, false, "Sideport's private signing snapshot did not match the expected app.", "recovery-artifact-lineage-changed"));
        }

        return (pinnedPath, pinnedSha, null);
    }

    private static void DeleteRecoverySnapshot(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is null)
            return;
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // A stale private snapshot is safe; a later recovery can clean it.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the recovery result; never widen permissions to force cleanup.
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private async Task InstallWithWatchdogAsync(
        string deviceUdid,
        string signedIpaPath,
        DateTimeOffset expiresAt,
        DeviceConnection? requiredConnection,
        CancellationToken ct)
    {
        if (_options.InstallTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("InstallTimeout must be positive.");
        if (_options.InstallCancellationGrace < TimeSpan.Zero)
            throw new InvalidOperationException("InstallCancellationGrace cannot be negative.");

        using var transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task transfer = _devices.InstallAsync(
            deviceUdid,
            signedIpaPath,
            transferCancellation.Token,
            requiredConnection);
        Task timeout = Task.Delay(_options.InstallTimeout, CancellationToken.None);
        Task callerCanceled = Task.Delay(Timeout.InfiniteTimeSpan, ct);
        Task winner = await Task.WhenAny(transfer, timeout, callerCanceled).ConfigureAwait(false);
        if (winner == transfer)
        {
            await transfer.ConfigureAwait(false);
            return;
        }

        bool deadlineExpired = winner == timeout;
        transferCancellation.Cancel();
        Task grace = Task.Delay(_options.InstallCancellationGrace, CancellationToken.None);
        if (await Task.WhenAny(transfer, grace).ConfigureAwait(false) == transfer)
        {
            if (deadlineExpired)
            {
                // Crossing the install deadline is ambiguous even when closing
                // the transport makes the task terminate: the phone may have
                // accepted some or all of the mutation. Preserve verify-only
                // reconciliation, but the completed task proves the lease can
                // be released without restarting Sideport.
                ObserveTransferFailure(transfer);
                throw new InstallOutcomeUnknownException(transfer, expiresAt);
            }

            await transfer.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return;
        }

        throw new InstallOutcomeUnknownException(transfer, expiresAt);
    }

    private static void ObserveTransferFailure(Task transfer)
    {
        if (!transfer.IsFaulted)
            return;

        _ = transfer.Exception;
    }

    private async Task ObserveUnknownInstallAsync(string deviceUdid, Task transfer)
    {
        try
        {
            await transfer.ConfigureAwait(false);
            _logger.LogWarning("previous outcome-unknown device install eventually stopped");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "previous outcome-unknown device install eventually stopped ({ErrorType})",
                ex.GetType().Name);
        }
        finally
        {
            if (string.Equals(Volatile.Read(ref _activeDeviceUdid), deviceUdid, StringComparison.OrdinalIgnoreCase))
                Volatile.Write(ref _activeDeviceUdid, null);
            _signGate.Release();
        }
    }

    private RefreshResult Record(
        AppRegistration app,
        DateTimeOffset? expiry,
        bool success,
        string? error,
        string? errorCode = null)
        => Record(app.DeviceUdid, app.BundleId, expiry, success, error, errorCode);

    private RefreshResult Record(
        string udid, string bundleId, DateTimeOffset? expiry, bool success, string? error)
        => Record(udid, bundleId, expiry, success, error, errorCode: null);

    private RefreshResult Record(
        string udid,
        string bundleId,
        DateTimeOffset? expiry,
        bool success,
        string? error,
        string? errorCode)
    {
        string key = KeyOf(udid, bundleId);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        // Carry the last SUCCESSFUL sign time forward so the re-sign cadence
        // measures success recency, not failed attempts.
        DateTimeOffset? lastSucceeded = success ? now : _states.GetValueOrDefault(key)?.LastSucceededUtc;
        _states[key] = new RefreshState(udid, bundleId, expiry, now, success, error, lastSucceeded);

        if (!success)
            _logger.LogWarning("refresh of {Bundle} on {Udid} failed: {Error}", bundleId, udid, error);

        return new RefreshResult(success, bundleId, expiry, error, errorCode);
    }

    private static string KeyOf(string udid, string bundleId) => $"{udid}:{bundleId}";

    private sealed class InstallOutcomeUnknownException(Task transferTask, DateTimeOffset expiresAt)
        : Exception("The device install outcome is unknown.")
    {
        public Task TransferTask { get; } = transferTask;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }
}
