using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

internal static class ReadableProjects
{
    /// <summary>
    /// 呼叫者看得到的所有專案：組織管理員是組織裡全部，其他人是 <see cref="OfMemberAsync"/>。
    /// 「看得到」的規則只有這一份，專案清單、通知與跨專案搜尋都用它。
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> IdsAsync(WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken)
    {
        if (actor.IsOrganizationAdmin)
        {
            var all = await db.Projects.AsNoTracking().Where(p => p.OrganizationId == actor.OrganizationId).Select(p => p.Id).ToListAsync(cancellationToken);
            return all.ToHashSet();
        }

        return (await OfMemberAsync(db, cache, actor, cancellationToken)).ToHashSet();
    }

    /// <summary>呼叫者是成員、而且角色讀得到項目（全部或只有自己的）的專案（組織管理員另外處理：他們看得到全部）。</summary>
    public static async Task<Guid[]> OfMemberAsync(WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken) =>
        [.. (await MembershipsAsync(db, cache, actor, cancellationToken)).Where(m => ProjectRoles.CanReadItems(m.Role, m.Schema)).Select(m => m.ProjectId)];

    /// <summary>
    /// 呼叫者看得到的沒刪除的項目（組織管理員是全部，其他人依各專案的角色：全部或只有自己的）。
    /// 跨專案要讀項目又不是 <see cref="CrossProjectScopes"/> 那一套（例如搜尋，封存的專案也要找）時，從這裡開始。
    /// </summary>
    public static async Task<IQueryable<ItemEntity>> ItemsAsync(WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken)
    {
        var live = ItemStore.Live(db).AsNoTracking().Where(i => i.OrganizationId == actor.OrganizationId);
        if (actor.IsOrganizationAdmin)
        {
            return live;
        }

        var byProject = await OwnOnlyByProjectAsync(db, cache, actor, cancellationToken);
        return ItemScope.Across(
            live,
            [.. byProject.Where(pair => !pair.Value).Select(pair => pair.Key)],
            [.. byProject.Where(pair => pair.Value).Select(pair => pair.Key)],
            actor.PersonId);
    }

    /// <summary>
    /// 這些項目裡呼叫者現在看得到的（不存在、刪除的不算）。範圍的規則和 <see cref="ItemScope"/> 一樣：
    /// 組織管理員看全部，其他人依專案角色是看全部、或只看負責或建立的。給收件匣這種「已經有一串項目編號」的讀法用。
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> ItemIdsAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        if (itemIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = itemIds.ToArray();
        var rows = await ItemStore.Live(db)
            .AsNoTracking()
            .Where(i => i.OrganizationId == actor.OrganizationId && ids.Contains(i.Id))
            .Select(i => new { i.Id, i.ProjectId, i.AssigneeId, i.CreatedBy })
            .ToListAsync(cancellationToken);
        if (actor.IsOrganizationAdmin)
        {
            return rows.Select(r => r.Id).ToHashSet();
        }

        var memberships = (await MembershipsAsync(db, cache, actor, cancellationToken)).ToDictionary(m => m.ProjectId);
        return rows
            .Where(r => memberships.TryGetValue(r.ProjectId, out var m)
                && (ProjectRoles.ReadsAllItems(m.Role, m.Schema)
                    || (ProjectRoles.CanReadItems(m.Role, m.Schema) && ItemScope.IsOwned(actor.PersonId, r.CreatedBy, r.AssigneeId))))
            .Select(r => r.Id)
            .ToHashSet();
    }

    /// <summary>呼叫者是成員、而且角色讀得到項目的專案，連同範圍：true 是只看自己的（沒有 <see cref="WorkPermissions.ItemRead"/>）。</summary>
    public static async Task<IReadOnlyDictionary<Guid, bool>> OwnOnlyByProjectAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken) =>
        (await MembershipsAsync(db, cache, actor, cancellationToken))
            .Where(m => ProjectRoles.CanReadItems(m.Role, m.Schema))
            .ToDictionary(m => m.ProjectId, m => !ProjectRoles.ReadsAllItems(m.Role, m.Schema));

    /// <summary>只看得到自己項目的專案（<see cref="OwnOnlyByProjectAsync"/> 裡是 true 的）。組織管理員看得到全部，沒有。</summary>
    public static async Task<IReadOnlySet<Guid>> OwnOnlyIdsAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken)
    {
        if (actor.IsOrganizationAdmin)
        {
            return new HashSet<Guid>();
        }

        return (await OwnOnlyByProjectAsync(db, cache, actor, cancellationToken)).Where(pair => pair.Value).Select(pair => pair.Key).ToHashSet();
    }

    private sealed record Membership(Guid ProjectId, string Role, ProjectSchema Schema);

    private static async Task<List<Membership>> MembershipsAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, CancellationToken cancellationToken)
    {
        var rows = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == actor.OrganizationId && m.PersonId == actor.PersonId)
            .Join(db.Projects, m => m.ProjectId, p => p.Id, (m, p) => new { p.Id, p.CurrentBlueprintVersion, m.Role })
            .ToListAsync(cancellationToken);
        var memberships = new List<Membership>(rows.Count);
        foreach (var row in rows)
        {
            // 自訂角色的權限寫在藍圖裡；藍圖有快取，這裡通常不會查資料庫。
            var schema = await cache.GetAsync(db, actor.OrganizationId, row.Id, row.CurrentBlueprintVersion, cancellationToken);
            memberships.Add(new Membership(row.Id, row.Role, schema));
        }

        return memberships;
    }
}
