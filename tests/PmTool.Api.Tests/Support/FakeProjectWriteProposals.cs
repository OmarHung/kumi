using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>四種新建議（週期與里程碑、改專案、留言、復原項目）的假服務共用的部分：記住每次呼叫，回應可以換掉。</summary>
internal abstract class FakeProposalPair<TProposal, TDraft>
{
    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, TDraft Draft)> _creates = new();
    private readonly ConcurrentQueue<CancellationToken> _applyTokens = new();

    public Func<Guid, Result<TProposal>> OnGet { get; set; }

    public Func<Guid, Result<TProposal>> OnApply { get; set; }

    public Func<Guid, Result<TProposal>> OnDismiss { get; set; }

    public Func<TDraft, Result<TProposal>> OnCreate { get; set; }

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    /// <summary>每次套用收到的取消權杖：端點不讓套用跟著請求取消，測試靠它確認。</summary>
    public IReadOnlyList<CancellationToken> ApplyTokens => _applyTokens.ToArray();

    public IReadOnlyList<(Actor Actor, TDraft Draft)> Creates => _creates.ToArray();

    protected FakeProposalPair(Func<TProposal> pending, Func<TProposal> applied, Func<TProposal> dismissed)
    {
        OnGet = _ => pending();
        OnApply = _ => applied();
        OnDismiss = _ => dismissed();
        OnCreate = _ => pending();
    }

    public Task<Result<TProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<TProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        _applyTokens.Enqueue(cancellationToken);
        return Record("apply", actor, proposalId, OnApply);
    }

    public Task<Result<TProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<TProposal>> CreateAsync(Actor actor, TDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<TProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<TProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }

    protected static readonly DateTimeOffset CreatedAt = new(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);

    protected static readonly DateTimeOffset DecidedAt = new(2026, 11, 27, 3, 5, 0, TimeSpan.Zero);
}

internal sealed class FakePlanningProposals()
    : FakeProposalPair<PlanningProposal, PlanningProposalDraft>(
        () => Proposal(NewItemProposalStatus.Pending), () => Proposal(NewItemProposalStatus.Applied), () => Proposal(NewItemProposalStatus.Dismissed)),
      IPlanningProposalService, IPlanningProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("a1a1a1a1-0000-0000-0000-000000000001");

    public static readonly Guid CycleId = Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000c1");

    public static PlanningProposal Proposal(NewItemProposalStatus status) => new(
        ProposalId,
        "WEB",
        [
            new PlanningChange(PlanningTarget.Cycle, PlanningAction.Create, status == NewItemProposalStatus.Applied ? CycleId : null, null,
                new PlanningValues("第 49 週", new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 4), null, null), null, null),
            new PlanningChange(PlanningTarget.Milestone, PlanningAction.Delete, Guid.Parse("a1a1a1a1-0000-0000-0000-0000000000d1"),
                new PlanningValues("舊的里程碑", null, null, new DateOnly(2026, 12, 1), null), null, 3, null),
        ],
        status,
        CreatedAt)
    {
        Reason = "下週開始新的週期",
        DecidedAt = status == NewItemProposalStatus.Pending ? null : DecidedAt,
    };
}

internal sealed class FakeProjectChangeProposals()
    : FakeProposalPair<ProjectChangeProposal, ProjectChangeProposalDraft>(
        () => Proposal(ItemProposalStatus.Pending), () => Proposal(ItemProposalStatus.Applied), () => Proposal(ItemProposalStatus.Dismissed)),
      IProjectChangeProposalService, IProjectChangeProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("a2a2a2a2-0000-0000-0000-000000000001");

    public static ProjectChangeProposal Proposal(ItemProposalStatus status) => new(
        ProposalId,
        "WEB",
        "官網改版",
        null,
        true,
        "## 網址\n- https://example.com",
        new ProjectSnapshot("官網", false, null, null),
        status,
        CreatedAt)
    {
        DecidedAt = status == ItemProposalStatus.Pending ? null : DecidedAt,
    };
}

