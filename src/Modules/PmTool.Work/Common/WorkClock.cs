namespace PmTool.Work.Common;

internal static class WorkClock
{
    /// <summary>
    /// 現在的 UTC 時刻，截到微秒。PostgreSQL 的 timestamptz 只存到微秒；
    /// 不先截掉的話，新增時回給呼叫端的時間會和之後讀回來的差一點點。
    /// </summary>
    public static DateTimeOffset Now(TimeProvider time)
    {
        var ticks = time.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - (ticks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);
    }

    /// <summary>主鍵用 UUIDv7，時間取自同一個時鐘，依主鍵排序就是建立的順序。</summary>
    public static Guid NewId(TimeProvider time) => Guid.CreateVersion7(time.GetUtcNow());
}
