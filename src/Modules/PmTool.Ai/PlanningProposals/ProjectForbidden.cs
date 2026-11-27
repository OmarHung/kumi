using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 沒有權限時的失敗，句型和成員建議同一種：寫出缺什麼，再寫出能做的人（專案的管理者與組織管理員），
/// 讓助理請使用者去找他們。週期與里程碑、改專案兩種建議共用。
/// </summary>
internal static class ProjectForbidden
{
    public static async Task<Failure> BuildAsync(
        IProjectStaffingQueries staffing,
        IPeopleDirectory people,
        Actor actor,
        ProjectDetail project,
        string lacking,
        CancellationToken cancellationToken)
    {
        var roles = await staffing.ListRolesAsync(actor, project.Project.Key, cancellationToken);
        var managers = roles.Failure is null
            ? await MemberManagers.ListAsync(people, actor.OrganizationId, project.Members, roles.Value, cancellationToken)
            : [];
        var who = managers.Count == 0
            ? "能做的是組織管理員。"
            : $"能做的是：{string.Join("、", managers.Select(m => MemberManagers.SafeName(m.DisplayName)))}（專案的管理者），以及組織管理員。";
        return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, $"你沒有{lacking}的權限。{who}");
    }
}
