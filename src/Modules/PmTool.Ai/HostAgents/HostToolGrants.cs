using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;
using PmTool.Identity;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 主機代理每件工作的工具授權（<c>kjt_</c>，給 <c>/mcp</c> 用）。授權就是工作列：每個請求都重新查（不快取）工作還在執行、
/// 沒有要求取消、沒過期、來源與代理都還開著、發問的人帳號沒停用而且不是訪客。專案與項目的權限由各模組用發問的人的
/// <see cref="PmTool.Abstractions.Actor"/> 照常判斷；角色用身分模組<b>現在</b>的，不是送出時的。
/// </summary>
internal sealed class HostToolGrants(
    IDbContextFactory<AiDbContext> contexts, HostPeople people, TimeProvider time) : IHostToolGrants
{
    public async Task<HostToolGrant?> ResolveAsync(string token, CancellationToken cancellationToken)
    {
        if (!HostSecrets.LooksLikeToken(token, HostSecrets.ToolTokenPrefix))
        {
            return null;
        }

        var hash = HostSecrets.Hash(token);
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var job = await (
            from j in db.HostJobs.AsNoTracking()
            join s in db.HostSources.AsNoTracking() on j.SourceId equals s.Id
            join a in db.HostAgents.AsNoTracking() on j.AgentId equals a.Id
            where j.ToolTokenHash == hash && j.State == HostJobStates.Running && !j.CancelRequested && j.ToolTokenExpiresAt > now
                && s.Enabled && a.State == HostAgentStates.Active
                && j.OrganizationId == s.OrganizationId && j.OrganizationId == a.OrganizationId
            select j).SingleOrDefaultAsync(cancellationToken);
        if (job?.Payload is null
            || await people.RoleOfAsync(job.OrganizationId, job.PersonId, cancellationToken) is not { } role)
        {
            return null;
        }

        var tools = AssistantJson.Deserialize<StoredHostJob>(job.Payload)!.AllowedTools;
        return new HostToolGrant(
            job.OrganizationId, job.PersonId, role is OrganizationRole.Owner or OrganizationRole.Admin, job.ProjectKey, tools, job.Id);
    }

    public async Task<HostToolReplay> BeginWriteAsync(Guid jobId, string tool, string argumentsHash, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var pending = HostToolCallStates.Pending;

        // 唯一索引擋重複：插入成功才是第一次呼叫。工作列不在（已經刪掉）就不插、不執行。
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO ai.host_tool_calls (job_id, tool, args_hash, organization_id, state, created_at)
            SELECT j.id, {tool}, {argumentsHash}, j.organization_id, {pending}, {now}
            FROM ai.host_jobs j WHERE j.id = {jobId}
            ON CONFLICT (job_id, tool, args_hash) DO NOTHING
            """, cancellationToken);
        if (inserted == 1)
        {
            return HostToolReplay.Proceed;
        }

        var existing = await db.HostToolCalls.AsNoTracking()
            .SingleOrDefaultAsync(c => c.JobId == jobId && c.Tool == tool && c.ArgsHash == argumentsHash, cancellationToken);
        if (existing is null)
        {
            return HostToolReplay.InProgress;
        }

        if (existing.State == HostToolCallStates.Done)
        {
            return new HostToolReplay(HostToolReplayKind.Replay, existing.Result);
        }

        // 進行中的請求當掉了（太久沒結束）：讓這一次接手，不然這個呼叫在工作結束前都建立不了。
        var stale = now - HostLimits.PendingWriteExpiry;
        var taken = await db.HostToolCalls
            .Where(c => c.JobId == jobId && c.Tool == tool && c.ArgsHash == argumentsHash && c.State == HostToolCallStates.Pending && c.CreatedAt < stale)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.CreatedAt, now), cancellationToken);
        return taken == 1 ? HostToolReplay.Proceed : HostToolReplay.InProgress;
    }

    public async Task CompleteWriteAsync(Guid jobId, string tool, string argumentsHash, string? resultJson, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var call = db.HostToolCalls.Where(c => c.JobId == jobId && c.Tool == tool && c.ArgsHash == argumentsHash);
        if (resultJson is null)
        {
            await call.Where(c => c.State == HostToolCallStates.Pending).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        await call.ExecuteUpdateAsync(
            set => set.SetProperty(c => c.State, HostToolCallStates.Done).SetProperty(c => c.Result, resultJson), cancellationToken);
    }
}
