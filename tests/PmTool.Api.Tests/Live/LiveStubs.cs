using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Api.Live;
using PmTool.Work;

namespace PmTool.Api.Tests.Live;

/// <summary>記下送給每個群組的訊息的假 hub；指定的群組會一直卡住（模擬有一條連線不讀資料）。</summary>
internal sealed class FakeHubContext : IHubContext<LiveHub>, IHubClients, IGroupManager
{
    private readonly List<(string Group, string Method, object?[] Arguments)> _sent = [];

    public HashSet<string> HangingGroups { get; } = [];

    public IReadOnlyList<(string Group, string Method, object?[] Arguments)> Sent
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

    public IClientProxy Group(string groupName) => new Proxy(this, groupName);

    public IClientProxy Client(string connectionId) => throw new NotSupportedException();

    public IClientProxy All => throw new NotSupportedException();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    IClientProxy IHubClients<IClientProxy>.Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();

    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

    IClientProxy IHubClients<IClientProxy>.Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

    public IClientProxy User(string userId) => throw new NotSupportedException();

    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private sealed class Proxy(FakeHubContext owner, string group) : IClientProxy
    {
        public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            if (owner.HangingGroups.Contains(group))
            {
                // 就算取消了也不離開：模擬底層的寫入不理會取消。
                await new TaskCompletionSource().Task;
            }

            lock (owner._sent)
            {
                owner._sent.Add((group, method, args));
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
