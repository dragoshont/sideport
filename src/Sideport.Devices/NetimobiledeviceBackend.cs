using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netimobiledevice;
using Netimobiledevice.Exceptions;
using Netimobiledevice.InstallationProxy;
using Netimobiledevice.Lockdown;
using Netimobiledevice.Lockdown.Pairing;
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
    private static readonly TimeSpan DefaultPairingTimeout = TimeSpan.FromMinutes(2);

    private readonly ILogger<NetimobiledeviceBackend> _logger;
    private readonly DeviceMetrics _metrics;
    private readonly string _pairingRecordsDir;
    private readonly TimeSpan _pairingTimeout;
    private readonly TimeProvider _timeProvider;

    public NetimobiledeviceBackend(
        ILogger<NetimobiledeviceBackend>? logger = null,
        DeviceMetrics? metrics = null,
        string? pairingRecordsDir = null,
        TimeSpan? pairingTimeout = null,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? NullLogger<NetimobiledeviceBackend>.Instance;
        _metrics = metrics ?? new DeviceMetrics();
        _pairingRecordsDir = string.IsNullOrWhiteSpace(pairingRecordsDir)
            ? DefaultPairingRecordsDir
            : pairingRecordsDir;
        _pairingTimeout = pairingTimeout is { } timeout && timeout > TimeSpan.Zero
            ? timeout
            : DefaultPairingTimeout;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<IReadOnlyList<BackendDevice>> ListDevicesAsync(CancellationToken ct)
    {
        var result = new List<BackendDevice>();
        foreach (UsbmuxdDevice muxDevice in Usbmux.GetDeviceList())
        {
            ct.ThrowIfCancellationRequested();
            TrustObservation observation = ObserveTrust(muxDevice, ct);
            result.Add(new BackendDevice(
                muxDevice.Serial,
                observation.Name,
                observation.ProductType,
                observation.OsVersion,
                observation.Connection,
                observation.TrustState,
                observation.TrustReason,
                observation.CheckedAt,
                observation.UsableForInstall));
        }
        return Task.FromResult<IReadOnlyList<BackendDevice>>(result);
    }

    public async Task<IReadOnlyList<BackendApp>> ListInstalledAppsAsync(string udid, CancellationToken ct)
    {
        UsbmuxdDevice mux = FindPreferredMuxDevice(udid);
        string connectionType = ConnectionLabel(mux);
        using var metric = _metrics.TrackBackendOperation("installation_proxy_browse", connectionType);
        using LockdownClient lockdown = CreateLockdown(mux);
        using var installProxy = new InstallationProxyService(lockdown, _logger);

        try
        {
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
            metric.Succeed(result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            metric.Cancel();
            throw;
        }
    }

    public async Task<IReadOnlyList<byte[]>> ListProvisioningProfilesAsync(string udid, CancellationToken ct)
    {
        UsbmuxdDevice mux = FindPreferredMuxDevice(udid);
        string connectionType = ConnectionLabel(mux);
        using var metric = _metrics.TrackBackendOperation("misagent_profiles", connectionType);
        using LockdownClient lockdown = CreateLockdown(mux);
        using var misagent = new MisagentService(lockdown, _logger);

        try
        {
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
                    _metrics.RecordProvisioningProfileShapeWarning(ProfileNodeType(profile));
                    _logger.LogWarning("unexpected provisioning-profile node shape: {Error}", ex.Message);
                }
            }
            metric.Succeed(result.Count);
            return result;
        }
        catch (OperationCanceledException)
        {
            metric.Cancel();
            throw;
        }
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
            _logger.LogWarning("usbmux transport probe failed ({ErrorType})", ex.GetType().Name);
            return Task.FromResult<BackendDiagnostics>(new BackendDiagnostics(
                false,
                "Sideport could not reach the usbmux transport.",
                []));
        }

        var probes = new List<BackendDeviceProbe>(muxDevices.Count);
        foreach (UsbmuxdDevice muxDevice in muxDevices)
        {
            ct.ThrowIfCancellationRequested();
            TrustObservation observation = ObserveTrust(muxDevice, ct);
            probes.Add(new BackendDeviceProbe(
                muxDevice.Serial,
                observation.Connection,
                string.Equals(observation.TrustState, "trusted", StringComparison.Ordinal),
                observation.Name,
                observation.TrustReason,
                observation.TrustState,
                observation.TrustReason,
                observation.CheckedAt,
                observation.UsableForInstall));
        }

        return Task.FromResult<BackendDiagnostics>(new BackendDiagnostics(true, null, probes));
    }

    public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ct.ThrowIfCancellationRequested();

        UsbmuxdDevice mux = FindPreferredMuxDevice(udid);
        TrustObservation observation = ObserveTrust(mux, ct);
        return Task.FromResult(new DeviceTrustProbe(
            udid,
            observation.Connection,
            observation.TrustState,
            observation.TrustReason,
            observation.CheckedAt,
            observation.UsableForInstall));
    }

    public async Task<DevicePairingResult> PairAsync(
        string udid,
        IProgress<DevicePairingProgress>? progress,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ct.ThrowIfCancellationRequested();

        UsbmuxdDevice mux = FindPreferredMuxDevice(udid);
        if (mux.ConnectionType == UsbmuxdConnectionType.Network)
        {
            return WifiPairingNotSupported(udid, _timeProvider.GetUtcNow());
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_pairingTimeout);
        var adapter = new PairingProgressAdapter(progress);

        try
        {
            // Opening the session is passive. The explicit PairAsync call below
            // is the sole point where Sideport is allowed to request Trust.
            using LockdownClient lockdown = CreatePairingLockdown(mux);
            if (lockdown.IsPaired)
            {
                progress?.Report(new DevicePairingProgress("paired", "This iPhone already trusts Sideport."));
                return TrustedPairingResult(udid, _timeProvider.GetUtcNow());
            }

            bool paired = await lockdown.PairAsync(adapter, deadline.Token).ConfigureAwait(false);
            if (!paired)
            {
                return adapter.LastState switch
                {
                    PairingState.PasswordProtected => LockedPairingResult(udid, _timeProvider.GetUtcNow()),
                    PairingState.UserDeniedPairing => DeniedPairingResult(udid, _timeProvider.GetUtcNow()),
                    _ => new DevicePairingResult(
                        udid,
                        DeviceConnection.Usb,
                        "untrusted",
                        "The iPhone did not accept the Trust request.",
                        _timeProvider.GetUtcNow(),
                        UsableForInstall: false),
                };
            }

            // The vendored PairAsync persists the accepted record through
            // usbmux. Prove that saved record in a fresh passive lockdown
            // session before claiming trust.
            DeviceTrustProbe verified = await ProbeTrustAsync(udid, ct).ConfigureAwait(false);
            return new DevicePairingResult(
                verified.Udid,
                verified.Connection,
                verified.TrustState,
                verified.TrustReason,
                verified.LockdownCheckedAt,
                verified.UsableForInstall);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            if (adapter.LastState == PairingState.PasswordProtected)
                return LockedPairingResult(udid, _timeProvider.GetUtcNow());

            return new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                "error",
                "The Trust request timed out. Reconnect the iPhone and try again.",
                _timeProvider.GetUtcNow(),
                UsableForInstall: false);
        }
        catch (Exception ex)
        {
            (string state, string reason) = ClassifyTrustFailure(ex);
            _logger.LogWarning(
                "USB pairing failed for iPhone {DeviceTag} ({ErrorType})",
                DeviceTag(udid),
                ex.GetType().Name);
            return new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                state,
                reason,
                _timeProvider.GetUtcNow(),
                UsableForInstall: false);
        }
    }

    private TrustObservation ObserveTrust(UsbmuxdDevice mux, CancellationToken ct)
    {
        DeviceConnection connection = mux.ConnectionType == UsbmuxdConnectionType.Network
            ? DeviceConnection.Wifi
            : DeviceConnection.Usb;

        try
        {
            ct.ThrowIfCancellationRequested();
            // CreateLockdown always uses autopair:false. A successful open with
            // IsPaired=false is an observation, never an invitation to pair.
            using LockdownClient lockdown = CreateLockdown(mux);
            ct.ThrowIfCancellationRequested();

            bool trusted = lockdown.IsPaired;
            return new TrustObservation(
                connection,
                trusted ? "trusted" : "untrusted",
                trusted
                    ? $"Lockdown session verified over {ConnectionLabel(mux).ToUpperInvariant()}."
                    : "No valid pairing record is available for this iPhone.",
                _timeProvider.GetUtcNow(),
                UsableForInstall: trusted,
                lockdown.DeviceName,
                lockdown.ProductType,
                lockdown.OsVersion.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            (string state, string reason) = ClassifyTrustFailure(ex);
            _logger.LogWarning(
                "lockdown trust check failed for iPhone {DeviceTag} over {Connection} ({ErrorType})",
                DeviceTag(mux.Serial),
                ConnectionLabel(mux),
                ex.GetType().Name);
            return new TrustObservation(
                connection,
                state,
                reason,
                _timeProvider.GetUtcNow(),
                UsableForInstall: false,
                Name: "",
                ProductType: "",
                OsVersion: "");
        }
    }

    internal static (string TrustState, string TrustReason) ClassifyTrustFailure(Exception ex) => ex switch
    {
        PasswordRequiredException =>
            ("locked", "The iPhone is locked. Unlock it and try again."),
        LockdownException
        {
            LockdownError: LockdownError.PasswordProtected or LockdownError.EscrowLocked,
        } => ("locked", "The iPhone is locked. Unlock it and try again."),
        NotPairedException or FatalPairingException =>
            ("untrusted", "No valid pairing record is available for this iPhone."),
        LockdownException
        {
            LockdownError: LockdownError.PairingFailed
                or LockdownError.UserDeniedPairing
                or LockdownError.MissingHostId
                or LockdownError.InvalidHostID
                or LockdownError.MissingPairRecord
                or LockdownError.InvalidPairRecord,
        } => ("untrusted", "No valid pairing record is available for this iPhone."),
        _ => ("error", "Sideport could not complete the lockdown trust check."),
    };

    internal static DevicePairingProgress MapPairingProgress(PairingState state) => state switch
    {
        PairingState.Paired => new DevicePairingProgress("paired", "This iPhone now trusts Sideport."),
        PairingState.PasswordProtected => new DevicePairingProgress("locked", "Unlock the iPhone, then try again."),
        PairingState.UserDeniedPairing => new DevicePairingProgress("denied", "Trust was declined on the iPhone."),
        PairingState.PairingDialogResponsePending => new DevicePairingProgress(
            "waiting-for-trust",
            "Unlock the iPhone and tap Trust when asked."),
        _ => new DevicePairingProgress("denied", "Sideport could not establish trust."),
    };

    internal static DevicePairingResult WifiPairingNotSupported(string udid, DateTimeOffset checkedAt) =>
        new(
            udid,
            DeviceConnection.Wifi,
            "error",
            "Connect this iPhone to the Sideport host with USB before pairing.",
            checkedAt,
            UsableForInstall: false);

    private static DevicePairingResult TrustedPairingResult(string udid, DateTimeOffset checkedAt) =>
        new(
            udid,
            DeviceConnection.Usb,
            "trusted",
            "Lockdown session verified over USB.",
            checkedAt,
            UsableForInstall: true);

    private static DevicePairingResult DeniedPairingResult(string udid, DateTimeOffset checkedAt) =>
        new(
            udid,
            DeviceConnection.Usb,
            "untrusted",
            "Trust was declined on the iPhone.",
            checkedAt,
            UsableForInstall: false);

    private static DevicePairingResult LockedPairingResult(string udid, DateTimeOffset checkedAt) =>
        new(
            udid,
            DeviceConnection.Usb,
            "locked",
            "The iPhone is locked. Unlock it and try again.",
            checkedAt,
            UsableForInstall: false);

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
        return CreateLockdown(FindPreferredMuxDevice(udid));
    }

    private static UsbmuxdDevice FindPreferredMuxDevice(string udid)
    {
        List<UsbmuxdDevice> matches = Usbmux.GetDeviceList()
            .Where(d => string.Equals(d.Serial, udid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        // Prefer USB (the daemon proxies it reliably); fall back to Wi-Fi.
        UsbmuxdDevice? mux = matches.FirstOrDefault(d => d.ConnectionType != UsbmuxdConnectionType.Network)
                             ?? matches.FirstOrDefault();
        if (mux is null)
            throw new InvalidOperationException("The requested iPhone is not currently reachable over usbmux.");
        return mux;
    }

    private static string ConnectionLabel(UsbmuxdDevice mux) =>
        mux.ConnectionType == UsbmuxdConnectionType.Network ? "wifi" : "usb";

    private static string ProfileNodeType(PropertyNode node) =>
        node.GetType().Name.Replace("Node", "", StringComparison.OrdinalIgnoreCase);

    private LockdownClient CreateLockdown(UsbmuxdDevice mux)
    {
        if (mux.ConnectionType != UsbmuxdConnectionType.Network)
        {
            // Every ordinary USB open is explicitly passive. PairAsync is the
            // only method that may invoke LockdownClient.PairAsync afterwards.
            return MobileDevice.CreateUsingUsbmux(
                mux.Serial,
                autopair: false,
                connectionType: mux.ConnectionType,
                pairingRecordsCacheDir: _pairingRecordsDir,
                logger: _logger);
        }

        string? host = DecodeNetworkAddress(mux.NetworkAddress);
        if (host is null)
            throw new InvalidOperationException("The Wi-Fi iPhone has no usable routable address.");

        _logger.LogDebug(
            "connecting to Wi-Fi iPhone {DeviceTag} directly over TCP at {Host}:{Port} (netmuxd does not proxy usbmux Connect)",
            DeviceTag(mux.Serial), host, LockdownPort);

        // autopair:false — the device is already trusted host-side, so only USE
        // the existing pairing record; never write one (the mount is read-only).
        return MobileDevice.CreateUsingTcp(
            hostname: host,
            identifier: mux.Serial,
            autopair: false,
            pairingRecordsCacheDir: _pairingRecordsDir,
            logger: _logger);
    }

    private LockdownClient CreatePairingLockdown(UsbmuxdDevice mux)
    {
        if (mux.ConnectionType == UsbmuxdConnectionType.Network)
            throw new InvalidOperationException("Pairing requires a USB connection.");

        // Opening remains passive. The caller must explicitly invoke PairAsync.
        // Do not point the pairing client at a potentially read-only host cache;
        // SavePairRecord persists through the usbmux daemon after acceptance.
        return MobileDevice.CreateUsingUsbmux(
            mux.Serial,
            autopair: false,
            connectionType: mux.ConnectionType,
            pairingRecordsCacheDir: "",
            logger: _logger);
    }

    private static string DeviceTag(string udid) => udid.Length > 8 ? $"…{udid[^8..]}" : udid;

    private sealed record TrustObservation(
        DeviceConnection Connection,
        string TrustState,
        string TrustReason,
        DateTimeOffset CheckedAt,
        bool UsableForInstall,
        string Name,
        string ProductType,
        string OsVersion);

    private sealed class PairingProgressAdapter(IProgress<DevicePairingProgress>? progress) : IProgress<PairingState>
    {
        public PairingState? LastState { get; private set; }

        public void Report(PairingState value)
        {
            LastState = value;
            progress?.Report(MapPairingProgress(value));
        }
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
