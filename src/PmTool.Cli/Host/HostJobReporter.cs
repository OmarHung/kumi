using System.Diagnostics;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Api;

namespace PmTool.Cli.Host;

/// <summary>
/// 一件主機工作的進度回報。和本機代理的 <see cref="JobReporter"/> 不同的地方：每批帶租約（<c>lease</c>）與這批第一則的流水號（<c>firstSeq</c>），
/// 送出去而不確定有沒有到的那一批<b>原封不動</b>重送（伺服器依流水號略過收過的，不會重複；若重新組批，同一個流水號可能對到不同的內容，少一段文字）。
/// 伺服器回的 <c>nextSeq</c> 為準。<c>cancelled</c>、404、409（租約不對）與 401 都表示不用再報了：停下引擎。
/// </summary>
internal sealed class HostJobReporter(
    ApiClient api, Guid jobId, int lease, AgentOptions options, Action onStop) : IEventSink
{
    // 一次請求的內文上限是 1 MB；文字以字元估算，留一半的餘裕。
    private const int MaxBatchCharacters = 400_000;
    private const int EventOverheadCharacters = 300;
    private const int MaxResendRounds = 4;

    /// <summary>連續幾批的內容被拒（4xx）就停下這件工作。</summary>
    private const int MaxRejectionsInARow = 3;

    private readonly object _gate = new();
    private readonly List<AgentEvent> _pending = [];
    private readonly SemaphoreSlim _batchReady = new(0);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Stopwatch _sinceLastSend = Stopwatch.StartNew();
    private int _emitted;
    private int _nextSeq = 1;
    private int _rejectedInARow;
    private List<AgentEvent>? _inflight;
    private int _inflightFirstSeq;

    public void Emit(AgentEvent agentEvent)
    {
        lock (_gate)
        {
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

    /// <summary>背景的定時回報（也是心跳：伺服器 60 秒沒收到就當作結果不明），直到 <paramref name="stop"/> 取消。</summary>
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

    public async Task FlushRemainingAsync(CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await FlushAsync(sendHeartbeat: false, cancellation);
            if (IsDrained())
            {
                return;
            }

            await options.Delay(TimeSpan.FromMilliseconds(500), cancellation);
        }
    }

    private bool IsDrained()
    {
        lock (_gate)
        {
            return _pending.Count == 0 && _inflight is null;
        }
    }

    private async Task FlushAsync(bool sendHeartbeat, CancellationToken cancellation)
    {
        await _sendLock.WaitAsync(cancellation);
        try
        {
            for (var round = 0; round < MaxResendRounds; round++)
            {
                if (_inflight is null && !CutBatch(sendHeartbeat))
                {
                    return;
                }

                if (!await SendInflightAsync(cancellation))
                {
                    return;
                }

                sendHeartbeat = false;
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>把累積的進度切出下一批（最多一個請求的量）；沒有進度又不用心跳時回 false。</summary>
    private bool CutBatch(bool sendHeartbeat)
    {
        List<AgentEvent> taken;
        lock (_gate)
        {
            taken = [.. _pending];
            _pending.Clear();
            _emitted = 0;
            while (_batchReady.CurrentCount > 0 && _batchReady.Wait(0))
            {
                // 信號已經用掉了。
            }
        }

        var pieces = taken.SelectMany(agentEvent => EventSplitting.SplitText(agentEvent, options.MaxTextDelta)).ToList();
        var batch = new List<AgentEvent>();
        var characters = 0;
        var index = 0;
        for (; index < pieces.Count && batch.Count < options.MaxEventsPerReport; index++)
        {
            var size = EventOverheadCharacters + (pieces[index] is TextEvent text ? text.Delta.Length : 0);
            if (batch.Count > 0 && characters + size > MaxBatchCharacters)
            {
                break;
            }

            batch.Add(pieces[index]);
            characters += size;
        }

        if (index < pieces.Count)
        {
            // 放不進這一批的放回最前面，順序不能亂。
            lock (_gate)
            {
                _pending.InsertRange(0, pieces.Skip(index));
            }
        }

        if (batch.Count == 0 && !sendHeartbeat)
        {
            return false;
        }

        _inflight = batch;
        _inflightFirstSeq = _nextSeq;
        _nextSeq += batch.Count;
        return true;
    }

    /// <summary>送出正在送的那一批。回 true 表示可以繼續（送成了）；false 是這一輪到此為止（失敗留著重送，或已經不用報了）。</summary>
    private async Task<bool> SendInflightAsync(CancellationToken cancellation)
    {
        var batch = _inflight!;
        var body = new JsonObject
        {
            ["lease"] = lease,
            ["firstSeq"] = _inflightFirstSeq,
            ["events"] = new JsonArray([.. batch.Select(agentEvent => (JsonNode?)agentEvent.ToJson())]),
        };
        try
        {
            var ack = await api.PostAsync($"host-agent/jobs/{jobId}/events", body, cancellation);
            _sinceLastSend.Restart();
            if (ack.Flag("cancelled"))
            {
                onStop();
            }

            _rejectedInARow = 0;
            return Acknowledge(batch.Count, ack.Number("nextSeq"));
        }
        catch (CliException exception)
        {
            return Classify(exception);
        }
    }

    /// <summary>伺服器的 nextSeq 沒涵蓋整批（只收了一部分）時，只留下沒收的、從 nextSeq 起重送。</summary>
    private bool Acknowledge(int count, long? serverNext)
    {
        var end = _inflightFirstSeq + count;
        if (serverNext is { } next && next >= _inflightFirstSeq && next < end)
        {
            var received = (int)(next - _inflightFirstSeq);
            _inflight = [.. _inflight!.Skip(received)];
            _inflightFirstSeq = (int)next;
            return true;
        }

        _inflight = null;
        return true;
    }

    private bool Classify(CliException exception)
    {
        switch (exception.HttpStatus)
        {
            case 401 or 403 or 404 or 409:
                // 憑證失效、工作不認得了、租約不對（已經被伺服器結束）：繼續跑沒有意義。
                _inflight = null;
                onStop();
                return false;
            case null or >= 500 or 429:
                return false;
            default:
                // 這一批的內容被拒絕：丟掉，流水號退回去，後面的進度才接得上。
                _nextSeq = _inflightFirstSeq;
                _inflight = null;

                // 一直被拒（例如伺服器說流水號對不上）就不是這一批的問題：停下引擎、以失敗結束，不要每一批都白送。
                if (++_rejectedInARow >= MaxRejectionsInARow)
                {
                    onStop();
                }

                return false;
        }
    }
}
