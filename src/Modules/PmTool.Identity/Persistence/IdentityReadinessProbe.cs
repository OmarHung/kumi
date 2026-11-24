using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 就緒檢查從根容器解析，所以和遷移一樣是單例、每次從工廠拿新的 DbContext。
/// 用執行時的連線：它看得到的才是請求實際會用到的。
/// </summary>
internal sealed class IdentityReadinessProbe(IDbContextFactory<IdentityDbContext> contexts) : IReadinessProbe, IMigrationStatus
{
    /// <summary>
    /// 連不上時 <c>CanConnectAsync</c> 回 false 而不是丟例外，就緒檢查才能回 503 而不是 500。
    /// 還有沒套用的遷移也是 false：啟動時不遷移的部署，要等 migrate 跑完才接流量，不然資料表對不上程式。
    /// </summary>
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken)
            && !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
    }

    public async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return [.. await db.Database.GetPendingMigrationsAsync(cancellationToken)];
    }
}
