namespace Sideport.DeveloperApi.GrandSlam;

/// <summary>
/// Configuration for the GrandSlam client. <see cref="DeviceId"/> is the stable
/// <c>X-Mme-Device-Id</c> UUID this Sideport instance presents to Apple; it must
/// persist across restarts (it is part of the device identity Apple correlates
/// with the anisette ADI machine), so it is supplied from configuration rather
/// than regenerated per process.
/// </summary>
public sealed class GrandSlamClientOptions
{
    /// <summary>The stable device UUID (uppercase) sent as <c>X-Mme-Device-Id</c>.</summary>
    public required string DeviceId { get; init; }
}
