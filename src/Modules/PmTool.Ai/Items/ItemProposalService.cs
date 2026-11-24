using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 修改建議的讀取、套用、作廢（<see cref="IItemProposalService"/>）。一定要是建立的人、同一個組織；別人的與不存在的都是
/// <see cref="AiErrorCodes.ItemProposalNotFound"/>。套用與作廢在同一個 advisory lock 裡排隊：一份建議只會被決定一次。
/// </summary>
internal sealed class ItemProposalService(
    IDbContextFactory<AiDbContext> contexts, IWorkItemService items, ItemProposalApplier applier, TimeProvider time) : IItemProposalService
{
    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ItemProposalNotFound, "找不到這份修改建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ItemProposalClosed, "這份修改建議已經套用或作廢了。");

    public async Task<Result<ItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        // 項目已經看不到（被移出專案、被刪掉）：建議也當作不存在，不再把項目的標題與修改內容給他看。
        var visible = await items.GetAsync(actor, row.ItemKey, cancellationToken);
        return visible.Failure is null ? ItemProposalMapper.ToProposal(row) : NotFound;
    }

    public async Task<Result<ItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        if (row.Status != ItemProposalRow.PendingStatus)
        {
            return Closed;
        }

        var outcome = await applier.ApplyAsync(actor, row, cancellationToken);
        if (outcome.Failure is { } failure)
        {
            return failure;
        }

        return await DecideAsync(db, transaction, row, ItemProposalRow.AppliedStatus, outcome.Value.Length == 0 ? null : outcome.Value, cancellationToken);
    }

    public async Task<Result<ItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null)
        {
            return NotFound;
        }

        return row.Status != ItemProposalRow.PendingStatus
            ? Closed
            : await DecideAsync(db, transaction, row, ItemProposalRow.DismissedStatus, error: null, cancellationToken);
    }

    private async Task<Result<ItemProposal>> DecideAsync(
        AiDbContext db,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        ItemProposalRow row,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        var decided = row with
        {
            Status = status,
            DecidedAt = time.GetUtcNow(),
            Error = error is null ? null : AiText.Cut(error, AiRules.MaxErrorLength),
        };

        // 項目已經改了：存狀態不能被使用者關掉頁面而取消，不然同一份建議會被再套用一次。
        await db.ItemProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, decided.Status)
                .SetProperty(p => p.DecidedAt, decided.DecidedAt)
                .SetProperty(p => p.Error, decided.Error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemProposalMapper.ToProposal(decided);
    }

    private static Task<ItemProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ItemProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_item_proposal:" + proposalId}, 0))", cancellationToken);
}
