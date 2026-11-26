using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Access;

/// <summary>
/// 「能管理這個專案成員的人」：角色有 <c>project.manage</c>、帳號沒停用的成員。使用者自己沒有權限時，錯誤訊息寫出他們，
/// 讓助理請使用者去找這些人（成員建議、新增項目建議的 addMembers 與 get_project 的 memberManagers 都用這一份，名單的規則只有這裡）。
/// 組織管理員在每個專案都能管理成員，但不一定是成員，名單不列，訊息固定補一句。
/// </summary>
internal static class MemberManagers
{
    /// <summary>名單與訊息最多列幾個人。</summary>
    public const int MaxListed = 10;

    /// <summary>
    /// 能管理成員的成員，照 <paramref name="members"/> 的順序、最多 <see cref="MaxListed"/> 個。
    /// <paramref name="roles"/> 是 <c>IProjectStaffingQueries.ListRolesAsync</c> 的結果（內建加自訂）；查不到角色的成員當作沒有權限。
    /// </summary>
    public static async Task<IReadOnlyList<(Guid PersonId, string DisplayName)>> ListAsync(
        IPeopleDirectory people,
        Guid organizationId,
        IReadOnlyList<ProjectMember> members,
        IReadOnlyList<ProjectRoleInfo> roles,
        CancellationToken cancellationToken)
    {
        var managerRoles = roles
            .Where(role => role.Permissions.Contains(WorkPermissions.ProjectManage))
            .Select(role => role.Key)
            .ToHashSet(StringComparer.Ordinal);
        var candidates = members.Where(member => managerRoles.Contains(member.Role)).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        var found = await people.FindManyAsync(organizationId, [.. candidates.Select(m => m.PersonId)], cancellationToken);
        return
        [
            .. candidates
                .Where(m => found.TryGetValue(m.PersonId, out var person) && !person.IsDisabled)
                .Select(m => (m.PersonId, found[m.PersonId].DisplayName))
                .Take(MaxListed),
        ];
    }

    /// <summary>
    /// 沒有管理成員的權限時的訊息：「你沒有管理這個專案成員的權限。能管理成員的是：甲、乙（專案的管理者），以及組織管理員。」
    /// 名稱是別人寫的內容，只當資料列出（「」換成『』，免得和這句的引號混在一起）。
    /// </summary>
    public static string ForbiddenMessage(IReadOnlyList<(Guid PersonId, string DisplayName)> managers) =>
        "你沒有管理這個專案成員的權限。" + WhoCanText(managers);

    /// <summary>「能管理成員的是：甲、乙（專案的管理者），以及組織管理員。」沒有人時只寫組織管理員。</summary>
    public static string WhoCanText(IReadOnlyList<(Guid PersonId, string DisplayName)> managers)
    {
        if (managers.Count == 0)
        {
            return "能管理成員的是組織管理員。";
        }

        var names = string.Join("、", managers.Select(m => SafeName(m.DisplayName)));
        return $"能管理成員的是：{names}（專案的管理者），以及組織管理員。";
    }

    /// <summary>名字放進訊息之前的處理，和 <c>NewItemMembers.Describe</c> 同一種做法。</summary>
    public static string SafeName(string displayName) => displayName.Replace('「', '『').Replace('」', '』');
}
