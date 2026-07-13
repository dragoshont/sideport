using System.Collections.Concurrent;

namespace Sideport.Api.AppleAccess;

internal sealed record AppleCredentialRateLimitOptions(
    int ClientPermitLimit,
    TimeSpan ClientWindow,
    int AccountPermitLimit,
    TimeSpan AccountWindow)
{
    public static AppleCredentialRateLimitOptions Default { get; } = new(
        ClientPermitLimit: 12,
        ClientWindow: TimeSpan.FromMinutes(1),
        AccountPermitLimit: 6,
        AccountWindow: TimeSpan.FromMinutes(5));
}

internal readonly record struct AppleCredentialRateLimitDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static AppleCredentialRateLimitDecision Permit() => new(true, TimeSpan.Zero);
    public static AppleCredentialRateLimitDecision Reject(TimeSpan retryAfter) => new(false, retryAfter);
}

/// <summary>
/// Small fixed-window limiter for the credential-establishment surface. It is
/// intentionally local to the Sideport process: ingress-wide limiting remains
/// useful, while this boundary prevents one authenticated client or account
/// from driving unbounded Apple authentication attempts.
/// </summary>
internal sealed class AppleCredentialRateLimiter
{
    private readonly AppleCredentialRateLimitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, WindowCounter> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WindowCounter> _accounts = new(StringComparer.Ordinal);
    private int _sweepCounter;

    public AppleCredentialRateLimiter(
        AppleCredentialRateLimitOptions options,
        TimeProvider? timeProvider = null)
    {
        if (options.ClientPermitLimit <= 0 || options.AccountPermitLimit <= 0 ||
            options.ClientWindow <= TimeSpan.Zero || options.AccountWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Apple credential rate limits must be positive.");
        }
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AppleCredentialRateLimitDecision AcquireClient(string clientKey) =>
        Acquire(_clients, NormalizeKey(clientKey), _options.ClientPermitLimit, _options.ClientWindow);

    public AppleCredentialRateLimitDecision AcquireAccount(string accountProfileId) =>
        Acquire(_accounts, NormalizeKey(accountProfileId), _options.AccountPermitLimit, _options.AccountWindow);

    private AppleCredentialRateLimitDecision Acquire(
        ConcurrentDictionary<string, WindowCounter> counters,
        string key,
        int permitLimit,
        TimeSpan window)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        WindowCounter counter = counters.GetOrAdd(key, _ => new WindowCounter(now));
        AppleCredentialRateLimitDecision decision;
        lock (counter.Gate)
        {
            if (now < counter.StartedAt || now - counter.StartedAt >= window)
            {
                counter.StartedAt = now;
                counter.Permits = 0;
            }

            if (counter.Permits >= permitLimit)
            {
                TimeSpan retryAfter = window - (now - counter.StartedAt);
                decision = AppleCredentialRateLimitDecision.Reject(
                    retryAfter <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : retryAfter);
            }
            else
            {
                counter.Permits++;
                decision = AppleCredentialRateLimitDecision.Permit();
            }
        }

        if ((Interlocked.Increment(ref _sweepCounter) & 0xff) == 0)
            SweepExpired(counters, now, window);
        return decision;
    }

    private static void SweepExpired(
        ConcurrentDictionary<string, WindowCounter> counters,
        DateTimeOffset now,
        TimeSpan window)
    {
        foreach ((string key, WindowCounter counter) in counters)
        {
            lock (counter.Gate)
            {
                if (now >= counter.StartedAt && now - counter.StartedAt >= window + window)
                    counters.TryRemove(new KeyValuePair<string, WindowCounter>(key, counter));
            }
        }
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private sealed class WindowCounter(DateTimeOffset startedAt)
    {
        public object Gate { get; } = new();
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Permits { get; set; }
    }
}

internal static class AppleCredentialOriginPolicy
{
    public static bool IsSameOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var values) || values.Count != 1)
            return false;
        if (!Uri.TryCreate(values[0], UriKind.Absolute, out Uri? origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            return false;
        }

        int requestPort = request.Host.Port ?? DefaultPort(request.Scheme);
        int originPort = origin.IsDefaultPort ? DefaultPort(origin.Scheme) : origin.Port;
        return string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(origin.IdnHost, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
               originPort == requestPort;
    }

    public static bool IsExplicitCrossSite(HttpRequest request) =>
        request.Headers.TryGetValue("Sec-Fetch-Site", out var values) &&
        values.Count == 1 &&
        !string.Equals(values[0], "same-origin", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(values[0], "none", StringComparison.OrdinalIgnoreCase);

    private static int DefaultPort(string scheme) =>
        string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
