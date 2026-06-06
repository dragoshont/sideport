namespace Sideport.Orchestrator;

/// <summary>
/// A registered app Sideport keeps signed on a device. Identifies what to
/// refresh and where its inputs live. Never holds credentials — the Apple ID's
/// password is resolved separately via <see cref="IAppleCredentialProvider"/>.
/// </summary>
public sealed record AppRegistration(
    string BundleId,
    string AppleId,
    string TeamId,
    string DeviceUdid,
    string InputIpaPath)
{
    /// <summary>A stable key for this registration (one app per device).</summary>
    public string Key => $"{DeviceUdid}:{BundleId}";
}
