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
        try
        {
            // 主機來源的兩段期限（排隊、執行）由轉送自己管；這裡只是整則回覆的最後防線。
            using var timeout = new CancellationTokenSource(
                isHosted ? hostTimings.QueueTimeout + timings.ReplyTimeout + HostLimits.RunSlack : timings.ReplyTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.StopToken, timeout.Token);
            try
            {
                outcome = isHosted
                    ? await hostRelay.RunAsync(await ResolveAttachmentsAsync(request, linked.Token), localRecorder, session, linked.Token)
                    : isLocal
                        ? await relay.RunAsync(await ResolveAttachmentsAsync(request, linked.Token), localRecorder, session, linked.Token)
                        : await StreamAsync(request, accumulator, session, linked.Token);
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
        var messages = await attachmentReplay.ResolveAsync(Compose(request), AttachmentTarget.ApiModel, cancellationToken);
        await foreach (var update in client.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var runEvent in accumulator.Apply(update))
            {
                session.Writer.TryWrite(runEvent);
            }
        }

        return Conclude(accumulator, session);
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
    /// 本機與主機上的引擎只收文字：把這一則與歷史裡的附件換成文字檔的內容或一行說明，結果放回 <c>History</c> 與 <c>UserText</c>，
    /// 後面的 <c>LocalPromptBuilder</c> 照舊排成文字。
    /// </summary>
    private async Task<ReplyRequest> ResolveAttachmentsAsync(ReplyRequest request, CancellationToken cancellationToken)
    {
        if (request.UserAttachments.Count == 0 && !request.History.Any(m => m.Contents.Any(c => c is AttachmentReference)))
        {
            return request;
        }

        var all = new List<ChatMessage>(request.History) { UserMessage(request) };
        var resolved = await attachmentReplay.ResolveAsync(all, AttachmentTarget.TextOnly, cancellationToken);
        var userText = string.Join("\n\n", resolved[^1].Contents.OfType<TextContent>().Select(t => t.Text));
        return request with { History = [.. resolved.Take(resolved.Count - 1)], UserText = userText };
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
            new(ChatRole.System, AssistantPrompt.SystemPrompt),
            new(ChatRole.System, AssistantPrompt.ProjectNote(request.ProjectKey, request.Permissions)),
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
}
