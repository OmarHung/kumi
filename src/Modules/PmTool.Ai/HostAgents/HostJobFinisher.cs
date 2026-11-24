using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>工作怎麼結束：狀態、原因、是否結果不明、是不是 worker 自己送的結束，以及引擎回報的 token。</summary>
internal sealed record HostFinish(AssistantMessageStatus Status, string? Error)
{
    public bool OutcomeUnknown { get; init; }

    public bool ByWorker { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    /// <summary>只結束還沒被領走的工作：排隊逾時的判斷與領走同時發生時，領走的贏，不能把已經在跑的當成沒輪到。</summary>
    public bool OnlyBeforeClaim { get; init; }

    /// <summary>
    /// 租約逾時的工作若領走之後一次都沒回報，改成「沒有開始執行」（不是結果不明、用量退還）。
    /// 在鎖住工作列之後才判斷，回報與逾時同時發生時以資料庫裡的為準。
    /// </summary>
    public bool CheckNeverStarted { get; init; }
}

/// <summary>
/// 結束一件工作與它的用量，所有路（worker 結束、逾時、取消、停用來源）都經過這裡：
/// 工作一直沒被領走過（<c>claimed_at</c> 是 null）的用量退還，領走過的結算（含失敗與中斷：已經用到引擎）。
/// 鎖住工作列再改，所以和領工作、回報互斥；已經結束的什麼都不改（回 false）。
/// </summary>
internal static class HostJobFinisher
{
    /// <summary>工作列的 <c>FOR UPDATE</c>：同一件工作的領取、回報、結束排隊。</summary>
    public static async Task<HostJobRow?> LockAsync(AiDbContext db, Guid jobId, CancellationToken cancellationToken)
    {
        var rows = await db.HostJobs.FromSql($"SELECT * FROM ai.host_jobs WHERE id = {jobId} FOR UPDATE")
            .AsNoTracking().ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }

    public static async Task<bool> FinishAsync(
        AiDbContext db, Guid jobId, HostFinish finish, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var owned = db.Database.CurrentTransaction is null;
        await using var transaction = owned ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var job = await LockAsync(db, jobId, cancellationToken);
        if (job is null || job.State == HostJobStates.Finished || (finish.OnlyBeforeClaim && job.ClaimedAt is not null))
        {
            return false;
        }

        // 領走時 last_report_at 等於 claimed_at，之後的回報（含空的心跳）才會更新它。
        var neverStarted = job.ClaimedAt is { } claimed && job.EventCount == 0 && (job.LastReportAt is null || job.LastReportAt <= claimed);
        if (finish.CheckNeverStarted && neverStarted)
        {
            finish = finish with { Error = HostMessages.NeverStarted, OutcomeUnknown = false };
        }

        var status = AssistantMessageMapper.StatusName(finish.Status);
        var error = finish.Error is null ? null : AiText.Cut(finish.Error, AiRules.MaxErrorLength);

        // 引擎根本沒跑起來（worker 回報失敗、沒有任何進度與用量，或一次都沒回報）的不算用量。
        var engineNeverRan = finish.Status == AssistantMessageStatus.Failed && finish.InputTokens is null && finish.OutputTokens is null
            && ((finish.ByWorker && job.EventCount == 0) || (finish.CheckNeverStarted && neverStarted));
        var ran = job.ClaimedAt is not null && !engineNeverRan;
        await db.HostJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.State, HostJobStates.Finished)
                .SetProperty(j => j.FinishStatus, status)
                .SetProperty(j => j.FinishError, error)
                .SetProperty(j => j.OutcomeUnknown, finish.OutcomeUnknown)
                .SetProperty(j => j.FinishedByWorker, finish.ByWorker)
                .SetProperty(j => j.InputTokens, finish.InputTokens)
                .SetProperty(j => j.OutputTokens, finish.OutputTokens)

                // 結束的工作不再有工具授權；內容（整段對話）不留到整列刪掉的時候。
                .SetProperty(j => j.ToolTokenHash, (string?)null)
                .SetProperty(j => j.Payload, (string?)null),
            cancellationToken);

        var usageState = ran ? HostUsageStates.Settled : HostUsageStates.Released;
        var input = finish.InputTokens ?? 0;
        var output = finish.OutputTokens ?? 0;
        await db.HostUsage.Where(u => u.Id == jobId && u.State == HostUsageStates.Reserved).ExecuteUpdateAsync(
            set => set
                .SetProperty(u => u.State, usageState)
                .SetProperty(u => u.FinishStatus, status)
                .SetProperty(u => u.InputTokens, input)
                .SetProperty(u => u.OutputTokens, output)
                .SetProperty(u => u.SettledAt, now),
            cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// 擁有者的動作（停用、登出、移除、關掉來源）讓一批工作取消：還沒領走的直接結束（用量退還），執行中的標上取消，
    /// worker 下一次回報時停下、工具授權立刻失效。呼叫端在自己的交易裡、已經拿了來源的 advisory lock。
    /// </summary>
    public static async Task CancelAsync(
        AiDbContext db, IQueryable<HostJobRow> jobs, string message, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var pending = await jobs
            .Where(j => j.State == HostJobStates.Reserved || j.State == HostJobStates.Queued)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in pending)
        {
            await FinishAsync(db, id, new HostFinish(AssistantMessageStatus.Failed, message), now, cancellationToken);
        }

        var interrupted = HostMessages.Interrupted(message);
        await jobs.Where(j => j.State == HostJobStates.Running).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.CancelRequested, true)
                .SetProperty(j => j.CancelMessage, interrupted)
                .SetProperty(j => j.ToolTokenHash, (string?)null),
            cancellationToken);
    }

    /// <summary>來源的 advisory lock：預留、領工作、擁有者的動作彼此排隊，數出來的名額才不會被同時的請求搶先。</summary>
    public static Task LockSourceAsync(AiDbContext db, Guid sourceId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_host_source:" + sourceId}, 0))", cancellationToken);

    /// <summary>組織層級的寫入（新增代理、來源、登入）排隊。</summary>
    public static Task LockOrganizationAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_host:" + organizationId}, 0))", cancellationToken);

    /// <summary>依編號排序一個一個鎖：兩個動作要鎖同一批來源時不會互相等。</summary>
    public static async Task LockSourcesAsync(AiDbContext db, IEnumerable<Guid> sourceIds, CancellationToken cancellationToken)
    {
        foreach (var id in sourceIds.Distinct().OrderBy(id => id))
        {
            await LockSourceAsync(db, id, cancellationToken);
        }
    }
}
