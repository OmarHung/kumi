using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的成員建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeMemberProposals : IMemberProposalService, IMemberProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    public static readonly Guid JoinerId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    public static readonly Guid ChangerId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    public static readonly Guid LeaverId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, MemberProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<MemberProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<MemberProposal>> OnApply { get; set; } = _ => Proposal(MemberProposalStatus.Applied);

    public Func<Guid, Result<MemberProposal>> OnDismiss { get; set; } = _ => Proposal(MemberProposalStatus.Dismissed);

    public Func<MemberProposalDraft, Result<MemberProposal>> OnCreate { get; set; } = _ => Proposal();

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, MemberProposalDraft Draft)> Creates => _creates.ToArray();

    public static MemberProposal Proposal(MemberProposalStatus status = MemberProposalStatus.Pending) => new(
        ProposalId,
        "WEB",
        [
            new MemberChange(JoinerId, "新人", "contributor", "執行者", null, null),
            new MemberChange(ChangerId, "老手", "editor", "編輯者", "contributor", "執行者"),
            new MemberChange(LeaverId, "舊人", null, null, "viewer", "檢視者"),
        ],
        status,
        new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero))
    {
        DecidedAt = status == MemberProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 27, 3, 5, 0, TimeSpan.Zero),
        Error = status == MemberProposalStatus.Applied ? "有 1 筆沒有套用：舊人（專案至少要留一位管理員）。其餘已經套用。" : null,
    };

    public Task<Result<MemberProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<MemberProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("apply", actor, proposalId, OnApply);

    public Task<Result<MemberProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<MemberProposal>> CreateAsync(Actor actor, MemberProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<MemberProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<MemberProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}
