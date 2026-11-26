using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>
/// 專案成員的名稱與編號，寫進錯誤訊息讓模型改用正確的負責人：空的專案沒有項目可以 get_item 拿成員名單。
/// 帳號停用的不列（指派給他們沒有意義）。名稱是別人寫的內容，只當資料列出。
/// </summary>
internal static class NewItemMembers
{
    private const int MaxListed = 30;

    public static async Task<string> DescribeAsync(
        IPeopleDirectory people, Guid organizationId, IReadOnlyList<ProjectMember> members, CancellationToken cancellationToken)
    {
        var found = await people.FindManyAsync(organizationId, [.. members.Select(m => m.PersonId)], cancellationToken);
        var active = members
            .Select(m => found.TryGetValue(m.PersonId, out var person) && !person.IsDisabled ? (Person: person, m.PersonId) : default)
            .Where(entry => entry.Person is not null)
            .Take(MaxListed)
            .Select(entry => $"{entry.Person!.DisplayName.Replace('「', '『').Replace('」', '』')}（{entry.PersonId}）")
            .ToList();
        return active.Count == 0 ? "這個專案目前沒有可以指派的成員。" : "專案成員：" + string.Join("、", active) + "。";
    }
}
