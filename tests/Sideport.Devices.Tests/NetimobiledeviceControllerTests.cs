using Microsoft.Extensions.Logging.Abstractions;
using Sideport.Core;

namespace Sideport.Devices.Tests;

public class NetimobiledeviceControllerTests
{
    private readonly FakeDeviceBackend _backend = new();

    private NetimobiledeviceController Build(
        TimeSpan? installedAppsCacheTtl = null,
        TimeProvider? timeProvider = null,
        DevicePairingOwner pairingOwner = DevicePairingOwner.Sideport) =>
        new(
            _backend,
            NullLogger<NetimobiledeviceController>.Instance,
            installedAppsCacheTtl: installedAppsCacheTtl,
            timeProvider: timeProvider,
            pairingOwner: pairingOwner);

    // --- device discovery + dedup -----------------------------------------

    [Theory]
    [InlineData(null, DevicePairingOwner.Sideport)]
    [InlineData("", DevicePairingOwner.Sideport)]
    [InlineData("sideport", DevicePairingOwner.Sideport)]
    [InlineData("HOST", DevicePairingOwner.Host)]
    public void PairingOwnerConfiguration_ParsesSupportedValues(
        string? configured,
        DevicePairingOwner expected) =>
        Assert.Equal(expected, DevicesServiceCollectionExtensions.ParsePairingOwner(configured));

    [Fact]
    public void PairingOwnerConfiguration_RejectsUnknownValue() =>
        Assert.Throws<InvalidOperationException>(() =>
            DevicesServiceCollectionExtensions.ParsePairingOwner("automatic"));

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

    [Fact]
    public void DeviceInfo_FiveArgumentConstruction_DefaultsTrustToUnknown()
    {
        var device = new DeviceInfo("UDID-1", "Phone", "iPhone15,2", "17.0", DeviceConnection.Usb);

        Assert.Equal("unknown", device.TrustState);
        Assert.Null(device.TrustReason);
        Assert.Null(device.LockdownCheckedAt);
        Assert.False(device.UsableForInstall);
    }

    [Fact]
    public async Task ListDevices_MapsPassiveTrustFieldsWithoutPairing()
    {
        DateTimeOffset checkedAt = DateTimeOffset.Parse("2026-07-11T12:00:00Z");
        _backend.Devices.Add(new BackendDevice(
            "UDID-1",
            "Phone",
            "iPhone15,2",
            "17.0",
            DeviceConnection.Usb,
            "trusted",
            "Lockdown session verified over USB.",
            checkedAt,
            UsableForInstall: true));

        DeviceInfo device = Assert.Single(await Build().ListDevicesAsync());

        Assert.Equal("trusted", device.TrustState);
        Assert.Equal(checkedAt, device.LockdownCheckedAt);
        Assert.True(device.UsableForInstall);
        Assert.Equal(0, _backend.PairCalls);
    }

