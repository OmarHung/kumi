using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Ai.Assistant;
using PmTool.Ai.Maintenance;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 網頁的回覆交給使用者電腦上的引擎跑（internal/HANDOFF-local-agent.md 第 4.3 節）：建立一件工作，然後讀本機代理回報的進度、
/// 轉成事件，直到結束、逾時、被停止。伺服器不連進使用者的電腦：本機代理自己來領、來回報。
/// 工作的狀態都在資料庫裡，所以領工作的程序和轉送的程序不必是同一個。
/// </summary>
internal sealed class LocalAgentRelay(
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time,
    LocalAgentTimings timings,
    LocalAgentSignals signals,
    ILogger<LocalAgentRelay> logger)
{
    public const string NoResponseMessage = "你電腦上的 Kumi 代理沒有回應，確認 kumi agent start 還開著。";

    public const string LostMessage = "你電腦上的 Kumi 代理中斷了回報，這則回覆沒有完成，請確認 kumi agent start 還開著再試一次。";

    public const string UnreportedFailureMessage = "你電腦上的引擎回報失敗，沒有說明原因。";

    /// <summary>
    /// 建立工作並一路讀到結束，事件寫進 <paramref name="session"/>。<paramref name="cancellationToken"/> 取消（使用者停止或逾時）時，
    /// 先請本機代理停下來、最多等 <see cref="LocalAgentTimings.StopGrace"/>，把最後的進度收完才丟 <see cref="OperationCanceledException"/>。
    /// </summary>
    public async Task<ReplyOutcome> RunAsync(
        ReplyRequest request, LocalReplyAccumulator recorder, AssistantRunSession session, CancellationToken cancellationToken)
    {
        var jobId = await CreateJobAsync(request, cancellationToken);
        recorder.JobId = jobId;
        var cursor = new Cursor();
        try
        {
            return await FollowAsync(jobId, recorder, session, cursor, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(jobId, recorder, session, cursor);

            // 停止的當下已經超過上限的，是失敗，不是「已停止」。
            if (recorder.Problem is { } problem)
            {
                return new ReplyOutcome(AssistantMessageStatus.Failed, problem);
            }

            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 讀進度失敗：工作不能留在等的隊伍裡被稍後領走，不然使用者看到失敗了、引擎卻還在跑。
            await ForceFinishQuietlyAsync(jobId);
            throw;
        }
        finally
        {
            await DeleteEventsQuietlyAsync(jobId);
        }
    }

    private async Task<Guid> CreateJobAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        var selection = request.Selection.Selection;
        var jobId = Guid.CreateVersion7();
        var job = new LocalAgentJob(
            jobId,
            request.Selection.Endpoint.Kind,
            selection.Model,
            selection.Effort,
            request.ProjectKey,
            request.ProjectKey is null
                ? LocalPromptBuilder.OrganizationSystemPrompt(
                    request.Actor.IsOrganizationAdmin, request.Asker, AgentToolLists.SupportsRevision(request.Selection.Endpoint.AgentVersion),
                    AgentToolLists.SupportsMyWork(request.Selection.Endpoint.AgentVersion))
                : LocalPromptBuilder.SystemPrompt(
                    request.ProjectKey, request.Permissions, request.ItemKey, request.Asker, AgentToolLists.SupportsNewItems(request.Selection.Endpoint.AgentVersion),
                    AgentToolLists.SupportsStaffing(request.Selection.Endpoint.AgentVersion),
                    AgentToolLists.SupportsRevision(request.Selection.Endpoint.AgentVersion),
                    AgentToolLists.SupportsMyWork(request.Selection.Endpoint.AgentVersion)),
            LocalPromptBuilder.Prompt(
                request.History,
                request.UserText,
                request.Asker,
                AssistantPrompt.TurnNote(request.Now, request.Proposals, AgentToolLists.SupportsRevision(request.Selection.Endpoint.AgentVersion))),
            AgentToolLists.For(request.ProjectKey, request.Selection.Endpoint.AgentVersion),
            request.McpUrl ?? string.Empty,
            AiRules.MaxToolCallsPerReply + 1)
        {
            Attachments = request.JobAttachments,
        };
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.LocalJobs.Add(new LocalJobRow
        {
            Id = jobId,
            OrganizationId = request.Actor.OrganizationId,
            PersonId = request.Actor.PersonId,
            EngineId = selection.ConnectionId,
            ConversationId = request.ConversationId,
            MessageId = request.AssistantMessageId,
            Payload = AssistantJson.Serialize(job),
            AttachmentIds = [.. request.JobAttachments.Select(a => a.Id)],
            State = LocalJobStates.Waiting,
            CreatedAt = time.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);

        // 同一個程序內等著的長輪詢立刻去領；別的程序靠資料庫的備援查詢。
        signals.Signal(request.Actor.PersonId);
        return jobId;
    }

    private async Task<ReplyOutcome> FollowAsync(
        Guid jobId, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor, CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var lastProgress = started;
        DateTimeOffset? lastReport = null;
        while (true)
        {
            var snapshot = await ReadAsync(jobId, cursor.LastSeq, cancellationToken);
            var news = Publish(snapshot.Events, recorder, session, cursor);

            // 先看有沒有超過上限：同一輪讀到超長的文字和「完成」時，被截斷的回覆不能算完整。
            if (recorder.Problem is { } problem)
            {
                await ForceFinishAsync(jobId, AssistantMessageStatus.Failed, problem);
                return new ReplyOutcome(AssistantMessageStatus.Failed, problem);
            }

            if (snapshot.Job.State == LocalJobStates.Finished)
            {
                return Concluded(snapshot.Job, recorder);
            }

            if (snapshot.Job.State == LocalJobStates.Claimed && (news || snapshot.Job.LastReportAt != lastReport))
            {
                lastProgress = time.GetTimestamp();
                lastReport = snapshot.Job.LastReportAt;
            }

            if (TimedOut(snapshot.Job.State, started, lastProgress) is { } message)
            {
                await ForceFinishAsync(jobId, AssistantMessageStatus.Failed, message);
                return new ReplyOutcome(AssistantMessageStatus.Failed, message);
            }

            await Task.Delay(timings.PollInterval, time, cancellationToken);
        }
    }

    private string? TimedOut(string state, long started, long lastProgress)
    {
        if (state == LocalJobStates.Waiting)
        {
            return time.GetElapsedTime(started) >= timings.ClaimTimeout ? NoResponseMessage : null;
        }

        return time.GetElapsedTime(lastProgress) >= timings.IdleTimeout ? LostMessage : null;
    }

    private static ReplyOutcome Concluded(LocalJobRow job, LocalReplyAccumulator recorder)
    {
        recorder.SetUsage(job.InputTokens is null && job.OutputTokens is null
            ? null
            : new AssistantUsage(job.InputTokens ?? 0, job.OutputTokens ?? 0));
        var status = job.FinishStatus is { } name ? AssistantMessageMapper.ParseStatus(name) : AssistantMessageStatus.Failed;
        return status switch
        {
            AssistantMessageStatus.Complete => new ReplyOutcome(status, null),
            AssistantMessageStatus.Failed => new ReplyOutcome(status, job.FinishError ?? UnreportedFailureMessage) { ReportedByEngine = true },
            _ => new ReplyOutcome(status, job.FinishError),
        };
    }

    /// <summary>
    /// 請本機代理停下來：還沒被領走的直接結束（沒有人在跑）；領走的標上取消，本機代理下一次回報時知道，結束引擎、送結束，
    /// 最多等 <see cref="LocalAgentTimings.StopGrace"/>，等不到就由伺服器結束它（照「中斷」存）。最後的進度照樣收進回覆。
    /// 這裡不能再被取消、也不能丟例外：回覆一定要存得下來。
    /// </summary>
    private async Task StopAsync(Guid jobId, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor)
    {
        try
        {
            await RequestCancelAsync(jobId);
            var started = time.GetTimestamp();
            while (true)
            {
                var snapshot = await ReadAsync(jobId, cursor.LastSeq, CancellationToken.None);
                Publish(snapshot.Events, recorder, session, cursor);
                if (snapshot.Job.State == LocalJobStates.Finished)
                {
                    recorder.SetUsage(snapshot.Job.InputTokens is null && snapshot.Job.OutputTokens is null
                        ? null
                        : new AssistantUsage(snapshot.Job.InputTokens ?? 0, snapshot.Job.OutputTokens ?? 0));
                    return;
                }

                if (time.GetElapsedTime(started) >= timings.StopGrace)
                {
                    await ForceFinishAsync(jobId, AssistantMessageStatus.Stopped, null);
                    return;
                }

                await Task.Delay(timings.PollInterval, time, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("本機代理的回覆停不下來：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task RequestCancelAsync(Guid jobId)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.LocalJobs.Where(j => j.Id == jobId && j.State != LocalJobStates.Finished)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.CancelRequested, true), CancellationToken.None);

        // 還沒被領走的直接結束，不然它會留在等的隊伍裡（雖然取消的不會再被領）。
        await db.LocalJobs.Where(j => j.Id == jobId && j.State == LocalJobStates.Waiting)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, LocalJobStates.Finished)
                    .SetProperty(j => j.Payload, LocalJobRetention.EmptyPayload)
                    .SetProperty(j => j.FinishStatus, AssistantMessageMapper.StatusName(AssistantMessageStatus.Stopped)),
                CancellationToken.None);
    }

    /// <summary>伺服器自己結束這件工作（逾時、放棄等、停不下來）。標上取消：本機代理再回報時會知道、停下引擎。</summary>
    private async Task ForceFinishAsync(Guid jobId, AssistantMessageStatus status, string? error)
    {
        var name = AssistantMessageMapper.StatusName(status);
        var cut = error is null ? null : AiText.Cut(error, AiRules.MaxErrorLength);
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.LocalJobs.Where(j => j.Id == jobId && j.State != LocalJobStates.Finished)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(j => j.State, LocalJobStates.Finished)
                    .SetProperty(j => j.CancelRequested, true)
                    .SetProperty(j => j.Payload, LocalJobRetention.EmptyPayload)
                    .SetProperty(j => j.FinishStatus, name)
                    .SetProperty(j => j.FinishError, cut),
                CancellationToken.None);
    }

    private async Task ForceFinishQuietlyAsync(Guid jobId)
    {
        try
        {
            await ForceFinishAsync(jobId, AssistantMessageStatus.Failed, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("本機代理的工作結束不了：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task<Snapshot> ReadAsync(Guid jobId, int afterSeq, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先讀工作、再讀進度：結束之前的進度都已經提交，工作是「已結束」時進度一定讀得齊。
        var job = await db.LocalJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, cancellationToken);
        var events = await db.LocalEvents.AsNoTracking()
            .Where(e => e.JobId == jobId && e.Seq > afterSeq)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);
        return new Snapshot(job, events);
    }

    private static bool Publish(
        IReadOnlyList<LocalEventRow> events, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor)
    {
        foreach (var row in events)
        {
            cursor.LastSeq = row.Seq;
            WriteAll(session.Writer, recorder.Apply(StoredEvent.Deserialize(row.Content)));
        }

        return events.Count > 0;
    }

    private static void WriteAll(ChannelWriter<AssistantRunEvent> writer, IEnumerable<AssistantRunEvent> events)
    {
        foreach (var runEvent in events)
        {
            writer.TryWrite(runEvent);
        }
    }

    /// <summary>回覆存完之後刪掉那件工作（連進度）：工作列的內容含整段對話，不留到下一次登記。</summary>
    public async Task DeleteJobQuietlyAsync(Guid jobId)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.LocalEvents.Where(e => e.JobId == jobId).ExecuteDeleteAsync(CancellationToken.None);
            await db.LocalJobs.Where(j => j.Id == jobId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 刪不掉也只是等登記時順手清。
            logger.LogWarning("本機代理的工作清不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task DeleteEventsQuietlyAsync(Guid jobId)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.LocalEvents.Where(e => e.JobId == jobId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 清不掉也只是等登記時順手清；這裡不能丟例外，不然回覆存不下來。
            logger.LogWarning("本機代理的進度清不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    private sealed class Cursor
    {
        public int LastSeq { get; set; }
    }

    private sealed record Snapshot(LocalJobRow Job, IReadOnlyList<LocalEventRow> Events);
}
