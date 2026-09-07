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

    /// <summary>Maximum duration of the complete login, including anisette and retries.</summary>
    public TimeSpan AuthenticationTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>Maximum duration of one retryable GrandSlam HTTP attempt.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum duration of one init or app-token exchange, including backoff.</summary>
    public TimeSpan ExchangeTimeout { get; init; } = TimeSpan.FromSeconds(40);

    /// <summary>Initial delay used for bounded exponential retry backoff.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Largest server-requested or client-generated retry delay Sideport accepts.</summary>
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}
