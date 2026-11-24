using System.Diagnostics;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// 一件工作的進度回報：先收起來，每 300 毫秒或累積 50 則送一批；沒有新進度時照樣定時送一個空的，
/// 伺服器靠它知道工作還活著，使用者按「停止」也靠它的回應通知本機。
/// 送不出去（網路、伺服器出錯）的留著下一輪再送；權杖失效（401／403）是整個代理的問題，交給 <paramref name="onFatal"/>。
/// </summary>
internal sealed class JobReporter(
    ApiClient api, Guid jobId, AgentOptions options, Action onCancelRequested, Action<CliException> onFatal) : IEventSink
{
    private readonly object _gate = new();
    private readonly List<AgentEvent> _pending = [];
    private readonly SemaphoreSlim _batchReady = new(0);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Stopwatch _sinceLastSend = Stopwatch.StartNew();
    private int _emitted;

    public void Emit(AgentEvent agentEvent)
    {
        lock (_gate)
        {
            // 連續的文字併成一則，一則就是一次送出的量，不會因為逐字串流把 50 則的額度吃光。
            if (agentEvent is TextEvent text && _pending.Count > 0 && _pending[^1] is TextEvent last)
            {
                _pending[^1] = new TextEvent(last.Delta + text.Delta);
            }
            else
            {
                _pending.Add(agentEvent);
            }

            if (++_emitted == options.FlushBatch)
            {
                _batchReady.Release();
            }
        }
    }

    /// <summary>背景的定時回報，直到 <paramref name="stop"/> 取消。</summary>
    public async Task PumpAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await _batchReady.WaitAsync(options.FlushInterval, stop);
                await FlushAsync(sendHeartbeat: _sinceLastSend.Elapsed >= options.HeartbeatInterval, stop);
            }
        }
        catch (OperationCanceledException)
        {
            // 工作結束了：剩下的由 FlushRemainingAsync 送。
        }
    }

    /// <summary>送出剩下的進度（工作結束時）；送不出去就再試幾次，最後一次也失敗就算了，結果還是要送。</summary>
    public async Task FlushRemainingAsync(CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await FlushAsync(sendHeartbeat: false, cancellation);
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }
            }

            await options.Delay(TimeSpan.FromMilliseconds(500), cancellation);
        }
    }

    private async Task FlushAsync(bool sendHeartbeat, CancellationToken cancellation)
    {
        await _sendLock.WaitAsync(cancellation);
        try
        {
            var batches = TakeBatches();
            if (batches.Count == 0 && sendHeartbeat)
            {
                batches = [[]];
            }

            for (var index = 0; index < batches.Count; index++)
            {
                if (!await SendAsync(batches[index], cancellation))
                {
                    // 沒送出去的放回最前面，順序不能亂。
                    Requeue([.. batches.Skip(index).SelectMany(batch => batch)]);
                    return;
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private List<List<AgentEvent>> TakeBatches()
    {
        List<AgentEvent> taken;
        lock (_gate)
        {
            taken = [.. _pending];
            _pending.Clear();
            _emitted = 0;
            DrainSignal();
        }

        var batches = new List<List<AgentEvent>>();
        foreach (var agentEvent in taken.SelectMany(SplitText))
        {
            if (batches.Count == 0 || batches[^1].Count >= options.MaxEventsPerReport)
            {
                batches.Add([]);
            }

            batches[^1].Add(agentEvent);
        }

        return batches;
    }

    private IEnumerable<AgentEvent> SplitText(AgentEvent agentEvent) => EventSplitting.SplitText(agentEvent, options.MaxTextDelta);

    private void Requeue(List<AgentEvent> events)
    {
        lock (_gate)
        {
            _pending.InsertRange(0, events);
        }
    }

    private void DrainSignal()
    {
        while (_batchReady.CurrentCount > 0 && _batchReady.Wait(0))
        {
            // 已經送出去了，之前累積的信號不用再算一次。
        }
    }

    /// <summary>送一批。回 false 表示沒送成、要留著重送；已經不用送了（工作沒了）回 true。</summary>
    private async Task<bool> SendAsync(List<AgentEvent> events, CancellationToken cancellation)
    {
        var body = new JsonObject { ["events"] = new JsonArray([.. events.Select(agentEvent => (JsonNode?)agentEvent.ToJson())]) };
        try
        {
            var ack = await api.PostAsync($"agent/jobs/{jobId}/events", body, cancellation);
            _sinceLastSend.Restart();
            if (ack.Flag("cancelled"))
            {
                onCancelRequested();
            }

            return true;
        }
        catch (CliException exception)
        {
            return Classify(exception);
        }
    }

    private bool Classify(CliException exception)
    {
        switch (exception.HttpStatus)
        {
            case 401 or 403:
                onFatal(exception);
                return true;
            case 404:
                // 伺服器已經不認得這件工作（逾時、被結束）：繼續跑沒有意義。
                onCancelRequested();
                return true;
            case null or >= 500 or 429:
                return false;
            default:
                // 其他 4xx 是這一批的內容被拒絕：重送也一樣，丟掉，不要卡住後面的進度。
                return true;
        }
    }
}
