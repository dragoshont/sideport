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
}

/// <summary>A device as seen by the backend, with lockdown-derived metadata.</summary>
internal sealed record BackendDevice(
    string Udid,
    string Name,
    string ProductType,
    string OsVersion,
    DeviceConnection Connection);

/// <summary>An installed app as reported by <c>installation_proxy</c>.</summary>
internal sealed record BackendApp(
    string BundleId,
    string Name,
    string Version,
    bool IsUserApp);
