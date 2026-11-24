using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的規劃服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakePlanningService : IPlanningService
{
    public static readonly Guid CycleId = Guid.Parse("018f0000-0000-7000-8000-0000000000c1");
    public static readonly Guid MilestoneId = Guid.Parse("018f0000-0000-7000-8000-0000000000a1");

    private readonly ConcurrentQueue<(string Name, Actor Actor, string ProjectKey, Guid? Id, object? Draft)> _calls = new();

    public static Cycle SampleCycle(CycleDraft? draft = null, Guid? id = null) => new(
        id ?? CycleId,
        draft?.Name ?? "衝刺 1",
        draft?.StartDate ?? new DateOnly(2026, 11, 2),
        draft?.EndDate ?? new DateOnly(2026, 11, 15),
        new ItemProgress(10, 3, 1, 21.5m, 8));

    public static Milestone SampleMilestone(MilestoneDraft? draft = null, Guid? id = null) => new(
        id ?? MilestoneId,
        draft?.Name ?? "v1.0 上線",
        draft?.DueDate ?? new DateOnly(2026, 12, 1),
        draft is null ? "第一個對外的版本。" : draft.Description,
        new ItemProgress(0, 0, 0, null, null));

    public Func<Result<IReadOnlyList<Cycle>>> OnListCycles { get; set; } = () => new List<Cycle> { SampleCycle() };

    public Func<CycleDraft, Result<Cycle>> OnCreateCycle { get; set; } = draft => SampleCycle(draft);

    public Func<Guid, CycleDraft, Result<Cycle>> OnUpdateCycle { get; set; } = (id, draft) => SampleCycle(draft, id);

    public Func<Result<Unit>> OnDeleteCycle { get; set; } = () => Unit.Value;

    public Func<Result<IReadOnlyList<Milestone>>> OnListMilestones { get; set; } = () => new List<Milestone> { SampleMilestone() };

    public Func<MilestoneDraft, Result<Milestone>> OnCreateMilestone { get; set; } = draft => SampleMilestone(draft);

    public Func<Guid, MilestoneDraft, Result<Milestone>> OnUpdateMilestone { get; set; } = (id, draft) => SampleMilestone(draft, id);

    public Func<Result<Unit>> OnDeleteMilestone { get; set; } = () => Unit.Value;

    /// <summary>每次呼叫的（方法、呼叫者、專案、編號、草稿）。</summary>
    public IReadOnlyList<(string Name, Actor Actor, string ProjectKey, Guid? Id, object? Draft)> Calls => _calls.ToArray();

    public Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Record(nameof(ListCyclesAsync), actor, projectKey, null, null, OnListCycles());

    public Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken) =>
        Record(nameof(CreateCycleAsync), actor, projectKey, null, draft, OnCreateCycle(draft));

    public Task<Result<Cycle>> UpdateCycleAsync(
        Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken) =>
        Record(nameof(UpdateCycleAsync), actor, projectKey, cycleId, draft, OnUpdateCycle(cycleId, draft));

    public Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken) =>
        Record(nameof(DeleteCycleAsync), actor, projectKey, cycleId, null, OnDeleteCycle());

    public Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Record(nameof(ListMilestonesAsync), actor, projectKey, null, null, OnListMilestones());

    public Task<Result<Milestone>> CreateMilestoneAsync(
        Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken) =>
        Record(nameof(CreateMilestoneAsync), actor, projectKey, null, draft, OnCreateMilestone(draft));

    public Task<Result<Milestone>> UpdateMilestoneAsync(
        Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken) =>
        Record(nameof(UpdateMilestoneAsync), actor, projectKey, milestoneId, draft, OnUpdateMilestone(milestoneId, draft));

    public Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken) =>
        Record(nameof(DeleteMilestoneAsync), actor, projectKey, milestoneId, null, OnDeleteMilestone());

    private Task<Result<T>> Record<T>(string name, Actor actor, string projectKey, Guid? id, object? draft, Result<T> result)
    {
        _calls.Enqueue((name, actor, projectKey, id, draft));
        return Task.FromResult(result);
    }
}
