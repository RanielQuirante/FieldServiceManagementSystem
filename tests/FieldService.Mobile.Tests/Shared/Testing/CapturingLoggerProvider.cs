using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> _entries = new();

    public IReadOnlyList<CapturedLogEntry> Entries => [.. _entries];

    public ILogger<T> CreateLogger<T>() => new CapturingLogger<T>(_entries);

    public ILogger CreateLogger(string categoryName) => new CategoryLogger(categoryName, _entries);

    public string AllText() =>
        string.Join('\n', Entries.Select(entry => entry.Message + " " + string.Join(' ', entry.Values.Select(pair => $"{pair.Key}={pair.Value}"))));

    public void Dispose()
    {
    }

    private static void Capture<TState>(
        ConcurrentQueue<CapturedLogEntry> entries,
        string category,
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value?.ToString())).ToList()
            : [];
        entries.Enqueue(new CapturedLogEntry(category, logLevel, eventId.Name, formatter(state, exception), values, exception));
    }

    private sealed class CapturingLogger<T>(ConcurrentQueue<CapturedLogEntry> entries) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Capture(entries, typeof(T).Name, logLevel, eventId, state, exception, formatter);
    }

    private sealed class CategoryLogger(string category, ConcurrentQueue<CapturedLogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Capture(entries, category, logLevel, eventId, state, exception, formatter);
    }
}

public sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    string? EventName,
    string Message,
    IReadOnlyList<KeyValuePair<string, string?>> Values,
    Exception? Exception)
{
    public string? Value(string key) => Values.FirstOrDefault(pair => pair.Key == key).Value;
}
