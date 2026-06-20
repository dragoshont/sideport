using System.Net;
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
    // The lockdown service port — identical whether the connection is proxied
    // through usbmux (USB) or opened directly over TCP (Wi-Fi).
    private const ushort LockdownPort = 62078;

    // Default host location of the trusted lockdown pairing records. The pod
    // mounts this read-only so the direct-TCP (Wi-Fi) path can validate the
    // already-established trust without re-pairing. Overridable via
    // Sideport:Devices:PairingRecordsDir for non-standard hosts.
    private const string DefaultPairingRecordsDir = "/var/lib/lockdown";

    private readonly ILogger<NetimobiledeviceBackend> _logger;
    private readonly string _pairingRecordsDir;

    public NetimobiledeviceBackend(
        ILogger<NetimobiledeviceBackend>? logger = null,
        string? pairingRecordsDir = null)
    {
        _logger = logger ?? NullLogger<NetimobiledeviceBackend>.Instance;
        _pairingRecordsDir = string.IsNullOrWhiteSpace(pairingRecordsDir)
            ? DefaultPairingRecordsDir
            : pairingRecordsDir;
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
                using LockdownClient lockdown = CreateLockdown(muxDevice);
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
        using LockdownClient lockdown = CreateLockdown(udid);
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
        using LockdownClient lockdown = CreateLockdown(udid);
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
        using LockdownClient lockdown = CreateLockdown(udid);
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
                using LockdownClient lockdown = CreateLockdown(muxDevice);
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

    /// <summary>
    /// Open a lockdown session to a device, choosing the transport by how the
    /// device is reachable. USB devices are proxied through usbmux (the daemon
    /// relays the connection). Wi-Fi/network devices are connected to DIRECTLY
    /// over TCP: netmuxd advertises them but does not relay the usbmux Connect,
    /// so a direct TCP lockdown to the device's network address — validated
    /// against the host's existing pairing record — is the only path that
    /// completes the trusted handshake.
    /// </summary>
    private LockdownClient CreateLockdown(string udid)
    {
        List<UsbmuxdDevice> matches = Usbmux.GetDeviceList()
            .Where(d => string.Equals(d.Serial, udid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Prefer USB (the daemon proxies it reliably); fall back to Wi-Fi.
        UsbmuxdDevice? mux = matches.FirstOrDefault(d => d.ConnectionType != UsbmuxdConnectionType.Network)
                             ?? matches.FirstOrDefault();
        if (mux is null)
            throw new InvalidOperationException($"device {udid} is not currently reachable over usbmux");
        return CreateLockdown(mux);
    }

    private LockdownClient CreateLockdown(UsbmuxdDevice mux)
    {
        if (mux.ConnectionType != UsbmuxdConnectionType.Network)
            return MobileDevice.CreateUsingUsbmux(mux.Serial, logger: _logger);

        string? host = DecodeNetworkAddress(mux.NetworkAddress);
        if (host is null)
            throw new InvalidOperationException(
                $"Wi-Fi device {mux.Serial} reported no usable (routable) network address");

        _logger.LogDebug(
            "connecting to Wi-Fi device {Udid} directly over TCP at {Host}:{Port} (netmuxd does not proxy usbmux Connect)",
            mux.Serial, host, LockdownPort);

        // autopair:false — the device is already trusted host-side, so only USE
        // the existing pairing record; never write one (the mount is read-only).
        return MobileDevice.CreateUsingTcp(
            hostname: host,
            identifier: mux.Serial,
            autopair: false,
            pairingRecordsCacheDir: _pairingRecordsDir,
            logger: _logger);
    }

    /// <summary>
    /// Convert <see cref="UsbmuxdDevice.NetworkAddress"/> to an IP string.
    /// Netimobiledevice already decodes the muxer sockaddr down to the raw
    /// address bytes in its constructor, so this is 4 bytes (IPv4) or 16 bytes
    /// (IPv6). IPv6 link-local (<c>fe80::/10</c>) needs a pod-local scope id we
    /// cannot supply and is treated as unusable.
    /// </summary>
    internal static string? DecodeNetworkAddress(byte[]? address)
    {
        if (address is null)
            return null;

        if (address.Length == 4) // IPv4
            return new IPAddress(address).ToString();

        if (address.Length == 16) // IPv6
        {
            // Link-local (fe80::/10) is not routable from the pod.
            if (address[0] == 0xFE && (address[1] & 0xC0) == 0x80)
                return null;
            return new IPAddress(address).ToString();
        }

        return null;
    }
}
