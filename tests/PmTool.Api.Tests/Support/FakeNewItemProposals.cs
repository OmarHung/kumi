using System.Collections.Concurrent;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的新增項目建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeNewItemProposals : INewItemProposalService, INewItemProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, NewItemProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<NewItemProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<NewItemProposal>> OnApply { get; set; } = _ => Proposal(NewItemProposalStatus.Applied);

    public Func<Guid, Result<NewItemProposal>> OnDismiss { get; set; } = _ => Proposal(NewItemProposalStatus.Dismissed);

    public Func<NewItemProposalDraft, Result<NewItemProposal>> OnCreate { get; set; } = _ => Proposal();

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, NewItemProposalDraft Draft)> Creates => _creates.ToArray();

    public static NewItemProposal Proposal(NewItemProposalStatus status = NewItemProposalStatus.Pending) => new(
        ProposalId,
        "WEB",
        [
            new NewItemDraft("epic", "搬遷") { Ref = "epic", Priority = "high", Estimate = 3.5m },
            new NewItemDraft("task", "盤點資源")
            {
                ParentRef = "epic",
                AssigneeId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Fields = new Dictionary<string, JsonElement> { ["area"] = JsonSerializer.SerializeToElement("infra") },
            },
        ],
        status == NewItemProposalStatus.Applied ? ["WEB-13", null] : [null, null],
        status,
        new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero))
    {
        DecidedAt = status == NewItemProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 26, 3, 5, 0, TimeSpan.Zero),
        Error = status == NewItemProposalStatus.Applied ? "有幾個沒有建立：第 2 個「盤點資源」（上層沒有建立成功）。其餘已經建立。" : null,
    };

    public Task<Result<NewItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<NewItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("apply", actor, proposalId, OnApply);

    public Task<Result<NewItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<NewItemProposal>> CreateAsync(Actor actor, NewItemProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<NewItemProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<NewItemProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}
