using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.NewItems;

/// <summary>
/// 新增項目建議的讀取、套用、作廢（<see cref="INewItemProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.NewItemProposalNotFound"/>。套用與作廢在同一個 advisory lock 裡排隊。
/// </summary>
internal sealed class NewItemProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, NewItemApplier applier, TimeProvider time) : INewItemProposalService
{
    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.NewItemProposalNotFound, "找不到這份新增項目的建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.NewItemProposalClosed, "這份新增項目的建議已經套用、作廢，或正在建立。");

    public async Task<Result<NewItemProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return NewItemMapper.ToProposal(row, time.GetUtcNow());
    }

    public async Task<Result<NewItemProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != NewItemProposalRow.PendingStatus)
        {
            return Closed;
        }

        // 先標成「建立中」並提交：建立不是冪等的，之後不管請求怎麼結束，同一份建議都不會被建立第二次。
        var startedAt = time.GetUtcNow();
        await db.NewItemProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, NewItemProposalRow.ApplyingStatus).SetProperty(p => p.DecidedAt, startedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return await RunAsync(db, actor, row with { DecidedAt = startedAt }, CancellationToken.None);
    }

    public async Task<Result<NewItemProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != NewItemProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.NewItemProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, NewItemProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return NewItemMapper.ToProposal(row with { Status = NewItemProposalRow.DismissedStatus, DecidedAt = decidedAt }, decidedAt);
    }

    private async Task<Result<NewItemProposal>> RunAsync(AiDbContext db, Actor actor, NewItemProposalRow row, CancellationToken cancellationToken)
    {
        // 不接例外：applier 只在第一個項目明確回失敗（工作引擎沒有寫入）時回 Failure。丟例外（含逾時的取消、記編號失敗）
        // 時工作引擎可能已經寫入，不能退回待決定（再按一次會重複建立），維持「建立中」，讀出來時過了時限就是已套用加說明。
        var outcome = await applier.ApplyAsync(actor, row.Id, row.ProjectKey, NewItemMapper.ReadItems(row.Items));
        if (outcome.Failure is { } failure)
        {
            await RevertToPendingAsync(db, row.Id, cancellationToken);
            return failure;
        }

        var error = outcome.Value.Error is { } text ? AiText.Cut(text, AiRules.MaxErrorLength) : null;
        var keysJson = NewItemMapper.WriteKeys(outcome.Value.Keys);
        var decidedAt = time.GetUtcNow();
        await db.NewItemProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, NewItemProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.CreatedKeys, keysJson)
                .SetProperty(p => p.Error, error),
            cancellationToken);
        var finished = row with { Status = NewItemProposalRow.AppliedStatus, DecidedAt = decidedAt, CreatedKeys = keysJson, Error = error };
        return NewItemMapper.ToProposal(finished, time.GetUtcNow());
    }

    // 回到待決定；只在第一個項目明確失敗（什麼都沒建立）時呼叫。
    private static async Task RevertToPendingAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        await db.NewItemProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, NewItemProposalRow.PendingStatus).SetProperty(p => p.DecidedAt, (DateTimeOffset?)null),
            cancellationToken);

    // 專案已經看不到：建議也當作不存在，不再把項目的內容給他看。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, NewItemProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<NewItemProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.NewItemProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_new_item_proposal:" + proposalId}, 0))", cancellationToken);
}
