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

        Result<HostCheckInResult> registration;
        try
        {
            registration = await RegisterAsync(agent, valid.Value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new HostCheckInResult(null, [], false);
        }

        if (registration.Failure is { } refused)
        {
            return refused;
        }

        var registered = registration.Value;
        // 停用的代理認證就過不了；這裡只會是認證之後才被停用的那一瞬間，直接回，不等。
        if (registered.Disabled || capacity <= 0)
        {
            return registered;
        }

        // 這次登記就要它登出的照樣等：登出失敗、CLI 還登入著時，每次登記都會再要求一次，不等的話 worker 會不停地登記、重跑登出。
        var loggedIn = valid.Value.Engines
            .Where(e => e.LoggedIn && !registered.Logout.Contains(e.Kind))
            .Select(e => AiEnumNames.NameOf(e.Kind))
            .ToHashSet();
        var (job, logout) = await WaitForJobAsync(agent, capacity, loggedIn, cancellationToken);
        return registered with { Job = job, Logout = [.. registered.Logout.Union(logout)] };
    }

    /// <summary>
    /// 記下在線時間、版本、網址與兩個引擎的回報；算出要它登出的引擎（擁有者要求、而且它還說登入著的），
    /// worker 回報已經沒登入的就把要求清掉。順手清掉殘留的工作。代理被移除了是 <see cref="HostFailures.AgentNotFound"/>；
    /// 機器編號和配對時的不同：什麼都不登記，只記下警告（<see cref="HostFailures.MachineMismatch"/>）。
    /// 機器憑證剩不到 <see cref="AiRules.HostCredentialRenewBefore"/> 時發下一把（雜湊存起來，明文只在回應裡）。
    /// </summary>
    private async Task<Result<HostCheckInResult>> RegisterAsync(HostAgentIdentity identity, ValidatedCheckIn checkIn, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.HostAgents.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == identity.AgentId && a.OrganizationId == identity.OrganizationId, cancellationToken);
        if (row is null)
        {
            return HostFailures.AgentNotFound();
        }

        var now = time.GetUtcNow();
        if (!string.Equals(row.MachineKey, checkIn.MachineKey, StringComparison.Ordinal))
        {
            // 條件帶著狀態與讀到的憑證：這次登記讀完之後被重新配對、停用的，警告不能寫進新的配對。
            var readCredential = row.CredentialHash;
            await db.HostAgents.Where(a => a.Id == row.Id && a.State == HostAgentStates.Active && a.CredentialHash == readCredential).ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.SuspectMachineKey, checkIn.MachineKey)
                    .SetProperty(a => a.SuspectMachineName, checkIn.MachineName)
                    .SetProperty(a => a.SuspectSeenAt, now),
                cancellationToken);
            return HostFailures.MachineMismatch();
        }

        var requested = HostEnginesJson.DeserializeNames(row.LogoutRequested);
        var stillLoggedIn = checkIn.Engines.Where(e => e.LoggedIn).Select(e => AiEnumNames.NameOf(e.Kind)).ToHashSet();
        var logout = requested.Where(stillLoggedIn.Contains).ToList();
        var engines = HostEnginesJson.Serialize(checkIn.Engines);
        var rotated = row.CredentialExpiresAt is { } expires && expires - now < AiRules.HostCredentialRenewBefore
            ? HostSecrets.NewToken(HostSecrets.CredentialPrefix)
            : null;
        await db.HostAgents.Where(a => a.Id == row.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(a => a.LastSeenAt, now)
                .SetProperty(a => a.AgentVersion, checkIn.AgentVersion)
                .SetProperty(a => a.ServerUrl, checkIn.ServerUrl)
                .SetProperty(a => a.Engines, engines),
            cancellationToken);
        // 只拿掉已經登出的那幾個，不把讀到的整份寫回：讀完之後擁有者剛按的登出不會被蓋掉。
        // 讀完之後才加入、而且已經登出的，下一次登記再拿掉。
        await HostLogoutRequests.RemoveAsync(db, row.Id, [.. requested.Where(name => !stillLoggedIn.Contains(name))], cancellationToken);
        if (rotated is not null && !await TryStoreNextCredentialAsync(db, row, rotated, now, cancellationToken))
        {
            rotated = null;
        }

        await HostJobSweeper.SweepAsync(db, identity.OrganizationId, now, timings, assistantTimings.ReplyTimeout, cancellationToken);
        return new HostCheckInResult(null, [.. logout.Select(AiEnumNames.ParseKind)], row.State == HostAgentStates.Disabled)
        {
            RotatedCredential = rotated,
        };
    }

    /// <summary>
    /// 單獨更新，不拿剛讀的舊值回寫（別的請求剛好把下一把升成目前的時，不會又寫回來）。條件：還是啟用中、
    /// 目前的憑證和下一把都和讀到的一樣（null 也比對）：讀完之後被重新配對、撤銷，或另一個登記已經先發了下一把的，
    /// 更新 0 筆、這次就不發（回 false），不會留下一把沒人拿得到、或屬於舊配對的有效憑證。
    /// </summary>
    private static async Task<bool> TryStoreNextCredentialAsync(
        AiDbContext db, HostAgentRow row, string rotated, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var readCredential = row.CredentialHash;
        var readNext = row.NextCredentialHash;
        var nextHash = HostSecrets.Hash(rotated);
        var nextExpires = now + AiRules.HostCredentialLifetime;
        var updated = await db.HostAgents
            .Where(a => a.Id == row.Id && a.State == HostAgentStates.Active
                && a.CredentialHash == readCredential && a.NextCredentialHash == readNext)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(a => a.NextCredentialHash, nextHash)
                    .SetProperty(a => a.NextCredentialExpiresAt, nextExpires),
                cancellationToken);
        return updated > 0;
    }

    /// <summary>
    /// 先看有沒有可以領的（不開交易的 EXISTS），有才開交易領；沒有就等同一個程序內排進工作時發的訊號，
    /// 或最多等 <see cref="HostTimings.ClaimPollInterval"/> 再問資料庫（別的 API 程序收進來的工作）。呼叫端斷線時什麼都不領。
    /// 等的時候擁有者新要求登出（<paramref name="loggedIn"/> 裡的引擎）也馬上回、帶著要登出的引擎：
    /// worker 大部分時間都在這裡等，不回的話登出要 25 秒以上才生效，看起來像沒成功。
    /// </summary>
    private async Task<(HostJob? Job, IReadOnlyList<AiProviderKind> Logout)> WaitForJobAsync(
        HostAgentIdentity agent, int capacity, IReadOnlySet<string> loggedIn, CancellationToken cancellationToken)
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
                    return (job, []);
                }

                if (await PendingLogoutAsync(agent, loggedIn, cancellationToken) is { Count: > 0 } logout)
                {
                    return (null, logout);
                }

                var remaining = timings.PollWait - time.GetElapsedTime(started);
                if (remaining <= TimeSpan.Zero)
                {
                    return (null, []);
                }

                await Task.WhenAny(wake, Task.Delay(remaining < timings.ClaimPollInterval ? remaining : timings.ClaimPollInterval, cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            return (null, []);
        }
    }

    /// <summary>擁有者要求登出、而且在 <paramref name="loggedIn"/> 裡（這次登記時還登入著、還沒告訴 worker 要登出）的引擎。</summary>
    private async Task<IReadOnlyList<AiProviderKind>> PendingLogoutAsync(
        HostAgentIdentity agent, IReadOnlySet<string> loggedIn, CancellationToken cancellationToken)
    {
        if (loggedIn.Count == 0)
        {
            return [];
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var requested = await db.HostAgents.AsNoTracking()
            .Where(a => a.Id == agent.AgentId && a.OrganizationId == agent.OrganizationId)
            .Select(a => a.LogoutRequested)
            .SingleOrDefaultAsync(cancellationToken);
        return requested is null
            ? []
            : [.. HostEnginesJson.DeserializeNames(requested).Where(loggedIn.Contains).Select(AiEnumNames.ParseKind)];
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
            expires)
        {
            Attachments = payload.Attachments,
        };
    }
}
