using System.Security.Cryptography.Pkcs;
using Claunia.PropertyList;
using Sideport.DeveloperApi.Plist;

namespace Sideport.DeveloperApi.Packaging;

/// <summary>
/// Parsed contents of a <c>.mobileprovision</c> provisioning profile.
/// </summary>
public sealed record ProvisioningProfileInfo(
    string Name,
    string? TeamName,
    string? AppIdName,
    DateTimeOffset ExpirationDate,
    IReadOnlyList<string> ProvisionedDeviceIds,
    IReadOnlyList<string> TeamIdentifiers,
    string? ApplicationIdentifier)
{
    /// <summary>Whether the profile has already expired as of <paramref name="now"/>.</summary>
    public bool IsExpired(DateTimeOffset now) => ExpirationDate <= now;

    /// <summary>Time remaining until expiry (negative if already expired).</summary>
    public TimeSpan TimeUntilExpiry(DateTimeOffset now) => ExpirationDate - now;

    /// <summary>
    /// The bundle-id portion of <see cref="ApplicationIdentifier"/> with the
    /// leading team/app-id prefix removed: <c>TEAMID.com.foo.bar</c> →
    /// <c>com.foo.bar</c>, <c>TEAMID.*</c> → <c>*</c>. <see langword="null"/> when
    /// no application-identifier is present.
    /// </summary>
    public string? ProfileBundlePattern
    {
        get
        {
            if (string.IsNullOrEmpty(ApplicationIdentifier))
                return null;
            int dot = ApplicationIdentifier.IndexOf('.');
            return dot < 0 ? ApplicationIdentifier : ApplicationIdentifier[(dot + 1)..];
        }
    }

    /// <summary>
    /// Whether this profile covers <paramref name="bundleId"/>, honoring the
    /// explicit (<c>com.foo.bar</c>), prefix-wildcard (<c>com.foo.*</c>), and
    /// full-wildcard (<c>*</c>) forms of the application-identifier.
    /// </summary>
    public bool CoversBundle(string bundleId)
    {
        string? pattern = ProfileBundlePattern;
        if (string.IsNullOrEmpty(pattern))
            return false;
        if (pattern == "*")
            return true;
        if (pattern.EndsWith(".*", StringComparison.Ordinal))
            return bundleId.StartsWith(pattern[..^1], StringComparison.Ordinal);
        return string.Equals(pattern, bundleId, StringComparison.Ordinal);
    }
}

/// <summary>
/// Reads Apple <c>.mobileprovision</c> files. The file is a CMS/PKCS#7
/// SignedData envelope whose content is the XML property list; we unwrap it with
/// <see cref="SignedCms"/> and fall back to locating the embedded
/// <c>&lt;plist&gt;…&lt;/plist&gt;</c> span if the CMS layer can't be decoded.
/// </summary>
public static class MobileProvision
{
    /// <summary>Extract the embedded property-list bytes from a profile.</summary>
    public static byte[] ExtractPlist(byte[] mobileProvision)
    {
        ArgumentNullException.ThrowIfNull(mobileProvision);

        try
        {
            var cms = new SignedCms();
            cms.Decode(mobileProvision);
            byte[]? content = cms.ContentInfo.Content;
            if (content is { Length: > 0 })
                return content;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Not a (decodable) CMS envelope — fall through to span extraction.
        }

        return ExtractPlistSpan(mobileProvision)
            ?? throw new FormatException("no property list found in mobileprovision");
    }

    /// <summary>Parse a profile into typed <see cref="ProvisioningProfileInfo"/>.</summary>
    public static ProvisioningProfileInfo Parse(byte[] mobileProvision)
    {
        NSDictionary dict = PlistCodec.ParseDictionary(ExtractPlist(mobileProvision));

        string name = PlistCodec.GetStringOrNull(dict, "Name") ?? "";
        string? appIdName = PlistCodec.GetStringOrNull(dict, "AppIDName");
        DateTimeOffset expiration = GetDate(dict, "ExpirationDate");

        string? teamName = PlistCodec.GetStringOrNull(dict, "TeamName");
        IReadOnlyList<string> teamIds = GetStringArray(dict, "TeamIdentifier");
        IReadOnlyList<string> devices = GetStringArray(dict, "ProvisionedDevices");
        string? appIdentifier = GetApplicationIdentifier(dict);

        return new ProvisioningProfileInfo(
            name, teamName, appIdName, expiration, devices, teamIds, appIdentifier);
    }

    private static string? GetApplicationIdentifier(NSDictionary dict)
    {
        if (dict.ContainsKey("Entitlements") && dict["Entitlements"] is NSDictionary entitlements)
            return PlistCodec.GetStringOrNull(entitlements, "application-identifier");
        return null;
    }

    private static byte[]? ExtractPlistSpan(byte[] data)
    {
        ReadOnlySpan<byte> span = data;
        int start = span.IndexOf("<?xml"u8);
        if (start < 0)
            start = span.IndexOf("<plist"u8);
        if (start < 0)
            return null;

        int end = span.IndexOf("</plist>"u8);
        if (end < 0)
            return null;
        end += "</plist>".Length;

        return data[start..end];
    }

    private static DateTimeOffset GetDate(NSDictionary dict, string key)
    {
        if (dict.ContainsKey(key) && dict[key] is NSDate date)
        {
            // plist-cil parses the ISO-8601 'Z' string with DateTimeStyles.None,
            // which yields a Kind=Local DateTime (the 'Z' is converted to local
            // time). Convert back to UTC rather than relabeling the clock value.
            return new DateTimeOffset(date.Date.ToUniversalTime());
        }
        throw new FormatException($"mobileprovision key '{key}' is not a date");
    }

    private static IReadOnlyList<string> GetStringArray(NSDictionary dict, string key)
    {
        if (!dict.ContainsKey(key) || dict[key] is not NSArray array)
            return [];

        var result = new List<string>(array.Count);
        foreach (NSObject item in array)
        {
            string? value = item.ToString();
            if (!string.IsNullOrEmpty(value))
                result.Add(value);
        }
        return result;
    }
}
