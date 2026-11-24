using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PmTool.Notifications.Tests;

/// <summary>一筆記錄：等級、整句訊息，以及結構化的屬性（不含訊息範本本身）。</summary>
internal sealed record CapturedLog(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);

/// <summary>把模組寫的記錄收起來，測試才看得到有沒有在該記的時候記。</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new Capturing(_entries);

    public void Dispose()
    {
    }

    private sealed class Capturing(ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Where(pair => pair.Key != OriginalFormatKey).ToDictionary(pair => pair.Key, pair => pair.Value)
                : [];
            entries.Enqueue(new CapturedLog(logLevel, formatter(state, exception), properties, exception));
        }
    }
}
