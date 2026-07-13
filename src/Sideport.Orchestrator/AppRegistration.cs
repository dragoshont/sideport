using System.Text.Json.Serialization;

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
    string InputIpaPath,
    string Lifecycle = "active",
    string? CatalogAppId = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? ActivatedAt = null,
    string? LastVerifiedOperationId = null,
    int? CatalogVersion = null,
    string? CatalogSha256 = null)
{
    /// <summary>A stable key for this registration (one app per device).</summary>
    public string Key => $"{DeviceUdid}:{BundleId}";

    /// <summary>
    /// Pending first installs are durable but must not enter unattended refresh
    /// until Sideport has read the installed app back from the iPhone.
    /// </summary>
    [JsonIgnore]
    public bool IsPendingInstall =>
        string.Equals(Lifecycle, "pending-install", StringComparison.Ordinal);
}
