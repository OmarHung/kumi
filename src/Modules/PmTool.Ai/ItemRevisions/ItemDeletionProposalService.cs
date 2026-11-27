using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemRevisions;

/// <summary>
/// 刪除建議的讀取、套用、作廢（<see cref="IItemDeletionProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.ItemDeletionProposalNotFound"/>。刪除本來就是冪等的（已經刪除的算成功），
/// 所以套用只在建議的 advisory lock 裡排隊，不需要「套用中」的狀態。
/// </summary>
internal sealed class ItemDeletionProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, IBulkItemService bulk, TimeProvider time) : IItemDeletionProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓刪除停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ItemDeletionProposalNotFound, "找不到這份刪除建議。");

    private static readonly Failure TimedOut =
        new(FailureKind.Conflict, AiErrorCodes.ApplyTimedOut, "套用逾時，已經完成的不會重做，可以再按一次。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ItemDeletionProposalClosed, "這份刪除建議已經套用或作廢了。");

    public async Task<Result<ItemDeletionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return ItemDeletionMapper.ToProposal(row);
    }

    public async Task<Result<ItemDeletionProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        if (row.Status != ItemDeletionProposalRow.PendingStatus)
        {
            return Closed;
        }

        var keys = ItemDeletionMapper.ReadItems(row.Items).Select(i => i.ItemKey).ToList();
        using var timeout = new CancellationTokenSource(ApplyTimeout);
        Result<BulkDeleteResult> deleted;
        try
        {
            deleted = await bulk.DeleteAsync(actor, row.ProjectKey, keys, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // 這個 token 只有逾時會取消（不跟著請求）。刪除是冪等的：維持待決定，再按一次只會處理還沒刪的。
            return TimedOut;
        }

        if (deleted.Failure is { } failure)
        {
            return failure;
        }

        // 一筆都沒刪成：什麼都沒改，維持待決定，回傳第一筆的失敗。
        if (deleted.Value.Deleted.Count == 0 && deleted.Value.Failed.Count > 0)
        {
            return deleted.Value.Failed[0].Failure;
        }

        var deletedKeys = ItemDeletionMapper.WriteKeys(deleted.Value.Deleted.Select(d => d.Key));
        var error = deleted.Value.Failed.Count == 0 ? null : AiText.Cut(PartlyFailed(deleted.Value.Failed), AiRules.MaxErrorLength);
        var decidedAt = time.GetUtcNow();
        await db.ItemDeletionProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, ItemDeletionProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.DeletedKeys, deletedKeys)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemDeletionMapper.ToProposal(
            row with { Status = ItemDeletionProposalRow.AppliedStatus, DecidedAt = decidedAt, DeletedKeys = deletedKeys, Error = error });
    }

    public async Task<Result<ItemDeletionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        if (row.Status != ItemDeletionProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.ItemDeletionProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ItemDeletionProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemDeletionMapper.ToProposal(row with { Status = ItemDeletionProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    private static string PartlyFailed(IReadOnlyList<BulkItemFailure> failed) =>
        "有幾筆沒有刪除：" + string.Join("；", failed.Select(f => $"{f.Key}（{f.Failure.Message}）")) + "。其餘已經刪除，可以在「已刪除」復原。";

    // 專案已經看不到：建議也當作不存在，不再把項目的標題給他看。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, ItemDeletionProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<ItemDeletionProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ItemDeletionProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_item_deletion_proposal:" + proposalId}, 0))", cancellationToken);
}
