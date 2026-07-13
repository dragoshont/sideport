using System.IO.Compression;
using System.Text;
using Sideport.DeveloperApi.Packaging;
using Sideport.DeveloperApi.Tests.Support;

namespace Sideport.DeveloperApi.Tests.Packaging;

public class IpaInspectorTests
{
    [Fact]
    public void Inspect_XmlInfoPlist_ReadsIdentity()
    {
        byte[] ipa = new TestIpaBuilder
        {
            BundleIdentifier = "com.example.diceroll",
            ExecutableName = "DiceRoll",
            AppBundleName = "DiceRoll.app",
            DisplayName = "Dice Roll",
            Version = "12",
            ShortVersion = "1.2.0",
        }.Build();

        IpaInfo info = IpaInspector.Inspect(new MemoryStream(ipa));

        Assert.Equal("com.example.diceroll", info.BundleIdentifier);
        Assert.Equal("DiceRoll", info.ExecutableName);
        Assert.Equal("DiceRoll.app", info.AppBundleName);
        Assert.Equal("Dice Roll", info.DisplayName);
        Assert.Equal("12", info.Version);
        Assert.Equal("1.2.0", info.ShortVersion);
        Assert.Null(info.Profile);
        Assert.Null(info.SignatureExpiresAt);
    }

    [Fact]
    public void Inspect_BinaryInfoPlist_ReadsIdentity()
    {
        byte[] ipa = new TestIpaBuilder
        {
            BundleIdentifier = "com.example.binary",
            BinaryInfoPlist = true,
        }.Build();

        IpaInfo info = IpaInspector.Inspect(new MemoryStream(ipa));
        Assert.Equal("com.example.binary", info.BundleIdentifier);
    }

    [Fact]
    public void Inspect_WithEmbeddedProfile_SurfacesExpiry()
    {
        DateTimeOffset expiry = DateTimeOffset.UtcNow.AddDays(7);
        byte[] profile = TestMobileProvisionBuilder.Build(expiration: expiry);
        byte[] ipa = new TestIpaBuilder { EmbeddedProfile = profile }.Build();

        IpaInfo info = IpaInspector.Inspect(new MemoryStream(ipa));

        Assert.NotNull(info.Profile);
        Assert.NotNull(info.SignatureExpiresAt);
        // Round-trip through plist date loses sub-second precision; compare loosely.
        Assert.True(Math.Abs((info.SignatureExpiresAt!.Value - expiry).TotalSeconds) < 2);
    }

    [Fact]
    public void Inspect_NestedPlugin_PicksTopLevelBundle()
    {
        byte[] ipa = new TestIpaBuilder
        {
            BundleIdentifier = "com.example.host",
            IncludeNestedPlugin = true,
        }.Build();

        IpaInfo info = IpaInspector.Inspect(new MemoryStream(ipa));
        Assert.Equal("com.example.host", info.BundleIdentifier);
    }

    [Fact]
    public void Inspect_OnDisk_Works()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-ipa-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = new TestIpaBuilder { BundleIdentifier = "com.example.ondisk" }.WriteToFile(dir);
            IpaInfo info = IpaInspector.Inspect(path);
            Assert.Equal("com.example.ondisk", info.BundleIdentifier);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExtractIconPng_ReturnsOnlyValidBoundedPng()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sideport-ipa-icon-" + Guid.NewGuid().ToString("N"));
        try
        {
            byte[] png = new byte[512];
            byte[] header = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 60, 0, 0, 0, 60];
            header.CopyTo(png, 0);
            new Random(42).NextBytes(png.AsSpan(header.Length));
            string valid = new TestIpaBuilder { AppIconPng = png }.WriteToFile(dir);
            Assert.Equal(png, IpaInspector.ExtractIconPng(valid));

            string invalidDir = Path.Combine(dir, "invalid");
            string invalid = new TestIpaBuilder { AppIconPng = "not-a-png"u8.ToArray() }.WriteToFile(invalidDir);
            Assert.Null(IpaInspector.ExtractIconPng(invalid));

