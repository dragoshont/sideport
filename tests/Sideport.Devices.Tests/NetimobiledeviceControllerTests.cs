using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;

namespace Sideport.Devices.Tests;

public class NetimobiledeviceControllerTests
{
    private readonly FakeDeviceBackend _backend = new();

    private NetimobiledeviceController Build() =>
        new(_backend, NullLogger<NetimobiledeviceController>.Instance);

    // --- device discovery + dedup -----------------------------------------

    [Fact]
    public async Task ListDevices_DistinctDevices_AllReturnedOrderedByName()
    {
        _backend.Devices.Add(new BackendDevice("UDID-B", "Zeta", "iPhone15,2", "17.0", DeviceConnection.Usb));
        _backend.Devices.Add(new BackendDevice("UDID-A", "Alpha", "iPhone14,5", "16.5", DeviceConnection.Wifi));

        IReadOnlyList<DeviceInfo> devices = await Build().ListDevicesAsync();

        Assert.Equal(2, devices.Count);
        Assert.Equal("Alpha", devices[0].Name);
        Assert.Equal("Zeta", devices[1].Name);
    }

    [Fact]
    public async Task ListDevices_SameUdidOnUsbAndWifi_DedupsPreferringUsb()
    {
        _backend.Devices.Add(new BackendDevice("UDID-1", "Phone", "iPhone15,2", "17.0", DeviceConnection.Wifi));
        _backend.Devices.Add(new BackendDevice("UDID-1", "Phone", "iPhone15,2", "17.0", DeviceConnection.Usb));

        IReadOnlyList<DeviceInfo> devices = await Build().ListDevicesAsync();

        DeviceInfo only = Assert.Single(devices);
        Assert.Equal(DeviceConnection.Usb, only.Connection);
    }

    [Fact]
    public async Task ListDevices_SameUdidUsbFirstThenWifi_KeepsUsb()
    {
        _backend.Devices.Add(new BackendDevice("UDID-1", "Phone", "iPhone15,2", "17.0", DeviceConnection.Usb));
        _backend.Devices.Add(new BackendDevice("UDID-1", "Phone", "iPhone15,2", "17.0", DeviceConnection.Wifi));

        IReadOnlyList<DeviceInfo> devices = await Build().ListDevicesAsync();

        Assert.Equal(DeviceConnection.Usb, Assert.Single(devices).Connection);
    }

    [Fact]
    public async Task ListDevices_Empty_ReturnsEmpty()
    {
        Assert.Empty(await Build().ListDevicesAsync());
    }

    // --- device connectivity self-test (DiagnoseAsync) --------------------

    [Fact]
    public async Task Diagnose_TransportDown_IsBlockedWithRemediation()
    {
        _backend.DiagnoseTransportReachable = false;
        _backend.DiagnoseTransportError = "Can't connect to Usbmux socket";

        DeviceDiagnostics result = await Build().DiagnoseAsync();

        Assert.Equal("blocked", result.Status);
        DeviceCheck usbmux = Assert.Single(result.Checks);
        Assert.Equal("usbmux", usbmux.Id);
        Assert.Equal("blocked", usbmux.Status);
        Assert.False(string.IsNullOrWhiteSpace(usbmux.Remediation));
    }

