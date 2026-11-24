namespace PmTool.Ai.Tests;

/// <summary>手動撥的時鐘：失敗要隔開時間才算次數，測試不能真的等。</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero).UtcTicks;

    private int _failNextRead;

    public override DateTimeOffset GetUtcNow() =>
        Interlocked.Exchange(ref _failNextRead, 0) == 1
            ? throw new InvalidOperationException("時鐘壞了。")
            : new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>讓下一次讀時間丟例外（只一次）：用來弄壞回覆收尾裡的某一步。</summary>
    public void FailNextRead() => Interlocked.Exchange(ref _failNextRead, 1);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
