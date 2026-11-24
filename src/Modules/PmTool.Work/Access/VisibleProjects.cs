using PmTool.Abstractions;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>專案連同呼叫者在裡面的角色；不是成員（組織管理員看別人的專案）時角色是 null。</summary>
internal sealed class ProjectWithRole
{
    public required ProjectEntity Project { get; init; }

    public string? Role { get; init; }
}

/// <summary>
/// 專案清單與專案搜尋的「看得到哪些專案」：組織管理員是組織裡全部，其他人是自己是成員的（不論角色，含封存的）。
/// 和 <see cref="ReadableProjects"/> 不同：那份是「讀得到項目」的專案，成員的角色連項目都讀不到時不算。規則只有這一份。
/// </summary>
internal static class VisibleProjects
{
    public static IQueryable<ProjectWithRole> Of(WorkDbContext db, Actor actor)
    {
        var isAdmin = actor.IsOrganizationAdmin;
        return db.Projects
            .Where(p => p.OrganizationId == actor.OrganizationId)
            .Select(p => new ProjectWithRole
            {
                Project = p,
                Role = db.ProjectMembers
                    .Where(m => m.OrganizationId == actor.OrganizationId && m.ProjectId == p.Id && m.PersonId == actor.PersonId)
                    .Select(m => m.Role)
                    .FirstOrDefault(),
            })
            .Where(x => isAdmin || x.Role != null);
    }
}
