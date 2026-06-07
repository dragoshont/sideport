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

    internal NetimobiledeviceController(IDeviceBackend backend, ILogger<NetimobiledeviceController>? logger = null)
    {
        _backend = backend;
        _logger = logger ?? NullLogger<NetimobiledeviceController>.Instance;
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

    public async Task<IReadOnlyList<InstalledApp>> ListInstalledAppsAsync(string udid, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(udid);

        IReadOnlyList<BackendApp> apps = await _backend.ListInstalledAppsAsync(udid, ct);
        IReadOnlyList<ProvisioningProfileInfo> profiles =
            ParseProfiles(await _backend.ListProvisioningProfilesAsync(udid, ct));

        return
        [
            .. apps
                .Where(a => a.IsUserApp) // sideloadable user apps, not system apps
                .Select(a => new InstalledApp(a.BundleId, a.Name, a.Version, ResolveExpiry(a.BundleId, profiles)))
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.BundleId, StringComparer.OrdinalIgnoreCase),
        ];
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

