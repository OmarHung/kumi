using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Live;
using PmTool.Work;

namespace PmTool.Api.Tests.Live;

/// <summary>記下送給每條連線的訊息的假 hub；指定的連線會一直卡住（模擬不讀資料）。</summary>
internal sealed class FakeHubContext : IHubContext<LiveHub>, IHubClients, IGroupManager
{
    private readonly List<(string Connection, string Method, object?[] Arguments)> _sent = [];

    public HashSet<string> HangingConnections { get; } = [];

    /// <summary>指定的連線要等這個工作被放行才寫得出去（寫入慢，但不是永遠卡住）。</summary>
    public Dictionary<string, TaskCompletionSource> Gates { get; } = [];

    public IReadOnlyList<(string Connection, string Method, object?[] Arguments)> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public IHubClients Clients => this;

    public IGroupManager Groups => this;

    public IClientProxy Group(string groupName) => throw new NotSupportedException();

    public IClientProxy Client(string connectionId) => new Proxy(this, connectionId);

    public IClientProxy All => throw new NotSupportedException();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

    public IClientProxy User(string userId) => throw new NotSupportedException();

    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

    /// <summary>離開群組、通知 SignalR 的那一刻會叫用；測試用它看當時連線自己記的群組。</summary>
    public Action<string>? OnRemoveFromGroup { get; set; }

    /// <summary>加入群組、通知 SignalR 的那一刻會叫用。</summary>
    public Action<string>? OnAddToGroup { get; set; }

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        OnAddToGroup?.Invoke(groupName);
        return Task.CompletedTask;
    }

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        OnRemoveFromGroup?.Invoke(groupName);
        return Task.CompletedTask;
    }

    private sealed class Proxy(FakeHubContext owner, string connection) : IClientProxy
    {
        public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (owner.HangingConnections.Contains(connection))
            {
                // 就算取消了也不離開：模擬底層的寫入不理會取消。
                await new TaskCompletionSource().Task;
            }

            if (owner.Gates.TryGetValue(connection, out var gate))
            {
                await gate.Task;
            }

            lock (owner._sent)
            {
                owner._sent.Add((connection, method, args));
            }
        }
    }
}

/// <summary>每次讀都從頭的事件流；可以讓下一次讀丟例外，並記下被問了幾次。</summary>
internal sealed class StubEventStream(WorkEventPosition latest) : IWorkEventStream
{
    private readonly List<WorkEvent> _events = [];

    public Exception? FailNextRead { get; set; }

    public int LatestCalls { get; private set; }

    public int ReadCalls { get; private set; }

    public void Add(WorkEvent workEvent) => _events.Add(workEvent);

    public Task<WorkEventPosition> GetLatestPositionAsync(CancellationToken cancellationToken)
    {
        LatestCalls++;
        return Task.FromResult(_events.Count == 0 ? latest : _events[^1].Position);
    }

    public Task<WorkEventPosition> ResolvePositionAsync(WorkEventPosition stored, CancellationToken cancellationToken) => Task.FromResult(stored);

    public Task<IReadOnlyList<WorkEvent>> ReadAfterAsync(WorkEventPosition after, int limit, CancellationToken cancellationToken)
    {
        ReadCalls++;
        if (FailNextRead is { } failure)
        {
            FailNextRead = null;
            throw failure;
        }

        return Task.FromResult<IReadOnlyList<WorkEvent>>([.. _events.Where(e => e.Position.CompareTo(after) > 0).Take(limit)]);
    }
}

internal sealed class StubMigrationStatus(params string[] pending) : IMigrationStatus
{
    public string[] Pending { get; set; } = pending;

    public Exception? FailWith { get; set; }

    public Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken) =>
        FailWith is { } failure ? throw failure : Task.FromResult<IReadOnlyList<string>>(Pending);
}

internal sealed class CapturedLogProvider : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new Capture(_entries);

    public void Dispose()
    {
    }

    private sealed class Capture(List<(LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (entries)
            {
                entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
