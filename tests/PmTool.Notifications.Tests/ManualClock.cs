namespace PmTool.Notifications.Tests;

/// <summary>手動撥的時鐘：失敗要隔開時間才算次數，測試不能真的等。</summary>
internal sealed class ManualClock : TimeProvider
{
    private long _ticks = new DateTimeOffset(2026, 11, 18, 8, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
