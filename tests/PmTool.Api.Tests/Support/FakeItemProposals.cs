using System.Collections.Concurrent;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的修改建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeItemProposals : IItemProposalService, IItemProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, ItemProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<ItemProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<ItemProposal>> OnApply { get; set; } = _ => Proposal(ItemProposalStatus.Applied);

    public Func<Guid, Result<ItemProposal>> OnDismiss { get; set; } = _ => Proposal(ItemProposalStatus.Dismissed);

    public Func<ItemProposalDraft, Result<ItemProposal>> OnCreate { get; set; } = _ => Proposal();

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, ItemProposalDraft Draft)> Creates => _creates.ToArray();

    public static ItemProposal Proposal(ItemProposalStatus status = ItemProposalStatus.Pending) => new(
        ProposalId,
        "WEB",
        "WEB-12",
        "改版首頁",
        [new ItemFieldChange("status", JsonSerializer.SerializeToElement("done")), new ItemFieldChange("dueDate", JsonSerializer.SerializeToElement((string?)null))],
        "做完了",
        status,
        new DateTimeOffset(2026, 11, 23, 3, 0, 0, TimeSpan.Zero))
    {
        DecidedAt = status == ItemProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 23, 3, 5, 0, TimeSpan.Zero),
        Error = null,
    };

    public Task<Result<ItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<ItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("apply", actor, proposalId, OnApply);

    public Task<Result<ItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<ItemProposal>> CreateAsync(Actor actor, ItemProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<ItemProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<ItemProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}
