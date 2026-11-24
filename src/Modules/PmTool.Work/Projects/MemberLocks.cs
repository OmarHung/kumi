using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Projects;

/// <summary>
/// 鎖住之後讀到的成員：要動的那個人（不是成員時是 null），與專案目前有幾位 admin。
/// </summary>
internal sealed record LockedMembers(ProjectMemberEntity? Target, int AdminCount)
{
    /// <summary>這次的修改會不會讓專案沒有 admin：只有動到的人正是唯一的 admin 才會。</summary>
    public bool RemovesLastAdmin => Target?.Role == ProjectRoles.Admin && AdminCount == 1;
}

/// <summary>
/// 不是組織管理員的人不能讓專案沒有 admin。移除與換角色都先用同一個陳述式鎖住這個專案所有的 admin 與要動的那個人，依編號的順序，
/// 再數還剩幾位：同時移除兩位 admin 時，後到的要等先到的提交，看得到少了一位，不會兩個都以為還有別人。
/// 每個操作都依同一個順序鎖，彼此不會卡死。
/// </summary>
internal static class MemberLocks
{
    /// <summary>
    /// 最後一位 admin 的規則只擋不是組織管理員的人。組織管理員在每個專案都有全部權限，專案沒有 admin 也管得到；
    /// 接案模板的轉換限「專案經理」，單人使用時擁有者要能把自己換成那個角色，才走得完流程。
    /// </summary>
    public static bool GuardsLastAdmin(Actor actor) => !actor.IsOrganizationAdmin;

    public static async Task<LockedMembers> LockAsync(WorkDbContext db, ProjectEntity project, Guid personId, CancellationToken cancellationToken)
    {
        var rows = await db.ProjectMembers
            .FromSql($"""
                SELECT * FROM work.project_members
                WHERE organization_id = {project.OrganizationId} AND project_id = {project.Id}
                  AND (role = {ProjectRoles.Admin} OR person_id = {personId})
                ORDER BY id
                FOR UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return new LockedMembers(rows.SingleOrDefault(m => m.PersonId == personId), rows.Count(m => m.Role == ProjectRoles.Admin));
    }

    public static Failure LastAdmin(string projectKey) => new(
        FailureKind.Conflict,
        WorkErrorCodes.LastAdmin,
        $"這是專案{TextRules.Quote(projectKey)}唯一的管理員；請先把另一位成員設成管理員，再移除或改這個人的角色。");

    public static Failure MemberNotFound(string projectKey) => new(
        FailureKind.NotFound, WorkErrorCodes.MemberNotFound, $"這個人不是專案{TextRules.Quote(projectKey)}的成員。");
}
