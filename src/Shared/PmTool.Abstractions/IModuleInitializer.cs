namespace PmTool.Abstractions;

/// <summary>
/// 模組的資料庫準備，例如把自己的資料表遷移到最新、授權給執行的角色。由組合根依序呼叫：
/// 啟動時在開始接受請求之前（Database:MigrateOnStartup），或 migrate 命令；所以每一步都要可以重跑。
/// </summary>
public interface IModuleInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
