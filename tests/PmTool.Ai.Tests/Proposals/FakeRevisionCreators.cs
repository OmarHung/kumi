using PmTool.Abstractions;

namespace PmTool.Ai.Tests.Proposals;

/// <summary>刪除與批次修改建議的建立者：記下收到的草稿，回事先放好的結果（沒放就回一份剛好對得上的建議）。</summary>
internal sealed class FakeRevisionCreators : IItemDeletionProposalCreator, IItemBatchProposalCreator
{
    public List<ItemDeletionProposalDraft> DeletionDrafts { get; } = [];

    public List<ItemBatchProposalDraft> BatchDrafts { get; } = [];

    public Failure? DeletionFailure { get; set; }

    public Failure? BatchFailure { get; set; }

    Task<Result<ItemDeletionProposal>> IItemDeletionProposalCreator.CreateAsync(
        Actor actor, ItemDeletionProposalDraft draft, CancellationToken cancellationToken)
    {
        DeletionDrafts.Add(draft);
        Result<ItemDeletionProposal> result = DeletionFailure is { } failure
            ? failure
            : new ItemDeletionProposal(
                Guid.CreateVersion7(), draft.ProjectKey, [.. draft.ItemKeys.Select(key => new ItemDeletionTarget(key, "標題"))], ItemProposalStatus.Pending, DateTimeOffset.UnixEpoch)
            {
                Reason = draft.Reason,
            };
        return Task.FromResult(result);
    }

    Task<Result<ItemBatchProposal>> IItemBatchProposalCreator.CreateAsync(
        Actor actor, ItemBatchProposalDraft draft, CancellationToken cancellationToken)
    {
        BatchDrafts.Add(draft);
        Result<ItemBatchProposal> result = BatchFailure is { } failure
            ? failure
            : new ItemBatchProposal(
                Guid.CreateVersion7(),
                draft.ProjectKey,
                [.. draft.Items.Select(entry => new ItemBatchEntry(entry.ItemKey, "標題", entry.Changes, entry.Comment, null))],
                ItemProposalStatus.Pending,
                DateTimeOffset.UnixEpoch);
        return Task.FromResult(result);
    }
}
