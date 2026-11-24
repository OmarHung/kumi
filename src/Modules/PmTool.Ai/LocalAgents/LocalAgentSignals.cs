using System.Collections.Concurrent;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 同一個程序內叫醒正在等工作的長輪詢：建立工作時發訊號，等的人立刻去領，不必等下一次問資料庫。
/// 別的 API 程序收進來的工作靠資料庫的備援查詢（<see cref="LocalAgentLimits.ClaimPollInterval"/>）。
/// 等的人先取得「下一次訊號」再查資料庫，訊號發在兩者之間也不會漏掉。
/// </summary>
internal sealed class LocalAgentSignals
{
    private readonly ConcurrentDictionary<Guid, Gate> _gates = new();

    /// <summary>取得這個人的「下一次訊號」；之後發的訊號會讓它完成。</summary>
    public Task Next(Guid personId) => _gates.GetOrAdd(personId, _ => new Gate()).Current.Task;

    public void Signal(Guid personId)
    {
        var gate = _gates.GetOrAdd(personId, _ => new Gate());
        var previous = Interlocked.Exchange(ref gate.Current, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        previous.TrySetResult();
    }

    private sealed class Gate
    {
        public TaskCompletionSource Current = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
