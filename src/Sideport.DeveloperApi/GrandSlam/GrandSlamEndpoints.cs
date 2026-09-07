namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// Apple GrandSlam (GSA) endpoint URLs and protocol constants. These are
/// documented protocol facts (the GsService2 surface), reimplemented clean-room
/// from the pypush spec — not translated from AGPL AltSign source.
/// </summary>
internal static class GrandSlamEndpoints
{
    /// <summary>The GrandSlam SRP authentication service.</summary>
    public const string GsService2 = "https://gsa.apple.com/grandslam/GsService2";

    /// <summary>Submit-2FA-code validation endpoint.</summary>
    public const string ValidateCode = "https://gsa.apple.com/grandslam/GsService2/validate";

    /// <summary>Trigger a trusted-device 2FA prompt.</summary>
    public const string TrustedDeviceVerify = "https://gsa.apple.com/auth/verify/trusteddevice";

    /// <summary>The plist content type GsService2 expects and returns.</summary>
    public const string PlistContentType = "text/x-xml-plist";

    /// <summary>The AuthKit/Xcode user agent sent on GrandSlam requests.</summary>
    public const string AuthKitUserAgent =
        "AuthKit/1 (Macintosh; OS X 26.6) (com.apple.dt.Xcode/26.0)";

    /// <summary>The protocol header version sent in every request body.</summary>
    public const string ProtocolVersion = "1.0.1";
}
