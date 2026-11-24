using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>呼叫者看得到的一個專案，以及他在這個專案能做什麼。</summary>
/// <param name="Role">他在專案裡被指派的角色；組織管理員不是成員時是 null。</param>
internal sealed record ProjectAccess(
    ProjectEntity Project, ProjectSchema Schema, string? Role, IReadOnlyList<string> Permissions, Guid PersonId)
{
    /// <summary>封存的專案只留這幾個權限：讀取，以及 project.manage（取消封存、改名、管理成員）。</summary>
    private static readonly IReadOnlyList<string> ArchivedPermissions =
        [WorkPermissions.ItemRead, WorkPermissions.ItemReadOwn, WorkPermissions.BlueprintRead, WorkPermissions.ProjectManage];

    public ProjectSummary Summary =>
        new(Project.Id, Project.Key, Project.Name, Project.CurrentBlueprintVersion, Role, Project.CreatedAt)
        {
            ArchivedAt = Project.ArchivedAt,
        };

    public bool IsArchived => Project.ArchivedAt is not null;

    /// <summary>
    /// 有這個權限。「讀項目」（<see cref="WorkPermissions.ItemRead"/>）是只要讀得到任何項目就算，包含只看自己的角色：
    /// 讀取的入口都用它確認能不能進來，要看哪些項目由 <see cref="Items"/> 決定。
    /// </summary>
    public bool Can(string permission) =>
        Permissions.Contains(permission) || (permission == WorkPermissions.ItemRead && Permissions.Contains(WorkPermissions.ItemReadOwn));

    /// <summary>這個人在這個專案看得到的項目範圍。所有讀項目的路都用它，規則只有這一份。</summary>
    public ItemScope Items => new(
        Permissions.Contains(WorkPermissions.ItemRead) ? ItemScopeKind.All
        : Permissions.Contains(WorkPermissions.ItemReadOwn) ? ItemScopeKind.OwnOnly
        : ItemScopeKind.None,
        PersonId);

    /// <summary>看得到專案裡所有的項目，不只自己的（<see cref="Items"/> 是全部）。</summary>
    public bool ReadsAllItems => Items.Kind == ItemScopeKind.All;

    /// <summary>
    /// 沒有這個權限時的失敗：封存的專案擋下的寫入回 project.archived，讓使用者知道是專案唯讀、不是自己的角色不夠；
    /// 讀取類的權限（封存時還留著）缺的話表示角色本來就沒有，照舊回沒有權限。
    /// </summary>
    public Failure Missing(string permission) =>
        IsArchived && !ArchivedPermissions.Contains(permission)
            ? WorkFailures.ProjectArchived(Project.Name)
            : WorkFailures.MissingPermission(permission);

    /// <summary>組織管理員在任何專案都有全部權限，即使不是成員；其他人看被指派的角色。封存的專案再收斂成唯讀。</summary>
    public static ProjectAccess For(Actor actor, ProjectEntity project, ProjectSchema schema, string? role)
    {
        var granted = actor.IsOrganizationAdmin ? ProjectRoles.AllPermissions : ProjectRoles.PermissionsOf(role, schema);
        return new(project, schema, role, project.ArchivedAt is null ? granted : [.. granted.Where(ArchivedPermissions.Contains)], actor.PersonId);
    }

    /// <summary>
    /// 找呼叫者看得到的專案。不存在、在別的組織、或他不是成員（也不是組織管理員），一律回 null，
    /// 呼叫端用同一個 NotFound 回覆，不透露專案存在。
    /// </summary>
    public static async Task<ProjectAccess?> LoadAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, string? projectKey, CancellationToken cancellationToken)
    {
        if (!ProjectKeys.TryNormalize(projectKey, out var key))
        {
            return null;
        }

        var found = await db.Projects
            .AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.Key == key)
            .Select(p => new
            {
                Project = p,
                Role = db.ProjectMembers
                    .Where(m => m.OrganizationId == actor.OrganizationId && m.ProjectId == p.Id && m.PersonId == actor.PersonId)
                    .Select(m => m.Role)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null || (found.Role is null && !actor.IsOrganizationAdmin))
        {
            return null;
        }

        var schema = await cache.GetAsync(
            db, actor.OrganizationId, found.Project.Id, found.Project.CurrentBlueprintVersion, cancellationToken);
        return For(actor, found.Project, schema, found.Role);
    }
}
