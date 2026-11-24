using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentWorkerService
{
    public async Task<Result<HostReportAck>> ReportAsync(
        HostAgentIdentity agent, Guid jobId, int lease, int firstSeq, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(events);
        var cleaned = CleanEvents(events, firstSeq);
        if (cleaned.Failure is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await HostJobFinisher.LockAsync(db, jobId, cancellationToken);
        if (!IsOurs(job, agent) || job!.State is HostJobStates.Reserved or HostJobStates.Queued)
        {
            return HostFailures.JobNotFound();
        }

        if (job.Lease != lease)
        {
            return HostFailures.LeaseLost();
        }

        if (job.State == HostJobStates.Finished)
        {
            // 伺服器自己取消的（停止、停用、登出）：告訴 worker 停下來；結果不明的、worker 自己已經結束的：租約已經沒有意義。
            return !job.FinishedByWorker && !job.OutcomeUnknown
                ? new HostReportAck(true, job.EventCount + 1)
                : HostFailures.LeaseLost();
        }

        var lastSeq = firstSeq + cleaned.Value.Count - 1;
        if (firstSeq > job.EventCount + 1)
        {
            return HostFailures.AgentInvalid("firstSeq", $"流水號跳號了：伺服器下一則要的是 {job.EventCount + 1}。");
        }

        var fresh = cleaned.Value.Select((progress, i) => (Progress: progress, Seq: firstSeq + i)).Where(p => p.Seq > job.EventCount).ToList();
        var kept = await LocalAgentService.KeepOwnProposalsAsync(
            db, new Actor(job.OrganizationId, job.PersonId, IsOrganizationAdmin: false), [.. fresh.Select(p => p.Progress)], cancellationToken);
        if (lastSeq > HostLimits.MaxEventsPerJob)
        {
            return HostFailures.AgentInvalid("events", $"一件工作最多 {HostLimits.MaxEventsPerJob} 則進度。");
        }

        for (var i = 0; i < fresh.Count; i++)
        {
            // 空字串的文字不存（只是白占一列）；流水號照樣算，兩邊的編號才對得上。
            if (kept[i] is LocalAgentText { Delta.Length: 0 })
            {
                continue;
            }

            db.HostJobEvents.Add(new HostJobEventRow
            {
                JobId = job.Id,
                OrganizationId = job.OrganizationId,
                Seq = fresh[i].Seq,
                Content = StoredEvent.Serialize(kept[i]),
            });
        }

        var count = Math.Max(job.EventCount, lastSeq);
        await db.SaveChangesAsync(cancellationToken);
        var now = time.GetUtcNow();
        await db.HostJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            set => set.SetProperty(j => j.EventCount, count).SetProperty(j => j.LastReportAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new HostReportAck(job.CancelRequested, count + 1);
    }

    public async Task<Result<Unit>> FinishAsync(
        HostAgentIdentity agent, Guid jobId, int lease, LocalAgentFinish finish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(finish);
        if (!Enum.IsDefined(finish.Status))
        {
            return HostFailures.AgentInvalid("status", "結束的狀態只能是 complete、stopped、failed。");
        }

        if (finish.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 })
        {
            return HostFailures.AgentInvalid("usage", "用量不能是負的。");
        }

        // worker 送的數字會進每月的合計：沒有上限的話，一個離譜的值就讓之後的合計溢位、整個來源的送出都失敗。
        if (finish.Usage is { } usage && (usage.InputTokens > HostLimits.MaxReportedTokens || usage.OutputTokens > HostLimits.MaxReportedTokens))
        {
            return HostFailures.AgentInvalid("usage", $"一件工作的 token 不會超過 {HostLimits.MaxReportedTokens:N0}。");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await HostJobFinisher.LockAsync(db, jobId, cancellationToken);
        if (!IsOurs(job, agent) || job!.State is HostJobStates.Reserved or HostJobStates.Queued)
        {
            return HostFailures.JobNotFound();
        }

        if (job.Lease != lease)
        {
            return HostFailures.LeaseLost();
        }

        // 重送同一件的結束不算錯，也不改第一次存的結果；伺服器已經結束的（逾時、結果不明）worker 的結束就沒有意義了。
        if (job.State == HostJobStates.Finished)
        {
            return job.FinishedByWorker ? Unit.Value : HostFailures.LeaseLost();
        }

        var error = finish.Error is null ? null : LocalAgentValidator.CleanText(finish.Error);
        var result = new HostFinish(finish.Status, error)
        {
            ByWorker = true,
            InputTokens = finish.Usage?.InputTokens,
            OutputTokens = finish.Usage?.OutputTokens,
        };
        await HostJobFinisher.FinishAsync(db, jobId, result, time.GetUtcNow(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 讓出的名額給下一件排隊的。
        signals.Signal(agent.AgentId);
        return Unit.Value;
    }

    private static bool IsOurs(HostJobRow? job, HostAgentIdentity agent) =>
        job is not null && job.OrganizationId == agent.OrganizationId && job.AgentId == agent.AgentId;

    private static Result<IReadOnlyList<LocalAgentEvent>> CleanEvents(IReadOnlyList<LocalAgentEvent> events, int firstSeq)
    {
        if (firstSeq < 1)
        {
            return HostFailures.AgentInvalid("firstSeq", "流水號從 1 起算。");
        }

        if (events.Count > AiRules.MaxLocalAgentEventsPerReport)
        {
            return HostFailures.AgentInvalid("events", $"一次回報最多 {AiRules.MaxLocalAgentEventsPerReport} 則進度。");
        }

        var cleaned = new List<LocalAgentEvent>(events.Count);
        foreach (var progress in events)
        {
            var checkedEvent = LocalAgentValidator.Progress(progress, cleaned.Count);
            if (checkedEvent.Failure is { } failure)
            {
                return HostFailures.AgentInvalid(failure.Details.FirstOrDefault()?.Path ?? "events", failure.Message);
            }

            cleaned.Add(checkedEvent.Value);
        }

        return cleaned;
    }
}
