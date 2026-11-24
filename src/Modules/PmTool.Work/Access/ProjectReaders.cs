using Microsoft.EntityFrameworkCore;
using PmTool.Work.Blueprints;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>
/// 現在讀得到專案的成員：角色讀得到項目（全部或只有自己的，自訂角色看藍圖）。
/// 組織的擁有者與管理員不是專案成員的話，工作引擎不知道他們是誰（組織是身分模組的事），這裡不算。
/// </summary>
internal static class ProjectReaders
{
    /// <summary>這些人裡面讀得到專案的有誰。一次查完，不逐一查。</summary>
    public static async Task<IReadOnlySet<Guid>> FilterAsync(
        WorkDbContext db,
        Guid organizationId,
        Guid projectId,
        ProjectSchema schema,
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var members = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.ProjectId == projectId && personIds.Contains(m.PersonId))
            .Select(m => new { m.PersonId, m.Role })
            .ToListAsync(cancellationToken);
        return members
            .Where(m => ProjectRoles.CanReadItems(m.Role, schema))
            .Select(m => m.PersonId)
            .ToHashSet();
    }

    /// <summary>
    /// 這些人裡面現在看得到這個項目的有誰：讀得到專案，而且角色讀得到全部項目、或只看自己的而項目是他負責或建立的。
    /// 和 <see cref="ItemScope"/> 同一個規則：「自己的」就是 <see cref="ItemScope.Owned"/>，這裡只是拿一個項目對多個人。
    /// </summary>
    public static async Task<IReadOnlySet<Guid>> FilterItemAsync(
        WorkDbContext db,
        Guid organizationId,
        Guid projectId,
        ProjectSchema schema,
        ItemOwnership item,
        IReadOnlyCollection<Guid> personIds,
        CancellationToken cancellationToken)
    {
        if (personIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var members = await db.ProjectMembers
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.ProjectId == projectId && personIds.Contains(m.PersonId))
            .Select(m => new { m.PersonId, m.Role })
            .ToListAsync(cancellationToken);
        return members
            .Where(m => ProjectRoles.ReadsAllItems(m.Role, schema)
                || (ProjectRoles.CanReadItems(m.Role, schema) && ItemScope.IsOwned(m.PersonId, item.CreatedBy, item.AssigneeId)))
            .Select(m => m.PersonId)
            .ToHashSet();
    }
}
