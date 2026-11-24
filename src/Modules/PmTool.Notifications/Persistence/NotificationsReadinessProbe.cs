using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Notifications.Persistence;

/// <summary>
/// 就緒檢查從根容器解析，所以是單例、每次從工廠拿新的 DbContext，用執行時的連線。
/// 連不上或還有沒套用的遷移都回 false：資料表對不上程式時不接流量。
/// </summary>
internal sealed class NotificationsReadinessProbe(IDbContextFactory<NotificationsDbContext> contexts)
    : IReadinessProbe, IMigrationStatus
{
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
