using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PmTool.Api.Tests.Support;

/// <summary>一筆記錄：等級、類別、整句訊息，以及結構化的屬性（不含訊息範本本身）。</summary>
internal sealed record CapturedLog(
    LogLevel Level, string Category, string Message, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);

/// <summary>把應用程式寫的記錄收起來，測試才看得到有沒有把不該記的東西寫進去。</summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public IReadOnlyList<string> Lines =>
        [.. _entries.Select(entry => $"{entry.Level}|{entry.Category}|{entry.Message}|{entry.Exception}")];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> entries) : ILogger
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
            entries.Enqueue(new CapturedLog(logLevel, category, formatter(state, exception), properties, exception));
        }
    }
}
