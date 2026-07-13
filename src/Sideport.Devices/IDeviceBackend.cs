using Sideport.Core;

namespace Sideport.Devices;

/// <summary>
/// Thin transport seam over the underlying device library
/// (<c>artehe/Netimobiledevice</c>). Splitting this out keeps the library's
/// concrete, hard-to-mock types out of <see cref="NetimobiledeviceController"/>,
/// so the controller's logic (device de-duplication, app filtering, profile →
/// expiry joining, install validation) is unit-testable against a fake backend.
/// The real adapter (<see cref="NetimobiledeviceBackend"/>) is validated by the
/// host integration gate.
/// </summary>
internal interface IDeviceBackend
{
    /// <summary>Enumerate reachable devices (USB and network), best-effort enriched.</summary>
    Task<IReadOnlyList<BackendDevice>> ListDevicesAsync(CancellationToken ct);

    /// <summary>List installed applications on a device.</summary>
    Task<IReadOnlyList<BackendApp>> ListInstalledAppsAsync(string udid, CancellationToken ct);

    /// <summary>
    /// The raw <c>.mobileprovision</c> blobs installed on a device (from
    /// <c>misagent</c>), used to derive per-app signing expiry.
    /// </summary>
    Task<IReadOnlyList<byte[]>> ListProvisioningProfilesAsync(string udid, CancellationToken ct);

    /// <summary>Install (or upgrade) an IPA already validated by the controller.</summary>
    Task InstallAsync(string udid, string ipaPath, IProgress<int>? progress, CancellationToken ct);

    /// <summary>
    /// Probe the device transport: whether the usbmux socket is reachable and,
    /// per enumerated device, whether the lockdown/trust handshake succeeds.
    /// </summary>
    Task<BackendDiagnostics> DiagnoseAsync(CancellationToken ct);

    /// <summary>
    /// Check existing lockdown trust. This operation must never create or
    /// modify a pairing record or request Trust on the device.
    /// </summary>
    Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct);

    /// <summary>
    /// Explicitly request Trust for a USB-connected device. No other backend
    /// operation is allowed to initiate pairing.
    /// </summary>
    Task<DevicePairingResult> PairAsync(
        string udid,
        IProgress<DevicePairingProgress>? progress,
        CancellationToken ct);
}

/// <summary>A device as seen by the backend, with lockdown-derived metadata.</summary>
internal sealed record BackendDevice(
    string Udid,
    string Name,
    string ProductType,
    string OsVersion,
    DeviceConnection Connection,
    string TrustState = "unknown",
    string? TrustReason = null,
    DateTimeOffset? LockdownCheckedAt = null,
    bool UsableForInstall = false);

/// <summary>An installed app as reported by <c>installation_proxy</c>.</summary>
internal sealed record BackendApp(
    string BundleId,
    string Name,
    string Version,
    bool IsUserApp);

/// <summary>Outcome of a transport probe: usbmux reachability + per-device trust.</summary>
internal sealed record BackendDiagnostics(
    bool TransportReachable,
    string? TransportError,
    IReadOnlyList<BackendDeviceProbe> Devices);

/// <summary>A single device's reachability + lockdown/trust handshake result.</summary>
internal sealed record BackendDeviceProbe(
    string Udid,
    DeviceConnection Connection,
    bool LockdownOk,
    string? Name,
    string? LockdownError,
    string TrustState = "unknown",
    string? TrustReason = null,
    DateTimeOffset? LockdownCheckedAt = null,
    bool UsableForInstall = false);
