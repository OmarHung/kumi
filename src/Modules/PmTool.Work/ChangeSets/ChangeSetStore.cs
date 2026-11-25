using Microsoft.EntityFrameworkCore;
using PmTool.Work.Persistence;

namespace PmTool.Work.ChangeSets;

/// <summary>變更集資料表的讀寫。每個查詢都帶組織與專案。</summary>
internal static class ChangeSetStore
{
    public const int MaxListed = 100;

    public static Task<ChangeSetEntity?> FindAsync(WorkDbContext db, ProjectEntity project, long number, CancellationToken cancellationToken) =>
        db.ChangeSets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.OrganizationId == project.OrganizationId && c.ProjectId == project.Id && c.Number == number, cancellationToken);

    /// <summary>鎖住這一份直到交易結束：同時進來的拒絕會等套用做完，再發現它已經不是等待核准。</summary>
    public static async Task<ChangeSetEntity?> LockAsync(WorkDbContext db, ProjectEntity project, long number, CancellationToken cancellationToken)
    {
        var rows = await db.ChangeSets
            .FromSql($"""
                SELECT * FROM work.changesets
                WHERE organization_id = {project.OrganizationId} AND project_id = {project.Id} AND number = {number}
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>只讀清單要的欄位：每一份的全文是整份藍圖，一百份讀進來只為了丟掉。</summary>
    public static async Task<ChangeSetSummary[]> ListAsync(WorkDbContext db, ProjectEntity project, CancellationToken cancellationToken)
    {
        var rows = await db.ChangeSets
            .AsNoTracking()
            .Where(c => c.OrganizationId == project.OrganizationId && c.ProjectId == project.Id)
            .OrderByDescending(c => c.Number)
            .Take(MaxListed)
            .Select(c => new { c.Number, c.Status, c.BaseVersion, c.Summary, c.ProposedBy, c.ProposedAt, c.DecidedBy, c.DecidedAt, c.AppliedVersion, c.ProposedVia })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(r => new ChangeSetSummary(r.Number, ChangeSetRecords.ParseStatus(r.Status), r.BaseVersion, r.Summary, r.ProposedBy, r.ProposedAt)
        {
            DecidedBy = r.DecidedBy,
            DecidedAt = r.DecidedAt,
            AppliedVersion = r.AppliedVersion,
            Origin = ChangeSetRecords.ParseOrigin(r.ProposedVia),
        })];
    }

    /// <summary>和項目的流水號一樣：在交易裡先加一再讀回來，同時送出的會排隊，失敗時號碼跟著退回。</summary>
    public static async Task<long> NextNumberAsync(WorkDbContext db, ProjectEntity project, CancellationToken cancellationToken)
    {
        await db.Projects
            .Where(p => p.OrganizationId == project.OrganizationId && p.Id == project.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.LastChangeSetNumber, p => p.LastChangeSetNumber + 1), cancellationToken);
        return await db.Projects
            .Where(p => p.OrganizationId == project.OrganizationId && p.Id == project.Id)
            .Select(p => p.LastChangeSetNumber)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// 還在等待核准、基準版本也是目前版本的數量。基準過期的雖然還標著等待中，下一次讀取就會標成過期，不算進上限。
    /// 呼叫端已經在發號時鎖住專案那一列。
    /// </summary>
    public static Task<int> CountPendingAsync(WorkDbContext db, ProjectEntity project, CancellationToken cancellationToken) =>
        db.ChangeSets.CountAsync(
            c => c.OrganizationId == project.OrganizationId
                && c.ProjectId == project.Id
                && c.Status == ChangeSetRecords.Pending
                && c.BaseVersion == project.CurrentBlueprintVersion,
            cancellationToken);

    /// <summary>
    /// 基準版本比 <paramref name="currentVersion"/> 舊的等待核准變更集標成過期。版本只會往上加，
    /// 所以比一個已經提交的版本舊，就一定過期了；讀到的版本稍舊也不會把剛送出的新變更集誤標。
    /// </summary>
    public static Task ExpireStaleAsync(
        WorkDbContext db, ProjectEntity project, long currentVersion, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.ChangeSets
            .Where(c => c.OrganizationId == project.OrganizationId
                && c.ProjectId == project.Id
                && c.Status == ChangeSetRecords.Pending
                && c.BaseVersion < currentVersion)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.Status, ChangeSetRecords.Expired).SetProperty(c => c.DecidedAt, now),
                cancellationToken);

    /// <summary>套用之後，專案其他等待核准的變更集都對照著舊的版本，一起標成過期。</summary>
    public static Task ExpireOthersAsync(
        WorkDbContext db, ProjectEntity project, Guid appliedId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.ChangeSets
            .Where(c => c.OrganizationId == project.OrganizationId
                && c.ProjectId == project.Id
                && c.Status == ChangeSetRecords.Pending
                && c.Id != appliedId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.Status, ChangeSetRecords.Expired).SetProperty(c => c.DecidedAt, now),
                cancellationToken);

    /// <summary>
    /// 等待核准的這一份改成 <paramref name="status"/>。只改還在等待核准的：同時有人套用或拒絕時，
    /// 後到的會等前一個提交，再發現它已經不是等待核准，回 false。
    /// </summary>
    public static async Task<bool> DecideAsync(
        WorkDbContext db, ChangeSetEntity changeSet, string status, Guid? decidedBy, DateTimeOffset now, long? appliedVersion, CancellationToken cancellationToken)
    {
        var updated = await db.ChangeSets
            .Where(c => c.OrganizationId == changeSet.OrganizationId && c.Id == changeSet.Id && c.Status == ChangeSetRecords.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, status)
                    .SetProperty(c => c.DecidedBy, decidedBy)
                    .SetProperty(c => c.DecidedAt, now)
                    .SetProperty(c => c.AppliedVersion, appliedVersion),
                cancellationToken);
        return updated == 1;
    }
}
