namespace Sideport.Core;

/// <summary>
/// Device transport seam (design §6). Backed by a vendored, pinned build of
/// <c>artehe/Netimobiledevice</c> (MIT) — usbmux + lockdown + pairing +
/// <c>installation_proxy</c> + <c>misagent</c>. Replaces the entire
/// libimobiledevice family. Wi-Fi/mDNS discovery is an open spike (design §6).
/// </summary>
public interface IDeviceController
{
    /// <summary>Discover reachable devices over USB and (later) Wi-Fi.</summary>
    Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default);

    /// <summary>List installed user apps and their signing expiry on a device.</summary>
    Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
        string udid, CancellationToken ct = default);

    /// <summary>
    /// Read installed apps and profiles directly from the device, bypassing any
    /// presentation cache. Evidence-producing verification must use this seam.
    /// </summary>
    Task<IReadOnlyList<InstalledApp>> ListInstalledAppsFreshAsync(
        string udid, CancellationToken ct = default) =>
        ListInstalledAppsAsync(udid, ct);

    /// <summary>Install (or upgrade) an IPA onto a device.</summary>
    Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default);

    /// <summary>
    /// Run a connectivity self-test of the device transport chain
    /// (usbmux socket → device enumeration → per-device trust/lockdown), returning
    /// human-readable remediation for whichever layer fails first.
    /// </summary>
    Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default);

    /// <summary>
    /// Check the device's existing lockdown trust without requesting pairing or
    /// showing a Trust prompt. Wi-Fi probes may only consume a pairing record
    /// that was created previously over USB.
    /// </summary>
    Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default) =>
        Task.FromException<DeviceTrustProbe>(
            new NotSupportedException("This device controller does not expose trust probing."));

    /// <summary>
    /// Explicitly request pairing for a USB-connected device. This is the only
    /// device-controller operation allowed to request Trust on the iPhone.
    /// </summary>
    Task<DevicePairingResult> PairAsync(
        string udid,
        IProgress<DevicePairingProgress>? progress = null,
        CancellationToken ct = default) =>
        Task.FromException<DevicePairingResult>(
            new NotSupportedException("This device controller does not expose pairing."));
}

/// <summary>A reachable iOS device.</summary>
public sealed record DeviceInfo(
    string Udid,
    string Name,
    string ProductType,
    string OsVersion,
    DeviceConnection Connection,
    string TrustState = "unknown",
    string? TrustReason = null,
    DateTimeOffset? LockdownCheckedAt = null,
    bool UsableForInstall = false);

/// <summary>How a device is currently reachable.</summary>
public enum DeviceConnection { Usb, Wifi }

/// <summary>
/// Result of a passive lockdown check. <paramref name="TrustState"/> is one of
/// <c>trusted</c>, <c>untrusted</c>, <c>locked</c>, <c>error</c>, or
/// <c>unknown</c>. <paramref name="UsableForInstall"/> describes whether the
/// current trusted transport can run an existing managed install/refresh; a
/// first install still requires the caller to additionally require USB.
/// </summary>
public sealed record DeviceTrustProbe(
    string Udid,
    DeviceConnection Connection,
    string TrustState,
    string? TrustReason,
    DateTimeOffset LockdownCheckedAt,
    bool UsableForInstall);

/// <summary>
/// User-visible progress from an explicit pairing request. <paramref
/// name="State"/> is <c>waiting-for-trust</c>, <c>paired</c>, <c>denied</c>, or
/// <c>locked</c>.
/// </summary>
public sealed record DevicePairingProgress(string State, string Detail);

/// <summary>Final trust observation after an explicit USB pairing request.</summary>
public sealed record DevicePairingResult(
    string Udid,
    DeviceConnection Connection,
    string TrustState,
    string? TrustReason,
    DateTimeOffset LockdownCheckedAt,
    bool UsableForInstall);

/// <summary>Result of a device-connectivity self-test (worst layer wins).</summary>
public sealed record DeviceDiagnostics(
    string Status,
    IReadOnlyList<DeviceCheck> Checks);

/// <summary>
/// One step of the device-connectivity self-test. <paramref name="Status"/> is
/// <c>ok</c>, <c>warning</c>, or <c>blocked</c>; <paramref name="Remediation"/>
/// is the operator-facing fix when not <c>ok</c>.
/// </summary>
public sealed record DeviceCheck(
    string Id,
    string Label,
    string Status,
    string Detail,
    string? Remediation = null);

/// <summary>An installed app and the date its signature expires.</summary>
public sealed record InstalledApp(
    string BundleId,
    string Name,
    string Version,
    DateTimeOffset? SignatureExpiresAt);
