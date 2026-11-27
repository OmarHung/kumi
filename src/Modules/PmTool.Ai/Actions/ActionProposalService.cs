using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Actions;

/// <summary>
/// 讀、套用、作廢動作建議（<see cref="IActionProposalService"/>）。一定要是建立的人、同一個組織；別人的、不存在的、
/// 專案已經看不到的都是 <see cref="AiErrorCodes.ActionProposalNotFound"/>。套用與作廢在同一個 advisory lock 裡排隊。
/// </summary>
internal sealed class ActionProposalService(
    IDbContextFactory<AiDbContext> contexts, IProjectService projects, IEnumerable<IActionProposalHandler> handlers, TimeProvider time)
    : IActionProposalService
{
    private static readonly Failure NotFound =
        new(FailureKind.NotFound, AiErrorCodes.ActionProposalNotFound, "找不到這份建議。");

    private static readonly Failure Closed =
        new(FailureKind.Conflict, AiErrorCodes.ActionProposalClosed, "這份建議已經套用、作廢，或正在套用。");

    public async Task<Result<ActionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        return ActionProposalMapper.ToProposal(row, time.GetUtcNow());
    }

    public async Task<Result<ActionProposal>> ApplyAsync(
        Actor actor, Guid proposalId, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(inputs);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(db, proposalId, cancellationToken);
        var row = await FindAsync(db, actor, proposalId, cancellationToken);
        if (row is null || !await IsProjectVisibleAsync(actor, row, cancellationToken))
        {
            return NotFound;
        }

        if (row.Status != ActionProposalRow.PendingStatus)
        {
            return Closed;
        }

        var handler = handlers.LastOrDefault(h => h.Kind == row.Kind);
        if (handler is null)
        {
            return NotFound;
        }

        if (CheckInputs(ActionProposalMapper.ReadInputs(row.Inputs), inputs) is { } missing)
        {
            return missing;
        }

        // 先標成「建立中」並提交：有些步驟不冪等，之後不管請求怎麼結束，同一份建議都不會被套用第二次。
        var startedAt = time.GetUtcNow();
        await db.ActionProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ActionProposalRow.ApplyingStatus).SetProperty(p => p.DecidedAt, startedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return await RunAsync(db, handler, new ActionContext(actor, row.ProjectKey, row.ConversationId), row, inputs);
    }

    public async Task<Result<ActionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken)
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

        if (row.Status != ActionProposalRow.PendingStatus)
        {
            return Closed;
        }

        var decidedAt = time.GetUtcNow();
        await db.ActionProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ActionProposalRow.DismissedStatus).SetProperty(p => p.DecidedAt, decidedAt),
            CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
        return ActionProposalMapper.ToProposal(row with { Status = ActionProposalRow.DismissedStatus, DecidedAt = decidedAt }, decidedAt);
    }

    // 宣告的輸入都是必填；訊息只寫欄位名稱，不回顯使用者填的值。
    private static Failure? CheckInputs(IReadOnlyList<ActionInput> declared, IReadOnlyDictionary<string, string> given)
    {
        var details = declared
            .Where(input => !given.TryGetValue(input.Key, out var value) || string.IsNullOrWhiteSpace(value))
            .Select(input => new FailureDetail(AiErrorCodes.FieldInvalid, "inputs." + input.Key, $"請填「{input.Label}」。"))
            .ToList();
        return details.Count == 0
            ? null
            : new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "還有欄位沒填，沒有套用。") { Details = details };
    }

    private async Task<Result<ActionProposal>> RunAsync(
        AiDbContext db, IActionProposalHandler handler, ActionContext context, ActionProposalRow row, IReadOnlyDictionary<string, string> inputs)
    {
        // 不接例外：處理器只在這一步明確被拒絕（還沒寫入）時回 Failure。丟例外（含逾時、記結果失敗）時可能已經寫入，
        // 不能退回待決定（再按一次會重複做），維持「建立中」，讀出來時過了時限就是已套用加說明。
        using var state = System.Text.Json.JsonDocument.Parse(row.State);
        var steps = ActionProposalMapper.ReadSteps(row.Steps).ToList();
        for (var index = 0; index < steps.Count; index++)
        {
            var result = await handler.ApplyStepAsync(context, state.RootElement, index, inputs, CancellationToken.None);
            if (result.Failure is { } failure && index == 0 && !result.Wrote)
            {
                await RevertToPendingAsync(db, row.Id);
                return failure;
            }

            steps[index] = result.Failure is { } failed
                ? steps[index] with { Error = AiText.Cut(failed.Message, AiRules.MaxErrorLength), Link = result.Wrote ? SafeLink(result.Link) : null }
                : steps[index] with { Done = true, Link = SafeLink(result.Link) };
            await SaveProgressAsync(db, row.Id, steps);
        }

        var error = ActionProposalMapper.Summarize(steps);
        var decidedAt = time.GetUtcNow();
        var stepsJson = ActionProposalMapper.WriteSteps(steps);
        await db.ActionProposals.Where(p => p.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(p => p.Status, ActionProposalRow.AppliedStatus)
                .SetProperty(p => p.DecidedAt, decidedAt)
                .SetProperty(p => p.Steps, stepsJson)
                .SetProperty(p => p.Error, error),
            CancellationToken.None);
        return ActionProposalMapper.ToProposal(
            row with { Status = ActionProposalRow.AppliedStatus, DecidedAt = decidedAt, Steps = stepsJson, Error = error }, decidedAt);
    }

    // 只收站內的相對路徑，處理器寫錯時也不會讓卡片連到外面。
    private static string? SafeLink(string? link) =>
        link is not null && link.StartsWith('/') && !link.StartsWith("//", StringComparison.Ordinal) && !link.Any(char.IsControl) ? link : null;

    // 每做完一步就記下結果，順便刷新 decided_at 當心跳。
    private Task<int> SaveProgressAsync(AiDbContext db, Guid proposalId, IReadOnlyList<ActionStep> steps)
    {
        var json = ActionProposalMapper.WriteSteps(steps);
        var beat = time.GetUtcNow();
        return db.ActionProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Steps, json).SetProperty(p => p.DecidedAt, beat), CancellationToken.None);
    }

    private static async Task RevertToPendingAsync(AiDbContext db, Guid proposalId) =>
        await db.ActionProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Status, ActionProposalRow.PendingStatus).SetProperty(p => p.DecidedAt, (DateTimeOffset?)null),
            CancellationToken.None);

    private async Task<bool> IsProjectVisibleAsync(Actor actor, ActionProposalRow row, CancellationToken cancellationToken) =>
        row.ProjectKey is null || (await projects.GetAsync(actor, row.ProjectKey, cancellationToken)).Failure is null;

    private static Task<ActionProposalRow?> FindAsync(AiDbContext db, Actor actor, Guid proposalId, CancellationToken cancellationToken) =>
        db.ActionProposals.AsNoTracking().SingleOrDefaultAsync(
            p => p.Id == proposalId && p.OrganizationId == actor.OrganizationId && p.PersonId == actor.PersonId, cancellationToken);

    private static Task<int> LockAsync(AiDbContext db, Guid proposalId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_action_proposal:" + proposalId}, 0))", cancellationToken);
}
