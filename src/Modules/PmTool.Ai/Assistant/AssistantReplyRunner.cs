using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Ai.Assistant.Tools;
using PmTool.Ai.Attachments;
using PmTool.Ai.HostAgents;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Assistant.Transcript;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 在背景跑一則回覆（internal/HANDOFF-ai-assistant.md 第 5.2 節）：呼叫模型、跑工具、把事件交給讀的人、不管怎麼結束都存。
/// 回覆比請求活得久，所以用自己的 DI 範圍：請求的範圍結束時，工作引擎的服務不能跟著被釋放。
/// 回覆中途程序當掉時，那則回覆不會存下來（使用者的訊息在）；租約過了就能再送，這個代價可以接受。
/// </summary>
internal sealed class AssistantReplyRunner(
    IServiceScopeFactory scopes,
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time,
    AssistantTimings timings,
    LocalAgentRelay relay,
    HostAgentRelay hostRelay,
    HostTimings hostTimings,
    AttachmentReplay attachmentReplay,
    ILogger<AssistantReplyRunner> logger)
{
    public const string TimeoutMessage = "超過 5 分鐘，回覆中斷了。";

    public const string RefusedMessage = "模型拒絕了這個要求，可以換一個模型或換個說法再試。";

    public const string TruncatedMessage = "回覆超過長度上限被截斷了，請把要求拆小一點再試。";

    public const string HistoryBinarySkippedNote = "之前附的圖片或 PDF 下一則不會再送；如果是它們讓引擎讀不了，再送一次就好。";

    public const string SaveFailedMessage = "回覆沒有存下來，請再送一次。";

    private const string CallLimitNote = "（這一則回覆呼叫工具的次數到了上限，先停在這裡。請告訴我接下來要做什麼。）";

    /// <summary>開始在背景回覆；事件從回傳的 session 讀。</summary>
    public AssistantRunSession Start(ReplyRequest request)
    {
        var session = new AssistantRunSession();
        session.Attach(Task.Run(() => RunAsync(request, session)));
        return session;
    }

    private async Task RunAsync(ReplyRequest request, AssistantRunSession session)
    {
        var isHosted = request.Selection.Endpoint.HostSourceId is not null;
        var isLocal = !isHosted && request.Selection.Endpoint.Kind.IsLocalEngine();
        var accumulator = new ReplyAccumulator();
        var localRecorder = new LocalReplyAccumulator();
        IReplyRecorder recorder = isLocal || isHosted ? localRecorder : accumulator;
        var outcome = new ReplyOutcome(AssistantMessageStatus.Complete, null);
        var sent = request;
        try
        {
            // 主機來源的兩段期限（排隊、執行）由轉送自己管；這裡只是整則回覆的最後防線。
            using var timeout = new CancellationTokenSource(
                isHosted ? hostTimings.QueueTimeout + timings.ReplyTimeout + HostLimits.RunSlack : timings.ReplyTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.StopToken, timeout.Token);
            try
            {
                if (isHosted || isLocal)
                {
                    sent = isHosted ? await ResolveForHostAsync(request, localRecorder, linked.Token) : await ResolveAttachmentsAsync(request, linked.Token);
                    outcome = isHosted
                        ? await hostRelay.RunAsync(sent, localRecorder, session, linked.Token)
                        : await relay.RunAsync(sent, localRecorder, session, linked.Token);
                }
                else
                {
                    outcome = await StreamAsync(request, accumulator, session, linked.Token);
                }
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            {
                outcome = new ReplyOutcome(
                    AssistantMessageStatus.Stopped, session.StopToken.IsCancellationRequested ? null : TimeoutMessage);
            }
            catch (Exception exception)
            {
                // 只記例外的種類：訊息可能帶請求的網址或內容，不能進記錄。
                logger.LogWarning("助理的回覆失敗：{ExceptionType}", exception.GetType().Name);
                outcome = new ReplyOutcome(AssistantMessageStatus.Failed, AiProviderFailures.Describe(exception, host: null, request.Selection.Endpoint.ApiKey, request.Selection.Endpoint.Personal));
            }

            outcome = await SkipHistoryBinaryAfterEngineFailureAsync(sent, outcome);
            var message = await FinishAsync(request, recorder, outcome, session);
            await SettleUsageQuietlyAsync(request, recorder, outcome);
            if (localRecorder.JobId is { } jobId)
            {
                await (isHosted ? hostRelay.DeleteJobQuietlyAsync(jobId) : relay.DeleteJobQuietlyAsync(jobId));
            }

            session.Writer.TryWrite(new AssistantRunCompleted(message));
        }
        finally
        {
            session.Writer.TryComplete();
            session.Dispose();
        }
    }

    private async Task<ReplyOutcome> StreamAsync(
        ReplyRequest request, ReplyAccumulator accumulator, AssistantRunSession session, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = new AssistantToolContext(
            request.Actor,
            request.ProjectKey,
            provider.GetRequiredService<IProjectService>(),
            provider.GetRequiredService<IBlueprintChangeService>(),
            provider.GetRequiredService<IBlueprintUsageService>(),
            provider.GetRequiredService<ITemplateLibrary>(),
            provider.GetRequiredService<IWorkItemService>(),
            provider.GetRequiredService<IItemActivityService>(),
            provider.GetRequiredService<IPeopleDirectory>(),
            provider.GetRequiredService<IItemProposalCreator>(),
            provider.GetRequiredService<IProjectProposalCreator>(),
            provider.GetRequiredService<INewItemProposalCreator>(),
            provider.GetRequiredService<IStaffingBriefService>(),
            provider.GetRequiredService<IMemberProposalCreator>(),
            request.ConversationId,
            logger);
        var selection = request.Selection.Selection;
        var options = AiRequestOptions.Create(request.Selection.Endpoint.Kind, selection.Model, selection.Effort);
        options.Tools = AssistantToolSet.Create(context);

        var inner = provider.GetRequiredService<IAiProviderGateway>().CreateChatClient(request.Selection.Endpoint, selection.Model);
        using var client = new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: loop =>
            {
                loop.MaximumIterationsPerRequest = AiRules.MaxToolCallsPerReply;
                loop.AllowConcurrentInvocation = false;
                loop.IncludeDetailedErrors = false;
            })
            .Build(provider);

        // 交易已經提交了：這時才讀附件的檔案（不佔著資料庫的交易），依預算換成圖片、PDF 與文字。
        var composed = Compose(request);
        var replay = new ReplayOptions { SkippedIds = request.SkippedAttachmentIds };
        var first = await attachmentReplay.ResolveAsync(composed, AttachmentTarget.ApiModel, replay, cancellationToken);
        var progress = new StreamProgress();
        try
        {
            await ForwardAsync(client, first.Messages, options, accumulator, session, progress, cancellationToken);
        }
        catch (Exception exception) when (ShouldRetryWithoutHistoryBinary(exception, first, progress))
        {
            // 供應商退回了請求，而請求帶著歷史的圖片或 PDF（加密、太多頁、損毀、尺寸太大）：拿掉重試一次，
            // 不然這則對話每一輪重送都被退回，永遠送不出去。退回的是這一則自己的附件時，重試也會失敗，照舊顯示原因。
            logger.LogWarning("供應商退回了請求，拿掉歷史的圖片與 PDF 重試一次：{ExceptionType}", exception.GetType().Name);
            await RecordRejectedCallQuietlyAsync(request);
            var retry = await attachmentReplay.ResolveAsync(
                composed, AttachmentTarget.ApiModel, replay with { OmitHistoryBinary = true }, cancellationToken);
            await ForwardAsync(client, retry.Messages, options, accumulator, session, progress, cancellationToken);
            await MarkReplaySkippedQuietlyAsync(request, first.HistoryBinaryIds);
        }

        return Conclude(accumulator, session);
    }

    private static async Task ForwardAsync(
        IChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        ReplyAccumulator accumulator,
        AssistantRunSession session,
        StreamProgress progress,
        CancellationToken cancellationToken)
    {
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            progress.HasOutput = true;
            foreach (var runEvent in accumulator.Apply(update))
            {
                session.Writer.TryWrite(runEvent);
            }
        }
    }

    // 已經有輸出（文字、工具呼叫）送給使用者之後不重試：重送會讓同一段文字重複出現。
    private static bool ShouldRetryWithoutHistoryBinary(Exception exception, ReplayResult first, StreamProgress progress) =>
        first.HistoryBinaryIds.Count > 0 && !progress.HasOutput && AiProviderFailures.IsInvalidRequest(exception);

    /// <summary>重試成功：不知道是歷史裡的哪一個被拒，那一次請求裡的歷史圖片與 PDF 一起記，之後不再重送。</summary>
    private async Task MarkReplaySkippedQuietlyAsync(ReplyRequest request, IReadOnlyList<Guid> attachmentIds)
    {
        try
        {
            var now = time.GetUtcNow();
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.Attachments
                .Where(a => a.OrganizationId == request.Actor.OrganizationId && attachmentIds.Contains(a.Id))
                .ExecuteUpdateAsync(set => set.SetProperty(a => a.ReplaySkippedAt, now), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 記不下來只是下一輪再被退回、再重試一次；回覆本身已經成功了。
            logger.LogError("助理附件的重送標記記不下來：{ExceptionType}", exception.GetType().Name);
        }
    }

    /// <summary>
    /// 本機或主機的引擎失敗、而這件工作帶著歷史的圖片或 PDF：不知道是不是它們讓引擎讀不了，但留著的話之後每一則都帶同一張、永遠失敗，
    /// 所以和 API 的路徑一樣記下不再重送，並在失敗訊息說明。不自動重跑（使用者的訊息已經算過一次了）。
    /// </summary>
    private async Task<ReplyOutcome> SkipHistoryBinaryAfterEngineFailureAsync(ReplyRequest sent, ReplyOutcome outcome)
    {
        if (outcome is not { Status: AssistantMessageStatus.Failed, ReportedByEngine: true } || sent.HistoryJobAttachmentIds.Count == 0)
        {
            return outcome;
        }

        await MarkReplaySkippedQuietlyAsync(sent, sent.HistoryJobAttachmentIds);
        return outcome with { Error = string.IsNullOrEmpty(outcome.Error) ? HistoryBinarySkippedNote : outcome.Error + "\n" + HistoryBinarySkippedNote };
    }

    private async Task RecordRejectedCallQuietlyAsync(ReplyRequest request)
    {
        if (!request.CountsUsage)
        {
            return;
        }

        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await ConnectionUsageLedger.RecordRejectedCallAsync(
                db,
                request.Actor.OrganizationId,
                request.Selection.Selection.ConnectionId,
                request.Actor.PersonId,
                request.ProjectKey,
                time.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError("助理被退回的那一次呼叫記不了用量：{ExceptionType}", exception.GetType().Name);
        }
    }

    private static ReplyOutcome Conclude(ReplyAccumulator accumulator, AssistantRunSession session)
    {
        if (accumulator.FinishReason == ChatFinishReason.ContentFilter)
        {
            return new ReplyOutcome(AssistantMessageStatus.Failed, RefusedMessage);
        }

        if (accumulator.FinishReason == ChatFinishReason.Length)
        {
            return new ReplyOutcome(AssistantMessageStatus.Failed, TruncatedMessage);
        }

        if (accumulator.HasUnresolvedCalls)
        {
            // 迴圈套件到了呼叫次數的上限就停下來，最後一輪模型要的工具沒有執行。
            Publish(session, accumulator.ResolveOpenCalls(ReplyAccumulator.CallLimitError));
            Publish(session, accumulator.AddNote(CallLimitNote));
        }

        return new ReplyOutcome(AssistantMessageStatus.Complete, null);
    }

    /// <summary>
    /// 本機與主機上的引擎：這一則與歷史裡的文字檔換成內容、圖片與 PDF 換成「〔附件 N：…〕」的位置（引擎的種類與 kumi 的版本不收、
    /// 超過預算的換成一行說明），結果放回 <c>History</c> 與 <c>UserText</c>，後面的 <c>LocalPromptBuilder</c> 照舊排成文字；
    /// 要交給引擎下載的清單放在 <c>JobAttachments</c>。
    /// </summary>
    private async Task<ReplyRequest> ResolveAttachmentsAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        if (request.UserAttachments.Count == 0 && !request.History.Any(m => m.Contents.Any(c => c is AttachmentReference)))
        {
            return request;
        }

        var all = new List<ChatMessage>(request.History) { UserMessage(request) };
        var options = new ReplayOptions
        {
            SkippedIds = request.SkippedAttachmentIds,
            Agent = AssistantService.AgentSourceOf(request.Selection),
        };
        var replayed = await attachmentReplay.ResolveAsync(all, AttachmentTarget.Agent, options, cancellationToken);
        var resolved = replayed.Messages;
        var userText = string.Join("\n\n", resolved[^1].Contents.OfType<TextContent>().Select(t => t.Text));
        return request with
        {
            History = [.. resolved.Take(resolved.Count - 1)],
            UserText = userText,
            JobAttachments = await LoadJobAttachmentsAsync(request, replayed.AgentAttachments, cancellationToken),
            HistoryJobAttachmentIds = replayed.HistoryBinaryIds,
        };
    }

    // 雜湊與大小以資料列為準（參照裡的是送出時的快照）；列不見了（對話剛好被刪）的略過，提示裡的編號會對不上，所以整件失敗比較對。
    private async Task<IReadOnlyList<LocalAgentJobAttachment>> LoadJobAttachmentsAsync(
        ReplyRequest request, IReadOnlyList<AttachmentReference> chosen, CancellationToken cancellationToken)
    {
        if (chosen.Count == 0)
        {
            return [];
        }

        var ids = chosen.Select(r => r.Id).ToList();
        var organizationId = request.Actor.OrganizationId;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id) && a.OrganizationId == organizationId)
            .ToDictionaryAsync(a => a.Id, cancellationToken);
        if (ids.Any(id => !rows.ContainsKey(id)))
        {
            throw new InvalidOperationException("對話裡的附件已經不存在。");
        }

        return [.. ids.Select(id => rows[id]).Select(row => new LocalAgentJobAttachment(
            row.Id, row.FileName, row.ContentType, AttachmentKinds.Parse(row.Kind), row.SizeBytes, row.Sha256))];
    }

    /// <summary>
    /// 主機來源在送出時就預留了一件工作（名額、用量）。讀附件失敗或被取消時要馬上退還，不要等預留過期：
    /// 不然那段時間同一個人被「已經有一則在排隊」擋著，用量也一直算在身上。
    /// </summary>
    private async Task<ReplyRequest> ResolveForHostAsync(ReplyRequest request, LocalReplyAccumulator recorder, CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveAttachmentsAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && request.HostJobId is not null)
        {
            // 設了編號，結尾的清理才會把這件已經結束的工作刪掉。
            recorder.JobId = request.HostJobId;
            await hostRelay.AbandonReservationQuietlyAsync(request.HostJobId.Value);
            throw;
        }
    }

    private static ChatMessage UserMessage(ReplyRequest request)
    {
        var contents = new List<AIContent>();
        if (request.UserText.Length > 0)
        {
            contents.Add(new TextContent(request.UserText));
        }

        contents.AddRange(request.UserAttachments);
        return new ChatMessage(ChatRole.User, contents);
    }

    private static List<ChatMessage> Compose(ReplyRequest request)
    {
        var messages = new List<ChatMessage>
        {
            request.ProjectKey is null
                ? new(ChatRole.System, AssistantPrompt.OrganizationSystemPrompt)
                : new(ChatRole.System, AssistantPrompt.SystemPrompt),
            request.ProjectKey is null
                ? new(ChatRole.System, AssistantPrompt.OrganizationNote(request.Actor.IsOrganizationAdmin, request.Asker))
                : new(ChatRole.System, AssistantPrompt.ProjectNote(request.ProjectKey, request.Permissions, request.Asker)),
        };
        if (request.ItemKey is not null)
        {
            messages.Add(new ChatMessage(ChatRole.System, AssistantPrompt.ItemNote(request.ItemKey)));
        }

        messages.AddRange(request.History);
        messages.Add(UserMessage(request));
        return messages;
    }

    /// <summary>
    /// 不管怎麼結束都要存、租約一定要放掉、最後一定要有訊息可以送完成事件：整個收尾（組資料列、存檔）都在 try 裡，
    /// 任何一步丟例外都先放租約，再回一則最簡單的失敗訊息；這裡不會丟例外，讀的人才收得到結束的事件。
    /// </summary>
    private async Task<AssistantMessage> FinishAsync(
        ReplyRequest request, IReplyRecorder accumulator, ReplyOutcome outcome, AssistantRunSession session)
    {
        AiMessageRow? row = null;
        try
        {
            var finished = accumulator.Finish(ReplyAccumulator.InterruptedError);
            Publish(session, finished.Events);
            row = ToRow(request, accumulator, outcome, finished.Parts, finished.Transcript);

            // 使用者按停止、關頁面時讀的 token 已經取消，存檔不能跟著取消。
            await SaveAsync(request, row);
            return AssistantMessageMapper.ToMessage(row);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError("助理的回覆收不了尾：{ExceptionType}", exception.GetType().Name);
            await ReleaseLeaseQuietlyAsync(request);
            return FailedMessage(request, row);
        }
    }

    // 不管回覆怎麼結束（完成、停止、失敗）都要記帳，不然預留一直是 reserved、token 沒算進月上限；記不了只寫記錄，不能讓讀的人收不到結束的事件。
    private async Task SettleUsageQuietlyAsync(ReplyRequest request, IReplyRecorder recorder, ReplyOutcome outcome)
    {
        if (!request.CountsUsage)
        {
            return;
        }

        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await ConnectionUsageLedger.SettleAsync(
                db, request.AssistantMessageId, outcome.Status, recorder.InputTokens, recorder.OutputTokens, time.GetUtcNow());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogError("助理的用量記不了帳：{ExceptionType}", exception.GetType().Name);
        }
    }

    // 存不下來時盡量保留已經有的內容；連資料列都組不出來就只剩失敗的說明。
    private AssistantMessage FailedMessage(ReplyRequest request, AiMessageRow? row) =>
        AssistantMessageMapper.ToMessage((row ?? new AiMessageRow
        {
            Id = request.AssistantMessageId,
            ConversationId = request.ConversationId,
            OrganizationId = request.Actor.OrganizationId,
            Sequence = request.AssistantSequence,
            Role = AssistantMessageMapper.AssistantRoleName,
            Parts = AssistantMessageMapper.WriteParts([]),
            Transcript = "[]",
            CreatedAt = time.GetUtcNow(),
        }) with { Status = "failed", Error = SaveFailedMessage });

    private AiMessageRow ToRow(
        ReplyRequest request,
        IReplyRecorder accumulator,
        ReplyOutcome outcome,
        IReadOnlyList<AssistantPart> parts,
        IReadOnlyList<TranscriptMessage> transcript) => new()
    {
        Id = request.AssistantMessageId,
        ConversationId = request.ConversationId,
        OrganizationId = request.Actor.OrganizationId,
        Sequence = request.AssistantSequence,
        Role = AssistantMessageMapper.AssistantRoleName,
        Parts = AssistantMessageMapper.WriteParts(parts),
        Transcript = AssistantJson.Serialize(transcript),
        Status = AssistantMessageMapper.StatusName(outcome.Status),
        Error = outcome.Error is { } error ? AiText.Cut(error, AiRules.MaxErrorLength) : null,
        ConnectionId = request.Selection.Selection.ConnectionId,
        ConnectionName = request.Selection.ConnectionName,
        Model = request.Selection.Selection.Model,
        Effort = AiEnumNames.NameOf(request.Selection.Selection.Effort),
        InputTokens = accumulator.InputTokens,
        OutputTokens = accumulator.OutputTokens,
        CreatedAt = time.GetUtcNow(),
    };

    private async Task SaveAsync(ReplyRequest request, AiMessageRow row)
    {
        await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        db.Messages.Add(row);
        await db.SaveChangesAsync(CancellationToken.None);
        var now = row.CreatedAt;
        var count = request.AssistantSequence;
        var lease = request.LeaseUntil;
        await db.Conversations
            .Where(c => c.Id == request.ConversationId && c.OrganizationId == request.Actor.OrganizationId)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(c => c.MessageCount, count)
                    .SetProperty(c => c.UpdatedAt, now)
                    .SetProperty(c => c.RunningUntil, c => c.RunningUntil == lease ? null : c.RunningUntil),
                CancellationToken.None);
        await transaction.CommitAsync(CancellationToken.None);
    }

    private async Task ReleaseLeaseQuietlyAsync(ReplyRequest request)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(CancellationToken.None);
            await db.Conversations
                .Where(c => c.Id == request.ConversationId
                    && c.OrganizationId == request.Actor.OrganizationId
                    && c.RunningUntil == request.LeaseUntil)
                .ExecuteUpdateAsync(set => set.SetProperty(c => c.RunningUntil, (DateTimeOffset?)null), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // 放不掉也只是等租約過期；這裡不能再丟例外，不然讀的人收不到結束的事件。
            logger.LogError("助理對話的租約放不掉：{ExceptionType}", exception.GetType().Name);
        }
    }

    private static void Publish(AssistantRunSession session, IEnumerable<AssistantRunEvent> events)
    {
        foreach (var runEvent in events)
        {
            session.Writer.TryWrite(runEvent);
        }
    }

    /// <summary>這一則回覆有沒有已經送出任何輸出給使用者（有了就不能重送請求）。</summary>
    private sealed class StreamProgress
    {
        public bool HasOutput { get; set; }
    }
}
