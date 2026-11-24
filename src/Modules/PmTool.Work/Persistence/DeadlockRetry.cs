namespace PmTool.Work.Persistence;

/// <summary>
/// 被選為互鎖犧牲者的操作整個重做一次。互鎖時 PostgreSQL 退回其中一邊的交易，那一邊什麼都沒寫；
/// 另一邊接著做完，重做時排在它後面、照它提交後的資料檢查。再互鎖一次就照常丟出，不無限重試。
/// </summary>
internal static class DeadlockRetry
{
    /// <param name="operation">完整的一次操作：自己開 DbContext 與交易，重做時從頭來，不沿用上一次追蹤的狀態。</param>
    public static async Task<T> OnceAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            return await operation();
        }
        catch (Exception exception) when (PostgresErrors.IsDeadlock(exception))
        {
            return await operation();
        }
    }
}
