using System.IO.Compression;
using Claunia.PropertyList;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi.Packaging;

/// <summary>
/// Identifying + lifecycle metadata read from an IPA's app bundle.
/// </summary>
public sealed record IpaInfo(
    string BundleIdentifier,
    string? DisplayName,
    string? Version,
    string? ShortVersion,
    string ExecutableName,
    string AppBundleName,
    ProvisioningProfileInfo? Profile)
{
    /// <summary>The signing expiry from the embedded profile, if any.</summary>
    public DateTimeOffset? SignatureExpiresAt => Profile?.ExpirationDate;
}

/// <summary>
/// Reads identifying + signing metadata from an IPA (a zip whose layout is
/// <c>Payload/&lt;App&gt;.app/{Info.plist, embedded.mobileprovision, …}</c>),
/// without extracting or executing anything. <c>Info.plist</c> may be binary or
/// XML; <c>plist-cil</c> handles both. The embedded profile, when present, is
/// parsed via <see cref="MobileProvision"/>.
/// </summary>
public static class IpaInspector
{
    /// <summary>Inspect an IPA on disk.</summary>
    public static IpaInfo Inspect(string ipaPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(ipaPath);
        using FileStream stream = File.OpenRead(ipaPath);
        return Inspect(stream);
    }

    /// <summary>Inspect an IPA from a (seekable) stream.</summary>
    public static IpaInfo Inspect(Stream ipaStream)
    {
        ArgumentNullException.ThrowIfNull(ipaStream);
        using var archive = new ZipArchive(ipaStream, ZipArchiveMode.Read, leaveOpen: true);

        string appPrefix = FindAppBundlePrefix(archive)
            ?? throw new FormatException("not a valid IPA: no Payload/*.app found");

        ZipArchiveEntry infoEntry = archive.GetEntry(appPrefix + "Info.plist")
            ?? throw new FormatException($"IPA missing {appPrefix}Info.plist");

        NSDictionary info = ParseEntry(infoEntry);

        string bundleId = PlistCodec.GetStringOrNull(info, "CFBundleIdentifier")
            ?? throw new FormatException("Info.plist missing CFBundleIdentifier");
        string executable = PlistCodec.GetStringOrNull(info, "CFBundleExecutable")
            ?? throw new FormatException("Info.plist missing CFBundleExecutable");
        string? displayName = PlistCodec.GetStringOrNull(info, "CFBundleDisplayName")
            ?? PlistCodec.GetStringOrNull(info, "CFBundleName");
        string? version = PlistCodec.GetStringOrNull(info, "CFBundleVersion");
        string? shortVersion = PlistCodec.GetStringOrNull(info, "CFBundleShortVersionString");

        string appBundleName = appPrefix.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1];

        ProvisioningProfileInfo? profile = null;
        ZipArchiveEntry? profileEntry = archive.GetEntry(appPrefix + "embedded.mobileprovision");
        if (profileEntry is not null)
        {
            using Stream s = profileEntry.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            profile = MobileProvision.Parse(ms.ToArray());
        }

        return new IpaInfo(bundleId, displayName, version, shortVersion, executable, appBundleName, profile);
    }

    /// <summary>
    /// The <c>Payload/&lt;App&gt;.app/</c> prefix (with trailing slash) of the
    /// single top-level app bundle, or <see langword="null"/> if none.
    /// </summary>
    private static string? FindAppBundlePrefix(ZipArchive archive)
    {
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // Normalize to forward slashes; match Payload/<name>.app/<file>.
            string name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith("Payload/", StringComparison.Ordinal))
                continue;

            int appIdx = name.IndexOf(".app/", StringComparison.Ordinal);
            if (appIdx < 0)
                continue;

            string prefix = name[..(appIdx + ".app/".Length)];
            // Only the top-level app bundle (Payload/X.app/), not nested ones
            // (PlugIns/extensions): exactly one '/' after the .app segment root.
            if (prefix.Count(c => c == '/') == 2)
                return prefix;
        }
        return null;
    }

    private static NSDictionary ParseEntry(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return PlistCodec.ParseDictionary(ms.ToArray());
    }
}
