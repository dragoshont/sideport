using System.Collections.Concurrent;

namespace Sideport.Api.WorkspaceAccess;

internal sealed record WorkspaceLinkRateLimitDecision(bool Allowed, TimeSpan RetryAfter);

internal sealed class WorkspaceLinkRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const int ClientPermitLimit = 20;
    private const int AuthorityPermitLimit = 10;
    private const int MaxCountersPerDimension = 10_000;

    private readonly ConcurrentDictionary<string, Counter> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Counter> _authorities = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Counter> _mintActors = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;

    internal WorkspaceLinkRateLimiter(TimeProvider? timeProvider = null) =>
        _time = timeProvider ?? TimeProvider.System;

    internal WorkspaceLinkRateLimitDecision Acquire(
        string kind,
        string? clientKey,
        string? authorityToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        PruneIfNeeded(_clients, now);
        PruneIfNeeded(_authorities, now);
        WorkspaceLinkRateLimitDecision client = Acquire(
            _clients,
            $"{kind}:{Normalize(clientKey)}",
            ClientPermitLimit,
            now);
        if (!client.Allowed)
            return client;

        string authorityId = ExtractAuthorityId(kind, authorityToken);
        return Acquire(
            _authorities,
            $"{kind}:{authorityId}",
            AuthorityPermitLimit,
            now);
    }

    internal WorkspaceLinkRateLimitDecision AcquireMint(
        string kind,
        string? clientKey,
        string actorKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorKey);
        DateTimeOffset now = _time.GetUtcNow();
        PruneIfNeeded(_clients, now);
        PruneIfNeeded(_mintActors, now);
        WorkspaceLinkRateLimitDecision client = Acquire(
            _clients,
            $"{kind}-mint:{Normalize(clientKey)}",
            ClientPermitLimit,
            now);
        if (!client.Allowed)
            return client;

        return Acquire(
            _mintActors,
            $"{kind}:{actorKey}",
            AuthorityPermitLimit,
            now);
    }

    private static WorkspaceLinkRateLimitDecision Acquire(
        ConcurrentDictionary<string, Counter> counters,
        string key,
        int limit,
        DateTimeOffset now)
    {
        if (counters.Count >= MaxCountersPerDimension && !counters.ContainsKey(key))
            key = "overflow";
        Counter counter = counters.GetOrAdd(key, _ => new Counter(now));
        lock (counter.Gate)
        {
            if (now < counter.StartedAt || now - counter.StartedAt >= Window)
            {
                counter.StartedAt = now;
                counter.Permits = 0;
            }
            if (counter.Permits >= limit)
                return new(false, Max(TimeSpan.Zero, Window - (now - counter.StartedAt)));
            counter.Permits++;
            return new(true, TimeSpan.Zero);
        }
    }

    private static string ExtractAuthorityId(string kind, string? token)
    {
        string expectedPrefix = kind == "invitation" ? "spinv1_" : "spown1_";
        string recordPrefix = kind == "invitation" ? "invitation_" : "owner_claim_";
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(expectedPrefix, StringComparison.Ordinal))
            return "invalid";
        int recordIdLength = recordPrefix.Length + 24;
        int secretSeparator = expectedPrefix.Length + recordIdLength;
        if (token.Length != secretSeparator + 1 + 43 || token[secretSeparator] != '_')
            return "invalid";
        string id = token.Substring(expectedPrefix.Length, recordIdLength);
        return id.StartsWith(recordPrefix, StringComparison.Ordinal) &&
               id[recordPrefix.Length..].All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
               token[(secretSeparator + 1)..].All(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            ? id
            : "invalid";
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static void PruneIfNeeded(
        ConcurrentDictionary<string, Counter> counters,
        DateTimeOffset now)
    {
        if (counters.Count < MaxCountersPerDimension)
            return;
        foreach ((string key, Counter counter) in counters)
        {
            if (now >= counter.StartedAt && now - counter.StartedAt >= Window + Window)
                counters.TryRemove(new KeyValuePair<string, Counter>(key, counter));
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

    private sealed class Counter(DateTimeOffset startedAt)
    {
        internal object Gate { get; } = new();
        internal DateTimeOffset StartedAt { get; set; } = startedAt;
        internal int Permits { get; set; }
    }
}
