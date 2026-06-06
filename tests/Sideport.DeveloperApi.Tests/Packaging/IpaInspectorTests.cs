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
    public void Inspect_NotAnIpa_Throws()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("random.txt");
        }
        ms.Position = 0;
        Assert.Throws<FormatException>(() => IpaInspector.Inspect(ms));
    }
}
