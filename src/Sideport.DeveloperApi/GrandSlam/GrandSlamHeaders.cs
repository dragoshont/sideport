using Sideport.Core;

namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// Builds the GrandSlam <c>cpd</c> (client provided data) dictionary and the
/// per-request anisette headers from an <see cref="AnisetteHeaders"/> snapshot.
///
/// The field set and the constant client identifiers are documented GrandSlam
/// protocol facts (reimplemented clean-room from the pypush spec). Sideport
/// emulates the AltServer/Xcode client (<c>iMac11,3</c> + AuthKit/Xcode) so
/// Apple treats it as a developer-tools login.
/// </summary>
internal static class GrandSlamHeaders
{
    // Constant client identifiers (the Xcode-on-Mac emulation pypush documents).
    public const string ClientInfo =
        "<iMac11,3> <Mac OS X;10.15.6;19G2021> <com.apple.AuthKit/1 (com.apple.dt.Xcode/3594.4.19)>";
    private const string AppInfo = "com.apple.gs.xcode.auth";
    private const string XcodeVersion = "11.2 (11B41)";
    private const string RoutingInfo = "17106176";
    private const string SerialNumber = "0";

    /// <summary>
    /// The anisette + device headers attached to a GrandSlam request body's
    /// <c>cpd</c> (and, with <paramref name="includeClientInfo"/>, to the 2FA
    /// HTTP requests).
    /// </summary>
    public static Dictionary<string, object> BuildHeaders(
        AnisetteHeaders anisette, string deviceId, bool includeClientInfo = false)
    {
        // Prefer the device identity the anisette ADI is provisioned for: sending
        // the SAME X-Mme-Device-Id / client-info that anisette (and AltServer)
        // already use means Apple sees a known, trusted device and does not demand
        // a fresh 2FA. The configured deviceId / built-in client-info are the
        // fallback for servers that don't surface them.
        string effectiveDeviceId =
            string.IsNullOrEmpty(anisette.DeviceId) ? deviceId : anisette.DeviceId;
        string effectiveClientInfo =
            string.IsNullOrEmpty(anisette.ClientInfo) ? ClientInfo : anisette.ClientInfo;

        var headers = new Dictionary<string, object>
        {
            ["X-Apple-I-Client-Time"] = FormatTimestamp(anisette.ClientTime),
            ["X-Apple-I-TimeZone"] = "UTC",
            ["loc"] = "en_US",
            ["X-Apple-Locale"] = "en_US",
            ["X-Apple-I-MD"] = anisette.OneTimePassword,
            ["X-Apple-I-MD-LU"] = anisette.LocalUserId,
            ["X-Apple-I-MD-M"] = anisette.MachineId,
            ["X-Apple-I-MD-RINFO"] = string.IsNullOrEmpty(anisette.RoutingInfo)
                ? RoutingInfo
                : anisette.RoutingInfo,
            ["X-Mme-Device-Id"] = effectiveDeviceId,
            ["X-Apple-I-SRL-NO"] = SerialNumber,
        };

        if (includeClientInfo)
        {
            headers["X-Mme-Client-Info"] = effectiveClientInfo;
            headers["X-Apple-App-Info"] = AppInfo;
            headers["X-Xcode-Version"] = XcodeVersion;
        }

        return headers;
    }

    /// <summary>
    /// The <c>cpd</c> dictionary: the AltServer-compatible flags plus the
    /// anisette/device headers.
    /// </summary>
    public static Dictionary<string, object> BuildCpd(AnisetteHeaders anisette, string deviceId)
    {
        var cpd = new Dictionary<string, object>
        {
            ["bootstrap"] = true,
            ["icscrec"] = true,
            ["pbe"] = false,
            ["prkgen"] = true,
            ["svct"] = "iCloud",
        };

        foreach ((string key, object value) in BuildHeaders(anisette, deviceId))
            cpd[key] = value;

        return cpd;
    }

    /// <summary>ISO 8601 second-precision UTC timestamp with a trailing 'Z'.</summary>
    private static string FormatTimestamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss") + "Z";
}
