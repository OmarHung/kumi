using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

internal sealed partial class HostAgentWorkerService
{
    public async Task<Result<HostCheckInResult>> CheckInAsync(
        HostAgentIdentity agent, HostAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(checkIn);
        var valid = HostValidator.CheckIn(checkIn);
        if (valid.Failure is { } invalid)
        {
            return invalid;
        }

        HostCheckInResult? registered;
        try
        {
            registered = await RegisterAsync(agent, valid.Value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new HostCheckInResult(null, [], false);
        }

        if (registered is null)
        {
            return HostFailures.AgentNotFound();
        }

        if (registered.Disabled && capacity > 0)
        {
            await WaitWhileDisabledAsync(agent, cancellationToken);
            return registered;
        }

        if (registered.Disabled || capacity <= 0)
        {
            return registered;
        }

        var job = await WaitForJobAsync(agent, capacity, cancellationToken);
        return registered with { Job = job };
    }

    /// <summary>
    /// 記下在線時間、版本、網址與兩個引擎的回報；算出要它登出的引擎（擁有者要求、而且它還說登入著的），
    /// worker 回報已經沒登入的就把要求清掉。順手清掉殘留的工作。代理被移除了是 null。
    /// </summary>
    private async Task<HostCheckInResult?> RegisterAsync(HostAgentIdentity identity, ValidatedCheckIn checkIn, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.HostAgents.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == identity.AgentId && a.OrganizationId == identity.OrganizationId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var requested = HostEnginesJson.DeserializeNames(row.LogoutRequested);
        var stillLoggedIn = checkIn.Engines.Where(e => e.LoggedIn).Select(e => AiEnumNames.NameOf(e.Kind)).ToHashSet();
        var logout = requested.Where(stillLoggedIn.Contains).ToList();
        var engines = HostEnginesJson.Serialize(checkIn.Engines);
        var remaining = HostEnginesJson.SerializeNames(logout);
        await db.HostAgents.Where(a => a.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.LastSeenAt, now)
                .SetProperty(a => a.AgentVersion, checkIn.AgentVersion)
                .SetProperty(a => a.ServerUrl, checkIn.ServerUrl)
                .SetProperty(a => a.Engines, engines)
                .SetProperty(a => a.LogoutRequested, remaining),
            cancellationToken);
        await HostJobSweeper.SweepAsync(db, identity.OrganizationId, now, timings, assistantTimings.ReplyTimeout, cancellationToken);
        return new HostCheckInResult(null, [.. logout.Select(AiEnumNames.ParseKind)], row.State == HostAgentStates.Disabled);
    }

