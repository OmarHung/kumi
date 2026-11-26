using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>派工查詢的假物件：角色清單與項目數由測試設定；預設是五個內建角色、沒有人有項目。</summary>
internal sealed class FakeStaffingQueries : IProjectStaffingQueries
{
    public static readonly IReadOnlyList<ProjectRoleInfo> DefaultRoles =
    [
        new("admin", "管理員", true, [WorkPermissions.ItemRead, WorkPermissions.ItemCreate, WorkPermissions.ProjectManage]),
        new("editor", "編輯者", true, [WorkPermissions.ItemRead, WorkPermissions.ItemCreate, WorkPermissions.ItemUpdate]),
        new("contributor", "執行者", true, [WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate]),
        new("viewer", "檢視者", true, [WorkPermissions.ItemRead]),
    ];

    public IReadOnlyList<ProjectRoleInfo> Roles { get; set; } = DefaultRoles;

    public Dictionary<Guid, int> Open { get; } = [];

    public Func<Failure?>? Failing { get; set; }

    public Task<Result<IReadOnlyList<ProjectRoleInfo>>> ListRolesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<ProjectRoleInfo>>>(Failing?.Invoke() is { } failure ? failure : Roles.ToList());

    public Task<Result<IReadOnlyDictionary<Guid, int>>> CountOpenByAssigneeAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyDictionary<Guid, int>>>(Failing?.Invoke() is { } failure ? failure : new Dictionary<Guid, int>(Open));
}

/// <summary>週期與里程碑的假物件；<see cref="Failure"/> 設了就整個讀不到（沒有 item.read）。</summary>
internal sealed class FakePlanning : IPlanningService
{
    private static readonly ItemProgress NoProgress = new(0, 0, 0, null, null);

    public List<Cycle> Cycles { get; } = [];

    public List<Milestone> Milestones { get; } = [];

    public Failure? Failure { get; set; }

    public Cycle AddCycle(string name, DateOnly start, DateOnly end)
    {
        var cycle = new Cycle(Guid.NewGuid(), name, start, end, NoProgress);
        Cycles.Add(cycle);
        return cycle;
    }

    public Milestone AddMilestone(string name, DateOnly due)
    {
        var milestone = new Milestone(Guid.NewGuid(), name, due, null, NoProgress);
        Milestones.Add(milestone);
        return milestone;
    }

    public Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Cycle>>>(Failure is { } failure ? failure : Cycles.ToList());

    public Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Milestone>>>(Failure is { } failure ? failure : Milestones.ToList());

    public Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Cycle>> UpdateCycleAsync(Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Milestone>> CreateMilestoneAsync(Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Milestone>> UpdateMilestoneAsync(
        Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