internal sealed class FakeCommentProposals()
    : FakeProposalPair<CommentProposal, CommentProposalDraft>(
        () => Proposal(ItemProposalStatus.Pending), () => Proposal(ItemProposalStatus.Applied), () => Proposal(ItemProposalStatus.Dismissed)),
      ICommentProposalService, ICommentProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("a3a3a3a3-0000-0000-0000-000000000001");

    public static readonly Guid CommentId = Guid.Parse("a3a3a3a3-0000-0000-0000-0000000000c1");

    public static CommentProposal Proposal(ItemProposalStatus status) => new(
        ProposalId,
        "WEB",
        [new CommentChange("WEB-12", CommentId, CommentAction.Edit, "測試者", CreatedAt.AddDays(-1), "原本的留言", "改過的留言", null)],
        status,
        CreatedAt)
    {
        DecidedAt = status == ItemProposalStatus.Pending ? null : DecidedAt,
    };
}

internal sealed class FakeItemRestoreProposals()
    : FakeProposalPair<ItemRestoreProposal, ItemRestoreProposalDraft>(
        () => Proposal(ItemProposalStatus.Pending), () => Proposal(ItemProposalStatus.Applied), () => Proposal(ItemProposalStatus.Dismissed)),
      IItemRestoreProposalService, IItemRestoreProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("a4a4a4a4-0000-0000-0000-000000000001");

    public static ItemRestoreProposal Proposal(ItemProposalStatus status) => new(
        ProposalId,
        "WEB",
        [new ItemRestoreTarget("WEB-7", "刪錯的任務", CreatedAt.AddHours(-2))],
        status,
        CreatedAt)
    {
        Reason = "刪錯了",
        DecidedAt = status == ItemProposalStatus.Pending ? null : DecidedAt,
        RestoredKeys = status == ItemProposalStatus.Applied ? ["WEB-7"] : [],
    };
}

/// <summary>通用的動作建議（第二段）。套用時收到的卡片輸入記在 <see cref="AppliedInputs"/>（測試確認有傳到、而且回應裡沒有）。</summary>
internal sealed class FakeActionProposals : IActionProposalService, IActionProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("a5a5a5a5-0000-0000-0000-000000000001");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, ActionProposalDraft Draft)> _creates = new();
    private readonly ConcurrentQueue<IReadOnlyDictionary<string, string>> _inputs = new();
    private readonly ConcurrentQueue<CancellationToken> _applyTokens = new();

    public Func<Guid, Result<ActionProposal>> OnGet { get; set; } = _ => Proposal(NewItemProposalStatus.Pending);

    public Func<Guid, Result<ActionProposal>> OnApply { get; set; } = _ => Proposal(NewItemProposalStatus.Applied);

    public Func<Guid, Result<ActionProposal>> OnDismiss { get; set; } = _ => Proposal(NewItemProposalStatus.Dismissed);

    public Func<ActionProposalDraft, Result<ActionProposal>> OnCreate { get; set; } = _ => Proposal(NewItemProposalStatus.Pending);

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, ActionProposalDraft Draft)> Creates => _creates.ToArray();

    public IReadOnlyList<IReadOnlyDictionary<string, string>> AppliedInputs => _inputs.ToArray();

    public IReadOnlyList<CancellationToken> ApplyTokens => _applyTokens.ToArray();

    public static ActionProposal Proposal(NewItemProposalStatus status) => new(
        ProposalId,
        ActionKinds.People,
        null,
        "建議調整人員",
        [
            new ActionStep("新增人員", "新同事", null, "新同事（new@example.com）· 一般成員", null, ActionTextFormat.Text)
            {
                Done = status == NewItemProposalStatus.Applied,
            },
        ],
        status,
        new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero))
    {
        Inputs = status == NewItemProposalStatus.Pending ? [new ActionInput("password:0", "新同事的初始密碼", ActionInputKind.Password, null)] : [],
        DecidedAt = status == NewItemProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 27, 3, 5, 0, TimeSpan.Zero),
    };

    public Task<Result<ActionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<ActionProposal>> ApplyAsync(
        Actor actor, Guid proposalId, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        _inputs.Enqueue(inputs);
        _applyTokens.Enqueue(cancellationToken);
        return Record("apply", actor, proposalId, OnApply);
    }

    public Task<Result<ActionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<ActionProposal>> CreateAsync(Actor actor, ActionProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<ActionProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<ActionProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}
