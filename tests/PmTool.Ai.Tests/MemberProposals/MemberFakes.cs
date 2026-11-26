using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.MemberProposals;

/// <summary>派工查詢的假物件：角色清單由測試設定（預設內建的五個）。</summary>
internal sealed class FakeStaffingQueries : IProjectStaffingQueries
{
    public IReadOnlyList<ProjectRoleInfo> Roles { get; set; } =
    [
        new("admin", "管理員", true, [WorkPermissions.ProjectManage, WorkPermissions.ItemRead, WorkPermissions.ItemCreate]),
        new("editor", "編輯者", true, [WorkPermissions.ItemRead, WorkPermissions.ItemCreate, WorkPermissions.ItemUpdate]),
        new("contributor", "執行者", true, [WorkPermissions.ItemCreate]),
        new("commenter", "留言者", true, [WorkPermissions.ItemRead]),
        new("viewer", "檢視者", true, [WorkPermissions.ItemRead]),
    ];

    public Failure? RolesFailure { get; set; }

    public Task<Result<IReadOnlyList<ProjectRoleInfo>>> ListRolesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<ProjectRoleInfo>>>(RolesFailure is { } failure ? failure : Roles.ToList());

    public Task<Result<IReadOnlyDictionary<Guid, int>>> CountOpenByAssigneeAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyDictionary<Guid, int>>>(new Dictionary<Guid, int>());
}

/// <summary>週期與里程碑的假物件：只有讀；<see cref="Readable"/> 設成 false 時回沒有權限。</summary>
internal sealed class FakePlanningService : IPlanningService
{
    public List<Cycle> Cycles { get; } = [];

    public List<Milestone> Milestones { get; } = [];

    public bool Readable { get; set; } = true;

    private static readonly Failure Denied = new(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "沒有權限。");

    public Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Cycle>>>(Readable ? Cycles.ToList() : Denied);

    public Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Milestone>>>(Readable ? Milestones.ToList() : Denied);

    public Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Cycle>> UpdateCycleAsync(Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Milestone>> CreateMilestoneAsync(Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Milestone>> UpdateMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>有名字的人員目錄：測試登記每個人的名稱與停用狀態；沒登記的人查不到（不在這個組織裡）。</summary>
internal sealed class NamedPeopleDirectory : IPeopleDirectory
{
    private readonly Dictionary<Guid, PersonSummary> _people = [];

    public Guid Add(string name, bool disabled = false, Guid? id = null)
    {
        var personId = id ?? Guid.CreateVersion7();
        _people[personId] = new PersonSummary(personId, name, null, OrganizationRole.Member, disabled);
        return personId;
    }

    public Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PersonSummary>>(
            personIds.Where(_people.ContainsKey).Distinct().ToDictionary(id => id, id => _people[id]));

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>([.. _people.Values]);

    public Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
