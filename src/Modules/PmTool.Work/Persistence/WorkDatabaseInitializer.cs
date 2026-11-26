using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Events;

namespace PmTool.Work.Persistence;

/// <summary>
/// 把 work schema 遷移到最新，再把讀寫權限授予執行的角色。啟動時（Database:MigrateOnStartup）或 migrate 命令執行。
/// 它從根容器解析，所以自己開 DbContext，不依賴請求範圍的服務。
/// </summary>
internal sealed class WorkDatabaseInitializer(WorkDatabase database) : IModuleInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = database.CreateMigrationsContext();
        await db.Database.MigrateAsync(cancellationToken);
        await NormalizeRestoredEventsAsync(db, cancellationToken);
        if (database.Connections.RuntimeRole is not { } role)
        {
            return;
        }

        // 同一個交易：授權做到一半失敗時不留下一半的權限，重跑就好。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var statement in RuntimeRoleGrants.StatementsFor(WorkDbContext.Schema, role, WorkDbContext.MigrationsHistoryTable, [WorkDbContext.EventsTable]))
        {
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 還原到新的資料庫伺服器之後，事件的交易編號比新伺服器的大（<see cref="WorkEventStream"/> 有說明）。讀的人在執行時也會歸一，
    /// 但執行的角色對 events 只能新增與讀（只能追加）：分開角色的部署一定要在這裡用擁有者先做，還原之後跑一次 migrate 就好。
    /// </summary>
    private static async Task NormalizeRestoredEventsAsync(WorkDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Database.SqlQueryRaw<bool>(WorkEventStream.NeedsNormalizingSql).SingleAsync(cancellationToken))
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [WorkEventStream.NormalizeLockKey], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(WorkEventStream.NormalizeSql, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
