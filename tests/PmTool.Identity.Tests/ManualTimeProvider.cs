namespace PmTool.Identity.Tests;

/// <summary>測試控制的時鐘：時間只在測試要它走的時候才走，工作階段的期限才能不等真的時間就跨過去。</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _utcTicks = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero).UtcTicks;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _utcTicks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _utcTicks, by.Ticks);
}
