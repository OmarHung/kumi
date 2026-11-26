using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 組織連線的用量（AI-01）。送出時在組織的 advisory lock 裡數每人今天、全組織這個月的則數與 token，通過就寫一筆 <c>reserved</c>；
/// 數與寫在同一個鎖裡，同時送出的請求不會一起超過上限。預留發生在存使用者的訊息<b>之前</b>：到了上限什麼都不存。
/// 回覆結束時記下 token（<see cref="SettleAsync"/>），送出的後續步驟失敗就退還（<see cref="ReleaseAsync"/>）。
/// 只算組織的連線；自己的連線（自己付錢）、本機代理、主機來源（有自己的配額）不走這裡。
/// 用量另存一張表，刪對話不會讓配額歸零。
/// </summary>
internal sealed class ConnectionUsageLedger(IDbContextFactory<AiDbContext> contexts, HostUsageReader windows, TimeProvider time)
{
    /// <summary>預留一則。<paramref name="usageId"/> 用助理那則訊息的編號，結束時才找得到同一筆。</summary>
    public async Task<Failure?> ReserveAsync(
        Actor actor, Guid connectionId, string projectKey, Guid usageId, CancellationToken cancellationToken)
    {
        var month = (await windows.MonthAsync(actor.OrganizationId, null, cancellationToken))!;
        var day = await windows.DayAsync(actor.OrganizationId, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_usage:" + actor.OrganizationId}, 0))", cancellationToken);

        var limits = await ReadLimitsAsync(db, actor.OrganizationId, cancellationToken);
        if (await CheckAsync(db, actor, limits, month, day, cancellationToken) is { } exceeded)
        {
            return exceeded;
        }

        db.ConnectionUsage.Add(new AiConnectionUsageRow
        {
            Id = usageId,
            OrganizationId = actor.OrganizationId,
            ConnectionId = connectionId,
            PersonId = actor.PersonId,
            ProjectKey = projectKey,
            State = HostUsageStates.Reserved,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    /// <summary>送出的後續步驟失敗：退掉預留（不算用量）。已經結束記帳的不動。</summary>
    public async Task ReleaseAsync(Guid usageId)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.ConnectionUsage
            .Where(u => u.Id == usageId && u.State == HostUsageStates.Reserved)
            .ExecuteUpdateAsync(set => set.SetProperty(u => u.State, HostUsageStates.Released), CancellationToken.None);
    }

    /// <summary>
    /// 供應商退回了請求、拿掉歷史的圖片與 PDF 重試：被退回的那一次也是一次實際的呼叫，補記一筆（已結算、失敗、沒有 token），
    /// 則數才照實際呼叫的次數算。不檢查上限：這一則已經預留過了，不能在重試的半途才拒絕。
    /// </summary>
    public static async Task RecordRejectedCallAsync(
        AiDbContext db, Guid organizationId, Guid connectionId, Guid personId, string projectKey, DateTimeOffset now)
    {
        db.ConnectionUsage.Add(new AiConnectionUsageRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
            ConnectionId = connectionId,
            PersonId = personId,
            ProjectKey = projectKey,
            State = HostUsageStates.Settled,
            FinishStatus = AssistantMessageMapper.StatusName(AssistantMessageStatus.Failed),
            CreatedAt = now,
            SettledAt = now,
        });
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>回覆結束（完成、停止、失敗都要）：記下結果與 token。回覆的執行器是單例、不能拿這個 scoped 的服務，所以是靜態的。</summary>
    public static async Task SettleAsync(
        AiDbContext db, Guid usageId, AssistantMessageStatus status, long? inputTokens, long? outputTokens, DateTimeOffset now)
    {
        var finish = AssistantMessageMapper.StatusName(status);
        var input = inputTokens ?? 0;
        var output = outputTokens ?? 0;
        await db.ConnectionUsage
            .Where(u => u.Id == usageId && u.State == HostUsageStates.Reserved)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(u => u.State, HostUsageStates.Settled)
                    .SetProperty(u => u.FinishStatus, finish)
                    .SetProperty(u => u.InputTokens, input)
                    .SetProperty(u => u.OutputTokens, output)
                    .SetProperty(u => u.SettledAt, now),
                CancellationToken.None);
    }

    /// <summary>這個月（組織的時區）全組織的則數與 token；設定頁顯示用。</summary>
    public async Task<AiMonthUsage> MonthAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        var month = (await windows.MonthAsync(organizationId, null, cancellationToken))!;
        var rows = Counted(db, organizationId, month);
        var requests = await rows.CountAsync(cancellationToken);
        var tokens = await rows.SumAsync(u => u.InputTokens + u.OutputTokens, cancellationToken);
        return new AiMonthUsage(month.Label, requests, tokens);
    }

    internal static AiUsageLimits ToLimits(AiSettingsRow? row) =>
        row is null ? AiUsageLimits.Default : new AiUsageLimits(row.DailyRequestsPerPerson, row.MonthlyRequests, row.MonthlyTokens);

    private static IQueryable<AiConnectionUsageRow> Counted(AiDbContext db, Guid organizationId, HostWindow window) =>
        db.ConnectionUsage.AsNoTracking().Where(u =>
            u.OrganizationId == organizationId && u.State != HostUsageStates.Released && u.CreatedAt >= window.Start && u.CreatedAt < window.End);

    private static async Task<AiUsageLimits> ReadLimitsAsync(AiDbContext db, Guid organizationId, CancellationToken cancellationToken) =>
        ToLimits(await db.Settings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken));

    private static async Task<Failure?> CheckAsync(
        AiDbContext db, Actor actor, AiUsageLimits limits, HostWindow month, HostWindow day, CancellationToken cancellationToken)
    {
        if (limits.DailyRequestsPerPerson is { } daily
            && await Counted(db, actor.OrganizationId, day).CountAsync(u => u.PersonId == actor.PersonId, cancellationToken) >= daily)
        {
            return AssistantFailures.UsageLimit($"你今天用組織 AI 的則數已經用完了（每天 {daily} 則），明天再試。");
        }

        var thisMonth = Counted(db, actor.OrganizationId, month);
        if (limits.MonthlyRequests is { } requests && await thisMonth.CountAsync(cancellationToken) >= requests)
        {
            return AssistantFailures.UsageLimit($"組織這個月用組織 AI 的則數已經用完了（每月 {requests} 則），下個月再試，或請管理員調高上限。");
        }

        return limits.MonthlyTokens is { } tokens
            && await thisMonth.SumAsync(u => u.InputTokens + u.OutputTokens, cancellationToken) >= tokens
            ? AssistantFailures.UsageLimit("組織這個月用組織 AI 的 token 已經用完了，下個月再試，或請管理員調高上限。")
            : null;
    }
}
