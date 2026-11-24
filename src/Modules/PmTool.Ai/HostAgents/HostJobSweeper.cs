using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 轉送（回覆的執行器）正常會自己結束逾時的工作、清掉進度；程序在回覆中途當掉或重啟時留下的，由這裡順手清（worker 每次登記時呼叫），
/// 不另外開背景工作。門檻都比轉送自己的多一個 <see cref="HostLimits.JobGrace"/>，不和它搶。
/// </summary>
internal static class HostJobSweeper
{
    public static async Task SweepAsync(
        AiDbContext db, Guid organizationId, DateTimeOffset now, HostTimings timings, TimeSpan replyTimeout, CancellationToken cancellationToken)
    {
        var jobs = db.HostJobs.AsNoTracking().Where(j => j.OrganizationId == organizationId);
        var reservedBefore = now - HostLimits.ReservedExpiry - HostLimits.JobGrace;
        var queuedBefore = now - timings.QueueTimeout - HostLimits.JobGrace;
        var silentBefore = now - timings.LeaseTimeout - HostLimits.JobGrace;
        var overranBefore = now - replyTimeout - timings.StopGrace - HostLimits.JobGrace;

        await FinishAllAsync(
            db,
            jobs.Where(j => j.State == HostJobStates.Reserved && j.CreatedAt < reservedBefore),
            new HostFinish(AssistantMessageStatus.Failed, HostMessages.Vanished) { OnlyBeforeClaim = true },
            now,
            cancellationToken);
        await FinishAllAsync(
            db,
            jobs.Where(j => j.State == HostJobStates.Queued && j.QueuedAt < queuedBefore),
            new HostFinish(AssistantMessageStatus.Failed, HostMessages.QueueTimeout) { OnlyBeforeClaim = true },
            now,
            cancellationToken);
        await FinishAllAsync(
            db,
            jobs.Where(j => j.State == HostJobStates.Running && j.LastReportAt < silentBefore),
            new HostFinish(AssistantMessageStatus.Failed, HostMessages.OutcomeUnknown) { OutcomeUnknown = true, CheckNeverStarted = true },
            now,
            cancellationToken);
        await FinishAllAsync(
            db,
            jobs.Where(j => j.State == HostJobStates.Running && j.ClaimedAt < overranBefore),
            new HostFinish(AssistantMessageStatus.Stopped, HostMessages.Timeout),
            now,
            cancellationToken);
        await DeleteOldAsync(db, organizationId, now, cancellationToken);
    }

    private static async Task FinishAllAsync(
        AiDbContext db, IQueryable<HostJobRow> jobs, HostFinish finish, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var ids = await jobs.Select(j => j.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await HostJobFinisher.FinishAsync(db, id, finish, now, cancellationToken);
        }
    }

    private static async Task DeleteOldAsync(AiDbContext db, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - HostLimits.JobRetention;
        var old = db.HostJobs.Where(j => j.OrganizationId == organizationId && j.State == HostJobStates.Finished && j.CreatedAt < cutoff);
        await db.HostJobEvents.Where(e => e.OrganizationId == organizationId && old.Any(j => j.Id == e.JobId)).ExecuteDeleteAsync(cancellationToken);
        await db.HostToolCalls.Where(c => c.OrganizationId == organizationId && old.Any(j => j.Id == c.JobId)).ExecuteDeleteAsync(cancellationToken);
        await old.ExecuteDeleteAsync(cancellationToken);
    }
}