    [Fact]
    public async Task ListConnectedDevices_UsesEnumerationOnlyBackendPath()
    {
        _backend.Devices.Add(new BackendDevice(
            "UDID-1", "", "", "", DeviceConnection.Usb,
            "unknown", "Trust has not been checked."));

        DeviceInfo device = Assert.Single(await Build().ListConnectedDevicesAsync());

        Assert.Equal("UDID-1", device.Udid);
        Assert.Equal(DeviceConnection.Usb, device.Connection);
        Assert.Equal(1, _backend.ListConnectedDevicesCalls);
        Assert.Equal(0, _backend.ListDevicesCalls);
        Assert.Equal(0, _backend.ProbeTrustCalls);
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
            "UDID-1",
            DeviceConnection.Wifi,
            LockdownOk: false,
            Name: null,
            LockdownError: "trust not accepted",
            TrustState: "untrusted",
            TrustReason: "No valid pairing record is available for this iPhone."));

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

    [Fact]
    public async Task Diagnose_DoesNotRequestPairing()
    {
        _backend.DiagnoseProbes.Add(new BackendDeviceProbe(
            "UDID-1",
            DeviceConnection.Usb,
            LockdownOk: false,
            Name: "Phone",
            LockdownError: null,
            TrustState: "untrusted",
            TrustReason: "No valid pairing record is available for this iPhone."));

        await Build().DiagnoseAsync();

        Assert.Equal(0, _backend.PairCalls);
    }

    // --- passive trust + explicit USB pairing -----------------------------

    [Fact]
    public async Task ProbeTrust_AlreadyTrusted_ReturnsObservationWithoutPairing()
    {
        DateTimeOffset checkedAt = DateTimeOffset.Parse("2026-07-11T12:00:00Z");
        _backend.TrustByUdid["UDID-1"] = new DeviceTrustProbe(
            "UDID-1",
            DeviceConnection.Usb,
            "trusted",
            "Lockdown session verified over USB.",
            checkedAt,
            UsableForInstall: true);

        DeviceTrustProbe result = await Build().ProbeTrustAsync("UDID-1");

        Assert.Equal("trusted", result.TrustState);
        Assert.True(result.UsableForInstall);
        Assert.Equal(1, _backend.ProbeTrustCalls);
        Assert.Equal(0, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_UntrustedUsb_InvokesExplicitPairExactlyOnce()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "untrusted");

        DevicePairingResult result = await Build().PairAsync("UDID-1");

        Assert.Equal("trusted", result.TrustState);
        Assert.Equal(1, _backend.ProbeTrustCalls);
        Assert.Equal(1, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_HostOwned_ReturnsTypedHostManagedOutcomeWithoutPairRequest()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "untrusted");

        DevicePairingResult result = await Build(pairingOwner: DevicePairingOwner.Host).PairAsync("UDID-1");

        Assert.Equal(DevicePairingDisposition.HostManaged, result.Disposition);
        Assert.Equal("untrusted", result.TrustState);
        Assert.Equal(1, _backend.ProbeTrustCalls);
        Assert.Equal(0, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_AlreadyTrustedUsb_SkipsPairRequest()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "trusted", usableForInstall: true);

        DevicePairingResult result = await Build().PairAsync("UDID-1");

        Assert.Equal("trusted", result.TrustState);
        Assert.Equal(1, _backend.ProbeTrustCalls);
        Assert.Equal(0, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_WifiDevice_ReturnsUsbRequiredWithoutPairRequest()
    {
        ScriptTrust("UDID-1", DeviceConnection.Wifi, "untrusted");

        DevicePairingResult result = await Build().PairAsync("UDID-1");

        Assert.Equal(DeviceConnection.Wifi, result.Connection);
        Assert.Equal("error", result.TrustState);
        Assert.Contains("USB", result.TrustReason!);
        Assert.Equal(0, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_UserDenial_RemainsUntrustedAndReportsDenied()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "untrusted");
        var progress = new CollectingProgress<DevicePairingProgress>();
        _backend.PairHandler = (udid, sink, _) =>
        {
            sink?.Report(new DevicePairingProgress("denied", "Trust was declined on the iPhone."));
            return Task.FromResult(new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                "untrusted",
                "Trust was declined on the iPhone.",
                DateTimeOffset.UtcNow,
                UsableForInstall: false));
        };

        DevicePairingResult result = await Build().PairAsync("UDID-1", progress);

        Assert.Equal("untrusted", result.TrustState);
        Assert.Contains(progress.Values, value => value.State == "denied");
        Assert.Equal(1, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_LockedDevice_MapsLockedTruthfully()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "untrusted");
        _backend.PairHandler = (udid, sink, _) =>
        {
            sink?.Report(new DevicePairingProgress("locked", "Unlock the iPhone, then try again."));
            return Task.FromResult(new DevicePairingResult(
                udid,
                DeviceConnection.Usb,
                "locked",
                "The iPhone is locked. Unlock it and try again.",
                DateTimeOffset.UtcNow,
                UsableForInstall: false));
        };

        DevicePairingResult result = await Build().PairAsync("UDID-1");

        Assert.Equal("locked", result.TrustState);
        Assert.False(result.UsableForInstall);
        Assert.Equal(1, _backend.PairCalls);
    }

    [Fact]
    public async Task Pair_CallerCancellation_PropagatesAndDoesNotRetry()
    {
        ScriptTrust("UDID-1", DeviceConnection.Usb, "untrusted");
        _backend.PairHandler = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        };
        using var cts = new CancellationTokenSource();

        Task<DevicePairingResult> pairing = Build().PairAsync("UDID-1", ct: cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pairing);
        Assert.Equal(1, _backend.PairCalls);
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

    [Fact]
    public async Task ListApps_WithinCacheTtl_ReusesInstalledAppsSnapshot()
    {
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.one", "One", "1.0", true)];
        NetimobiledeviceController controller = Build(installedAppsCacheTtl: TimeSpan.FromMinutes(5));

        InstalledApp first = Assert.Single(await controller.ListInstalledAppsAsync("U"));
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.two", "Two", "1.0", true)];
        InstalledApp second = Assert.Single(await controller.ListInstalledAppsAsync("U"));

        Assert.Equal("One", first.Name);
        Assert.Equal("One", second.Name);
        Assert.Equal(1, _backend.ListInstalledAppsCalls["U"]);
        Assert.Equal(1, _backend.ListProvisioningProfilesCalls["U"]);
    }

    [Fact]
    public async Task ListApps_FreshRead_BypassesAndReplacesCachedSnapshot()
    {
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.one", "One", "1.0", true)];
        NetimobiledeviceController controller = Build(installedAppsCacheTtl: TimeSpan.FromMinutes(5));

        Assert.Equal("One", Assert.Single(await controller.ListInstalledAppsAsync("U")).Name);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.two", "Two", "2.0", true)];

        Assert.Equal("Two", Assert.Single(await controller.ListInstalledAppsFreshAsync("U")).Name);
        Assert.Equal("Two", Assert.Single(await controller.ListInstalledAppsAsync("U")).Name);
        Assert.Equal(2, _backend.ListInstalledAppsCalls["U"]);
        Assert.Equal(2, _backend.ListProvisioningProfilesCalls["U"]);
    }

    [Fact]
    public async Task ListApps_AfterCacheTtl_ReloadsDeviceInventory()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-06-24T12:00:00Z"));
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.one", "One", "1.0", true)];
        NetimobiledeviceController controller = Build(TimeSpan.FromMinutes(1), clock);

        Assert.Equal("One", Assert.Single(await controller.ListInstalledAppsAsync("U")).Name);
        _backend.AppsByUdid["U"] = [new BackendApp("com.example.two", "Two", "1.0", true)];
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal("Two", Assert.Single(await controller.ListInstalledAppsAsync("U")).Name);
        Assert.Equal(2, _backend.ListInstalledAppsCalls["U"]);
        Assert.Equal(2, _backend.ListProvisioningProfilesCalls["U"]);
    }

    [Fact]
    public async Task Install_Success_InvalidatesInstalledAppsCache()
    {
        string dir = NewDir();
        try
        {
            _backend.AppsByUdid["UDID-1"] = [new BackendApp("com.example.old", "Old", "1.0", true)];
            NetimobiledeviceController controller = Build(installedAppsCacheTtl: TimeSpan.FromMinutes(5));
            Assert.Equal("Old", Assert.Single(await controller.ListInstalledAppsAsync("UDID-1")).Name);

            string ipa = DeviceFixtures.WriteMinimalIpa(dir, "com.example.new");
            await controller.InstallAsync("UDID-1", ipa);
            _backend.AppsByUdid["UDID-1"] = [new BackendApp("com.example.new", "New", "1.0", true)];

            Assert.Equal("New", Assert.Single(await controller.ListInstalledAppsAsync("UDID-1")).Name);
            Assert.Equal(2, _backend.ListInstalledAppsCalls["UDID-1"]);
        }
        finally { Cleanup(dir); }
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

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private void ScriptTrust(
        string udid,
        DeviceConnection connection,
        string trustState,
        bool usableForInstall = false)
    {
        _backend.TrustByUdid[udid] = new DeviceTrustProbe(
            udid,
            connection,
            trustState,
            trustState == "trusted"
                ? $"Lockdown session verified over {connection}."
                : "No valid pairing record is available for this iPhone.",
            DateTimeOffset.Parse("2026-07-11T12:00:00Z"),
            usableForInstall);
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }
}
