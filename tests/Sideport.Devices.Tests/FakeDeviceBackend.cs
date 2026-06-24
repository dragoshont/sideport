using System.IO.Compression;
using System.Text;
using Sideport.Core;

namespace Sideport.Devices.Tests;

/// <summary>A scriptable in-memory <see cref="IDeviceBackend"/> for controller tests.</summary>
internal sealed class FakeDeviceBackend : IDeviceBackend
{
    public List<BackendDevice> Devices { get; } = [];
    public Dictionary<string, List<BackendApp>> AppsByUdid { get; } = [];
    public Dictionary<string, List<byte[]>> ProfilesByUdid { get; } = [];
    public List<(string Udid, string IpaPath)> Installs { get; } = [];
    public Dictionary<string, int> ListInstalledAppsCalls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> ListProvisioningProfilesCalls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Exception? ThrowOnInstall { get; set; }

    public Task<IReadOnlyList<BackendDevice>> ListDevicesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BackendDevice>>(Devices);

    public Task<IReadOnlyList<BackendApp>> ListInstalledAppsAsync(string udid, CancellationToken ct)
    {
        ListInstalledAppsCalls[udid] = ListInstalledAppsCalls.GetValueOrDefault(udid) + 1;
        return Task.FromResult<IReadOnlyList<BackendApp>>(
            AppsByUdid.TryGetValue(udid, out List<BackendApp>? apps) ? apps : []);
    }

    public Task<IReadOnlyList<byte[]>> ListProvisioningProfilesAsync(string udid, CancellationToken ct)
    {
        ListProvisioningProfilesCalls[udid] = ListProvisioningProfilesCalls.GetValueOrDefault(udid) + 1;
        return Task.FromResult<IReadOnlyList<byte[]>>(
            ProfilesByUdid.TryGetValue(udid, out List<byte[]>? p) ? p : []);
    }

    public Task InstallAsync(string udid, string ipaPath, IProgress<int>? progress, CancellationToken ct)
    {
        if (ThrowOnInstall is not null)
            throw ThrowOnInstall;
        Installs.Add((udid, ipaPath));
        return Task.CompletedTask;
    }

    public bool DiagnoseTransportReachable { get; set; } = true;
    public string? DiagnoseTransportError { get; set; }
    public List<BackendDeviceProbe> DiagnoseProbes { get; } = [];

    public Task<BackendDiagnostics> DiagnoseAsync(CancellationToken ct) =>
        Task.FromResult<BackendDiagnostics>(
            new BackendDiagnostics(DiagnoseTransportReachable, DiagnoseTransportError, DiagnoseProbes));
}

/// <summary>
/// Dependency-free builders for test fixtures: a bare-plist
/// <c>.mobileprovision</c> (parsed via MobileProvision's span fallback) and a
/// minimal IPA zip.
/// </summary>
internal static class DeviceFixtures
{
    public static byte[] MobileProvision(
        string applicationIdentifier, DateTimeOffset expiration, string name = "Profile")
    {
        // A minimal Apple XML property list with Entitlements.application-identifier
        // and ExpirationDate — enough for MobileProvision.Parse + CoversBundle.
        string date = expiration.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<plist version=\"1.0\"><dict>" +
            $"<key>Name</key><string>{name}</string>" +
            "<key>TeamIdentifier</key><array><string>TEAM123456</string></array>" +
            "<key>Entitlements</key><dict>" +
            $"<key>application-identifier</key><string>{applicationIdentifier}</string>" +
            "</dict>" +
            $"<key>ExpirationDate</key><date>{date}</date>" +
            "</dict></plist>";
        return Encoding.UTF8.GetBytes(xml);
    }

    public static string WriteMinimalIpa(string directory, string bundleId = "com.example.app", string exe = "App")
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{exe}.ipa");

        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        string prefix = $"Payload/{exe}.app/";

        string info =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<plist version=\"1.0\"><dict>" +
            $"<key>CFBundleIdentifier</key><string>{bundleId}</string>" +
            $"<key>CFBundleExecutable</key><string>{exe}</string>" +
            "</dict></plist>";

        WriteEntry(zip, prefix + "Info.plist", Encoding.UTF8.GetBytes(info));
        WriteEntry(zip, prefix + exe, "MZ"u8.ToArray());
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name);
        using Stream s = entry.Open();
        s.Write(content);
    }
}
