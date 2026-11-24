namespace PmTool.Knowledge.Tests;

/// <summary>測試控制的時鐘：時間只在測試要它走的時候才走。「15 分鐘內」這類規則才測得準，也不會因為過了午夜而壞。</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            // 每讀一次前進一毫秒：建立時間與更新時間才比得出先後，也不會有兩次讀到同一個時間。
            var now = _now;
            _now = _now.AddMilliseconds(1);
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
}
