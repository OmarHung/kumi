using System.Collections.Concurrent;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的刪除建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeItemDeletionProposals : IItemDeletionProposalService, IItemDeletionProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, ItemDeletionProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<ItemDeletionProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<ItemDeletionProposal>> OnApply { get; set; } = _ => Proposal(ItemProposalStatus.Applied);

    public Func<Guid, Result<ItemDeletionProposal>> OnDismiss { get; set; } = _ => Proposal(ItemProposalStatus.Dismissed);

    public Func<ItemDeletionProposalDraft, Result<ItemDeletionProposal>> OnCreate { get; set; } = _ => Proposal();

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, ItemDeletionProposalDraft Draft)> Creates => _creates.ToArray();

    public static ItemDeletionProposal Proposal(ItemProposalStatus status = ItemProposalStatus.Pending) => new(
        ProposalId,
        "WEB",
        [new ItemDeletionTarget("WEB-45", "重複的任務"), new ItemDeletionTarget("WEB-46", "另一個重複的任務")],
        status,
        new DateTimeOffset(2026, 11, 27, 4, 0, 0, TimeSpan.Zero))
    {
        Reason = "重複建立",
        DecidedAt = status == ItemProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 27, 4, 5, 0, TimeSpan.Zero),
        DeletedKeys = status == ItemProposalStatus.Applied ? ["WEB-45"] : [],
        Error = status == ItemProposalStatus.Applied ? "有幾筆沒有刪除：WEB-46（沒有權限）。其餘已經刪除，可以在「已刪除」復原。" : null,
    };

    public Task<Result<ItemDeletionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<ItemDeletionProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("apply", actor, proposalId, OnApply);

    public Task<Result<ItemDeletionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<ItemDeletionProposal>> CreateAsync(Actor actor, ItemDeletionProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<ItemDeletionProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<ItemDeletionProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}

/// <summary>測試用的批次修改建議：記住每次呼叫，回應可以換掉。</summary>
internal sealed class FakeItemBatchProposals : IItemBatchProposalService, IItemBatchProposalCreator
{
    public static readonly Guid ProposalId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private readonly ConcurrentQueue<(string Action, Actor Actor, Guid Id)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, ItemBatchProposalDraft Draft)> _creates = new();

    public Func<Guid, Result<ItemBatchProposal>> OnGet { get; set; } = _ => Proposal();

    public Func<Guid, Result<ItemBatchProposal>> OnApply { get; set; } = _ => Proposal(ItemProposalStatus.Applied);

    public Func<Guid, Result<ItemBatchProposal>> OnDismiss { get; set; } = _ => Proposal(ItemProposalStatus.Dismissed);

    public Func<ItemBatchProposalDraft, Result<ItemBatchProposal>> OnCreate { get; set; } = _ => Proposal();

    public IReadOnlyList<(string Action, Actor Actor, Guid Id)> Calls => _calls.ToArray();

    public IReadOnlyList<(Actor Actor, ItemBatchProposalDraft Draft)> Creates => _creates.ToArray();

    public static ItemBatchProposal Proposal(ItemProposalStatus status = ItemProposalStatus.Pending)
    {
        var applied = status == ItemProposalStatus.Applied;
        return new ItemBatchProposal(
            ProposalId,
            "WEB",
            [
                new ItemBatchEntry("WEB-12", "改版首頁", [new ItemFieldChange("dueDate", JsonSerializer.SerializeToElement("2026-12-05"))], "順延一週", null),
                new ItemBatchEntry("WEB-13", "整理文案", [new ItemFieldChange("dueDate", JsonSerializer.SerializeToElement((string?)null))], null, applied ? "沒有權限修改這個項目。" : null),
            ],
            status,
            new DateTimeOffset(2026, 11, 27, 4, 10, 0, TimeSpan.Zero))
        {
            DecidedAt = status == ItemProposalStatus.Pending ? null : new DateTimeOffset(2026, 11, 27, 4, 15, 0, TimeSpan.Zero),
            Error = applied ? "有 1 筆沒有完全套用：WEB-13。" : null,
        };
    }

    public Task<Result<ItemBatchProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("get", actor, proposalId, OnGet);

    public Task<Result<ItemBatchProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("apply", actor, proposalId, OnApply);

    public Task<Result<ItemBatchProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        Record("dismiss", actor, proposalId, OnDismiss);

    public Task<Result<ItemBatchProposal>> CreateAsync(Actor actor, ItemBatchProposalDraft draft, CancellationToken cancellationToken)
    {
        _creates.Enqueue((actor, draft));
        return Task.FromResult(OnCreate(draft));
    }

    private Task<Result<ItemBatchProposal>> Record(string action, Actor actor, Guid id, Func<Guid, Result<ItemBatchProposal>> handler)
    {
        _calls.Enqueue((action, actor, id));
        return Task.FromResult(handler(id));
    }
}

/// <summary>測試用的建議狀態與撤回：記住每次撤回，回應可以換掉。</summary>
internal sealed class FakeProposalLedger : IAssistantProposalLedger
{
    private readonly ConcurrentQueue<(Actor Actor, Guid ConversationId, string Reference)> _withdrawals = new();

    public Func<string, Result<ProposalStatusEntry>> OnWithdraw { get; set; } = reference => Entry(reference);

    public IReadOnlyList<(Actor Actor, Guid ConversationId, string Reference)> Withdrawals => _withdrawals.ToArray();

    public static ProposalStatusEntry Entry(string reference) => new(
        reference,
        ProposalKind.NewItems,
        ProposalState.Dismissed,
        "新增 7 個項目",
        new DateTimeOffset(2026, 11, 27, 3, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 27, 3, 5, 0, TimeSpan.Zero));

    public Task<IReadOnlyList<ProposalStatusEntry>> ListAsync(Actor actor, Guid conversationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ProposalStatusEntry>>([]);

    public Task<Result<ProposalStatusEntry>> WithdrawAsync(Actor actor, Guid conversationId, string reference, CancellationToken cancellationToken)
    {
        _withdrawals.Enqueue((actor, conversationId, reference));
        return Task.FromResult(OnWithdraw(reference));
    }
}
