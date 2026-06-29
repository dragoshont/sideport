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
    string? LastError,
    DateTimeOffset? LastSucceededUtc = null)
{
    /// <summary>Time until the signature expires (null if never refreshed).</summary>
    public TimeSpan? TimeUntilExpiry(DateTimeOffset now) =>
        ExpiresAt is { } e ? e - now : null;

    /// <summary>
    /// Whether this app is due for a proactive refresh: it has never been signed,
    /// it expires within <paramref name="leadTime"/>, or — when
    /// <paramref name="resignInterval"/> is set — its last successful sign is
    /// older than that cadence (keeps a fresh margin, e.g. daily re-signing).
    /// </summary>
    public bool IsDue(DateTimeOffset now, TimeSpan leadTime, TimeSpan? resignInterval = null, TimeSpan? retryBackoff = null)
    {
        // After a failed attempt, hold off until the backoff elapses — otherwise an
        // unreachable device leaves ExpiresAt null and is "due" every tick, hot-looping
        // the signer. Backoff caps the retry rate; it still retries once the window passes.
        if (!LastSucceeded && LastAttemptUtc is { } last && retryBackoff is { } b && now - last < b)
            return false;
        if (ExpiresAt is not { } e || e - now <= leadTime)
            return true;
        if (resignInterval is { } interval && (LastSucceededUtc is not { } signed || now - signed >= interval))
            return true;
        return false;
    }
}
