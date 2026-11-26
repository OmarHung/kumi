using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Attachments;
using PmTool.Ai.Maintenance;
using PmTool.Ai.Persistence;
using PmTool.Ai.Settings;

namespace PmTool.Ai.LocalAgents;

internal sealed partial class LocalAgentService
{
    public async Task<Result<LocalAgentReportAck>> ReportAsync(
        Actor actor, Guid jobId, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(events);
        if (!await AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken))
        {
            return LocalAgentFailures.Disabled();
        }

        var cleaned = CleanEvents(events);
        if (cleaned.Failure is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(db, actor, jobId, cancellationToken);
        if (job is null || job.State == LocalJobStates.Waiting)
        {
            return LocalAgentFailures.JobNotFound();
        }

        // 伺服器自己結束的（使用者停止、逾時、放棄等）：不再收進度，告訴本機代理停下來。本機代理自己結束的，之後再回報就是找不到。
        if (job.State == LocalJobStates.Finished)
        {
            return job.CancelRequested ? new LocalAgentReportAck(true) : LocalAgentFailures.JobNotFound();
        }

        // 程序重啟後殘留的工作：回覆那一頭早就不在了，不再收進度，叫本機代理停下來。
        var now = time.GetUtcNow();
        if (now - job.CreatedAt > AbandonedAfter)
        {
            await db.LocalJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, LocalJobStates.Finished)
                    .SetProperty(j => j.CancelRequested, true)
                    .SetProperty(j => j.Payload, LocalJobRetention.EmptyPayload)
                    .SetProperty(j => j.FinishStatus, AssistantMessageMapper.StatusName(AssistantMessageStatus.Failed)),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new LocalAgentReportAck(true);
        }

        // 空字串的文字不存（沒有內容，只是白占一列）；心跳照樣更新最後回報的時間。
        var stored = (await KeepOwnProposalsAsync(db, actor, cleaned.Value, cancellationToken))
            .Where(progress => progress is not LocalAgentText { Delta.Length: 0 }).ToList();
        if (job.EventCount + stored.Count > LocalAgentLimits.MaxEventsPerJob)
        {
            return LocalAgentFailures.Invalid("events", $"一件工作最多 {LocalAgentLimits.MaxEventsPerJob} 則進度。");
        }

        var seq = job.EventCount;
        foreach (var progress in stored)
        {
            db.LocalEvents.Add(new LocalEventRow
            {
                JobId = job.Id,
                OrganizationId = actor.OrganizationId,
                Seq = ++seq,
                Content = StoredEvent.Serialize(progress),
            });
        }

        var count = seq;
        await db.SaveChangesAsync(cancellationToken);
        await db.LocalJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            set => set.SetProperty(j => j.EventCount, count).SetProperty(j => j.LastReportAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new LocalAgentReportAck(job.CancelRequested);
    }

    public async Task<Result<Unit>> FinishAsync(Actor actor, Guid jobId, LocalAgentFinish finish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(finish);
        if (!await AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken))
        {
            return LocalAgentFailures.Disabled();
        }

        if (!Enum.IsDefined(finish.Status))
        {
            return LocalAgentFailures.Invalid("status", "結束的狀態只能是 complete、stopped、failed。");
        }

