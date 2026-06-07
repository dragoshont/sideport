namespace Sideport.DeveloperApi.DeveloperServices;

/// <summary>
/// Apple developer-services (<c>developerservices2.apple.com</c>) endpoint URLs,
/// action names, and protocol constants. These are documented protocol facts
/// (the Xcode/Apple developer portal surface), reimplemented clean-room from
/// Apple's documented endpoints — not translated from AGPL AltSign source.
/// </summary>
internal static class DeveloperServicesEndpoints
{
    /// <summary>Base for the plist "action" endpoints (the QH65B2 service).</summary>
    public const string PlistServiceBase = "https://developerservices2.apple.com/services/QH65B2/";

    /// <summary>Base for the JSON (vnd.api+json) services endpoints.</summary>
    public const string JsonServiceBase = "https://developerservices2.apple.com/services/v1/";

    // Plist actions (POST a plist body to PlistServiceBase + action).
    public const string ListTeams = "listTeams.action";
    public const string ListDevices = "ios/listDevices.action";
    public const string AddDevice = "ios/addDevice.action";
    public const string SubmitDevelopmentCsr = "ios/submitDevelopmentCSR.action";
    public const string ListAppIds = "ios/listAppIds.action";
    public const string AddAppId = "ios/addAppId.action";
    public const string DownloadTeamProvisioningProfile = "ios/downloadTeamProvisioningProfile.action";

    /// <summary>Stable client identifier Apple's developer portal expects.</summary>
    public const string ClientId = "XABBG36SBA";

    /// <summary>The protocol version string sent in every plist request body.</summary>
    public const string ProtocolVersion = "QH65B2";

    /// <summary>The plist content type the action endpoints expect and return.</summary>
    public const string PlistContentType = "text/x-xml-plist";

    /// <summary>The JSON content type the services endpoints expect and return.</summary>
    public const string JsonContentType = "application/vnd.api+json";

    /// <summary>The Xcode version string Apple correlates with the client.</summary>
    public const string XcodeVersion = "11.2 (11B41)";

    /// <summary>The app-info identifier for the Xcode-auth client.</summary>
    public const string AppInfo = "com.apple.gs.xcode.auth";

    /// <summary>The platform value for iOS device/profile requests.</summary>
    public const string PlatformIos = "ios";
}
