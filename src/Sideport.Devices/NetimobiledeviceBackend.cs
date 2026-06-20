using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netimobiledevice;
using Netimobiledevice.InstallationProxy;
using Netimobiledevice.Lockdown;
using Netimobiledevice.Misagent;
using Netimobiledevice.Plist;
using Netimobiledevice.Usbmuxd;
using Sideport.Core;

namespace Sideport.Devices;

/// <summary>
/// The real <see cref="IDeviceBackend"/>, talking to devices through
/// <c>artehe/Netimobiledevice</c> (usbmux + lockdown + installation_proxy +
/// misagent). This is the only code that touches the device library; its
/// behaviour is validated by the host integration gate (a physically-connected
/// iPhone), while <see cref="NetimobiledeviceController"/>'s logic is covered by
/// unit tests against a fake backend.
/// </summary>
internal sealed class NetimobiledeviceBackend : IDeviceBackend
{
    private readonly ILogger<NetimobiledeviceBackend> _logger;

    public NetimobiledeviceBackend(ILogger<NetimobiledeviceBackend>? logger = null)
    {
        _logger = logger ?? NullLogger<NetimobiledeviceBackend>.Instance;
    }

    public Task<IReadOnlyList<BackendDevice>> ListDevicesAsync(CancellationToken ct)
    {
        var result = new List<BackendDevice>();
        foreach (UsbmuxdDevice muxDevice in Usbmux.GetDeviceList())
        {
            DeviceConnection connection = muxDevice.ConnectionType == UsbmuxdConnectionType.Network
                ? DeviceConnection.Wifi
                : DeviceConnection.Usb;

            // Best-effort lockdown enrichment; a discovered-but-unpaired/asleep
            // device is still listed (with blank metadata) rather than dropped.
            string name = "", productType = "", osVersion = "";
            try
            {
                using UsbmuxLockdownClient lockdown = MobileDevice.CreateUsingUsbmux(muxDevice.Serial, logger: _logger);
                name = lockdown.DeviceName;
                productType = lockdown.ProductType;
                osVersion = lockdown.OsVersion.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("could not read lockdown info for {Udid}: {Error}", muxDevice.Serial, ex.Message);
            }

            result.Add(new BackendDevice(muxDevice.Serial, name, productType, osVersion, connection));
        }
        return Task.FromResult<IReadOnlyList<BackendDevice>>(result);
    }

    public async Task<IReadOnlyList<BackendApp>> ListInstalledAppsAsync(string udid, CancellationToken ct)
    {
        using UsbmuxLockdownClient lockdown = MobileDevice.CreateUsingUsbmux(udid, logger: _logger);
        using var installProxy = new InstallationProxyService(lockdown, _logger);

        ArrayNode apps = await installProxy.Browse(cancellationToken: ct).ConfigureAwait(false);

        var result = new List<BackendApp>(apps.Count);
        foreach (PropertyNode node in apps)
        {
            DictionaryNode app = node.AsDictionaryNode();

            string bundleId = ReadString(app, "CFBundleIdentifier");
            if (string.IsNullOrEmpty(bundleId))
                continue;

            string name = ReadString(app, "CFBundleDisplayName");
            if (string.IsNullOrEmpty(name))
                name = ReadString(app, "CFBundleName");
            string version = ReadString(app, "CFBundleShortVersionString");
            if (string.IsNullOrEmpty(version))
                version = ReadString(app, "CFBundleVersion");

            bool isUser = string.Equals(ReadString(app, "ApplicationType"), "User", StringComparison.OrdinalIgnoreCase);

            result.Add(new BackendApp(bundleId, name, version, isUser));
        }
        return result;
    }

    public async Task<IReadOnlyList<byte[]>> ListProvisioningProfilesAsync(string udid, CancellationToken ct)
    {
        using UsbmuxLockdownClient lockdown = MobileDevice.CreateUsingUsbmux(udid, logger: _logger);
        using var misagent = new MisagentService(lockdown, _logger);

        List<PropertyNode> profiles = await misagent.GetInstalledProvisioningProfiles().ConfigureAwait(false);

        var result = new List<byte[]>(profiles.Count);
        foreach (PropertyNode profile in profiles)
        {
            try
            {
                result.Add(profile.AsDataNode().Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("unexpected provisioning-profile node shape: {Error}", ex.Message);
            }
        }
        return result;
    }

    public async Task InstallAsync(string udid, string ipaPath, IProgress<int>? progress, CancellationToken ct)
    {
        using UsbmuxLockdownClient lockdown = MobileDevice.CreateUsingUsbmux(udid, logger: _logger);
        using var installProxy = new InstallationProxyService(lockdown, _logger);
        await installProxy.Install(ipaPath, ct, options: null, progress).ConfigureAwait(false);
    }

    public Task<BackendDiagnostics> DiagnoseAsync(CancellationToken ct)
    {
        List<UsbmuxdDevice> muxDevices;
        try
        {
            muxDevices = Usbmux.GetDeviceList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("usbmux transport probe failed: {Error}", ex.Message);
            return Task.FromResult<BackendDiagnostics>(new BackendDiagnostics(false, ex.Message, []));
        }

        var probes = new List<BackendDeviceProbe>(muxDevices.Count);
        foreach (UsbmuxdDevice muxDevice in muxDevices)
        {
            DeviceConnection connection = muxDevice.ConnectionType == UsbmuxdConnectionType.Network
                ? DeviceConnection.Wifi
                : DeviceConnection.Usb;
            try
            {
                using UsbmuxLockdownClient lockdown = MobileDevice.CreateUsingUsbmux(muxDevice.Serial, logger: _logger);
                probes.Add(new BackendDeviceProbe(muxDevice.Serial, connection, true, lockdown.DeviceName, null));
            }
            catch (Exception ex)
            {
                probes.Add(new BackendDeviceProbe(muxDevice.Serial, connection, false, null, ex.Message));
            }
        }

        return Task.FromResult<BackendDiagnostics>(new BackendDiagnostics(true, null, probes));
    }

    private static string ReadString(DictionaryNode dict, string key) =>
        dict.TryGetValue(key, out PropertyNode? node) ? node.AsStringNode().Value : "";
}
