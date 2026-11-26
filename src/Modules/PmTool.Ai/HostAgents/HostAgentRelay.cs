using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Ai.Assistant;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.HostAgents;

/// <summary>
/// 網頁的回覆交給主機上的 worker 跑（internal/HANDOFF-host-agent.md 第 6.5 節）：把送出時預留的工作填上內容、排進隊伍，
/// 然後讀 worker 回報的進度、轉成事件，直到結束、逾時、被停止。兩段期限分開：排隊 <see cref="HostTimings.QueueTimeout"/>，
/// 執行（從領走算）<see cref="AssistantTimings.ReplyTimeout"/>；領走之後 <see cref="HostTimings.LeaseTimeout"/> 沒回報是「結果不明」，不重跑。
/// 工作的狀態都在資料庫裡，所以領工作的程序和轉送的程序不必是同一個。
/// </summary>
internal sealed class HostAgentRelay(
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time,
    HostTimings timings,
    AssistantTimings assistantTimings,
    HostAgentSignals signals,
    ILogger<HostAgentRelay> logger)
{
    public async Task<ReplyOutcome> RunAsync(
        ReplyRequest request, LocalReplyAccumulator recorder, AssistantRunSession session, CancellationToken cancellationToken)
    {
        var jobId = request.HostJobId ?? throw new InvalidOperationException("主機來源的回覆沒有預留的工作。");
        recorder.JobId = jobId;
        var cursor = new Cursor();
        try
        {
            return await EnqueueAsync(request, jobId, cancellationToken)
                ? await FollowAsync(jobId, recorder, session, cursor, cancellationToken)
                : new ReplyOutcome(AssistantMessageStatus.Failed, HostMessages.Vanished);
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
            // 讀進度失敗：工作不能留在隊伍裡被稍後領走，不然使用者看到失敗了、引擎卻還在跑。
            await ForceFinishQuietlyAsync(jobId);
            throw;
        }
        finally
        {
            await DeleteEventsQuietlyAsync(jobId);
        }
    }

    /// <summary>
    /// 預留之後、排進隊伍之前就失敗了（讀附件的檔案失敗、被取消）：馬上退還預留的名額，不要等預留過期；
    /// 不然同一個人在那之前會一直被「已經有一則在排隊」擋著。盡力而為，退不掉也只是等過期。
    /// </summary>
    public async Task AbandonReservationQuietlyAsync(Guid jobId)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await HostJobFinisher.FinishAsync(
                db, jobId, new HostFinish(AssistantMessageStatus.Failed, "送出失敗，沒有執行。"), time.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("主機代理預留的工作退不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    /// <summary>預留的工作填上內容、排進隊伍。預留已經被退還（來源被關、逾時清掉）時回 false。</summary>
    private async Task<bool> EnqueueAsync(ReplyRequest request, Guid jobId, CancellationToken cancellationToken)
    {
        var selection = request.Selection.Selection;
        var payload = AssistantJson.Serialize(new StoredHostJob(
            request.Selection.Endpoint.Kind,
            selection.Model,
            selection.Effort,
            LocalPromptBuilder.HostSystemPrompt(request.ProjectKey, request.Permissions, request.ItemKey, request.Asker),
            LocalPromptBuilder.Prompt(request.History, request.UserText, request.Asker),
            LocalAgentTools.Allowed,
            AiRules.MaxToolCallsPerReply + 1)
        {
            Attachments = request.JobAttachments,
        });
        var attachmentIds = request.JobAttachments.Select(a => a.Id).ToArray();
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var agentId = await db.HostJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => (Guid?)j.AgentId).SingleOrDefaultAsync(cancellationToken);
        var updated = await db.HostJobs.Where(j => j.Id == jobId && j.State == HostJobStates.Reserved).ExecuteUpdateAsync(
            set => set
                .SetProperty(j => j.Payload, payload)
                .SetProperty(j => j.AttachmentIds, attachmentIds)
                .SetProperty(j => j.State, HostJobStates.Queued)
                .SetProperty(j => j.QueuedAt, now),
            cancellationToken);
        if (updated == 0 || agentId is null)
        {
            return false;
        }

        // 同一個程序內等著的長輪詢立刻去領；別的程序靠資料庫的備援查詢。
        signals.Signal(agentId.Value);
        return true;
    }

    private async Task<ReplyOutcome> FollowAsync(
        Guid jobId, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor, CancellationToken cancellationToken)
    {
        var queuedAt = time.GetTimestamp();
        var lastProgress = queuedAt;
        long? runStartedAt = null;
        DateTimeOffset? lastReport = null;
        int? lastPosition = null;
        long? lastPositionCheck = null;
        while (true)
        {
            var snapshot = await ReadAsync(jobId, cursor.LastSeq, cancellationToken);
            if (runStartedAt is null && snapshot.Job.ClaimedAt is not null)
            {
                // 輪到了：先送「開始」，再送它的進度。
                session.Writer.TryWrite(new AssistantRunStarted());
                runStartedAt = lastProgress = time.GetTimestamp();
            }

            var news = Publish(snapshot.Events, recorder, session, cursor);

            // 先看有沒有超過上限：同一輪讀到超長的文字和「完成」時，被截斷的回覆不能算完整。
            if (recorder.Problem is { } problem)
            {
                await ForceFinishAsync(jobId, new HostFinish(AssistantMessageStatus.Failed, problem));
                return new ReplyOutcome(AssistantMessageStatus.Failed, problem);
            }

            if (snapshot.Job.State == HostJobStates.Finished)
            {
                return Concluded(snapshot.Job, recorder);
            }

            if (runStartedAt is null)
            {
                if (await WaitingAsync(snapshot.Job, queuedAt, lastPositionCheck, lastPosition, session) is { } waiting)
                {
                    if (waiting.Outcome is { } outcome)
                    {
                        return outcome;
                    }

                    (lastPositionCheck, lastPosition) = (waiting.CheckedAt, waiting.Position);
                }
            }
            else if (await RunningAsync(snapshot.Job, news, runStartedAt.Value, lastReport, lastProgress, jobId, recorder, session, cursor) is { } running)
            {
                if (running.Outcome is { } outcome)
                {
                    return outcome;
                }

                (lastProgress, lastReport) = (running.LastProgress, running.LastReport);
            }

            await Task.Delay(timings.PollInterval, time, cancellationToken);
        }
    }

    private sealed record WaitStep(ReplyOutcome? Outcome, long? CheckedAt, int? Position);

    private sealed record RunStep(ReplyOutcome? Outcome, long LastProgress, DateTimeOffset? LastReport);

    /// <summary>還在排隊：逾時就結束（用量退還）；位置變了（或第一次）才送一次。</summary>
    private async Task<WaitStep> WaitingAsync(
        HostJobRow job, long queuedAt, long? lastCheck, int? lastPosition, AssistantRunSession session)
    {
        if (time.GetElapsedTime(queuedAt) >= timings.QueueTimeout)
        {
            // 同一刻被領走的話領走的贏（OnlyBeforeClaim），下一輪就看得到「開始」。
            var finish = new HostFinish(AssistantMessageStatus.Failed, HostMessages.QueueTimeout) { OnlyBeforeClaim = true };
            if (await ForceFinishAsync(job.Id, finish))
            {
                return new WaitStep(new ReplyOutcome(AssistantMessageStatus.Failed, HostMessages.QueueTimeout), lastCheck, lastPosition);
            }
        }

        if (lastCheck is { } checkedAt && time.GetElapsedTime(checkedAt) < timings.PositionInterval)
        {
            return new WaitStep(null, lastCheck, lastPosition);
        }

        var position = await PositionAsync(job);
        if (position != lastPosition)
        {
            session.Writer.TryWrite(new AssistantRunQueued(position));
        }

        return new WaitStep(null, time.GetTimestamp(), position);
    }

    /// <summary>執行中：領走之後太久沒回報是結果不明；執行太久就請它停下。</summary>
    private async Task<RunStep> RunningAsync(
        HostJobRow job,
        bool news,
        long runStartedAt,
        DateTimeOffset? lastReport,
        long lastProgress,
        Guid jobId,
        LocalReplyAccumulator recorder,
        AssistantRunSession session,
        Cursor cursor)
    {
        if (news || job.LastReportAt != lastReport)
        {
            lastProgress = time.GetTimestamp();
            lastReport = job.LastReportAt;
        }

        if (time.GetElapsedTime(lastProgress) >= timings.LeaseTimeout)
        {
            var finish = new HostFinish(AssistantMessageStatus.Failed, HostMessages.OutcomeUnknown) { OutcomeUnknown = true, CheckNeverStarted = true };
            await ForceFinishAsync(jobId, finish);
            var message = await FinishErrorAsync(jobId) ?? HostMessages.OutcomeUnknown;
            return new RunStep(new ReplyOutcome(AssistantMessageStatus.Failed, message), lastProgress, lastReport);
        }

        if (time.GetElapsedTime(runStartedAt) >= assistantTimings.ReplyTimeout)
        {
            await StopAsync(jobId, recorder, session, cursor);
            return new RunStep(new ReplyOutcome(AssistantMessageStatus.Stopped, HostMessages.Timeout), lastProgress, lastReport);
        }

        return new RunStep(null, lastProgress, lastReport);
    }

    private static ReplyOutcome Concluded(HostJobRow job, LocalReplyAccumulator recorder)
    {
        recorder.SetUsage(UsageOf(job));
        var status = job.FinishStatus is { } name ? AssistantMessageMapper.ParseStatus(name) : AssistantMessageStatus.Failed;
        return status switch
        {
            AssistantMessageStatus.Complete => new ReplyOutcome(status, null),
            AssistantMessageStatus.Failed => new ReplyOutcome(status, job.FinishError ?? HostMessages.Unreported) { ReportedByEngine = true },
            _ => new ReplyOutcome(status, job.CancelMessage ?? job.FinishError),
        };
    }

    /// <summary>結束時實際存下的原因（「結果不明」或「沒有開始執行」由鎖住工作列之後的狀態決定）。</summary>
    private async Task<string?> FinishErrorAsync(Guid jobId)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        return await db.HostJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.FinishError).SingleOrDefaultAsync(CancellationToken.None);
    }

    private static AssistantUsage? UsageOf(HostJobRow job) =>
        job.InputTokens is null && job.OutputTokens is null ? null : new AssistantUsage(job.InputTokens ?? 0, job.OutputTokens ?? 0);

    /// <summary>
    /// 前面還有幾件（執行中的加排在前面的）再加一；1 是下一個。不算自己：讀到的快照還是排隊中，
    /// 但查到這裡時自己可能剛被領走，算進去的話會在「開始」之前多跳一次「排第 2」。
    /// </summary>
    private async Task<int> PositionAsync(HostJobRow job)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        var seq = job.Seq;
        var source = job.SourceId;
        var self = job.Id;
        var ahead = await db.HostJobs.AsNoTracking().CountAsync(
            j => j.SourceId == source
                && j.Id != self
                && (j.State == HostJobStates.Running
                    || ((j.State == HostJobStates.Queued || j.State == HostJobStates.Reserved) && j.Seq < seq)),
            CancellationToken.None);
        return ahead + 1;
    }

    /// <summary>
    /// 請 worker 停下來：還沒被領走的直接結束（用量退還）；領走的標上取消，worker 下一次回報時知道、結束引擎、送結束，
    /// 最多等 <see cref="HostTimings.StopGrace"/>，等不到就由伺服器結束它（照「中斷」存、用量照算）。最後的進度照樣收進回覆。
    /// 這裡不能再被取消、也不能丟例外：回覆一定要存得下來。
    /// </summary>
    private async Task StopAsync(Guid jobId, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor)
    {
        try
        {
            var stopped = new HostFinish(AssistantMessageStatus.Stopped, null) { OnlyBeforeClaim = true };
            if (!await ForceFinishAsync(jobId, stopped))
            {
                await RequestCancelAsync(jobId);
            }

            var started = time.GetTimestamp();
            while (true)
            {
                var snapshot = await ReadAsync(jobId, cursor.LastSeq, CancellationToken.None);
                Publish(snapshot.Events, recorder, session, cursor);
                if (snapshot.Job.State == HostJobStates.Finished)
                {
                    recorder.SetUsage(UsageOf(snapshot.Job));
                    return;
                }

                if (time.GetElapsedTime(started) >= timings.StopGrace)
                {
                    await ForceFinishAsync(jobId, new HostFinish(AssistantMessageStatus.Stopped, null));
                    return;
                }

                await Task.Delay(timings.PollInterval, time, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("主機代理的回覆停不下來：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task RequestCancelAsync(Guid jobId)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await db.HostJobs.Where(j => j.Id == jobId && j.State == HostJobStates.Running).ExecuteUpdateAsync(
            set => set.SetProperty(j => j.CancelRequested, true).SetProperty(j => j.ToolTokenHash, (string?)null), CancellationToken.None);
    }

    /// <summary>伺服器自己結束這件工作（逾時、停不下來）。標上取消：worker 再回報時會知道、停下引擎。</summary>
    private async Task<bool> ForceFinishAsync(Guid jobId, HostFinish finish)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        var finished = await HostJobFinisher.FinishAsync(db, jobId, finish, time.GetUtcNow(), CancellationToken.None);
        if (finished)
        {
            await db.HostJobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(
                set => set.SetProperty(j => j.CancelRequested, true), CancellationToken.None);
            if (await db.HostJobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => (Guid?)j.AgentId).SingleOrDefaultAsync(CancellationToken.None) is { } agentId)
            {
                signals.Signal(agentId);
            }
        }

        return finished;
    }

    private async Task ForceFinishQuietlyAsync(Guid jobId)
    {
        try
        {
            await ForceFinishAsync(jobId, new HostFinish(AssistantMessageStatus.Failed, null));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("主機代理的工作結束不了：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task<Snapshot> ReadAsync(Guid jobId, int afterSeq, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 先讀工作、再讀進度：結束之前的進度都已經提交，工作是「已結束」時進度一定讀得齊。
        var job = await db.HostJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, cancellationToken);
        var events = await db.HostJobEvents.AsNoTracking()
            .Where(e => e.JobId == jobId && e.Seq > afterSeq)
            .OrderBy(e => e.Seq)
            .ToListAsync(cancellationToken);
        return new Snapshot(job, events);
    }

    private static bool Publish(
        IReadOnlyList<HostJobEventRow> events, LocalReplyAccumulator recorder, AssistantRunSession session, Cursor cursor)
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

    /// <summary>回覆存完之後刪掉那件工作（連進度、工具呼叫的紀錄）：工作列的內容含整段對話。用量另外存，不刪。</summary>
    public async Task DeleteJobQuietlyAsync(Guid jobId)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.HostJobEvents.Where(e => e.JobId == jobId).ExecuteDeleteAsync(CancellationToken.None);
            await db.HostToolCalls.Where(c => c.JobId == jobId).ExecuteDeleteAsync(CancellationToken.None);
            await db.HostJobs.Where(j => j.Id == jobId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 刪不掉也只是等登記時順手清。
            logger.LogWarning("主機代理的工作清不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    private async Task DeleteEventsQuietlyAsync(Guid jobId)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.HostJobEvents.Where(e => e.JobId == jobId).ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogWarning("主機代理的進度清不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    private sealed class Cursor
    {
        public int LastSeq { get; set; }
    }

    private sealed record Snapshot(HostJobRow Job, IReadOnlyList<HostJobEventRow> Events);
}
