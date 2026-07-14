using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;
using Sideport.DeveloperApi.Packaging;

namespace Sideport.Devices;

/// <summary>
/// <see cref="IDeviceController"/> backed by <c>artehe/Netimobiledevice</c> (MIT,
/// pinned exact 2.5.2) via the <see cref="IDeviceBackend"/> seam. Replaces the
/// whole libimobiledevice family (usbmux + lockdown + installation_proxy +
/// misagent). USB and Wi-Fi (network/usbmux) devices are both enumerated; the
/// per-app signing expiry is derived by joining installed apps to their
/// on-device provisioning profiles.
/// </summary>
public sealed class NetimobiledeviceController : IDeviceController
{
    private readonly IDeviceBackend _backend;
    private readonly ILogger<NetimobiledeviceController> _logger;
    private readonly DeviceMetrics _metrics;
    private readonly DevicePairingOwner _pairingOwner;
    private readonly TimeSpan _installedAppsCacheTtl;
    private readonly TimeProvider _timeProvider;
    private readonly object _installedAppsCacheGate = new();
    private readonly Dictionary<string, InstalledAppsCacheEntry> _installedAppsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _installedAppsLocks = new(StringComparer.OrdinalIgnoreCase);

    internal NetimobiledeviceController(
        IDeviceBackend backend,
        ILogger<NetimobiledeviceController>? logger = null,
        DeviceMetrics? metrics = null,
        TimeSpan? installedAppsCacheTtl = null,
        TimeProvider? timeProvider = null,
        DevicePairingOwner pairingOwner = DevicePairingOwner.Sideport)
    {
        _backend = backend;
        _logger = logger ?? NullLogger<NetimobiledeviceController>.Instance;
        _metrics = metrics ?? new DeviceMetrics();
        _pairingOwner = pairingOwner;
        _installedAppsCacheTtl = installedAppsCacheTtl ?? TimeSpan.FromMinutes(5);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackendDevice> raw = await _backend.ListDevicesAsync(ct);
        return MapDevices(raw);
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListConnectedDevicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackendDevice> raw = await _backend.ListConnectedDevicesAsync(ct);
        return MapDevices(raw);
    }