        if (finish.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 })
        {
            return LocalAgentFailures.Invalid("usage", "用量不能是負的。");
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var job = await LockJobAsync(db, actor, jobId, cancellationToken);
        if (job is null || job.State == LocalJobStates.Waiting)
        {
            return LocalAgentFailures.JobNotFound();
        }

        // 重送同一件的結束不算錯，也不改第一次存的結果。
        if (job.State == LocalJobStates.Finished)
        {
            return Unit.Value;
        }

        var status = AssistantMessageMapper.StatusName(finish.Status);
        var error = finish.Error is null ? null : AiText.Cut(LocalAgentValidator.CleanText(finish.Error), AiRules.MaxErrorLength);
        var input = finish.Usage?.InputTokens;
        var output = finish.Usage?.OutputTokens;
        var now = time.GetUtcNow();
        await db.LocalJobs.Where(j => j.Id == job.Id).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.State, LocalJobStates.Finished)
                .SetProperty(j => j.FinishStatus, status)
                .SetProperty(j => j.FinishError, error)
                .SetProperty(j => j.InputTokens, input)
                .SetProperty(j => j.OutputTokens, output)
                .SetProperty(j => j.LastReportAt, now),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }

    public async Task<Result<AssistantAttachmentContent>> OpenJobAttachmentAsync(
        Actor actor, Guid jobId, Guid attachmentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!await AiPersonalPolicy.IsAllowedAsync(contexts, actor.OrganizationId, cancellationToken))
        {
            return LocalAgentFailures.Disabled();
        }

        // 只有領走這件工作的人、工作還在執行（沒結束、沒被要求停止）時下載得到；清單以建立工作時存的編號為準。
        var org = actor.OrganizationId;
        var person = actor.PersonId;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var listed = await db.LocalJobs.AsNoTracking()
            .Where(j => j.Id == jobId && j.OrganizationId == org && j.PersonId == person
                && j.State == LocalJobStates.Claimed && !j.CancelRequested)
            .Select(j => j.AttachmentIds)
            .SingleOrDefaultAsync(cancellationToken);
        return listed is null
            ? AttachmentFailures.NotFound()
            : await attachmentReader.OpenAsync(org, listed, attachmentId, cancellationToken);
    }

    /// <summary>
    /// 回報的 itemProposalId、projectProposalId、newItemProposalId、memberProposalId 要是這個人、這個組織的建議才記：不然本機代理（外部送來的）能讓網頁畫出別人的建議卡片。
    /// 不是的當作沒有。
    /// </summary>
    internal static async Task<IReadOnlyList<LocalAgentEvent>> KeepOwnProposalsAsync(
        AiDbContext db, Actor actor, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken)
    {
        var tools = events.OfType<LocalAgentTool>().Select(e => e.Tool).ToList();
        var claimedItems = tools.Select(t => t.ItemProposalId).OfType<Guid>().Distinct().ToList();
        var claimedProjects = tools.Select(t => t.ProjectProposalId).OfType<Guid>().Distinct().ToList();
        var claimedNewItems = tools.Select(t => t.NewItemProposalId).OfType<Guid>().Distinct().ToList();
        var claimedMembers = tools.Select(t => t.MemberProposalId).OfType<Guid>().Distinct().ToList();
        if (claimedItems.Count == 0 && claimedProjects.Count == 0 && claimedNewItems.Count == 0 && claimedMembers.Count == 0)
        {
            return events;
        }

        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var ownItems = claimedItems.Count == 0
            ? []
            : (await db.ItemProposals.AsNoTracking()
                .Where(p => claimedItems.Contains(p.Id) && p.OrganizationId == org && p.PersonId == person)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        var ownProjects = claimedProjects.Count == 0
            ? []
            : (await db.ProjectProposals.AsNoTracking()
                .Where(p => claimedProjects.Contains(p.Id) && p.OrganizationId == org && p.PersonId == person)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        var ownNewItems = claimedNewItems.Count == 0
            ? []
            : (await db.NewItemProposals.AsNoTracking()
                .Where(p => claimedNewItems.Contains(p.Id) && p.OrganizationId == org && p.PersonId == person)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        var ownMembers = claimedMembers.Count == 0
            ? []
            : (await db.MemberProposals.AsNoTracking()
                .Where(p => claimedMembers.Contains(p.Id) && p.OrganizationId == org && p.PersonId == person)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken)).ToHashSet();
        return [.. events.Select(e => e is LocalAgentTool tool ? new LocalAgentTool(KeepOwn(tool.Tool, ownItems, ownProjects, ownNewItems, ownMembers)) : e)];
    }

    private static AssistantToolPart KeepOwn(
        AssistantToolPart tool, HashSet<Guid> ownItems, HashSet<Guid> ownProjects, HashSet<Guid> ownNewItems, HashSet<Guid> ownMembers) => tool with
    {
        ItemProposalId = tool.ItemProposalId is { } item && ownItems.Contains(item) ? item : null,
        ProjectProposalId = tool.ProjectProposalId is { } project && ownProjects.Contains(project) ? project : null,
        NewItemProposalId = tool.NewItemProposalId is { } newItems && ownNewItems.Contains(newItems) ? newItems : null,
        MemberProposalId = tool.MemberProposalId is { } members && ownMembers.Contains(members) ? members : null,
    };

    private static Result<IReadOnlyList<LocalAgentEvent>> CleanEvents(IReadOnlyList<LocalAgentEvent> events)
    {
        if (events.Count > AiRules.MaxLocalAgentEventsPerReport)
        {
            return LocalAgentFailures.Invalid("events", $"一次回報最多 {AiRules.MaxLocalAgentEventsPerReport} 則進度。");
        }

        var cleaned = new List<LocalAgentEvent>(events.Count);
        foreach (var progress in events)
        {
            var checkedEvent = LocalAgentValidator.Progress(progress, cleaned.Count);
            if (checkedEvent.Failure is { } failure)
            {
                return failure;
            }

            cleaned.Add(checkedEvent.Value);
        }

        return cleaned;
    }

    /// <summary>鎖住這一列（<c>FOR UPDATE</c>）：同一件工作的回報與結束排隊，進度的流水號才不會重複。一定要是這個人、這個組織的。</summary>
    private static async Task<LocalJobRow?> LockJobAsync(AiDbContext db, Actor actor, Guid jobId, CancellationToken cancellationToken)
    {
        var org = actor.OrganizationId;
        var person = actor.PersonId;
        var rows = await db.LocalJobs.FromSql($"""
            SELECT * FROM ai.local_agent_jobs
            WHERE id = {jobId} AND organization_id = {org} AND person_id = {person}
            FOR UPDATE
            """).AsNoTracking().ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }
}
