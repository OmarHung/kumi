using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.PlanningProposals;

/// <summary>週期與里程碑的假物件：記下每一次寫入；<see cref="OnWrite"/> 決定第幾次的結果（從 0 起算），丟例外也行。</summary>
internal sealed class WritablePlanningService : IPlanningService
{
    private static readonly ItemProgress NoProgress = new(0, 0, 0, null, null);

    private readonly List<string> _writes = [];

    public List<Cycle> Cycles { get; } = [];

    public List<Milestone> Milestones { get; } = [];

    /// <summary>每一次寫入的樣子，例如 "create-cycle:第一期"、"update-milestone:{id}"、"delete-cycle:{id}"。</summary>
    public IReadOnlyList<string> Writes => _writes;

    public IReadOnlyList<Actor> Actors => _actors;

    /// <summary>每一次送出的週期、里程碑內容（新增與修改），照寫入的順序。</summary>
    public List<CycleDraft> CycleDrafts { get; } = [];

    public List<MilestoneDraft> MilestoneDrafts { get; } = [];

    private readonly List<Actor> _actors = [];

    /// <summary>設了就用它決定第幾次寫入的結果；null 是成功。</summary>
    public Func<int, Failure?>? OnWrite { get; set; }

    public Cycle AddCycle(string name, DateOnly start, DateOnly end, int items = 0)
    {
        var cycle = new Cycle(Guid.CreateVersion7(), name, start, end, new ItemProgress(items, 0, 0, null, null));
        Cycles.Add(cycle);
        return cycle;
    }

    public Milestone AddMilestone(string name, DateOnly due, string? description = null, int items = 0)
    {
        var milestone = new Milestone(Guid.CreateVersion7(), name, due, description, new ItemProgress(items, 0, 0, null, null));
        Milestones.Add(milestone);
        return milestone;
    }

    public Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Cycle>>>(Cycles.ToList());

    public Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Milestone>>>(Milestones.ToList());

    public Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken) =>
        Record(draft).Write(actor, $"create-cycle:{draft.Name}", () => new Cycle(Guid.CreateVersion7(), draft.Name, draft.StartDate, draft.EndDate, NoProgress));

    public Task<Result<Cycle>> UpdateCycleAsync(Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken) =>
        Record(draft).Write(actor, $"update-cycle:{cycleId}:{draft.Name}", () => new Cycle(cycleId, draft.Name, draft.StartDate, draft.EndDate, NoProgress));

    public Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken) =>
        Write(actor, $"delete-cycle:{cycleId}", () => Unit.Value);

    public Task<Result<Milestone>> CreateMilestoneAsync(Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken) =>
        Record(draft).Write(actor, $"create-milestone:{draft.Name}:{draft.Description}", () => new Milestone(Guid.CreateVersion7(), draft.Name, draft.DueDate, draft.Description, NoProgress));

    public Task<Result<Milestone>> UpdateMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken) =>
        Record(draft).Write(actor, $"update-milestone:{milestoneId}:{draft.Name}:{draft.Description}", () => new Milestone(milestoneId, draft.Name, draft.DueDate, draft.Description, NoProgress));

    public Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken) =>
        Write(actor, $"delete-milestone:{milestoneId}", () => Unit.Value);

    private WritablePlanningService Record(CycleDraft draft)
    {
        CycleDrafts.Add(draft);
        return this;
    }

    private WritablePlanningService Record(MilestoneDraft draft)
    {
        MilestoneDrafts.Add(draft);
        return this;
    }

    private Task<Result<T>> Write<T>(Actor actor, string text, Func<T> value)
    {
        int index;
        lock (_writes)
        {
            index = _writes.Count;
            _writes.Add(text);
            _actors.Add(actor);
        }

        return Task.FromResult<Result<T>>(OnWrite?.Invoke(index) is { } failure ? failure : value());
    }
}

/// <summary>專案說明的假物件：說明與修改時間由測試設定；每次成功寫入都把修改時間往後推。</summary>
internal sealed class WritableDescriptions : IProjectDescriptionService
{
    private int _sets;

    public ProjectDescription Current { get; set; } = ProjectDescription.Empty;

    public List<string?> Sets { get; } = [];

    public Func<int, Failure?>? OnSet { get; set; }

    public Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ProjectDescription>>(Current);

    public Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken)
    {
        var index = _sets++;
        Sets.Add(text);
        if (OnSet?.Invoke(index) is { } failure)
        {
            return Task.FromResult<Result<ProjectDescription>>(failure);
        }

        Current = new ProjectDescription(text, DateTimeOffset.UnixEpoch.AddDays(1000 + index), actor.PersonId);
        return Task.FromResult<Result<ProjectDescription>>(Current);
    }

    public Task<IReadOnlyList<DescriptionTemplate>> ListTemplatesAsync(Actor actor, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<DescriptionTemplate>> CreateTemplateAsync(Actor actor, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<DescriptionTemplate>> UpdateTemplateAsync(Actor actor, Guid templateId, DescriptionTemplateInput input, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<DescriptionTemplate>> DeleteTemplateAsync(Actor actor, Guid templateId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(Actor actor, string text, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>專案服務的假物件：只有讀與改名、封存、取消封存（記下每一次呼叫）；<see cref="OnWrite"/> 決定寫入的結果。</summary>
internal sealed class WritableProjectService : IProjectService
{
    private readonly List<string> _writes = [];

    public Func<Actor, string, Result<ProjectDetail>> OnGet { get; set; } = (_, _) => throw new InvalidOperationException("測試要先設定專案。");

    /// <summary>每一次寫入：rename:新名稱、archive、unarchive。</summary>
    public IReadOnlyList<string> Writes => _writes;

    public Func<string, Failure?>? OnWrite { get; set; }

    public Func<string, Exception?>? OnThrow { get; set; }

    public IReadOnlyList<TemplateSummary> ListTemplates() => [];

    public Task<Result<ProjectDetail>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult(OnGet(actor, projectKey));

    public Task<Result<ProjectSummary>> RenameAsync(Actor actor, string projectKey, string name, CancellationToken cancellationToken) =>
        Write($"rename:{name}");

    public Task<Result<ProjectSummary>> ArchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) => Write("archive");

    public Task<Result<ProjectSummary>> UnarchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) => Write("unarchive");

    private Task<Result<ProjectSummary>> Write(string text)
    {
        _writes.Add(text);
        if (OnThrow?.Invoke(text) is { } exception)
        {
            throw exception;
        }

        return Task.FromResult<Result<ProjectSummary>>(
            OnWrite?.Invoke(text) is { } failure ? failure : new ProjectSummary(WorkSamples.ProjectId, "WEB", "網站改版", 3, "admin", DateTimeOffset.UnixEpoch));
    }

    public Task<IReadOnlyList<ProjectSummary>> ListAsync(Actor actor, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<ProjectSummary>> CreateAsync(Actor actor, NewProject project, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<Unit>> CheckNewProjectAsync(Actor actor, NewProject project, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<ProjectMember>> SetMemberAsync(Actor actor, string projectKey, Guid personId, string role, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<ProjectMember>> RemoveMemberAsync(Actor actor, string projectKey, Guid personId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
