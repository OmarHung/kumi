using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的新專案建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeProjectProposals : IProjectProposalService, IProjectProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, ProjectProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<ProjectProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<ProjectProposal>> OnApply { get; set; } = _ => Proposal(ProjectProposalStatus.Applied);

    public Func<Guid, Result<ProjectProposal>> OnDismiss { get; set; } = _ => Proposal(ProjectProposalStatus.Dismissed);

    public Func<ProjectProposalDraft, Result<ProjectProposal>> OnCreate { get; set; } = _ => Proposal();

    /// <summary>每次套用收到的取消權杖（驗證端點沒有把請求的取消傳下去）。</summary>
    public Action<CancellationToken>? OnApplyToken { get; set; }

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, ProjectProposalDraft Draft)> Creates => _creates.ToArray();

    public static ProjectProposal Proposal(ProjectProposalStatus status = ProjectProposalStatus.Pending) => new(
        ProposalId,
        "WEB",
        "網站改版",
        "simple_tasks",
        "簡單任務",
        IncludeExamples: false,
        Description: "## 目標\n改版",
        status,
        new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero))
    {
        DecidedAt = status == ProjectProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 26, 3, 5, 0, TimeSpan.Zero),
        CreatedProjectKey = status == ProjectProposalStatus.Applied ? "WEB" : null,
    };

    public Task<Result<ProjectProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<ProjectProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        OnApplyToken?.Invoke(cancellationToken);
        return Record("apply", actor, proposalId, OnApply);
    }

    public Task<Result<ProjectProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<ProjectProposal>> CreateAsync(Actor actor, ProjectProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<ProjectProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<ProjectProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}
