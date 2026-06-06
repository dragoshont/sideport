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

/// <summary>An installed app and the date its signature expires.</summary>
public sealed record InstalledApp(
    string BundleId,
    string Name,
    string Version,
    DateTimeOffset? SignatureExpiresAt);
