namespace PmTool.Abstractions;

/// <summary>
/// 模組的資料表還差哪些遷移。啟動時不遷移（Database:MigrateOnStartup 是 false）的話，組合根用它在啟動時說清楚
/// 「要先跑 migrate」；就緒檢查不必經過它，各模組的 <see cref="IReadinessProbe"/> 自己會看。
/// </summary>
public interface IMigrationStatus
{
    /// <summary>還沒套用的遷移名稱；都套用了是空的。連不上資料庫時照常丟例外。</summary>
    Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken);
}
