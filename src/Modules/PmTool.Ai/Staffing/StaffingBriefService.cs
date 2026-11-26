using PmTool.Abstractions;
using PmTool.Ai.Access;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Staffing;

/// <summary>
/// 派工用的摘要：專案（get_project）與組織裡的人（list_people）。權限全部交給工作引擎與人員目錄判斷
/// （<see cref="IProjectService.GetAsync"/>、<see cref="IProjectStaffingQueries"/>），這裡只負責把結果整理成給模型看的形狀；
/// 內建助理與 MCP 的同名工具都呼叫它，規則只有這一份。
/// </summary>
internal sealed class StaffingBriefService(
    IProjectService projects,
    IProjectStaffingQueries staffing,
    IPlanningService planning,
    IPeopleDirectory people,
    IOrganizationClock organizationClock,
    TimeProvider time) : IStaffingBriefService
{
    private const int MaxPlanningListed = 10;

    public async Task<Result<ProjectBrief>> GetProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var project = await projects.GetAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } projectFailure)
        {
            return projectFailure;
        }

        var roles = await staffing.ListRolesAsync(actor, projectKey, cancellationToken);
        if (roles.Failure is { } rolesFailure)
        {
            return rolesFailure;
        }

        var open = await staffing.CountOpenByAssigneeAsync(actor, projectKey, cancellationToken);
        if (open.Failure is { } openFailure)
        {
            return openFailure;
        }

        var detail = project.Value;
        var found = await people.FindManyAsync(actor.OrganizationId, [.. detail.Members.Select(m => m.PersonId)], cancellationToken);
        var managers = await MemberManagers.ListAsync(people, actor.OrganizationId, detail.Members, roles.Value, cancellationToken);
        var today = OrganizationTime.Today(await organizationClock.TimeZoneAsync(actor.OrganizationId, cancellationToken), time.GetUtcNow());
        return new ProjectBrief(
            detail.Project.Key,
            detail.Project.Name,
            detail.Project.ArchivedAt is not null,
            detail.Members.FirstOrDefault(m => m.PersonId == actor.PersonId)?.Role,
            detail.MyPermissions,
            BriefItemTypes.From(detail.Blueprint),
            MembersOf(detail.Members, found, open.Value, actor.PersonId, detail.MyPermissions.Contains(WorkPermissions.ItemRead)),
            [.. roles.Value.Select(role => new ProjectBriefRole(role.Key, role.Name, role.IsBuiltIn, role.Permissions))],
            [.. managers.Select(m => m.DisplayName)],
            await CyclesAsync(actor, projectKey, today, cancellationToken),
            await MilestonesAsync(actor, projectKey, today, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<PersonBrief>>> ListPeopleAsync(Actor actor, PeopleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(query);
        var self = await people.FindManyAsync(actor.OrganizationId, [actor.PersonId], cancellationToken);
        if (self.TryGetValue(actor.PersonId, out var asker) && asker.Role == OrganizationRole.Guest)
        {
            return new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "訪客看不到組織的人員名單。");
        }

        IReadOnlyDictionary<Guid, string> projectRoles = new Dictionary<Guid, string>();
        if (query.ProjectKey is { } projectKey)
        {
            var project = await projects.GetAsync(actor, projectKey, cancellationToken);
            if (project.Failure is { } failure)
            {
                return failure;
            }

            projectRoles = project.Value.Members.ToDictionary(m => m.PersonId, m => m.Role);
        }

        var limit = Math.Clamp(query.Limit, 1, AiRules.MaxPeopleListed);
        var listed = (await people.ListAsync(actor.OrganizationId, cancellationToken))
            .Where(person => !person.IsDisabled && Matches(person, query.Text))
            .OrderBy(person => person.DisplayName, StringComparer.Ordinal)
            .Take(limit)
            .Select(person => new PersonBrief(
                person.PersonId,
                person.DisplayName,
                OrganizationRoleText(person.Role),
                person.JobTitle,
                person.Responsibilities,
                projectRoles.GetValueOrDefault(person.PersonId)))
            .ToList();
        return listed;
    }

    private static IReadOnlyList<ProjectBriefMember> MembersOf(
        IReadOnlyList<ProjectMember> members,
        IReadOnlyDictionary<Guid, PersonSummary> found,
        IReadOnlyDictionary<Guid, int> open,
        Guid askerId,
        bool seesAllItems) =>
    [
        .. members
            .Where(member => found.TryGetValue(member.PersonId, out var person) && !person.IsDisabled)
            .Select(member => (Member: member, Person: found[member.PersonId]))
            .OrderBy(entry => entry.Person.DisplayName, StringComparer.Ordinal)
            .Select(entry => new ProjectBriefMember(
                entry.Member.PersonId,
                entry.Person.DisplayName,
                entry.Member.Role,
                entry.Person.JobTitle,
                entry.Person.Responsibilities,
                // 沒有 item.read 時只算得到自己的：別人的給 null，0 會讓模型以為他很閒。
                seesAllItems || entry.Member.PersonId == askerId ? open.GetValueOrDefault(entry.Member.PersonId) : null)),
    ];

    // 讀不到（沒有 item.read）不算失敗：派工用不到時照樣能建任務，只是少了週期可以選。
    private async Task<IReadOnlyList<ProjectBriefCycle>> CyclesAsync(
        Actor actor, string projectKey, DateOnly today, CancellationToken cancellationToken)
    {
        var cycles = await planning.ListCyclesAsync(actor, projectKey, cancellationToken);
        return cycles.Failure is not null
            ? []
            :
            [
                .. cycles.Value
                    .Where(cycle => cycle.EndDate >= today)
                    .OrderBy(cycle => cycle.StartDate)
                    .Take(MaxPlanningListed)
                    .Select(cycle => new ProjectBriefCycle(cycle.Id, cycle.Name, cycle.StartDate, cycle.EndDate)),
            ];
    }

    private async Task<IReadOnlyList<ProjectBriefMilestone>> MilestonesAsync(
        Actor actor, string projectKey, DateOnly today, CancellationToken cancellationToken)
    {
        var milestones = await planning.ListMilestonesAsync(actor, projectKey, cancellationToken);
        return milestones.Failure is not null
            ? []
            :
            [
                .. milestones.Value
                    .Where(milestone => milestone.DueDate >= today)
                    .OrderBy(milestone => milestone.DueDate)
                    .Take(MaxPlanningListed)
                    .Select(milestone => new ProjectBriefMilestone(milestone.Id, milestone.Name, milestone.DueDate)),
            ];
    }

    private static bool Matches(PersonSummary person, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var needle = text.Trim();
        return Contains(person.DisplayName, needle) || Contains(person.JobTitle, needle) || Contains(person.Responsibilities, needle);
    }

    private static bool Contains(string? value, string needle) => value?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;

    private static string OrganizationRoleText(OrganizationRole role) => role switch
    {
        OrganizationRole.Owner => "owner",
        OrganizationRole.Admin => "admin",
        OrganizationRole.Member => "member",
        OrganizationRole.Guest => "guest",
        _ => role.ToString().ToLowerInvariant(),
    };
}
