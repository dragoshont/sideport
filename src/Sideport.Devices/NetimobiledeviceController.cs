using Sideport.Core;

namespace Sideport.Devices;

/// <summary>
/// <see cref="IDeviceController"/> backed by a vendored, pinned build of
/// <c>artehe/Netimobiledevice</c> (MIT). Not yet wired — the dependency is
/// vendored at a pinned commit (do NOT float the NuGet feed; design §6) in a
/// later phase. Wi-Fi/mDNS discovery is the open spike (design §6/§9).
/// </summary>
public sealed class NetimobiledeviceController : IDeviceController
{
    public Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Phase 1: vendor Netimobiledevice and implement usbmux discovery.");

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 1: installation_proxy enumeration + expiry parsing.");

    public Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
        => throw new NotImplementedException("Phase 1: installation_proxy install over lockdown.");
}
