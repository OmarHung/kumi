using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.History;

/// <summary>一筆外部編號對到的東西，連同它所在的項目（項目編號已經組好）。</summary>
internal sealed record HistoryRefRow(
    string ExternalId, string Kind, Guid ProjectId, string ProjectKey, Guid ItemId, long ItemNumber, Guid TargetId)
{
    public string ItemKey => ItemKeys.Format(ProjectKey, ItemNumber);
}

/// <summary>外部編號的查詢與寫入前的互斥。</summary>
internal static class HistoryRefs
{
    /// <summary>
    /// 同一個組織、同一個來源的匯入一次只做一個（交易結束才放開）：重跑與同時進來的兩個匯入，
    /// 不會都看到「還沒匯入」而各建一份。唯一索引是最後的防線，這裡讓第二個請求等到第一個提交後再判斷、改成略過。
    /// </summary>
    public static Task SerializeAsync(WorkDbContext db, Guid organizationId, string? source, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"history:{organizationId:D}:{source}");
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    /// <summary>
    /// 寫入前的共同步驟：互斥、對專案取共用鎖再讀藍圖（和新增一樣，套用中的變更集會先做完）、
    /// 要有 project.manage（可以替別人署名、指定過去的時間）、封存的專案擋下。
    /// 看不到的專案和不存在一樣是找不到。
    /// </summary>
    public static async Task<Result<ProjectAccess>> OpenForWriteAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, string projectKey, string? source, CancellationToken cancellationToken)
    {
        await SerializeAsync(db, actor.OrganizationId, source, cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        if (!access.Can(WorkPermissions.ProjectManage))
        {
            return access.Missing(WorkPermissions.ProjectManage);
        }

        return access.IsArchived ? WorkFailures.ProjectArchived(access.Project.Name) : access;
    }

    /// <summary>已經匯入過的：外部編號 → 它對到的東西。沒有的不在結果裡。</summary>
    public static async Task<Dictionary<string, HistoryRefRow>> FindAsync(
        WorkDbContext db, Guid organizationId, string source, HistoryRefKind kind, IEnumerable<string?> externalIds, CancellationToken cancellationToken)
    {
        var ids = externalIds.OfType<string>().Where(id => HistoryRules.ExternalIdProblem(id) is null).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var kindText = HistoryRules.KindText(kind);
        var rows = await db.HistoryRefs
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId && r.Source == source && r.Kind == kindText && ids.Contains(r.ExternalId))
            .Join(db.Items.Where(i => i.OrganizationId == organizationId), r => r.ItemId, i => i.Id, (r, i) => new { Reference = r, i.Number })
            .Join(db.Projects.Where(p => p.OrganizationId == organizationId), x => x.Reference.ProjectId, p => p.Id, (x, p) => new { x.Reference, x.Number, ProjectKey = p.Key })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            x => x.Reference.ExternalId,
            x => new HistoryRefRow(
                x.Reference.ExternalId, x.Reference.Kind, x.Reference.ProjectId, x.ProjectKey, x.Reference.ItemId, x.Number, x.Reference.TargetId),
            StringComparer.Ordinal);
    }

    public static HistoryRefEntity New(
        Actor actor, string source, HistoryRefKind kind, string externalId, Guid projectId, Guid itemId, Guid targetId, DateTimeOffset now) => new()
        {
            OrganizationId = actor.OrganizationId,
            Source = source,
            Kind = HistoryRules.KindText(kind),
            ExternalId = externalId,
            ProjectId = projectId,
            ItemId = itemId,
            TargetId = targetId,
            ImportedBy = actor.PersonId,
            CreatedAt = now,
        };

    /// <summary>這幾個人之中是專案成員的（任何角色）。</summary>
    public static async Task<HashSet<Guid>> MembersAmongAsync(
        WorkDbContext db, ProjectAccess access, IEnumerable<Guid> people, CancellationToken cancellationToken)
    {
        var ids = people.Distinct().ToArray();
        var found = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == access.Project.OrganizationId && m.ProjectId == access.Project.Id && ids.Contains(m.PersonId))
            .Select(m => m.PersonId)
            .ToListAsync(cancellationToken);
        return [.. found];
    }
}
