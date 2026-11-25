using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 把 identity schema 遷移到最新，再把讀寫權限授予執行的角色。啟動時（Database:MigrateOnStartup）或 migrate 命令執行。
/// 組合根從根容器解析它，所以是單例，每次自己開 DbContext，不依賴 scoped 服務。
/// </summary>
internal sealed class IdentityMigrator(IdentityDatabase database, TimeProvider time) : IModuleInitializer
{
    /// <summary>加密金鑰與稽核紀錄只會新增與讀取；執行的角色被攻破時不能改掉或刪掉既有的金鑰。</summary>
    private static readonly string[] AppendOnlyTables = [IdentityDbContext.DataProtectionKeysTable, IdentityDbContext.AuditEventsTable];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = database.CreateMigrationsContext();
        await db.Database.MigrateAsync(cancellationToken);
        await DeleteExpiredAuditEventsAsync(db, cancellationToken);
        if (database.Connections.RuntimeRole is not { } role)
        {
            return;
        }

        // 同一個交易：授權做到一半失敗時不留下一半的權限，重跑就好。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in RuntimeRoleGrants.StatementsFor(IdentityDbContext.Schema, role, IdentityDbContext.MigrationsHistoryTable, AppendOnlyTables))
        {
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 稽核紀錄只能新增，執行的角色刪不掉，所以保存期限的清理在這裡用擁有者的連線做；
    /// 雲端關掉啟動時遷移時，每次部署的 migrate 命令就是一次清理。
    /// </summary>
    private async Task DeleteExpiredAuditEventsAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow().AddDays(-AuditRules.RetentionDays);
        await db.AuditEvents.Where(e => e.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
