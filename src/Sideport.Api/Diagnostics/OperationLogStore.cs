using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Sideport.Api.Diagnostics;

public sealed record OperationLogEntry(
    long Id,
    DateTimeOffset At,
    string Level,
    string Category,
    int EventId,
    string Message,
    string? ExceptionType,
    string? ExceptionMessage);

public sealed class OperationLogStore
{
    private readonly object _gate = new();
    private readonly Queue<OperationLogEntry> _entries = new();
    private long _nextId;

    public OperationLogStore(int capacity = 500)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    public void Add(
        LogLevel level,
        string category,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var entry = new OperationLogEntry(
            Interlocked.Increment(ref _nextId),
            DateTimeOffset.UtcNow,
            level.ToString(),
            category,
            eventId.Id,
            OperationLogSanitizer.Sanitize(message),
            exception?.GetType().Name,
            exception is null ? null : OperationLogSanitizer.Sanitize(exception.Message));

        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }

    public IReadOnlyList<OperationLogEntry> Read(int limit = 100)
    {
        int take = Math.Clamp(limit, 1, Capacity);
        lock (_gate)
            return _entries.Reverse().Take(take).ToArray();
    }
}

internal static partial class OperationLogSanitizer
{
    private const int MaximumLength = 4_096;

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        sanitized = LinkAuthority().Replace(sanitized, "[private-link]");
        sanitized = CommonCredential().Replace(sanitized, "[credential]");
        sanitized = Authorization().Replace(sanitized, "$1[redacted]");
        sanitized = NamedSecret().Replace(sanitized, "$1[redacted]");
        sanitized = Email().Replace(sanitized, "[email]");
        sanitized = OidcActor().Replace(sanitized, "[identity]");
        sanitized = GitHubRepository().Replace(sanitized, "$1[private-repository]");
        sanitized = DeviceUdid().Replace(sanitized, "[device]");
        sanitized = LegacyDeviceUdid().Replace(sanitized, "[device]");
        sanitized = AppleIdentifier().Replace(sanitized, "$1[apple-identifier]");
        sanitized = ProseCertificateSerial().Replace(sanitized, "$1[apple-identifier]");
        sanitized = WindowsPath().Replace(sanitized, "[host-path]");
        sanitized = UnixPath().Replace(sanitized, "$1[host-path]");
        sanitized = RepeatedWhitespace().Replace(sanitized, " ").Trim();
        return sanitized.Length <= MaximumLength
            ? sanitized
            : sanitized[..MaximumLength] + "…";
    }

    [GeneratedRegex(@"\b(?:spinv1|spown1|sphnd1)_[A-Za-z0-9_-]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex LinkAuthority();

    [GeneratedRegex(@"(?i)\b(?:gh[pousr]_[A-Za-z0-9]{16,}|github_pat_[A-Za-z0-9_]{16,}|eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}|[a-z]{4}(?:-[a-z]{4}){3})\b", RegexOptions.CultureInvariant)]
    private static partial Regex CommonCredential();

    [GeneratedRegex(@"(?i)\b(authorization\s*[:=]\s*(?:bearer\s+)?)[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex Authorization();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_])((?:\""?(?:password|passwd|secret|token|access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key)\""?)\s*[:=]\s*[\""']?)[^\s,;\]\}\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex NamedSecret();

    [GeneratedRegex(@"(?i)\b[A-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?(?:\.[A-Z0-9](?:[A-Z0-9-]{0,61}[A-Z0-9])?)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?i)\boidc:[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex OidcActor();

    [GeneratedRegex(@"(?i)(\b(?:github(?:\.com)?|repository|repo)\s*[:=/]\s*)[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubRepository();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])[0-9A-F]{8,}-[0-9A-F-]{16,}(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex DeviceUdid();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])[0-9A-F]{40}(?![A-Za-z0-9])", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyDeviceUdid();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_])((?:\""?(?:apple[_ -]?id|team[_ -]?id|certificate(?:[_ -]?(?:id|serial))?|profile[_ -]?id|key[_ -]?id|issuer[_ -]?id)\""?)\s*[:=]\s*[\""']?)[^\s,;\]\}\""']+", RegexOptions.CultureInvariant)]
    private static partial Regex AppleIdentifier();

    [GeneratedRegex(@"(?i)(\bcertificate\s+serial\s+)[0-9A-F]{8,64}\b", RegexOptions.CultureInvariant)]
    private static partial Regex ProseCertificateSerial();

    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9])[A-Z]:\\(?:[^\s\\]+\\)*[^\s,;]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPath();

    [GeneratedRegex(@"(^|[\s=:])/(?:var|tmp|home|Users|opt|srv|mnt|data|run|etc)(?:/[^\s,;]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex UnixPath();

    [GeneratedRegex(@"\s{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex RepeatedWhitespace();
}

public sealed class OperationLogProvider(OperationLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new OperationLogger(store, categoryName);

    public void Dispose() { }

    private sealed class OperationLogger(OperationLogStore store, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information &&
            logLevel != LogLevel.None &&
            (category.StartsWith("Sideport", StringComparison.Ordinal) || category == "Program");

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message = formatter(state, exception);
            if (string.IsNullOrWhiteSpace(message) && exception is null) return;

            store.Add(logLevel, category, eventId, message, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
