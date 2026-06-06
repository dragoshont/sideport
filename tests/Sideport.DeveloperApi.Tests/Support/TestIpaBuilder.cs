using System.IO.Compression;
using System.Text;
using Claunia.PropertyList;

namespace Sideport.DeveloperApi.Tests.Support;

/// <summary>
/// Builds real IPA fixtures: a zip with the
/// <c>Payload/&lt;App&gt;.app/{Info.plist, [embedded.mobileprovision], &lt;exe&gt;}</c>
/// layout. <see cref="BinaryInfoPlist"/> selects a binary vs XML
/// <c>Info.plist</c> so both decode paths are exercised.
/// </summary>
internal sealed class TestIpaBuilder
{
    public string BundleIdentifier { get; init; } = "com.sideport.test";
    public string ExecutableName { get; init; } = "TestApp";
    public string AppBundleName { get; init; } = "TestApp.app";
    public string? DisplayName { get; init; } = "Test App";
    public string Version { get; init; } = "1.0";
    public string ShortVersion { get; init; } = "1.0.0";
    public bool BinaryInfoPlist { get; init; }
    public byte[]? EmbeddedProfile { get; init; }
    public bool IncludeNestedPlugin { get; init; }

    public byte[] Build()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            string prefix = $"Payload/{AppBundleName}/";

            WriteEntry(zip, prefix + "Info.plist", BuildInfoPlist());
            WriteEntry(zip, prefix + ExecutableName, "MZ-fake-macho"u8.ToArray());

            if (EmbeddedProfile is not null)
                WriteEntry(zip, prefix + "embedded.mobileprovision", EmbeddedProfile);

            if (IncludeNestedPlugin)
            {
                // A nested app/extension that must NOT be mistaken for the top
                // bundle by the inspector.
                string plugin = prefix + "PlugIns/Widget.appex/";
                WriteEntry(zip, plugin + "Info.plist",
                    BuildInfoPlistFor("com.sideport.test.widget", "Widget"));
            }
        }

        return ms.ToArray();
    }

    public string WriteToFile(string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{ExecutableName}.ipa");
        File.WriteAllBytes(path, Build());
        return path;
    }

    private byte[] BuildInfoPlist()
    {
        var dict = new NSDictionary();
        dict.Add("CFBundleIdentifier", BundleIdentifier);
        dict.Add("CFBundleExecutable", ExecutableName);
        if (DisplayName is not null) dict.Add("CFBundleDisplayName", DisplayName);
        dict.Add("CFBundleVersion", Version);
        dict.Add("CFBundleShortVersionString", ShortVersion);

        if (BinaryInfoPlist)
        {
            using var bms = new MemoryStream();
            BinaryPropertyListWriter.Write(bms, dict);
            return bms.ToArray();
        }

        return Encoding.UTF8.GetBytes(dict.ToXmlPropertyList());
    }

    private static byte[] BuildInfoPlistFor(string bundleId, string exe)
    {
        var dict = new NSDictionary();
        dict.Add("CFBundleIdentifier", bundleId);
        dict.Add("CFBundleExecutable", exe);
        return Encoding.UTF8.GetBytes(dict.ToXmlPropertyList());
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using Stream s = entry.Open();
        s.Write(content);
    }
}
