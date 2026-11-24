using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Notifications.Persistence;

/// <summary>
/// 把 notifications schema 遷移到最新，再把讀寫權限授予執行的角色。啟動時（Database:MigrateOnStartup）或 migrate 命令執行。
/// 從根容器解析，所以自己開 DbContext，不依賴請求範圍的服務。
/// </summary>
internal sealed class NotificationsMigrator(NotificationsDatabase database) : IModuleInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = database.CreateMigrationsContext();
        await db.Database.MigrateAsync(cancellationToken);
        if (database.Connections.RuntimeRole is not { } role)
        {
            return;
        }

        // 同一個交易：授權做到一半失敗時不留下一半的權限，重跑就好。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in RuntimeRoleGrants.StatementsFor(
            NotificationsDbContext.Schema, role, NotificationsDbContext.MigrationsHistoryTable))
        {
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
