namespace PmTool.Work.Tests;

/// <summary>
/// 第一次讀時間時停住，等測試放行才繼續。修改是在鎖裡讀時間的，所以停住的那段時間它一直鎖著那一列、還沒提交，
/// 用來模擬「讀了時間之後拖很久才提交」的修改。讀到的是停住那一刻的時間：真的時鐘也是先讀時間，慢的是後面的事。
/// </summary>
internal sealed class PausingTimeProvider(TimeProvider inner) : TimeProvider
{
    private readonly TaskCompletionSource _paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _resumed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _reads;

    /// <summary>有人讀了時間、正停在那裡。</summary>
    public Task Paused => _paused.Task;

    public void Resume() => _resumed.TrySetResult();

    public override DateTimeOffset GetUtcNow()
    {
        var now = inner.GetUtcNow();
        if (Interlocked.Increment(ref _reads) == 1)
        {
            _paused.TrySetResult();

            // TimeProvider 的介面是同步的，只能在這裡擋住呼叫端的執行緒。
            _resumed.Task.Wait();
        }

        return now;
    }
}
