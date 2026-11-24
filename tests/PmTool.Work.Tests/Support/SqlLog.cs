using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PmTool.Work.Tests;

/// <summary>
/// 收集 EF 送到資料庫的 SQL，用來確認查詢讀了哪些欄位。參數的值不會出現（沒有開敏感資料的記錄）。
/// </summary>
internal sealed class SqlLog : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _commands = new();

    public IReadOnlyList<string> Commands => [.. _commands];

    public void Clear() => _commands.Clear();

    public ILogger CreateLogger(string categoryName) =>
        categoryName == DbLoggerCategory.Database.Command.Name ? new CommandLogger(_commands) : NoLogger.Instance;

    public void Dispose()
    {
    }

    private sealed class CommandLogger(ConcurrentQueue<string> commands) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            commands.Enqueue(formatter(state, exception));
    }

    private sealed class NoLogger : ILogger
    {
        public static readonly NoLogger Instance = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
