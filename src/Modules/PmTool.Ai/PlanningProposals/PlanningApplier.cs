using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 照順序把一份建議的週期與里程碑寫進工作引擎，一律用套用的人的身分。新增不是冪等的，所以每處理一筆就立刻把結果
/// （新增的編號、失敗的原因、「處理過了」的記號）記進資料庫，同時把 <c>decided_at</c> 刷新成現在當心跳。
/// 只有第一筆明確回 <see cref="Failure"/>（工作引擎沒有寫入）時才回傳失敗、呼叫的人可以退回待決定；
/// 其餘任何例外（含逾時的取消）都不保證工作引擎沒寫入，一律照丟、讓建議維持「建立中」。
/// 之後的失敗不復原已經寫的，繼續做下一筆。
/// </summary>
internal sealed class PlanningApplier(
    IDbContextFactory<AiDbContext> contexts, IPlanningService planning, TimeProvider time, ILogger logger)
{
    /// <summary>每一筆寫入的時限。不跟著請求取消：使用者關掉頁面不能讓套用停在一半。</summary>
    internal static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(60);

    public async Task<Result<IReadOnlyList<StoredPlanningChange>>> ApplyAsync(Actor actor, Guid proposalId, string projectKey, IReadOnlyList<StoredPlanningChange> changes)
    {
        var results = changes.ToList();
        var anyDone = false;
        for (var i = 0; i < results.Count; i++)
        {
            var outcome = await WriteOneAsync(actor, projectKey, results[i], anyDone);
            if (outcome.Failure is { } failure)
            {
                if (!anyDone)
                {
                    return failure;
                }

                results[i] = results[i] with { Error = AiText.Cut(failure.Message, AiRules.MaxErrorLength), Done = true };
            }
            else
            {
                results[i] = results[i] with { Id = outcome.Value ?? results[i].Id, Done = true };
            }

            anyDone = true;
            await SaveProgressAsync(proposalId, results);
        }

        return results;
    }

    // 已經有一筆處理過之後丟的例外不能讓整個請求變成 500、建議維持待決定（再按一次會重複套用）：當作那一筆失敗。
    // 第一筆丟例外時照常丟出去：工作引擎可能已經寫入（例如逾時的取消）。
    private async Task<Result<Guid?>> WriteOneAsync(Actor actor, string projectKey, StoredPlanningChange change, bool somethingDone)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout);
        try
        {
            return await DispatchAsync(actor, projectKey, change, timeout.Token);
        }
        catch (Exception exception) when (somethingDone && exception is not OutOfMemoryException)
        {
            // 只記例外的種類：訊息可能帶內部資訊。
            logger.LogError("套用週期與里程碑的建議時有一筆丟了例外：{ExceptionType}", exception.GetType().Name);
            return new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "發生未預期的錯誤。");
        }
    }

    private async Task<Result<Guid?>> DispatchAsync(Actor actor, string projectKey, StoredPlanningChange change, CancellationToken cancellationToken)
    {
        return change.Target == PlanningTarget.Cycle
            ? await DispatchCycleAsync(actor, projectKey, change, cancellationToken)
            : await DispatchMilestoneAsync(actor, projectKey, change, cancellationToken);
    }

    private async Task<Result<Guid?>> DispatchCycleAsync(Actor actor, string projectKey, StoredPlanningChange change, CancellationToken cancellationToken)
    {
        switch (change.Action)
        {
            case PlanningAction.Create:
                var values = change.After!;
                return Map(
                    await planning.CreateCycleAsync(actor, projectKey, new CycleDraft(values.Name, values.StartDate.GetValueOrDefault(), values.EndDate.GetValueOrDefault()), cancellationToken),
                    c => c.Id);
            case PlanningAction.Update:
                return await UpdateCycleAsync(actor, projectKey, change, cancellationToken);
            default:
                return Map(await planning.DeleteCycleAsync(actor, projectKey, change.Id!.Value, cancellationToken), _ => change.Id);
        }
    }

    // 修改只送這份建議真的要改的欄位（after 和 before 不同的），疊在「現在」的內容上：
    // 建議提出之後別人改過的其他欄位不會被改回舊的。已經被刪掉的記成這一筆的失敗。
    private async Task<Result<Guid?>> UpdateCycleAsync(Actor actor, string projectKey, StoredPlanningChange change, CancellationToken cancellationToken)
    {
        var listed = await planning.ListCyclesAsync(actor, projectKey, cancellationToken);
        if (listed.Failure is { } failure)
        {
            return failure;
        }

        var current = listed.Value.FirstOrDefault(c => c.Id == change.Id);
        if (current is null)
        {
            return new Failure(FailureKind.NotFound, WorkErrorCodes.CycleNotFound, "這個週期已經不存在了（可能被別人刪掉），沒有修改。");
        }

        PlanningValues before = change.Before!, after = change.After!;
        var draft = new CycleDraft(
            after.Name != before.Name ? after.Name : current.Name,
            after.StartDate != before.StartDate ? after.StartDate.GetValueOrDefault() : current.StartDate,
            after.EndDate != before.EndDate ? after.EndDate.GetValueOrDefault() : current.EndDate);
        return Map(await planning.UpdateCycleAsync(actor, projectKey, current.Id, draft, cancellationToken), c => c.Id);
    }

    private async Task<Result<Guid?>> DispatchMilestoneAsync(Actor actor, string projectKey, StoredPlanningChange change, CancellationToken cancellationToken)
    {
        switch (change.Action)
        {
            case PlanningAction.Create:
                var values = change.After!;
                return Map(
                    await planning.CreateMilestoneAsync(actor, projectKey, new MilestoneDraft(values.Name, values.DueDate.GetValueOrDefault(), values.Description), cancellationToken),
                    m => m.Id);
            case PlanningAction.Update:
                return await UpdateMilestoneAsync(actor, projectKey, change, cancellationToken);
            default:
                return Map(await planning.DeleteMilestoneAsync(actor, projectKey, change.Id!.Value, cancellationToken), _ => change.Id);
        }
    }

    private async Task<Result<Guid?>> UpdateMilestoneAsync(Actor actor, string projectKey, StoredPlanningChange change, CancellationToken cancellationToken)
    {
        var listed = await planning.ListMilestonesAsync(actor, projectKey, cancellationToken);
        if (listed.Failure is { } failure)
        {
            return failure;
        }

        var current = listed.Value.FirstOrDefault(m => m.Id == change.Id);
        if (current is null)
        {
            return new Failure(FailureKind.NotFound, WorkErrorCodes.MilestoneNotFound, "這個里程碑已經不存在了（可能被別人刪掉），沒有修改。");
        }

        PlanningValues before = change.Before!, after = change.After!;
        var draft = new MilestoneDraft(
            after.Name != before.Name ? after.Name : current.Name,
            after.DueDate != before.DueDate ? after.DueDate.GetValueOrDefault() : current.DueDate,
            after.Description != before.Description ? after.Description : current.Description);
        return Map(await planning.UpdateMilestoneAsync(actor, projectKey, current.Id, draft, cancellationToken), m => m.Id);
    }

    private static Result<Guid?> Map<T>(Result<T> result, Func<T, Guid?> id) =>
        result.Failure is { } failure ? failure : id(result.Value);

    // 失敗時重試一次；還是失敗就丟出去：已經有寫入了，呼叫的人不能退回待決定。
    private async Task SaveProgressAsync(Guid proposalId, IReadOnlyList<StoredPlanningChange> changes)
    {
        try
        {
            await WriteProgressAsync(proposalId, changes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("記下週期與里程碑的套用進度失敗，重試一次：{ExceptionType}", exception.GetType().Name);
            await WriteProgressAsync(proposalId, changes);
        }
    }

    private async Task WriteProgressAsync(Guid proposalId, IReadOnlyList<StoredPlanningChange> changes)
    {
        var json = PlanningProposalMapper.WriteChanges(changes);
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.PlanningProposals.Where(p => p.Id == proposalId).ExecuteUpdateAsync(
            set => set.SetProperty(p => p.Changes, json).SetProperty(p => p.DecidedAt, now), CancellationToken.None);
    }
}
