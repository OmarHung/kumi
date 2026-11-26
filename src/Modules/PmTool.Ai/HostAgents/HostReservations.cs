using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 送出時在來源的 advisory lock 裡預留一個名額（internal/HANDOFF-host-agent.md 第 6.5 節）：檢查每人等候數、全來源排隊數、
/// 每天與每月的則數與 token，通過就同時寫 <c>reserved</c> 的工作與用量。數與寫在同一個鎖裡，同時送出的請求不會一起超過配額。
/// 預留發生在存使用者的訊息<b>之前</b>：失敗什麼都不存；之後的步驟失敗要呼叫 <see cref="ReleaseAsync"/>。
/// </summary>
internal sealed class HostReservations(
    IDbContextFactory<AiDbContext> contexts, HostUsageReader usage, TimeProvider time)
{
    public async Task<Result<Guid>> ReserveAsync(
        Actor actor, Guid sourceId, string? projectKey, Guid conversationId, Guid messageId, CancellationToken cancellationToken)
    {
        var month = (await usage.MonthAsync(actor.OrganizationId, null, cancellationToken))!;
        var day = await usage.DayAsync(actor.OrganizationId, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await HostJobFinisher.LockSourceAsync(db, sourceId, cancellationToken);
        var now = time.GetUtcNow();
        await ReleaseStaleAsync(db, sourceId, now, cancellationToken);

        var view = await HostReadiness.LoadViewAsync(db, actor.OrganizationId, sourceId, cancellationToken);
        if (view is null)
        {
            return HostFailures.SourceUnavailable("主機上的這個來源已經不存在了，請換一個。");
        }

        // 額度的問題另外說（HostQuotaExceeded），所以狀態先不看額度。
        var (status, _) = HostReadiness.SourceStatus(view, quotaReached: false, now);
        if (status != HostSourceStatus.Ready)
        {
            return HostFailures.SourceUnavailable(HostReadiness.MemberNotice(status, view.Engine.Readiness) ?? "這個來源現在不能用。");
        }

        if (await CheckQueueAsync(db, view.Source, actor, cancellationToken) is { } full)
        {
            return full;
        }

        if (await CheckQuotaAsync(db, view.Source, actor, month, day, cancellationToken) is { } exceeded)
        {
            return exceeded;
        }

        var id = Guid.CreateVersion7();
        db.HostJobs.Add(new HostJobRow
        {
            Id = id,
            OrganizationId = actor.OrganizationId,
            SourceId = sourceId,
            AgentId = view.Agent.Id,
            PersonId = actor.PersonId,
            ProjectKey = projectKey,
            ConversationId = conversationId,
            MessageId = messageId,
            State = HostJobStates.Reserved,
            CreatedAt = now,
        });
        db.HostUsage.Add(new HostUsageRow
        {
            Id = id,
            OrganizationId = actor.OrganizationId,
            SourceId = sourceId,
            PersonId = actor.PersonId,
            ProjectKey = projectKey,
            State = HostUsageStates.Reserved,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    /// <summary>送出的後續步驟失敗（存訊息、開回覆）：退掉預留，什麼都不留。</summary>
    public async Task ReleaseAsync(Guid jobId)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await HostJobFinisher.FinishAsync(
            db, jobId, new HostFinish(AssistantMessageStatus.Failed, "送出失敗，沒有執行。"), time.GetUtcNow(), CancellationToken.None);
    }

    /// <summary>預留之後程序在中間當掉、沒有變成排隊工作的：超過 <see cref="HostLimits.ReservedExpiry"/> 就退還。</summary>
    private static async Task ReleaseStaleAsync(AiDbContext db, Guid sourceId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - HostLimits.ReservedExpiry;
        var stale = await db.HostJobs.AsNoTracking()
            .Where(j => j.SourceId == sourceId && j.State == HostJobStates.Reserved && j.CreatedAt < cutoff)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in stale)
        {
            await HostJobFinisher.FinishAsync(
                db, id, new HostFinish(AssistantMessageStatus.Failed, "送出沒有完成，這則沒有執行。"), now, cancellationToken);
        }
    }

    private static async Task<Failure?> CheckQueueAsync(AiDbContext db, HostSourceRow source, Actor actor, CancellationToken cancellationToken)
    {
        var waiting = db.HostJobs.AsNoTracking()
            .Where(j => j.SourceId == source.Id && (j.State == HostJobStates.Reserved || j.State == HostJobStates.Queued));
        if (await waiting.CountAsync(j => j.PersonId == actor.PersonId, cancellationToken) >= AiRules.HostMaxQueuedPerPerson)
        {
            return HostFailures.QueueFull("你在這個來源已經有一則在排隊，等它輪到之後再送下一則。");
        }

        return await waiting.CountAsync(cancellationToken) >= AiRules.HostMaxQueuedPerSource
            ? HostFailures.QueueFull("這個來源的隊伍已經排滿了，請稍後再試。")
            : null;
    }

    private static async Task<Failure?> CheckQuotaAsync(
        AiDbContext db, HostSourceRow source, Actor actor, HostWindow month, HostWindow day, CancellationToken cancellationToken)
    {
        if (source.DailyRequestsPerPerson is { } daily
            && await HostUsageReader.Counted(db, actor.OrganizationId, day)
                .CountAsync(u => u.SourceId == source.Id && u.PersonId == actor.PersonId, cancellationToken) >= daily)
        {
            return HostFailures.QuotaExceeded($"你今天在這個來源的使用次數已經用完了（每天 {daily} 則）。");
        }

        var thisMonth = HostUsageReader.Counted(db, actor.OrganizationId, month).Where(u => u.SourceId == source.Id);
        if (source.MonthlyRequests is { } requests && await thisMonth.CountAsync(cancellationToken) >= requests)
        {
            return HostFailures.QuotaExceeded($"這個來源這個月的則數已經用完了（每月 {requests} 則）。");
        }

        if (source.MonthlyTokens is { } tokens
            && await thisMonth.SumAsync(u => u.InputTokens + u.OutputTokens, cancellationToken) >= tokens)
        {
            return HostFailures.QuotaExceeded("這個來源這個月的 token 已經用完了。");
        }

        return null;
    }
}
