using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的派工資料（get_project、list_people）：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeStaffingBrief : IStaffingBriefService
{
    public static readonly Guid CycleId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static readonly Guid MilestoneId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _projectCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, PeopleQuery Query)> _peopleCalls = new();

    public Func<string, Result<ProjectBrief>> OnGetProject { get; set; } = key => Brief(key);

    public Func<PeopleQuery, Result<IReadOnlyList<PersonBrief>>> OnListPeople { get; set; } = _ => People();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> ProjectCalls => _projectCalls.ToArray();

    public IReadOnlyList<(Actor Actor, PeopleQuery Query)> PeopleCalls => _peopleCalls.ToArray();

    public static ProjectBrief Brief(string key) => new(
        key,
        "網站",
        IsArchived: false,
        MyRole: "admin",
        MyPermissions: ["item.read", "project.manage"],
        ItemTypes: [new ProjectBriefItemType("task", "任務", ["epic"], "待辦", [new ProjectBriefField("area", "領域", "select", true, ["infra", "web"])])],
        Members: [new ProjectBriefMember(TestUsers.Member.PersonId, TestUsers.Member.DisplayName, "editor", "前端工程師", "WordPress 維運", 3)],
        Roles: [new ProjectBriefRole("editor", "編輯者", true, ["item.read", "item.create"])],
        MemberManagers: ["甲"],
        Cycles: [new ProjectBriefCycle(CycleId, "第 1 週期", new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 13))],
        Milestones: [new ProjectBriefMilestone(MilestoneId, "上線", new DateOnly(2026, 12, 31))]);

    public static PersonBrief[] People() =>
        [new PersonBrief(TestUsers.Member.PersonId, TestUsers.Member.DisplayName, "member", "前端工程師", "WordPress 維運", "editor")];

    public Task<Result<ProjectBrief>> GetProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _projectCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnGetProject(projectKey));
    }

    public Task<Result<IReadOnlyList<PersonBrief>>> ListPeopleAsync(Actor actor, PeopleQuery query, CancellationToken cancellationToken)
    {
        _peopleCalls.Enqueue((actor, query));
        return Task.FromResult(OnListPeople(query));
    }
}
