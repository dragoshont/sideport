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

    internal NetimobiledeviceController(
        IDeviceBackend backend,
        ILogger<NetimobiledeviceController>? logger = null,
        DeviceMetrics? metrics = null)
    {
        _backend = backend;
        _logger = logger ?? NullLogger<NetimobiledeviceController>.Instance;
        _metrics = metrics ?? new DeviceMetrics();
    }

    public async Task<IReadOnlyList<DeviceInfo>> ListDevicesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<BackendDevice> raw = await _backend.ListDevicesAsync(ct);

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
                .Select(d => new DeviceInfo(d.Udid, d.Name, d.ProductType, d.OsVersion, d.Connection))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Udid, StringComparer.OrdinalIgnoreCase),
        ];
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
            if (d.LockdownOk)
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "ok",
                    $"{d.Name} is paired and reachable over {d.Connection}.", null));
            }
            else
            {
                checks.Add(new DeviceCheck(
                    $"trust:{d.Udid}", $"Trust / pairing (…{tag})", "blocked",
                    $"Discovered over {d.Connection}, but the lockdown/trust handshake failed: {d.LockdownError}",
                    "Unlock the iPhone and tap “Trust This Computer”. If it persists, Sideport cannot read the device's pairing record — pair the device from the host and make sure the lockdown pairing records are reachable by Sideport."));
            }
        }

        string status = checks.Any(c => c.Status == "blocked") ? "blocked"
            : checks.Any(c => c.Status == "warning") ? "warning" : "ok";
        return new DeviceDiagnostics(status, checks);
    }

    public async Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);

        using var metric = _metrics.TrackInstalledAppsRequest();
        try
        {
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
            metric.Succeed(result.Length);
            return result;
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

        _logger.LogInformation("installing {Bundle} onto {Udid}", info.BundleIdentifier, udid);
        await _backend.InstallAsync(udid, ipaPath, progress: null, ct);
    }

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

