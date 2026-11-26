using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Attachments;
using PmTool.Ai.Maintenance;
using PmTool.Ai.Persistence;
using PmTool.Ai.Settings;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 本機代理那一頭（internal/HANDOFF-local-agent.md 第 4.2 節）。這個檔案是登記與領工作；回報與結束在 <c>LocalAgentService.Jobs.cs</c>。
/// 一件工作只能被「這個人、這個引擎所在的那台電腦」領走，不放寬：OpenAI 的條款要求別人的操作不能觸發使用者帳號的請求。
/// </summary>
internal sealed partial class LocalAgentService(
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time,
    LocalAgentTimings timings,
    AssistantTimings assistantTimings,
    LocalAgentSignals signals,
    LocalAgentMachineNotice machineNotice,
    AgentAttachmentReader attachmentReader) : ILocalAgentService
{
    public async Task<Result<LocalAgentJob?>> CheckInAsync(
        Actor actor, LocalAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(checkIn);
        if (!await AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken))
        {
            return LocalAgentFailures.Disabled();
        }

        var valid = LocalAgentValidator.CheckIn(checkIn);
        if (valid.Failure is { } invalid)
        {
            return invalid;
        }

        Guid agentId;
        try
        {
            var registered = await RegisterAsync(actor, valid.Value, cancellationToken);
            agentId = registered.AgentId;
            if (registered.ShouldNotify)
            {
                await machineNotice.PublishAsync(actor, valid.Value.MachineName, time.GetUtcNow());
            }
        }
        catch (OperationCanceledException)
        {
            return (LocalAgentJob?)null;
        }

        return capacity > 0 ? await WaitForJobAsync(actor, agentId, cancellationToken) : (LocalAgentJob?)null;
    }

    /// <summary>
    /// 在這個人的 advisory lock 裡登記電腦與引擎：同一個人的兩次登記（或兩台電腦同時登記）排隊，
    /// 「超過台數時換掉最舊的」才數得準。
    /// </summary>
    private async Task<(Guid AgentId, bool ShouldNotify)> RegisterAsync(Actor actor, LocalAgentCheckIn checkIn, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_local_agents:" + actor.OrganizationId + ":" + actor.PersonId}, 0))",
            cancellationToken);

        var now = time.GetUtcNow();
        var (agent, isNew) = await UpsertAgentAsync(db, actor, checkIn, now, cancellationToken);
        var shouldNotify = isNew && !await AnotherMachineAddedRecentlyAsync(db, actor, agent.Id, now, cancellationToken);
        await SyncEnginesAsync(db, actor, agent.Id, checkIn.Engines, cancellationToken);
        await TrimMachinesAsync(db, actor, agent.Id, cancellationToken);
        await ExpireAbandonedJobsAsync(db, actor, now, cancellationToken);
        await DeleteStaleJobsAsync(db, actor, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (agent.Id, shouldNotify);
    }

    /// <summary>
    /// 同一個人 <see cref="LocalAgentMachineNotice.Interval"/> 內只發一則「新電腦」的通知（審查 L-9）：拿到權杖的人
    /// 用一連串機器編號登記，不能用它洗本人的收件匣與信。用電腦自己的建立時間判斷，不另外記；在這個人的鎖裡數，同時兩個登記不會都通過。
    /// </summary>
    private static async Task<bool> AnotherMachineAddedRecentlyAsync(
        AiDbContext db, Actor actor, Guid agentId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now - LocalAgentMachineNotice.Interval;
        return await db.LocalAgents.AsNoTracking().AnyAsync(
            a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId && a.Id != agentId && a.CreatedAt >= since,
            cancellationToken);
    }

    private static async Task<(LocalAgentRow Agent, bool IsNew)> UpsertAgentAsync(
        AiDbContext db, Actor actor, LocalAgentCheckIn checkIn, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.LocalAgents.SingleOrDefaultAsync(
            a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId && a.MachineKey == checkIn.MachineKey,
            cancellationToken);
        if (existing is null)
        {
            var created = new LocalAgentRow
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = actor.OrganizationId,
                PersonId = actor.PersonId,
                MachineKey = checkIn.MachineKey,
                Name = checkIn.MachineName,
                AgentVersion = checkIn.AgentVersion,
                LastSeenAt = now,
                CreatedAt = now,
            };
            db.LocalAgents.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            return (created, true);
        }

        await db.LocalAgents.Where(a => a.Id == existing.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.Name, checkIn.MachineName)
                .SetProperty(a => a.AgentVersion, checkIn.AgentVersion)
                .SetProperty(a => a.LastSeenAt, now),
            cancellationToken);
        return (existing with { Name = checkIn.MachineName, AgentVersion = checkIn.AgentVersion, LastSeenAt = now }, false);
    }

    // 引擎的編號就是能選清單裡的連線編號：同一台電腦的同一種引擎一直用同一個，網頁選著的模型才不會因為重新登記而失效。
    private static async Task SyncEnginesAsync(
        AiDbContext db, Actor actor, Guid agentId, IReadOnlyList<LocalEngineInfo> engines, CancellationToken cancellationToken)
    {
        var kinds = engines.Select(e => AiEnumNames.NameOf(e.Kind)).ToList();
        await db.LocalEngines
            .Where(e => e.AgentId == agentId && !kinds.Contains(e.Kind))
            .ExecuteDeleteAsync(cancellationToken);
        var existing = await db.LocalEngines.AsNoTracking().Where(e => e.AgentId == agentId).ToListAsync(cancellationToken);
        foreach (var engine in engines)
        {
            var kind = AiEnumNames.NameOf(engine.Kind);
            var models = LocalModelsJson.Serialize(engine.Models);
            if (existing.FirstOrDefault(e => e.Kind == kind) is { } found)
            {
                await db.LocalEngines.Where(e => e.Id == found.Id).ExecuteUpdateAsync(
                    set => set.SetProperty(e => e.Version, engine.Version).SetProperty(e => e.Models, models),
                    cancellationToken);
                continue;
            }

            db.LocalEngines.Add(new LocalEngineRow
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = actor.OrganizationId,
                AgentId = agentId,
                Kind = kind,
                Version = engine.Version,
                Models = models,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task TrimMachinesAsync(AiDbContext db, Actor actor, Guid keepId, CancellationToken cancellationToken)
    {
        var machines = await db.LocalAgents.AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.PersonId == actor.PersonId)
            .OrderByDescending(a => a.LastSeenAt)
            .ThenByDescending(a => a.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        var surplus = machines.Skip(AiRules.MaxLocalAgentMachines).Where(id => id != keepId).ToList();
        if (surplus.Count > 0)
        {
            await db.LocalAgents.Where(a => surplus.Contains(a.Id)).ExecuteDeleteAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 程序重啟、回覆的執行器不見了之後殘留的工作：領走或等領、卻超過回覆的逾時加寬限還沒結束的，標成結束加取消，
    /// 本機代理下一次回報就會停下引擎。
    /// </summary>
    private async Task ExpireAbandonedJobsAsync(AiDbContext db, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - AbandonedAfter;
        await db.LocalJobs
            .Where(j => j.OrganizationId == actor.OrganizationId && j.PersonId == actor.PersonId
                && j.State != LocalJobStates.Finished && j.CreatedAt < cutoff)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, LocalJobStates.Finished)
                    .SetProperty(j => j.CancelRequested, true)
                    .SetProperty(j => j.Payload, LocalJobRetention.EmptyPayload)
                    .SetProperty(j => j.FinishStatus, AssistantMessageMapper.StatusName(AssistantMessageStatus.Failed)),
                cancellationToken);
    }

    private TimeSpan AbandonedAfter => assistantTimings.ReplyTimeout + LocalAgentLimits.JobGrace;

    // 回覆的執行器正常會自己清掉進度；程序在回覆中途當掉時留下的，在這裡順手清，不另外開背景工作。
    private static async Task DeleteStaleJobsAsync(AiDbContext db, Actor actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - LocalAgentLimits.JobRetention;
        var stale = db.LocalJobs.Where(j => j.OrganizationId == actor.OrganizationId && j.PersonId == actor.PersonId && j.CreatedAt < cutoff);
        await db.LocalEvents
            .Where(e => e.OrganizationId == actor.OrganizationId && stale.Any(j => j.Id == e.JobId))
            .ExecuteDeleteAsync(cancellationToken);
        await stale.ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// 先看有沒有可以領的（不開交易的 EXISTS），有才開交易領；沒有就等同一個程序內建立工作時發的訊號，
    /// 或最多等 <see cref="LocalAgentLimits.ClaimPollInterval"/> 再問資料庫（別的 API 程序收進來的工作）。
    /// 呼叫端斷線時立刻回 null，什麼都不領。
    /// </summary>
    private async Task<Result<LocalAgentJob?>> WaitForJobAsync(Actor actor, Guid agentId, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        try
        {
            while (true)
            {
                // 先取得訊號再查資料庫：訊號發在兩者之間也不會漏掉。
                var wake = signals.Next(actor.PersonId);
                if (await HasClaimableAsync(actor, agentId, cancellationToken)
                    && await TryClaimAsync(actor, agentId, cancellationToken) is { } job)
                {
                    return job;
                }

                var remaining = AiRules.LocalAgentPollWait - time.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    return (LocalAgentJob?)null;
                }

                var pause = Task.Delay(remaining < LocalAgentLimits.ClaimPollInterval ? remaining : LocalAgentLimits.ClaimPollInterval, cancellationToken);
                await Task.WhenAny(wake, pause);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            return (LocalAgentJob?)null;
        }
    }

    // 只領還新的工作：超過「領走的逾時加寬限」的，回覆那一頭早就放棄了（程序重啟後的殘留）。
    private DateTimeOffset ClaimableSince => time.GetUtcNow() - (timings.ClaimTimeout + LocalAgentLimits.JobGrace);

    private async Task<bool> HasClaimableAsync(Actor actor, Guid agentId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var since = ClaimableSince;
        return await db.LocalJobs.AsNoTracking().AnyAsync(
            j => j.OrganizationId == actor.OrganizationId && j.PersonId == actor.PersonId
                && j.State == LocalJobStates.Waiting && !j.CancelRequested && j.CreatedAt >= since
                && db.LocalEngines.Any(e => e.AgentId == agentId && e.Id == j.EngineId),
            cancellationToken);
    }

    /// <summary>
    /// 領這台電腦的引擎、這個人、還在等的最舊的一件。<c>FOR UPDATE SKIP LOCKED</c>：兩個同時來領的請求不會領到同一件，也不互相等。
    /// 已經要求取消的不領（使用者按了停止，或伺服器放棄等了）。
    /// </summary>
    private async Task<LocalAgentJob?> TryClaimAsync(Actor actor, Guid agentId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var since = ClaimableSince;
        var rows = await db.LocalJobs.FromSql($"""
            SELECT j.* FROM ai.local_agent_jobs j
            WHERE j.organization_id = {org} AND j.person_id = {person}
              AND j.state = {LocalJobStates.Waiting} AND j.cancel_requested = false AND j.created_at >= {since}
              AND j.engine_id IN (SELECT e.id FROM ai.local_engines e WHERE e.agent_id = {agentId})
            ORDER BY j.created_at, j.id
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).AsNoTracking().ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        var now = time.GetUtcNow();
        var row = rows[0];
        await db.LocalJobs.Where(j => j.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.State, LocalJobStates.Claimed)
                // 整段對話只經這一次回應交給本機代理，領走之後伺服器不再留著（AG-16）；上面已經讀進 row。
                .SetProperty(j => j.Payload, LocalJobRetention.EmptyPayload)
                .SetProperty(j => j.ClaimedAt, now)
                .SetProperty(j => j.LastReportAt, now),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AssistantJson.Deserialize<LocalAgentJob>(row.Payload);
    }
}
