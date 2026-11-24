using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Work.Persistence;

/// <summary>
/// 資料庫連不連得上、資料表是不是最新的。和初始化一樣從根容器解析，所以用工廠開 DbContext；
/// 用執行時的連線，它看得到的才是請求實際會用到的。
/// </summary>
internal sealed class WorkReadinessProbe(IDbContextFactory<WorkDbContext> contexts) : IReadinessProbe, IMigrationStatus
{
    /// <summary>還有沒套用的遷移也是 false：啟動時不遷移的部署，要等 migrate 跑完才接流量，不然資料表對不上程式。</summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // CanConnectAsync 自己會把連線失敗轉成 false，取消則照常往外丟。
        return await db.Database.CanConnectAsync(cancellationToken)
            && !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
    }
}
