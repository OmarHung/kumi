using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemRestoreProposals;

/// <summary>
/// 復原建議的讀取、套用、作廢（<see cref="IItemRestoreProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.ItemRestoreProposalNotFound"/>。復原本來就是冪等的，
/// 所以套用只在建議的 advisory lock 裡排隊，不需要「套用中」的狀態。
/// </summary>
internal sealed class ItemRestoreProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, IBulkItemService bulk, TimeProvider time) : IItemRestoreProposalService
{
    /// <summary>整份建議套用的時限。不跟著請求取消：使用者關掉頁面不能讓復原停在一半。</summary>
    internal static readonly TimeSpan ApplyTimeout = TimeSpan.FromSeconds(60);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ItemRestoreProposalNotFound, "找不到這份復原建議。");

    private static readonly Failure TimedOut =
        new(FailureKind.Conflict, AiErrorCodes.ApplyTimedOut, "套用逾時，已經完成的不會重做，可以再按一次。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ItemRestoreProposalClosed, "這份復原建議已經套用或作廢了。");

    public async Task<Result<ItemRestoreProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return ItemRestoreMapper.ToProposal(row);
    }

    public async Task<Result<ItemRestoreProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ItemRestoreProposalRow.PendingStatus)
        {
            return Closed;
        }

        var keys = ItemRestoreMapper.ReadItems(row.Items).Select(i => i.ItemKey).ToList();
        using var timeout = new CancellationTokenSource(ApplyTimeout);
        Result<BulkRestoreResult> restored;
        try
        {
            restored = await bulk.RestoreAsync(actor, row.ProjectKey, keys, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // 這個 token 只有逾時會取消（不跟著請求）。復原是冪等的：維持待決定，再按一次只會處理還沒復原的。
            return TimedOut;
        }

        if (restored.Failure is { } failure)
        {
            return failure;
        }

        // 一筆都沒復原成：什麼都沒改，維持待決定，回傳第一筆的失敗。
        if (restored.Value.Restored.Count == 0 && restored.Value.Failed.Count > 0)
        {
            return restored.Value.Failed[0].Failure;
        }

        var restoredKeys = ItemRestoreMapper.WriteKeys(restored.Value.Restored.Select(r => r.Key));
        var error = restored.Value.Failed.Count == 0 ? null : AiText.Cut(PartlyFailed(restored.Value.Failed), AiRules.MaxErrorLength);
        var decidedAt = time.GetUtcNow();
        await db.ItemRestoreProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, ItemRestoreProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.RestoredKeys, restoredKeys)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemRestoreMapper.ToProposal(
            row with { Status = ItemRestoreProposalRow.AppliedStatus, DecidedAt = decidedAt, RestoredKeys = restoredKeys, Error = error });
    }

    public async Task<Result<ItemRestoreProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ItemRestoreProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.ItemRestoreProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ItemRestoreProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ItemRestoreMapper.ToProposal(row with { Status = ItemRestoreProposalRow.DismissedStatus, DecidedAt = decidedAt });
    }

    private static string PartlyFailed(IReadOnlyList<BulkItemFailure> failed) =>
        "有幾筆沒有復原：" + string.Join("；", failed.Select(f => $"{f.Key}（{f.Failure.Message}）")) + "。其餘已經復原。";

    // 專案已經看不到：建議也當作不存在，不再把項目的標題給他看。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, ItemRestoreProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<ItemRestoreProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ItemRestoreProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_item_restore_proposal:" + proposalId}, 0))", cancellationToken);
}
