using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FoodOrdering.UnitTests.Helpers;

public sealed record LogEntry(
    string Category,
    LogLevel Level,
    Exception? Exception,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);

public sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class ListLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Dictionary<string, object?> properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : [];

            entries.Enqueue(new LogEntry(category, logLevel, exception, formatter(state, exception), properties));
        }
    }
}
