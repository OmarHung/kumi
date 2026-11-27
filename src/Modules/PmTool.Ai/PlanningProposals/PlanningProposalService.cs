using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 週期與里程碑建議的讀取、套用、作廢（<see cref="IPlanningProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.PlanningProposalNotFound"/>。套用與作廢在同一個 advisory lock 裡排隊。
/// </summary>
internal sealed class PlanningProposalService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IPlanningService planning,
    TimeProvider time,
    ILogger<PlanningProposalService> logger) : IPlanningProposalService
{
    private readonly PlanningApplier _applier = new(contexts, planning, time, logger);

    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.PlanningProposalNotFound, "找不到這份週期與里程碑的建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.PlanningProposalClosed, "這份週期與里程碑的建議已經套用、作廢，或正在套用。");

    public async Task<Result<PlanningProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return PlanningProposalMapper.ToProposal(row, time.GetUtcNow());
    }

    public async Task<Result<PlanningProposal>> ApplyAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != PlanningProposalRow.PendingStatus)
        {
            return Closed;
        }

        // 先標成「建立中」並提交：新增不是冪等的，之後不管請求怎麼結束，同一份建議都不會被套用第二次。
        var startedAt = time.GetUtcNow();
        await db.PlanningProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, PlanningProposalRow.ApplyingStatus).SetProperty(p => p.DecidedAt, startedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return await RunAsync(db, actor, row with { DecidedAt = startedAt }, CancellationToken.None);
    }

    public async Task<Result<PlanningProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != PlanningProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.PlanningProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, PlanningProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return PlanningProposalMapper.ToProposal(row with { Status = PlanningProposalRow.DismissedStatus, DecidedAt = decidedAt }, decidedAt);
    }

    private async Task<Result<PlanningProposal>> RunAsync(AiDbContext db, Actor actor, PlanningProposalRow row, CancellationToken cancellationToken)
    {
        // 不接例外：applier 只在第一筆明確回 Failure（工作引擎沒有寫入）時回 Failure。丟例外（含逾時的取消、記進度失敗）
        // 時工作引擎可能已經寫入，不能退回待決定（再按一次會重複新增），維持「建立中」，讀出來時過了時限就是已套用加說明。
        var outcome = await _applier.ApplyAsync(actor, row.Id, row.ProjectKey, PlanningProposalMapper.ReadChanges(row.Changes));
        if (outcome.Failure is { } failure)
        {
            await RevertToPendingAsync(db, row.Id, cancellationToken);
            return failure;
        }

        var failed = outcome.Value.Count(c => c.Error is not null);
        var error = failed == 0 ? null : AiText.Cut($"有 {failed} 筆沒有套用成功，原因在各筆的說明。其餘已經套用。", AiRules.MaxErrorLength);
        var changesJson = PlanningProposalMapper.WriteChanges(outcome.Value);
        var decidedAt = time.GetUtcNow();
        await db.PlanningProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, PlanningProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Changes, changesJson)
                .SetProperty(p => p.Error, error),
            cancellationToken);
        var finished = row with { Status = PlanningProposalRow.AppliedStatus, DecidedAt = decidedAt, Changes = changesJson, Error = error };
        return PlanningProposalMapper.ToProposal(finished, decidedAt);
    }

    // 回到待決定；只在第一筆明確失敗（什麼都沒寫入）時呼叫。
    private static async Task RevertToPendingAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        await db.PlanningProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, PlanningProposalRow.PendingStatus).SetProperty(p => p.DecidedAt, (DateTimeOffset?)null),
            cancellationToken);

    // 專案已經看不到：建議也當作不存在。
    private async Task<bool> IsProjectVisibleAsync(Actor actor, PlanningProposalRow row, CancellationToken cancellationToken) =>
        (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<PlanningProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.PlanningProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_planning_proposal:" + proposalId}, 0))", cancellationToken);
}
