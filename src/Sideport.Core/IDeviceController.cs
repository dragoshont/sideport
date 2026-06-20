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

    /// <summary>Install (or upgrade) an IPA onto a device.</summary>
    Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default);

    /// <summary>
    /// Run a connectivity self-test of the device transport chain
    /// (usbmux socket → device enumeration → per-device trust/lockdown), returning
    /// human-readable remediation for whichever layer fails first.
    /// </summary>
    Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default);
}

/// <summary>A reachable iOS device.</summary>
public sealed record DeviceInfo(
    string Udid,
    string Name,
    string ProductType,
    string OsVersion,
    DeviceConnection Connection);

/// <summary>How a device is currently reachable.</summary>
public enum DeviceConnection { Usb, Wifi }

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
