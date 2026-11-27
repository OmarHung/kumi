using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 十一種建議的服務，依種類讀現在的狀態與作廢，結果都換成同一個 <see cref="ProposalStatusEntry"/>。
/// 讀與作廢都用發問的人的 <see cref="Actor"/>：每個服務自己判斷「是不是他的、專案還看不看得到」。
/// </summary>
internal sealed class ProposalServices(
    IItemProposalService items,
    IItemBatchProposalService batches,
    INewItemProposalService newItems,
    IItemDeletionProposalService deletions,
    IMemberProposalService members,
    IProjectProposalService projects,
    IPlanningProposalService planning,
    ICommentProposalService comments,
    IItemRestoreProposalService restores,
    IProjectChangeProposalService projectChanges,
    IActionProposalService actions,
    IBlueprintChangeService changes)
{
    private static readonly HashSet<string> NotFoundCodes = new(StringComparer.Ordinal)
    {
        AiErrorCodes.ItemProposalNotFound,
        AiErrorCodes.ItemBatchProposalNotFound,
        AiErrorCodes.NewItemProposalNotFound,
        AiErrorCodes.ItemDeletionProposalNotFound,
        AiErrorCodes.MemberProposalNotFound,
        AiErrorCodes.ProjectProposalNotFound,
        AiErrorCodes.PlanningProposalNotFound,
        AiErrorCodes.CommentProposalNotFound,
        AiErrorCodes.ItemRestoreProposalNotFound,
        AiErrorCodes.ProjectChangeProposalNotFound,
        AiErrorCodes.ActionProposalNotFound,
    };

    private static readonly HashSet<string> ClosedCodes = new(StringComparer.Ordinal)
    {
        AiErrorCodes.ItemProposalClosed,
        AiErrorCodes.ItemBatchProposalClosed,
        AiErrorCodes.NewItemProposalClosed,
        AiErrorCodes.ItemDeletionProposalClosed,
        AiErrorCodes.MemberProposalClosed,
        AiErrorCodes.ProjectProposalClosed,
        AiErrorCodes.PlanningProposalClosed,
        AiErrorCodes.CommentProposalClosed,
        AiErrorCodes.ItemRestoreProposalClosed,
        AiErrorCodes.ProjectChangeProposalClosed,
        AiErrorCodes.ActionProposalClosed,
    };

    /// <param name="projectKey">變更集要用；其他種類不看。</param>
    public async Task<Result<ProposalStatusEntry>> GetAsync(
        Actor actor, ProposalReference reference, string? projectKey, CancellationToken cancellationToken)
    {
        if (reference.Kind == ProposalKind.ChangeSet)
        {
            return await GetChangeSetAsync(actor, reference.Number!.Value, projectKey, cancellationToken);
        }

        var id = reference.Id!.Value;
        return reference.Kind switch
        {
            ProposalKind.ItemChanges => Map(await items.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.BulkItemChanges => Map(await batches.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.NewItems => Map(await newItems.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ItemDeletions => Map(await deletions.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Members => Map(await members.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Project => Map(await projects.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Planning => Map(await planning.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Comments => Map(await comments.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ItemRestores => Map(await restores.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ProjectChanges => Map(await projectChanges.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Action => Map(await actions.GetAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            _ => ProposalFailures.NotFound(),
        };
    }

    /// <summary>作廢。各服務的「找不到」「已經結束」換成撤回自己的代碼，其他失敗原樣。</summary>
    public async Task<Result<ProposalStatusEntry>> DismissAsync(Actor actor, ProposalReference reference, CancellationToken cancellationToken)
    {
        var id = reference.Id!.Value;
        var result = reference.Kind switch
        {
            ProposalKind.ItemChanges => Map(await items.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.BulkItemChanges => Map(await batches.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.NewItems => Map(await newItems.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ItemDeletions => Map(await deletions.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Members => Map(await members.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Project => Map(await projects.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Planning => Map(await planning.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Comments => Map(await comments.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ItemRestores => Map(await restores.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.ProjectChanges => Map(await projectChanges.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            ProposalKind.Action => Map(await actions.DismissAsync(actor, id, cancellationToken), ProposalSummaries.Of),
            _ => ProposalFailures.NotWithdrawable(),
        };
        return result.Failure is { } failure ? Translate(failure) : result;
    }

    private async Task<Result<ProposalStatusEntry>> GetChangeSetAsync(
        Actor actor, long number, string? projectKey, CancellationToken cancellationToken)
    {
        if (projectKey is null)
        {
            return ProposalFailures.NotFound();
        }

        return Map(await changes.GetAsync(actor, projectKey, number, cancellationToken), detail => ProposalSummaries.Of(detail.ChangeSet));
    }

    private static Failure Translate(Failure failure)
    {
        if (NotFoundCodes.Contains(failure.Code))
        {
            return ProposalFailures.NotFound();
        }

        return ClosedCodes.Contains(failure.Code) ? ProposalFailures.Closed() : failure;
    }

    private static Result<ProposalStatusEntry> Map<T>(Result<T> result, Func<T, ProposalStatusEntry> map) =>
        result.Failure is { } failure ? failure : map(result.Value);
}