    [Fact]
    public async Task Diagnose_NoDevices_IsBlockedAtEnumeration()
    {
        DeviceDiagnostics result = await Build().DiagnoseAsync();

        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Checks, c => c.Id == "usbmux" && c.Status == "ok");
        Assert.Contains(result.Checks, c => c.Id == "devices" && c.Status == "blocked");
    }

    [Fact]
    public async Task Diagnose_UnpairedDevice_IsBlockedAtTrust()
    {
        _backend.DiagnoseProbes.Add(new BackendDeviceProbe(
            "UDID-1", DeviceConnection.Wifi, LockdownOk: false, Name: null, LockdownError: "trust not accepted"));

        DeviceDiagnostics result = await Build().DiagnoseAsync();

        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Checks, c => c.Id == "devices" && c.Status == "ok");
        DeviceCheck trust = Assert.Single(result.Checks, c => c.Id.StartsWith("trust:"));
        Assert.Equal("blocked", trust.Status);
        Assert.Contains("Trust This Computer", trust.Remediation!);
    }

    [Fact]
    public async Task Diagnose_HealthyDevice_IsOk()
    {
        _backend.DiagnoseProbes.Add(new BackendDeviceProbe(
            "UDID-1", DeviceConnection.Usb, LockdownOk: true, Name: "Test iPhone", LockdownError: null));

        DeviceDiagnostics result = await Build().DiagnoseAsync();

        Assert.Equal("ok", result.Status);
        Assert.All(result.Checks, c => Assert.Equal("ok", c.Status));
        Assert.Contains(result.Checks, c => c.Detail.Contains("Test iPhone"));
    }

    // --- installed apps + expiry join -------------------------------------

    [Fact]
    public async Task ListApps_FiltersOutSystemApps()
    {
        _backend.AppsByUdid["U"] =
        [
            new BackendApp("com.example.user", "User App", "1.0", IsUserApp: true),
            new BackendApp("com.apple.system", "System", "1.0", IsUserApp: false),
        ];

        IReadOnlyList<InstalledApp> apps = await Build().ListInstalledAppsAsync("U");

        InstalledApp only = Assert.Single(apps);
        Assert.Equal("com.example.user", only.BundleId);
    }

    [Fact]
    public async Task ListApps_JoinsExactProfileExpiry()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(7);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.app", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] =
        [
            DeviceFixtures.MobileProvision("TEAM123456.com.example.app", expiry),
        ];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));

        Assert.NotNull(app.SignatureExpiresAt);
        Assert.True(Math.Abs((app.SignatureExpiresAt!.Value - expiry).TotalSeconds) < 2);
    }

    [Fact]
    public async Task ListApps_PrefixWildcardProfileCoversApp()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(5);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.app", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] = [DeviceFixtures.MobileProvision("TEAM123456.com.example.*", expiry)];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));
        Assert.NotNull(app.SignatureExpiresAt);
    }

    [Fact]
    public async Task ListApps_FullWildcardProfileCoversApp()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(3);
        _backend.AppsByUdid["U"] = [new BackendApp("anything.at.all", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] = [DeviceFixtures.MobileProvision("TEAM123456.*", expiry)];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));
        Assert.NotNull(app.SignatureExpiresAt);
    }

    [Fact]
    public async Task ListApps_MultipleProfiles_PicksLatestExpiry()
    {
        DateTimeOffset soon = DateTimeOffset.UtcNow.AddDays(2);
        DateTimeOffset later = DateTimeOffset.UtcNow.AddDays(9);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.app", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] =
        [
            DeviceFixtures.MobileProvision("TEAM123456.com.example.app", soon, "old"),
            DeviceFixtures.MobileProvision("TEAM123456.com.example.app", later, "new"),
        ];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));
        Assert.True(Math.Abs((app.SignatureExpiresAt!.Value - later).TotalSeconds) < 2);
    }

    [Fact]
    public async Task ListApps_NoCoveringProfile_NullExpiry()
    {
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.app", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] =
        [
            DeviceFixtures.MobileProvision("TEAM123456.com.other.thing", DateTimeOffset.UtcNow.AddDays(7)),
        ];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));
        Assert.Null(app.SignatureExpiresAt);
    }

    [Fact]
    public async Task ListApps_UnparseableProfile_SkippedNotFatal()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(7);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.app", "App", "1.0", true)];
        _backend.ProfilesByUdid["U"] =
        [
            "garbage not a plist"u8.ToArray(),
            DeviceFixtures.MobileProvision("TEAM123456.com.example.app", expiry),
        ];

        InstalledApp app = Assert.Single(await Build().ListInstalledAppsAsync("U"));
        Assert.NotNull(app.SignatureExpiresAt); // the good profile still applied
    }

    [Fact]
    public async Task ListApps_OrdersByName()
    {
        _backend.AppsByUdid["U"] =
        [
            new BackendApp("com.z", "Zebra", "1.0", true),
            new BackendApp("com.a", "Apple", "1.0", true),
        ];

        IReadOnlyList<InstalledApp> apps = await Build().ListInstalledAppsAsync("U");
        Assert.Equal("Apple", apps[0].Name);
        Assert.Equal("Zebra", apps[1].Name);
    }

    // --- install validation -----------------------------------------------

    [Fact]
    public async Task Install_ValidIpa_DelegatesToBackend()
    {
        string dir = NewDir();
        try
        {
            string ipa = DeviceFixtures.WriteMinimalIpa(dir, "com.example.installed");
            await Build().InstallAsync("UDID-1", ipa);

            (string Udid, string IpaPath) install = Assert.Single(_backend.Installs);
            Assert.Equal("UDID-1", install.Udid);
            Assert.Equal(ipa, install.IpaPath);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Install_MissingFile_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => Build().InstallAsync("UDID-1", "/no/such/file.ipa"));
        Assert.Empty(_backend.Installs);
    }

    [Fact]
    public async Task Install_NotAnIpa_ThrowsInvalidOperation()
    {
        string dir = NewDir();
        try
        {
            string bogus = Path.Combine(dir, "bogus.ipa");
            File.WriteAllText(bogus, "this is not a zip");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Build().InstallAsync("UDID-1", bogus));
            Assert.Empty(_backend.Installs);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public async Task Install_BackendThrows_Propagates()
    {
        string dir = NewDir();
        try
        {
            string ipa = DeviceFixtures.WriteMinimalIpa(dir);
            _backend.ThrowOnInstall = new InvalidOperationException("device asleep");
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => Build().InstallAsync("UDID-1", ipa));
        }
        finally { Cleanup(dir); }
    }

    private static string NewDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
