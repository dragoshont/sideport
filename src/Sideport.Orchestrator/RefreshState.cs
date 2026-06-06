namespace Sideport.Orchestrator;

/// <summary>
/// The last-known refresh state of a registered app, for countdown display and
/// scheduler decisions.
/// </summary>
public sealed record RefreshState(
    string DeviceUdid,
    string BundleId,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastAttemptUtc,
    bool LastSucceeded,
    string? LastError)
{
    /// <summary>Time until the signature expires (null if never refreshed).</summary>
    public TimeSpan? TimeUntilExpiry(DateTimeOffset now) =>
        ExpiresAt is { } e ? e - now : null;

    /// <summary>
    /// Whether this app is due for a proactive refresh: it has never been signed,
    /// or it expires within <paramref name="leadTime"/>.
    /// </summary>
    public bool IsDue(DateTimeOffset now, TimeSpan leadTime) =>
        ExpiresAt is not { } e || e - now <= leadTime;
}