    /// <summary>
    /// 先看有沒有可以領的（不開交易的 EXISTS），有才開交易領；沒有就等同一個程序內排進工作時發的訊號，
    /// 或最多等 <see cref="HostTimings.ClaimPollInterval"/> 再問資料庫（別的 API 程序收進來的工作）。呼叫端斷線時什麼都不領。
    /// </summary>
    private async Task<HostJob?> WaitForJobAsync(HostAgentIdentity agent, int capacity, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        try
        {
            while (true)
            {
                // 先取得訊號再查資料庫：訊號發在兩者之間也不會漏掉。
                var wake = signals.Next(agent.AgentId);
                if (await HasClaimableAsync(agent, cancellationToken) && await TryClaimAsync(agent, capacity, cancellationToken) is { } job)
                {
                    return job;
                }

                var remaining = timings.PollWait - time.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    return null;
                }

                await Task.WhenAny(wake, Task.Delay(remaining < timings.ClaimPollInterval ? remaining : timings.ClaimPollInterval, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 停用的代理照長輪詢的節奏等（重新啟用時的訊號會叫醒它），不要立刻回：worker 會馬上再登記，
    /// 每次登記都跑一遍殘留工作的清理，停用期間就變成每秒一次的空轉。
    /// </summary>
    private async Task WaitWhileDisabledAsync(HostAgentIdentity agent, CancellationToken cancellationToken)
    {
        await Task.WhenAny(signals.Next(agent.AgentId), Task.Delay(timings.PollWait, cancellationToken));
    }

    private async Task<bool> HasClaimableAsync(HostAgentIdentity agent, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.HostJobs.AsNoTracking().AnyAsync(
            j => j.OrganizationId == agent.OrganizationId && j.AgentId == agent.AgentId
                && j.State == HostJobStates.Queued && !j.CancelRequested,
            cancellationToken);
    }

    private async Task<HostJob?> TryClaimAsync(HostAgentIdentity agent, int capacity, CancellationToken cancellationToken)
    {
        List<Guid> sourceIds;
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            sourceIds = await db.HostSources.AsNoTracking()
                .Where(s => s.AgentId == agent.AgentId && s.OrganizationId == agent.OrganizationId && s.Enabled)
                .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        // 一個來源一個交易、各自拿自己的鎖：擁有者的動作依編號排序拿多個來源的鎖，這裡一次只拿一個，不會互等。
        foreach (var sourceId in sourceIds)
        {
            if (await TryClaimFromAsync(agent, sourceId, capacity, cancellationToken) is { } job)
            {
                return job;
            }
        }

        return null;
    }

    private async Task<HostJob?> TryClaimFromAsync(HostAgentIdentity identity, Guid sourceId, int capacity, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockSourceAsync(db, sourceId, cancellationToken);
        var view = await HostReadiness.LoadViewAsync(db, identity.OrganizationId, sourceId, cancellationToken);
        if (view is not { Source.Enabled: true, Agent.State: HostAgentStates.Active, Engine.Readiness: HostEngineReadiness.Ready })
        {
            return null;
        }

        var sourceRunning = await db.HostJobs.CountAsync(j => j.SourceId == sourceId && j.State == HostJobStates.Running, cancellationToken);
        var agentRunning = await db.HostJobs.CountAsync(j => j.AgentId == identity.AgentId && j.State == HostJobStates.Running, cancellationToken);
        // capacity 是 worker 自己數的「還能多跑幾件」（和本機代理相同）；伺服器另外用它自己數的執行中件數擋住上限，
        // 兩邊對不上（例如 worker 剛重啟、舊的工作還沒結束）時取比較保守的那一個。
        if (sourceRunning >= AiRules.HostSourceMaxConcurrent || capacity <= 0 || agentRunning >= AiRules.HostWorkerMaxConcurrent)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var since = now - timings.QueueTimeout;
        for (var scan = 0; scan < HostLimits.MaxClaimScans; scan++)
        {
            var candidates = await db.HostJobs.FromSql($"""
                SELECT * FROM ai.host_jobs
                WHERE source_id = {sourceId} AND state = {HostJobStates.Queued} AND cancel_requested = false AND queued_at >= {since}
                ORDER BY seq
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """).AsNoTracking().ToListAsync(cancellationToken);
            if (candidates.Count == 0)
            {
                // 前面不派的（發問的人不是有效成員）已經結束，要提交。
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var job = candidates[0];
            if (!await people.IsMemberAsync(job.OrganizationId, job.PersonId, cancellationToken))
            {
                // 發問的人現在不是有效成員了：這件工作不派，用量退還，找下一件。
                await HostJobFinisher.FinishAsync(
                    db, job.Id, new HostFinish(AssistantMessageStatus.Failed, HostMessages.NotAMember) { OnlyBeforeClaim = true }, now, cancellationToken);
                continue;
            }

            var claimed = await ClaimAsync(db, job, view, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed;
        }

        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task<HostJob> ClaimAsync(
        AiDbContext db, HostJobRow job, HostSourceView view, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var payload = AssistantJson.Deserialize<StoredHostJob>(job.Payload!)!;
        var lease = RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var token = HostSecrets.NewToken(HostSecrets.ToolTokenPrefix);
        var tokenHash = HostSecrets.Hash(token);
        var expires = now + assistantTimings.ReplyTimeout + HostLimits.ToolTokenSlack;
        await db.HostJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.State, HostJobStates.Running)
                .SetProperty(j => j.ClaimedAt, now)
                .SetProperty(j => j.LastReportAt, now)
                .SetProperty(j => j.Lease, lease)
                .SetProperty(j => j.ToolTokenHash, tokenHash)
                .SetProperty(j => j.ToolTokenExpiresAt, expires),
            cancellationToken);
        return new HostJob(
            job.Id,
            lease,
            payload.Engine,
            payload.Model,
            payload.Effort,
            job.ProjectKey,
            payload.SystemPrompt,
            payload.Prompt,
            payload.AllowedTools,
            view.Agent.ServerUrl!.TrimEnd('/') + "/mcp",
            payload.MaxTurns,
            token,
            expires);
    }
}
