using System.Collections.Concurrent;
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

    public async Task<RefreshResult> RefreshAsync(
        string udid, string bundleId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        AppRegistration? registration = await _registry.FindAsync(udid, bundleId, ct);
        if (registration is null)
            return Record(udid, bundleId, null, false, "app is not registered");

        // Single-flight: only one refresh runs at a time, process-wide.
        await _signGate.WaitAsync(ct);
        try
        {
            return await RunLockedAsync(registration, ct);
        }
        finally
        {
            _signGate.Release();
        }
    }

    private async Task<RefreshResult> RunLockedAsync(AppRegistration app, CancellationToken ct)
    {
        _logger.LogInformation("refreshing {Bundle} on {Udid}", app.BundleId, app.DeviceUdid);

        if (!File.Exists(app.InputIpaPath))
            return Record(app, null, false, $"input IPA not found: {app.InputIpaPath}");

        AppleSession session;
        try
        {
            session = await _sessions.GetSessionAsync(app.AppleId, ct);
        }
        catch (InteractiveLoginRequiredException)
        {
            return Record(app, null, false, "interactive sign-in (2FA) required");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Record(app, null, false, $"authentication failed: {ex.Message}");
        }

        PreparedSigningInputs inputs;
        try
        {
            inputs = await _identity.PrepareAsync(session, app.TeamId, app.BundleId, app.DeviceUdid, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Record(app, null, false, $"could not prepare signing identity: {ex.Message}");
        }

        string outputIpa = Path.Combine(
            _options.WorkDirectory, app.DeviceUdid, $"{app.BundleId}.ipa");

        SignResult sign;
        try
        {
            sign = await _signer.SignAsync(new SignRequest(
                app.InputIpaPath, outputIpa,
                inputs.Pkcs12Path, inputs.ProvisioningProfilePath,
                inputs.Pkcs12Password), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Record(app, null, false, $"signing threw: {ex.Message}");
        }

        if (!sign.Success)
            return Record(app, null, false, sign.Error ?? "signing failed");

        try
        {
            await _devices.InstallAsync(app.DeviceUdid, sign.OutputIpaPath, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Record(app, inputs.ExpiresAt, false, $"install failed: {ex.Message}");
        }

        _logger.LogInformation(
            "refreshed {Bundle} on {Udid}; expires {Expiry:u}",
            app.BundleId, app.DeviceUdid, inputs.ExpiresAt);
        return Record(app, inputs.ExpiresAt, true, null);
    }

    private RefreshResult Record(
        AppRegistration app, DateTimeOffset? expiry, bool success, string? error)
        => Record(app.DeviceUdid, app.BundleId, expiry, success, error);

    private RefreshResult Record(
        string udid, string bundleId, DateTimeOffset? expiry, bool success, string? error)
    {
        _states[KeyOf(udid, bundleId)] = new RefreshState(
            udid, bundleId, expiry, DateTimeOffset.UtcNow, success, error);

        if (!success)
            _logger.LogWarning("refresh of {Bundle} on {Udid} failed: {Error}", bundleId, udid, error);

        return new RefreshResult(success, bundleId, expiry, error);
    }

    private static string KeyOf(string udid, string bundleId) => $"{udid}:{bundleId}";
}