    private static IReadOnlyList<DeviceInfo> MapDevices(IReadOnlyList<BackendDevice> raw)
    {

        // A device reachable over both USB and Wi-Fi appears once; prefer USB
        // (more reliable for install). Dedup by UDID, USB winning.
        var byUdid = new Dictionary<string, BackendDevice>(StringComparer.OrdinalIgnoreCase);
        foreach (BackendDevice device in raw)
        {
            if (!byUdid.TryGetValue(device.Udid, out BackendDevice? existing) ||
                (existing.Connection != DeviceConnection.Usb && device.Connection == DeviceConnection.Usb))
            {
                byUdid[device.Udid] = device;
            }
        }

        return
        [
            .. byUdid.Values
                .Select(d => new DeviceInfo(
                    d.Udid,
                    d.Name,
                    d.ProductType,
                    d.OsVersion,
                    d.Connection,
                    d.TrustState,
                    d.TrustReason,
                    d.LockdownCheckedAt,
                    d.UsableForInstall))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Udid, StringComparer.OrdinalIgnoreCase),
        ];
    }

    public Task<DeviceTrustProbe> ProbeTrustAsync(string udid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        return _backend.ProbeTrustAsync(udid, ct);
    }

    public async Task<DevicePairingResult> PairAsync(
        string udid,
        IProgress<DevicePairingProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);

        // Pair only from a definite passive observation. An ambiguous/error
        // probe must never be converted into a new Trust request.
        DeviceTrustProbe current = await _backend.ProbeTrustAsync(udid, ct).ConfigureAwait(false);
        if (current.Connection != DeviceConnection.Usb)
        {
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                "error",
                "Connect this iPhone to the Sideport host with USB before pairing.",
                current.LockdownCheckedAt,
                UsableForInstall: false,
                DevicePairingDisposition.UsbRequired);
        }

        if (string.Equals(current.TrustState, "trusted", StringComparison.Ordinal))
        {
            progress?.Report(new DevicePairingProgress("paired", "This iPhone already trusts Sideport."));
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                current.TrustState,
                current.TrustReason,
                current.LockdownCheckedAt,
                current.UsableForInstall,
                DevicePairingDisposition.Trusted);
        }

        if (string.Equals(current.TrustState, "locked", StringComparison.Ordinal))
        {
            progress?.Report(new DevicePairingProgress("locked", "Unlock the iPhone, then try again."));
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                current.TrustState,
                current.TrustReason,
                current.LockdownCheckedAt,
                UsableForInstall: false,
                DevicePairingDisposition.Locked);
        }

        if (!string.Equals(current.TrustState, "untrusted", StringComparison.Ordinal))
        {
            DevicePairingDisposition disposition = current.Disposition == DevicePairingDisposition.Unknown
                ? DevicePairingDisposition.TransportUnavailable
                : current.Disposition;
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                current.TrustState,
                current.TrustReason ?? "Sideport could not verify the iPhone before pairing.",
                current.LockdownCheckedAt,
                UsableForInstall: false,
                disposition);
        }

        if (current.Disposition == DevicePairingDisposition.RepairRequired)
        {
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                "error",
                current.TrustReason ?? "The saved pairing record is damaged and must be repaired before pairing can continue.",
                current.LockdownCheckedAt,
                UsableForInstall: false,
                DevicePairingDisposition.RepairRequired);
        }

        if (_pairingOwner == DevicePairingOwner.Host)
        {
            return new DevicePairingResult(
                current.Udid,
                current.Connection,
                "untrusted",
                "Pairing is managed by the host. Keep the iPhone connected and approve Trust if the host asks.",
                current.LockdownCheckedAt,
                UsableForInstall: false,
                DevicePairingDisposition.HostManaged);
        }

        return await _backend.PairAsync(udid, progress, ct).ConfigureAwait(false);
    }

    public async Task<DeviceDiagnostics> DiagnoseAsync(CancellationToken ct = default)
    {
        BackendDiagnostics probe = await _backend.DiagnoseAsync(ct);
        var checks = new List<DeviceCheck>();

        // Layer 1 — usbmux transport (the pod's socket to usbmuxd/netmuxd).
        if (!probe.TransportReachable)
        {
            checks.Add(new DeviceCheck(
                "usbmux", "usbmux transport", "blocked",
                $"Could not reach the usbmux socket: {probe.TransportError}",
                "usbmuxd/netmuxd is down on the host, or the pod's /var/run/usbmuxd socket is stale because the host daemon restarted. Restart the daemon and roll out the Sideport pod again."));
            return new DeviceDiagnostics("blocked", checks);
        }
        checks.Add(new DeviceCheck("usbmux", "usbmux transport", "ok", "Connected to the usbmux socket.", null));

        // Layer 2 — device enumeration (USB and/or Wi-Fi).
        int usb = probe.Devices.Count(d => d.Connection == DeviceConnection.Usb);
        int wifi = probe.Devices.Count(d => d.Connection == DeviceConnection.Wifi);
        if (probe.Devices.Count == 0)
        {
            checks.Add(new DeviceCheck(
                "devices", "Device reachable", "blocked",
                "No devices are visible over USB or Wi-Fi.",
                "Connect the iPhone with a USB cable, or enable Wi-Fi sync and keep the iPhone unlocked on the same network as the host."));
            return new DeviceDiagnostics("blocked", checks);
        }
        checks.Add(new DeviceCheck(
            "devices", "Device reachable", "ok",
            $"{probe.Devices.Count} device(s): {usb} over USB, {wifi} over Wi-Fi.", null));

        // Layer 3 — per-device trust / lockdown handshake.
        foreach (BackendDeviceProbe d in probe.Devices)
        {
            string tag = d.Udid.Length > 8 ? d.Udid[^8..] : d.Udid;
            string trustState = string.Equals(d.TrustState, "unknown", StringComparison.Ordinal)
                ? d.LockdownOk ? "trusted" : "error"
                : d.TrustState;
            if (string.Equals(trustState, "trusted", StringComparison.Ordinal))
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "ok",
                    $"{d.Name} is paired and reachable over {d.Connection}.", null));
            }
            else if (string.Equals(trustState, "locked", StringComparison.Ordinal))
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "blocked",
                    d.TrustReason ?? "The iPhone is locked, so Sideport could not verify trust.",
                    "Unlock the iPhone, keep this screen open, and try again."));
            }
            else if (string.Equals(trustState, "untrusted", StringComparison.Ordinal))
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "blocked",
                    d.TrustReason ?? $"The iPhone is visible over {d.Connection}, but it does not trust this Sideport host yet.",
                    "Connect the iPhone over USB and start Add iPhone; Sideport will then ask you to unlock it and tap “Trust This Computer”."));
            }
            else
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "blocked",
                    d.TrustReason ?? $"Sideport could not complete the lockdown check over {d.Connection}.",
                    "Check the cable or Wi-Fi connection, unlock the iPhone, and retry the diagnostic."));
            }
        }

        string status = checks.Any(c => c.Status == "blocked") ? "blocked"
            : checks.Any(c => c.Status == "warning") ? "warning" : "ok";
        return new DeviceDiagnostics(status, checks);
    }

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(
        string udid,
        CancellationToken ct = default) =>
        ListInstalledAppsCoreAsync(udid, forceFresh: false, ct);

    public Task<IReadOnlyList<InstalledApp>> ListInstalledAppsFreshAsync(
        string udid,
        CancellationToken ct = default) =>
        ListInstalledAppsCoreAsync(udid, forceFresh: true, ct);

    private async Task<IReadOnlyList<InstalledApp>> ListInstalledAppsCoreAsync(
        string udid,
        bool forceFresh,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);

        using var metric = _metrics.TrackInstalledAppsRequest();
        try
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (!forceFresh && TryGetInstalledAppsCache(udid, now, out IReadOnlyList<InstalledApp>? cached))
            {
                _metrics.RecordInstalledAppsCacheEvent("hit");
                metric.Succeed(cached!.Count);
                return cached;
            }

            SemaphoreSlim readLock = GetInstalledAppsLock(udid);
            await readLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                now = _timeProvider.GetUtcNow();
                if (!forceFresh && TryGetInstalledAppsCache(udid, now, out cached))
                {
                    _metrics.RecordInstalledAppsCacheEvent("hit_after_wait");
                    metric.Succeed(cached!.Count);
                    return cached;
                }

                _metrics.RecordInstalledAppsCacheEvent(forceFresh ? "bypass" : CacheEnabled ? "miss" : "disabled");

                IReadOnlyList<BackendApp> apps = await _backend.ListInstalledAppsAsync(udid, ct);
                IReadOnlyList<ProvisioningProfileInfo> profiles =
                    ParseProfiles(await _backend.ListProvisioningProfilesAsync(udid, ct));

                InstalledApp[] result =
                [
                    .. apps
                        .Where(a => a.IsUserApp) // sideloadable user apps, not system apps
                        .Select(a => new InstalledApp(a.BundleId, a.Name, a.Version, ResolveExpiry(a.BundleId, profiles)))
                        .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(a => a.BundleId, StringComparer.OrdinalIgnoreCase),
                ];
                StoreInstalledAppsCache(udid, result, _timeProvider.GetUtcNow());
                metric.Succeed(result.Length);
                return result;
            }
            finally
            {
                readLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            metric.Cancel();
            throw;
        }
    }

    public async Task InstallAsync(string udid, string ipaPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);
        ArgumentException.ThrowIfNullOrEmpty(ipaPath);

        if (!File.Exists(ipaPath))
            throw new FileNotFoundException("IPA to install was not found", ipaPath);

        // Validate it is a well-formed IPA before handing it to the device, so a
        // corrupt artifact fails here with a clear error rather than mid-install.
        IpaInfo info;
        try
        {
            info = IpaInspector.Inspect(ipaPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"not a valid IPA: {ex.Message}", ex);
        }

        _logger.LogInformation("installing {Bundle} onto iPhone {DeviceTag}", info.BundleIdentifier, DeviceTag(udid));
        await _backend.InstallAsync(udid, ipaPath, progress: null, ct);
        InvalidateInstalledAppsCache(udid);
    }

    private static string DeviceTag(string udid) => udid.Length > 8 ? $"…{udid[^8..]}" : udid;

    private bool CacheEnabled => _installedAppsCacheTtl > TimeSpan.Zero;

    private bool TryGetInstalledAppsCache(string udid, DateTimeOffset now, out IReadOnlyList<InstalledApp>? apps)
    {
        apps = null;
        if (!CacheEnabled)
            return false;

        lock (_installedAppsCacheGate)
        {
            if (!_installedAppsCache.TryGetValue(udid, out InstalledAppsCacheEntry? entry))
                return false;

            if (entry.ExpiresAt <= now)
            {
                _installedAppsCache.Remove(udid);
                _metrics.RecordInstalledAppsCacheEvent("expired");
                return false;
            }

            apps = entry.Apps;
            return true;
        }
    }

    private void StoreInstalledAppsCache(string udid, IReadOnlyList<InstalledApp> apps, DateTimeOffset now)
    {
        if (!CacheEnabled)
            return;

        lock (_installedAppsCacheGate)
            _installedAppsCache[udid] = new InstalledAppsCacheEntry([.. apps], now.Add(_installedAppsCacheTtl));
    }

    private void InvalidateInstalledAppsCache(string udid)
    {
        lock (_installedAppsCacheGate)
        {
            if (_installedAppsCache.Remove(udid))
                _metrics.RecordInstalledAppsCacheEvent("invalidated");
        }
    }

    private SemaphoreSlim GetInstalledAppsLock(string udid)
    {
        lock (_installedAppsCacheGate)
        {
            if (!_installedAppsLocks.TryGetValue(udid, out SemaphoreSlim? readLock))
            {
                readLock = new SemaphoreSlim(1, 1);
                _installedAppsLocks[udid] = readLock;
            }

            return readLock;
        }
    }

    private sealed record InstalledAppsCacheEntry(IReadOnlyList<InstalledApp> Apps, DateTimeOffset ExpiresAt);

    /// <summary>
    /// Resolve the signing expiry for a bundle id from the device's profiles: the
    /// latest expiry among the profiles that cover it (the active profile is the
    /// one with the furthest-out expiry).
    /// </summary>
    private static DateTimeOffset? ResolveExpiry(string bundleId, IReadOnlyList<ProvisioningProfileInfo> profiles)
    {
        DateTimeOffset? best = null;
        foreach (ProvisioningProfileInfo profile in profiles)
        {
            if (!profile.CoversBundle(bundleId))
                continue;
            if (best is null || profile.ExpirationDate > best)
                best = profile.ExpirationDate;
        }
        return best;
    }

    private IReadOnlyList<ProvisioningProfileInfo> ParseProfiles(IReadOnlyList<byte[]> blobs)
    {
        var parsed = new List<ProvisioningProfileInfo>(blobs.Count);
        foreach (byte[] blob in blobs)
        {
            try
            {
                parsed.Add(MobileProvision.Parse(blob));
            }
            catch (Exception ex)
            {
                // A single unparseable profile must not blank out every app's expiry.
                _logger.LogWarning("skipping unparseable provisioning profile: {Error}", ex.Message);
            }
        }
        return parsed;
    }
}
