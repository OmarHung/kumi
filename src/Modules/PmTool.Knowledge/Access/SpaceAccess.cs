using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;
using PmTool.Work;

namespace PmTool.Knowledge.Access;

/// <summary>
/// 呼叫者看得到的一個空間，以及他在裡面能做什麼。<see cref="IsReadOnly"/> 是空間封存、或它綁的專案封存：
/// 唯讀時 <see cref="SpacePermissions.CanEdit"/> 與 <see cref="SpacePermissions.CanComment"/> 一律是 false。
/// </summary>
internal sealed record SpaceContext(SpaceRow Space, ProjectAccessSummary? Project, SpacePermissions Permissions, bool IsReadOnly)
{
    public SpaceSummary ToSummary() =>
        new(
            Space.Id,
            Space.Key,
            Space.Name,
            Space.Description,
            Project is null ? null : new SpaceProject(Project.ProjectId, Project.Key, Project.Name, Project.IsArchived),
            Space.CreatedAt,
            Space.CreatedBy,
            Space.ArchivedAt,
            Permissions);

    /// <summary>寫入頁面前的檢查：先看唯讀（讓使用者知道是空間唯讀、不是角色不夠），再看權限。</summary>
    public Failure? CheckEdit() =>
        IsReadOnly ? KnowledgeFailures.SpaceArchived(Space.Name)
        : Permissions.CanEdit ? null
        : KnowledgeFailures.Forbidden("你在這個空間沒有新增或修改頁面的權限。");

    /// <summary>改空間本身（名稱、簡介、封存）的檢查。封存的空間也能改，所以不看唯讀。</summary>
    public Failure? CheckManage() =>
        Permissions.CanManage ? null : KnowledgeFailures.Forbidden("你在這個空間沒有管理空間的權限。");
}

/// <summary>
/// 誰看得到哪些空間、能做什麼。規則只有這一份：所有讀寫空間與頁面的路都用它。
/// 組織的角色每次問身分模組、專案的權限每次問工作引擎，不快取。
/// </summary>
internal sealed class SpaceAccess(IPeopleDirectory people, IWorkAccessQueries work)
{
    /// <summary>依代號（不分大小寫）找呼叫者看得到的空間；不存在、在別的組織或看不到都是 null。</summary>
    public async Task<SpaceContext?> FindAsync(KnowledgeDbContext db, Actor actor, string? key, CancellationToken cancellationToken)
    {
        if (!KnowledgeText.TryNormalizeKey(key, out var normalized))
        {
            return null;
        }

        var space = await db.Spaces.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId && s.Key == normalized, cancellationToken);
        return space is null ? null : await ResolveAsync(actor, space, cancellationToken);
    }

    /// <summary>把一個空間算成呼叫者的視角；看不到是 null。</summary>
    public async Task<SpaceContext?> ResolveAsync(Actor actor, SpaceRow space, CancellationToken cancellationToken)
    {
        if (space.ProjectId is not { } projectId)
        {
            return ForOrganizationSpace(actor, space, await RoleOfAsync(actor, cancellationToken));
        }

        var projects = await work.FindVisibleProjectsAsync(actor, [projectId], cancellationToken);
        return projects.TryGetValue(projectId, out var project) ? ForProjectSpace(space, project) : null;
    }

    /// <summary>
    /// 看得到的空間：沒封存的在前，再依名稱（不分大小寫）、代號。綁的專案封存了的算在封存的那一邊。
    /// 身分模組與工作引擎各問一次，不是每個空間各問一次。
    /// </summary>
    public async Task<IReadOnlyList<SpaceContext>> ListAsync(KnowledgeDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var spaces = await db.Spaces.AsNoTracking().Where(s => s.OrganizationId == actor.OrganizationId).ToListAsync(cancellationToken);
        var role = spaces.Any(s => s.ProjectId is null) ? await RoleOfAsync(actor, cancellationToken) : null;
        var boundProjectIds = spaces.Where(s => s.ProjectId is not null).Select(s => s.ProjectId!.Value).Distinct().ToList();

        // 只問空間綁的專案：不替所有看得到的專案算權限。
        var projects = boundProjectIds.Count > 0
            ? await work.FindVisibleProjectsAsync(actor, boundProjectIds, cancellationToken)
            : new Dictionary<Guid, ProjectAccessSummary>();

        var visible = new List<SpaceContext>(spaces.Count);
        foreach (var space in spaces)
        {
            var context = space.ProjectId is { } projectId
                ? (projects.TryGetValue(projectId, out var project) ? ForProjectSpace(space, project) : null)
                : ForOrganizationSpace(actor, space, role);
            if (context is not null)
            {
                visible.Add(context);
            }
        }

        return
        [
            .. visible
                .OrderBy(c => c.IsReadOnly)
                .ThenBy(c => c.Space.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Space.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>組織空間：擁有者、管理員與一般成員看得到、能寫、能留言，訪客與已經不在組織的人看不到；只有組織管理員能管理。</summary>
    internal static SpaceContext? ForOrganizationSpace(Actor actor, SpaceRow space, OrganizationRole? role)
    {
        if (role is null or OrganizationRole.Guest)
        {
            return null;
        }

        var isReadOnly = space.ArchivedAt is not null;
        return new SpaceContext(space, null, new SpacePermissions(!isReadOnly, !isReadOnly, actor.IsOrganizationAdmin), isReadOnly);
    }

    /// <summary>
    /// 專案空間：權限沿用專案裡的（工作引擎已經把組織管理員與封存算進去）。能寫要同時能新增項目、而且看得到全部的項目：
    /// 只看自己項目的角色（內建的「執行者」）看得到整個專案空間，卻不能改它，包括自己寫過的頁面。
    /// </summary>
    internal static SpaceContext ForProjectSpace(SpaceRow space, ProjectAccessSummary project)
    {
        var isReadOnly = space.ArchivedAt is not null || project.IsArchived;
        var permissions = new SpacePermissions(
            !isReadOnly && project.Permissions.Contains(WorkPermissions.ItemCreate) && project.Permissions.Contains(WorkPermissions.ItemRead),
            !isReadOnly && project.Permissions.Contains(WorkPermissions.CommentCreate),
            project.Permissions.Contains(WorkPermissions.ProjectManage));
        return new SpaceContext(space, project, permissions, isReadOnly);
    }

    private async Task<OrganizationRole?> RoleOfAsync(Actor actor, CancellationToken cancellationToken)
    {
        var found = await people.FindManyAsync(actor.OrganizationId, [actor.PersonId], cancellationToken);
        return found.TryGetValue(actor.PersonId, out var person) ? person.Role : null;
    }
}
