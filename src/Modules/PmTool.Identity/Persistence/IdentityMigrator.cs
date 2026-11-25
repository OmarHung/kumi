using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

/// <summary>
/// 把 identity schema 遷移到最新，再把讀寫權限授予執行的角色。啟動時（Database:MigrateOnStartup）或 migrate 命令執行。
/// 組合根從根容器解析它，所以是單例，每次自己開 DbContext，不依賴 scoped 服務。
/// </summary>
internal sealed class IdentityMigrator(IdentityDatabase database, TimeProvider time, ILogger<IdentityMigrator>? logger = null) : IModuleInitializer
{
    /// <summary>加密金鑰與稽核紀錄只會新增與讀取；執行的角色被攻破時不能改掉或刪掉既有的金鑰。</summary>
    private static readonly string[] AppendOnlyTables = [IdentityDbContext.DataProtectionKeysTable, IdentityDbContext.AuditEventsTable];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var db = database.CreateMigrationsContext();
        await db.Database.MigrateAsync(cancellationToken);
        await DeleteExpiredAuditEventsAsync(db, cancellationToken);
        await NormalizeEmailsAsync(db, cancellationToken);
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

    /// <summary>
    /// 把既有帳號的 Email 改成登入與新增人員用的寫法（<see cref="EmailAddress.TryNormalize"/>：NFKC 加小寫）。
    /// 在 .NET 裡算而不是用 SQL：資料庫的 lower 和 .NET 對少數字元結果不同，本人用原本的寫法就登不進去了。
    /// 用擁有者的連線、一個交易，每次啟動都跑，沒有要改的就什麼都不做。
    /// 新規則下不合格的維持原樣（只記筆數，不記 Email）；改完之後有重複（也包含和不用改的那些重複）就整個不改、丟例外，
    /// 服務起不來，要人工處理，因為該保留哪一個帳號只有人知道。
    /// </summary>
    private async Task NormalizeEmailsAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var accounts = await db.Accounts.AsNoTracking().Select(a => new { a.Id, a.Email }).ToListAsync(cancellationToken);
        var targets = accounts.Select(a => (a.Id, Old: a.Email, New: EmailAddress.TryNormalize(a.Email, out var normalized) ? normalized : null)).ToList();

        var rejected = targets.Count(t => t.New is null);
        if (rejected > 0)
        {
            logger?.LogWarning("有 {Count} 個帳號的 Email 在目前的規則下不合格，維持原樣。", rejected);
        }

        var duplicated = targets.GroupBy(t => t.New ?? t.Old, StringComparer.Ordinal).Count(group => group.Count() > 1);
        if (duplicated > 0)
        {
            throw new InvalidOperationException(
                $"identity.accounts 有 {duplicated} 組 Email 在改成 NFKC 加小寫之後會重複，沒有改任何資料。請先用 SQL 人工修改其中一個帳號的 Email，再重新部署。");
        }

        foreach (var (id, old, normalized) in targets.Where(t => t.New is not null && t.New != t.Old))
        {
            await db.Accounts.Where(a => a.Id == id && a.Email == old)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Email, normalized!), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