            string oversizedDir = Path.Combine(dir, "oversized");
            byte[] oversized = new byte[4 * 1024 * 1024 + 1];
            png.CopyTo(oversized, 0);
            string oversizedPath = new TestIpaBuilder { AppIconPng = oversized }.WriteToFile(oversizedDir);
            Assert.Throws<InvalidDataException>(() => IpaInspector.ExtractIconPng(oversizedPath));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Inspect_NotAnIpa_Throws()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("random.txt");
        }
        ms.Position = 0;
        Assert.Throws<FormatException>(() => IpaInspector.Inspect(ms));
    }

    [Fact]
    public void Inspect_TooManyEntries_RejectsBeforeReadingTargets()
    {
        byte[] ipa = BuildArchive(
            ("Payload/Test.app/Info.plist", ValidInfoPlist()),
            ("Payload/Test.app/resource.bin", [1]));
        IpaInspectionLimits limits = IpaInspectionLimits.Default with { MaxEntryCount = 1 };

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa), limits));

        Assert.Contains("ZIP entries", error.Message);
    }

    [Fact]
    public void Inspect_OversizedTargetEntry_IsRejected()
    {
        byte[] ipa = BuildArchive(("Payload/Test.app/Info.plist", ValidInfoPlist()));
        IpaInspectionLimits limits = IpaInspectionLimits.Default with { MaxTargetEntryBytes = 32 };

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa), limits));

        Assert.Contains("Info.plist exceeds", error.Message);
    }

    [Fact]
    public void Inspect_AggregateUncompressedLimit_IsEnforced()
    {
        byte[] info = ValidInfoPlist();
        byte[] ipa = BuildArchive(
            ("Payload/Test.app/Info.plist", info),
            ("Payload/Test.app/resource.bin", new byte[64]));
        IpaInspectionLimits limits = IpaInspectionLimits.Default with
        {
            MaxTargetEntryBytes = info.Length,
            MaxTotalUncompressedBytes = info.Length,
        };

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa), limits));

        Assert.Contains("expands beyond", error.Message);
    }

    [Fact]
    public void Inspect_HighCompressionRatio_IsRejectedByDefault()
    {
        byte[] ipa = BuildArchive(
            ("Payload/Test.app/Info.plist", ValidInfoPlist()),
            ("Payload/Test.app/compressed-resource.bin", new byte[1_000_000]));

        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa)));

        Assert.Contains("compression ratio", error.Message);
    }

    [Theory]
    [InlineData("Payload/Test.app/../../escape")]
    [InlineData("Payload\\Test.app\\..\\escape")]
    [InlineData("/Payload/Test.app/escape")]
    [InlineData("C:/Payload/Test.app/escape")]
    public void Inspect_TraversingOrRootedEntry_IsRejected(string maliciousPath)
    {
        byte[] ipa = BuildArchive(
            ("Payload/Test.app/Info.plist", ValidInfoPlist()),
            (maliciousPath, [1]));

        FormatException error = Assert.Throws<FormatException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa)));

        Assert.Contains("ZIP entry path", error.Message);
    }

    [Theory]
    [InlineData("Info.plist")]
    [InlineData("embedded.mobileprovision")]
    public void Inspect_DuplicateInspectedTarget_IsRejected(string target)
    {
        var entries = new List<(string Name, byte[] Content)>
        {
            ("Payload/Test.app/Info.plist", ValidInfoPlist()),
            ($"Payload/Test.app/{target}", [1]),
            ($"Payload/Test.app/{target}", [2]),
        };
        if (target == "Info.plist")
            entries.RemoveAt(0);
        byte[] ipa = BuildArchive([.. entries]);

        FormatException error = Assert.Throws<FormatException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa)));

        Assert.Contains("duplicate", error.Message);
    }

    [Fact]
    public void Inspect_MultipleTopLevelApps_IsRejected()
    {
        byte[] ipa = BuildArchive(
            ("Payload/First.app/Info.plist", ValidInfoPlist("com.example.first")),
            ("Payload/Second.app/Info.plist", ValidInfoPlist("com.example.second")));

        FormatException error = Assert.Throws<FormatException>(
            () => IpaInspector.Inspect(new MemoryStream(ipa)));

        Assert.Contains("multiple top-level", error.Message);
    }

    private static byte[] BuildArchive(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using Stream target = entry.Open();
                target.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] ValidInfoPlist(string bundleId = "com.example.test") =>
        Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0"><dict>
              <key>CFBundleIdentifier</key><string>{bundleId}</string>
              <key>CFBundleExecutable</key><string>Test</string>
            </dict></plist>
            """);
}
