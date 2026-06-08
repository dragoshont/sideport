using Microsoft.Extensions.Logging;

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
            message,
            exception?.GetType().Name,
            exception?.Message);

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