using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.MemberProposals;

/// <summary>
/// 成員建議的檢查（internal/HANDOFF-ai-assign.md 第 6.2 節）：每個問題一個細項（路徑 <c>changes[1].role</c>），一次回給模型，它才能一次修好。
/// </summary>
internal static class MemberProposalChecker
{
    public static Result<IReadOnlyList<MemberChange>> Check(
        IReadOnlyList<MemberChangeDraft>? drafts,
        IReadOnlyList<ProjectMember> members,
        IReadOnlyList<ProjectRoleInfo> roles,
        IReadOnlyDictionary<Guid, PersonSummary> people)
    {
        var list = drafts ?? [];
        var issues = new List<FailureDetail>();
        if (list.Count is 0 or > AiRules.MaxMemberChangesPerProposal)
        {
            issues.Add(Issue("changes", $"一份建議要有 1 到 {AiRules.MaxMemberChangesPerProposal} 筆變動。"));
        }

        var seen = new HashSet<Guid>();
        var checkedChanges = new List<MemberChange>();
        for (var i = 0; i < list.Count && i < AiRules.MaxMemberChangesPerProposal; i++)
        {
            if (list[i] is not { } draft)
            {
                issues.Add(Issue($"changes[{i}]", "這一筆是空的。"));
                continue;
            }

            var change = CheckOne(draft, $"changes[{i}]", members, roles, people, seen, issues);
            if (change is not null)
            {
                checkedChanges.Add(change);
            }
        }

        return issues.Count == 0
            ? checkedChanges
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "成員建議不合規定，沒有建立。") { Details = issues };
    }

    internal static FailureDetail Issue(string path, string message) => new(AiErrorCodes.FieldInvalid, path, message);

    /// <summary>角色的說明：「editor（編輯者）」。</summary>
    internal static string DescribeRole(ProjectRoleInfo role) => $"{role.Key}（{MemberManagers.SafeName(role.Name)}）";

    /// <summary>可用的角色一覽，角色不對時附在訊息後面。</summary>
    internal static string DescribeRoles(IReadOnlyList<ProjectRoleInfo> roles) =>
        "可用的角色：" + string.Join("、", roles.Select(DescribeRole)) + "。";

    /// <summary>檢查一個角色 key；回傳整理過的角色（不對時 null，問題記進 <paramref name="issues"/>）。</summary>
    internal static ProjectRoleInfo? CheckRole(string? key, string path, IReadOnlyList<ProjectRoleInfo> roles, List<FailureDetail> issues)
    {
        var role = roles.FirstOrDefault(r => r.Key == key?.Trim());
        if (role is null)
        {
            issues.Add(Issue(path, $"這個專案沒有角色「{key}」。{DescribeRoles(roles)}"));
        }

        return role;
    }

    private static MemberChange? CheckOne(
        MemberChangeDraft draft,
        string path,
        IReadOnlyList<ProjectMember> members,
        IReadOnlyList<ProjectRoleInfo> roles,
        IReadOnlyDictionary<Guid, PersonSummary> people,
        HashSet<Guid> seen,
        List<FailureDetail> issues)
    {
        if (!seen.Add(draft.PersonId))
        {
            issues.Add(Issue($"{path}.person", "同一個人在一份建議裡只能出現一次。"));
            return null;
        }

        if (!people.TryGetValue(draft.PersonId, out var person))
        {
            issues.Add(Issue($"{path}.person", $"找不到人員「{draft.PersonId}」；人員編號要是這個組織裡的人（list_people 查得到）。"));
            return null;
        }

        var current = members.FirstOrDefault(m => m.PersonId == draft.PersonId);
        var previous = current is null ? null : roles.FirstOrDefault(r => r.Key == current.Role);
        var name = MemberManagers.SafeName(person.DisplayName);
        return draft.Role is null
            ? CheckRemoval(person, current, previous, name, path, issues)
            : CheckAssignment(draft.Role, person, current, previous, name, path, roles, issues);
    }

    private static MemberChange? CheckRemoval(
        PersonSummary person, ProjectMember? current, ProjectRoleInfo? previous, string name, string path, List<FailureDetail> issues)
    {
        if (current is null)
        {
            issues.Add(Issue($"{path}.person", $"{name}現在不是這個專案的成員，不必移出。"));
            return null;
        }

        return new MemberChange(person.PersonId, person.DisplayName, null, null, current.Role, previous?.Name ?? current.Role);
    }

    private static MemberChange? CheckAssignment(
        string roleKey,
        PersonSummary person,
        ProjectMember? current,
        ProjectRoleInfo? previous,
        string name,
        string path,
        IReadOnlyList<ProjectRoleInfo> roles,
        List<FailureDetail> issues)
    {
        var role = CheckRole(roleKey, $"{path}.role", roles, issues);
        if (person.IsDisabled)
        {
            issues.Add(Issue($"{path}.person", $"{name}的帳號已經停用，不能加進專案或改角色。"));
            return null;
        }

        if (role is null)
        {
            return null;
        }

        if (current?.Role == role.Key)
        {
            issues.Add(Issue($"{path}.role", $"{name}已經是「{MemberManagers.SafeName(role.Name)}」了。"));
            return null;
        }

        return new MemberChange(person.PersonId, person.DisplayName, role.Key, role.Name, current?.Role, current is null ? null : previous?.Name ?? current.Role);
    }
}
