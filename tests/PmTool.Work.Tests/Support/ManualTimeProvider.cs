namespace PmTool.Work.Tests;

/// <summary>測試控制的時鐘：時間只在測試要它走的時候才走，建立與修改時間才比得出先後。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;
    private TimeSpan _step = TimeSpan.Zero;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            var now = _now;
            _now = _now.Add(_step);
            return now;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now = _now.Add(by);
        }
    }

    /// <summary>
    /// 之後每讀一次時間就自己往前走一步，像真的時鐘一樣不會有兩次讀到同一個時間。
    /// 同時進來的請求在鎖裡讀時間時，讀到的先後就是拿到鎖的先後。
    /// </summary>
    public void AdvanceOnEveryRead(TimeSpan step)
    {
        lock (_gate)
        {
            _step = step;
        }
    }
}
