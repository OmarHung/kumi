using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Ai.MemberProposals;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>
/// 新增項目建議的 <c>addMembers</c> 檢查（internal/HANDOFF-ai-assign.md 6.3）：路徑 <c>addMembers[0].role</c>，問題一次全列。
/// 角色的規則和成員建議共用 <see cref="MemberProposalChecker"/>。
/// </summary>
internal static class NewItemAddMembersChecker
{
    public static IReadOnlyList<MemberAddition> Check(
        IReadOnlyList<MemberAddition>? additions,
        IReadOnlyList<ProjectMember> members,
        IReadOnlyList<ProjectRoleInfo> roles,
        IReadOnlyDictionary<Guid, PersonSummary> people,
        List<FailureDetail> issues)
    {
        var list = additions ?? [];
        if (list.Count > AiRules.MaxMemberChangesPerProposal)
        {
            issues.Add(MemberProposalChecker.Issue("addMembers", $"addMembers 最多 {AiRules.MaxMemberChangesPerProposal} 個人。"));
        }

        var seen = new HashSet<Guid>();
        var cleaned = new List<MemberAddition>();
        for (var i = 0; i < list.Count && i < AiRules.MaxMemberChangesPerProposal; i++)
        {
            var path = $"addMembers[{i}]";
            if (list[i] is not { } addition)
            {
                issues.Add(MemberProposalChecker.Issue(path, "這一筆是空的。"));
                continue;
            }

            var result = CheckOne(addition, path, members, roles, people, seen, issues);
            if (result is not null)
            {
                cleaned.Add(result);
            }
        }

        return cleaned;
    }

    private static MemberAddition? CheckOne(
        MemberAddition addition,
        string path,
        IReadOnlyList<ProjectMember> members,
        IReadOnlyList<ProjectRoleInfo> roles,
        IReadOnlyDictionary<Guid, PersonSummary> people,
        HashSet<Guid> seen,
        List<FailureDetail> issues)
    {
        if (!seen.Add(addition.PersonId))
        {
            issues.Add(MemberProposalChecker.Issue($"{path}.person", "同一個人在 addMembers 裡只能出現一次。"));
            return null;
        }

        var role = MemberProposalChecker.CheckRole(addition.Role, $"{path}.role", roles, issues);
        if (!people.TryGetValue(addition.PersonId, out var person))
        {
            issues.Add(MemberProposalChecker.Issue($"{path}.person", $"找不到人員「{addition.PersonId}」；人員編號要是這個組織裡的人（list_people 查得到）。"));
            return null;
        }

        var name = MemberManagers.SafeName(person.DisplayName);
        if (person.IsDisabled)
        {
            issues.Add(MemberProposalChecker.Issue($"{path}.person", $"{name}的帳號已經停用，不能加進專案。"));
            return null;
        }

        if (members.Any(m => m.PersonId == person.PersonId))
        {
            issues.Add(MemberProposalChecker.Issue($"{path}.person", $"{name}已經是這個專案的成員，不必放進 addMembers。"));
            return null;
        }

        return role is null ? null : new MemberAddition(person.PersonId, role.Key) { DisplayName = person.DisplayName, RoleName = role.Name };
    }
}
