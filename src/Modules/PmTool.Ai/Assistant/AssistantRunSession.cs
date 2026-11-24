using System.Threading.Channels;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 一則在背景進行的回覆與讀它的人之間的橋：背景的工作把事件寫進 Channel，讀的人（API）從另一頭讀。
/// 分開的原因是 C# 的 <c>yield return</c> 不能放在有 <c>catch</c> 的 <c>try</c> 裡，而回覆的迴圈一定要接住例外、不管怎麼結束都要存。
/// 讀的人停下來（取消 token 或提早 Dispose）等於按「停止」；DisposeAsync 等背景的工作存完才回。
/// </summary>
internal sealed class AssistantRunSession : IAsyncEnumerable<AssistantRunEvent>
{
    private readonly Channel<AssistantRunEvent> _channel = Channel.CreateUnbounded<AssistantRunEvent>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private readonly CancellationTokenSource _stop = new();
    private int _enumerated;

    public ChannelWriter<AssistantRunEvent> Writer => _channel.Writer;

    public CancellationToken StopToken => _stop.Token;

    /// <summary>背景的工作結束（已經存完、事件都寫進 Channel 了）。</summary>
    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Attach(Task completion) => Completion = completion;

    public void RequestStop()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 背景的工作已經結束、釋放過了：沒有東西要停。
        }
    }

    public IAsyncEnumerator<AssistantRunEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) == 1)
        {
            throw new InvalidOperationException("回覆的事件只能讀一次。");
        }

        return new Enumerator(this, cancellationToken);
    }

    public void Dispose() => _stop.Dispose();

    private sealed class Enumerator : IAsyncEnumerator<AssistantRunEvent>
    {
        private readonly AssistantRunSession _session;
        private readonly CancellationTokenRegistration _registration;

        public Enumerator(AssistantRunSession session, CancellationToken cancellationToken)
        {
            _session = session;
            _registration = cancellationToken.Register(session.RequestStop);
        }

        public AssistantRunEvent Current { get; private set; } = null!;

        public async ValueTask<bool> MoveNextAsync()
        {
            // 不用讀的人的 token 等：取消只是叫背景的工作停下來存檔，最後一個事件（結束）照樣要讀到。
            while (await _session._channel.Reader.WaitToReadAsync(CancellationToken.None))
            {
                if (_session._channel.Reader.TryRead(out var next))
                {
                    Current = next;
                    return true;
                }
            }

            return false;
        }

        public async ValueTask DisposeAsync()
        {
            await _registration.DisposeAsync();
            if (!_session._channel.Reader.Completion.IsCompleted)
            {
                _session.RequestStop();
            }

            await _session.Completion;
        }
    }
}
