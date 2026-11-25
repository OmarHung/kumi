using System.Collections.Concurrent;
using PmTool.Abstractions;

namespace PmTool.Identity.Tests;

/// <summary>測試設定的請求來源；預設沒有請求。</summary>
internal sealed class FakeRequestSource : IRequestSource
{
    private volatile RequestSource? _current;

    public RequestSource? Current => _current;

    public void Set(string? address, string? userAgent = null) => _current = new RequestSource(address, userAgent);

    public void Clear() => _current = null;
}

/// <summary>記下收到的帳號事件，測試看得出模組發了什麼。</summary>
internal sealed class RecordingAccountEventSink : IAccountEventSink
{
    private readonly ConcurrentQueue<AccountEvent> _events = new();

    public IReadOnlyList<AccountEvent> Received => [.. _events];

    public Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
    {
        _events.Enqueue(accountEvent);
        return Task.CompletedTask;
    }
}
