using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant.Transcript;
using PmTool.Ai.Attachments;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Items;
using PmTool.Ai.LocalAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

internal sealed partial class AssistantService
{
    public async Task<Result<AssistantRun>> StartAsync(
        Actor actor, string? projectKey, Guid conversationId, AssistantMessageInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);

        // 檢查的順序見第 5.2 節；任何一項不過就什麼都不存。
        var attachmentIds = input.AttachmentIds ?? [];
        if (CheckOrganizationInput(projectKey, input, attachmentIds) is { } unsupported)
        {
            return unsupported;
        }

        var text = CheckText(input.Text, allowEmpty: attachmentIds.Count > 0);
        if (text.Failure is { } invalidText)
        {
            return invalidText;
        }

        var selection = await resolver.ResolveAsync(actor, input.Selection, cancellationToken);
        if (selection.Failure is { } invalidSelection)
        {
            return invalidSelection;
        }

        var scope = await OpenScopeAsync(actor, projectKey, cancellationToken);
        if (scope.Failure is { } noProject)
        {
            return noProject;
        }

        var canonicalKey = scope.Value.ProjectKey;
        if (CheckAgentVersionForOrganization(projectKey, selection.Value) is { } tooOld)
        {
            return tooOld;
        }

        // 在預留名額、拿租約之前查：查人失敗時什麼都還沒存，不會留下沒放掉的預留。
        var asker = await FindAskerAsync(actor, cancellationToken);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var conversation = await FindVisibleConversationAsync(db, actor, scope.Value, conversationId, cancellationToken);
        if (conversation is null)
        {
            return AssistantFailures.ConversationNotFound();
        }

        // 選的項目要是這個專案、看得到的；不然什麼都不存。
        var item = await FindSelectedItemAsync(actor, canonicalKey, input.ItemKey, cancellationToken);
        if (item.Failure is { } noItem)
        {
            return noItem;
        }

        var hostSourceId = selection.Value.Endpoint.HostSourceId;
        var isLocal = hostSourceId is null && selection.Value.Endpoint.Kind.IsLocalEngine();

        // 附件與文字附件的長度：在預留名額與用量之前擋下來，什麼都不存。
        var attachments = await CheckAttachmentsAsync(
            db, actor, canonicalKey, attachmentIds, selection.Value, text.Value.Length, cancellationToken);
        if (attachments.Failure is { } badAttachments)
        {
            return badAttachments;
        }

        if (conversation.MessageCount + 2 > AiRules.MaxMessagesPerConversation)
        {
            return AssistantFailures.TooLong();
        }

        var mcpUrl = isLocal ? mcpUrls.GetMcpUrl() : null;
        if (isLocal && mcpUrl is null)
        {
            return AssistantFailures.McpUrlMissing();
        }

        // 在存使用者的訊息之前預留名額與用量；預留失敗什麼都不存。
        var assistantMessageId = Guid.CreateVersion7();
        var reservation = await ReserveAsync(actor, selection.Value, canonicalKey, conversationId, assistantMessageId, cancellationToken);
        if (reservation.Failure is { } notReserved)
        {
            return notReserved;
        }

        var (hostJobId, countsUsage) = reservation.Value;

        var begun = await BeginReleasingOnFailureAsync(
            db, actor, conversation, new UserTurn(text.Value, item.Value?.Key, attachments.Value), canonicalKey,
            hostSourceId is not null, scope.Value.Project is not null && scope.Value.CanReadAllItems,
            hostJobId, countsUsage ? assistantMessageId : null, cancellationToken);
        if (begun.Failure is { } notBegun)
        {
            return notBegun;
        }

        var started = begun.Value;
        var session = runner.Start(new ReplyRequest(
            actor,
            canonicalKey,
            scope.Value.Project?.MyPermissions ?? [],
            conversationId,
            assistantMessageId,
            started.User.Sequence + 1,
            started.LeaseUntil,
            selection.Value,
            started.History,
            text.Value)
        {
            McpUrl = mcpUrl,
            Asker = asker,
            ItemKey = item.Value?.Key,
            HostJobId = hostJobId,
            CountsUsage = countsUsage,
            UserAttachments = AttachmentSelection.ToReferences(attachments.Value),
            SkippedAttachmentIds = started.SkippedAttachmentIds,
        });
        return new AssistantRun(
            AssistantMessageMapper.ToMessage(started.User, AttachmentSelection.ToContracts(attachments.Value)), assistantMessageId, session);
    }

    /// <summary>
    /// 主機來源預留排隊名額（排隊數、每天與每月的配額）；組織的連線預留用量（每人每天、全組織每月）。到了上限回失敗。
    /// 用量預留失敗時，剛才預留的主機名額要退掉。
    /// </summary>
    private async Task<Result<Reservation>> ReserveAsync(
        Actor actor, ResolvedSelection selection, string? projectKey, Guid conversationId, Guid assistantMessageId, CancellationToken cancellationToken)
    {
        var hostSourceId = selection.Endpoint.HostSourceId;
        Guid? hostJobId = null;
        if (hostSourceId is { } sourceId)
        {
            var reserved = await hostReservations.ReserveAsync(actor, sourceId, projectKey, conversationId, assistantMessageId, cancellationToken);
            if (reserved.Failure is { } notReserved)
            {
                return notReserved;
            }

            hostJobId = reserved.Value;
        }

        var countsUsage = hostSourceId is null && !selection.Endpoint.Kind.IsLocalEngine() && !selection.Endpoint.Personal;
        if (countsUsage
            && await usageLedger.ReserveAsync(actor, selection.Selection.ConnectionId, projectKey, assistantMessageId, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        return new Reservation(hostJobId, countsUsage);
    }

    private async Task<Result<WorkItem?>> FindSelectedItemAsync(
        Actor actor, string? projectKey, string? itemKey, CancellationToken cancellationToken)
    {
        if (itemKey is null)
        {
            return (WorkItem?)null;
        }

        var found = await ItemLookup.FindAsync(items, actor, projectKey!, itemKey, cancellationToken);
        return found.Failure is { } failure ? failure : (WorkItem?)found.Value;
    }

    // 數量、重複、都是自己在這個專案待送的、合計大小、選的來源讀得到；文字附件放不進去（API 模型每一輪的重送預算、
    // 本機與主機引擎的提示長度）也在這裡擋：不擋的話超過的部分會被悄悄丟掉，模型沒讀到卻照樣回答。
    private async Task<Result<IReadOnlyList<AiAttachmentRow>>> CheckAttachmentsAsync(
        AiDbContext db, Actor actor, string? projectKey, IReadOnlyList<Guid> ids, ResolvedSelection selection, int textLength, CancellationToken cancellationToken)
    {
        var attachments = await AttachmentSelection.CheckAsync(db, actor, projectKey, ids, AgentSourceOf(selection), time.GetUtcNow(), cancellationToken);
        if (attachments.Failure is not null)
        {
            return attachments;
        }

        var isEngine = selection.Endpoint.HostSourceId is not null || selection.Endpoint.Kind.IsLocalEngine();
        return await textLimits.CheckAsync(attachments.Value, textLength, isEngine, cancellationToken) is { } tooLong ? tooLong : attachments;
    }

    /// <summary>存使用者的訊息並拿租約；失敗或丟例外時把剛預留的名額與用量退掉。</summary>
    private async Task<Result<BegunRun>> BeginReleasingOnFailureAsync(
        AiDbContext db, Actor actor, AiConversationRow conversation, UserTurn turn, string? projectKey, bool queued, bool seesAllItems,
        Guid? hostJobId, Guid? usageId, CancellationToken cancellationToken)
    {
        try
        {
            var begun = await BeginRunAsync(db, actor, conversation, turn, projectKey, queued, seesAllItems, cancellationToken);
            if (begun.Failure is not null)
            {
                await ReleaseReservationsAsync(hostJobId, usageId);
            }

            return begun;
        }
        catch
        {
            await ReleaseReservationsAsync(hostJobId, usageId);
            throw;
        }
    }

    private sealed record Reservation(Guid? HostJobId, bool CountsUsage);

    /// <summary>不選專案的對話不收選的項目與附件。</summary>
    private static Failure? CheckOrganizationInput(string? projectKey, AssistantMessageInput input, IReadOnlyList<Guid> attachmentIds) =>
        projectKey is null && (input.ItemKey is not null || attachmentIds.Count > 0)
            ? AssistantFailures.OrganizationChatUnsupported()
            : null;

    /// <summary>不選專案的對話用本機或主機的引擎時，kumi 要夠新（舊版一定要有專案代號、也不認得新工具）。</summary>
    private static Failure? CheckAgentVersionForOrganization(string? projectKey, ResolvedSelection selection) =>
        projectKey is null
            && AgentSourceOf(selection) is { } agent
            && !AgentVersions.IsAtLeast(agent.Version, AiRules.MinAgentVersionForOrganizationChat)
                ? AssistantFailures.AgentUpdateRequired(agent)
                : null;

    /// <summary>發問的人自己的帳號資料（寫給模型的說明用）；查不到（理論上不會）就不寫，不擋這則訊息。</summary>
    private async Task<Asker?> FindAskerAsync(Actor actor, CancellationToken cancellationToken)
    {
        var found = await people.FindManyAsync(actor.OrganizationId, [actor.PersonId], cancellationToken);
        return found.TryGetValue(actor.PersonId, out var person)
            ? new Asker(person.PersonId, person.DisplayName, person.Email, person.Role, person.JobTitle)
            : null;
    }

    /// <summary>選的是本機或主機的引擎時，那個來源的能力；API 的模型是 null（都讀得到）。</summary>
    internal static AgentSource? AgentSourceOf(ResolvedSelection selection) =>
        selection.Endpoint.HostSourceId is not null || selection.Endpoint.Kind.IsLocalEngine()
            ? new AgentSource(selection.Endpoint.Kind, selection.Endpoint.AgentVersion, selection.Endpoint.HostSourceId is not null, selection.ConnectionName)
            : null;

    private async Task ReleaseReservationsAsync(Guid? hostJobId, Guid? usageId)
    {
        if (hostJobId is { } jobId)
        {
            await hostReservations.ReleaseAsync(jobId);
        }

        if (usageId is { } id)
        {
            await usageLedger.ReleaseAsync(id);
        }
    }

    private static Result<string> CheckText(string? text, bool allowEmpty)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0 && !allowEmpty)
        {
            return AssistantFailures.MessageInvalid("訊息不能是空的。");
        }

        if (trimmed.Length > AiRules.MaxMessageLength)
        {
            return AssistantFailures.MessageInvalid($"訊息最長 {AiRules.MaxMessageLength} 個字元。");
        }

        // PostgreSQL 不收 NUL，其他控制字元（換行與 Tab 除外）也沒有人會打；擋在這裡，不要等到資料庫丟 500。
        return trimmed.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))
            ? AssistantFailures.MessageInvalid("訊息裡有不能用的控制字元。")
            : trimmed;
    }

    /// <summary>
    /// 在一個交易裡、以這個人的 advisory lock 排隊：數他同時在回覆的對話、拿這則對話的租約、存下使用者的訊息。
    /// 條件式的 UPDATE 是真正的互斥（好幾個 API 程序之間也只有一個拿得到）；advisory lock 只讓「數出來的數量」不被同時的請求搶先。
    /// </summary>
    private async Task<Result<BegunRun>> BeginRunAsync(
        AiDbContext db, Actor actor, AiConversationRow conversation, UserTurn turn, string? projectKey, bool queued, bool seesAllItems,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"ai_runs:" + actor.PersonId}, 0))", cancellationToken);

        // 主機來源要先排隊：租約多給排隊的期限，不然排隊中租約就過了，同一則對話又能再送。
        var until = now + timings.RunLease + (queued ? hostTimings.QueueTimeout : TimeSpan.Zero);
        if (await TakeLeaseAsync(db, actor, conversation.Id, now, until, cancellationToken) is { } notTaken)
        {
            return notTaken;
        }

        // 拿到租約之後才讀訊息數與歷史：這時沒有別的回覆能再改它們。
        var current = await db.Conversations.AsNoTracking().SingleAsync(c => c.Id == conversation.Id, cancellationToken);
        if (current.MessageCount + 2 > AiRules.MaxMessagesPerConversation)
        {
            return AssistantFailures.TooLong();
        }

        var history = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && m.OrganizationId == actor.OrganizationId)
            .ToListAsync(cancellationToken);

        // 歷史要在提交之前還原：讀不出來就整個交易不提交（租約與使用者的訊息一起回滾），
        // 不然租約已經拿了、請求卻失敗，這則對話會一直是「正在回覆」直到租約過期。
        if (!TryReplay(history, out var replay))
        {
            return AssistantFailures.Unreadable();
        }

        // 供應商拒絕過、之後不再重送的附件（在這則對話裡）：交易裡只讀編號，檔案要到交易之後才讀。
        var skipped = await db.Attachments.AsNoTracking()
            .Where(a => a.ConversationId == conversation.Id && a.ReplaySkippedAt != null)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var user = NewUserRow(actor, conversation.Id, current.MessageCount + 1, turn, now);
        db.Messages.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        // 附件在同一個交易裡綁上：綁不到（剛好被刪掉、過期）整個交易不提交，租約與使用者的訊息一起回滾。
        if (await AttachmentSelection.BindAsync(db, actor, projectKey, conversation.Id, user.Id, turn.Attachments, now, cancellationToken) is { } notBound)
        {
            return notBound;
        }

        var title = TitleOf(turn.Text.Length > 0 ? turn.Text : turn.Attachments[0].FileName);
        var sequence = user.Sequence;
        await db.Conversations
            .Where(c => c.Id == conversation.Id)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(c => c.MessageCount, sequence)
                    .SetProperty(c => c.UpdatedAt, now)
                    .SetProperty(c => c.Title, c => c.Title ?? title)
                    .SetProperty(c => c.SawAllItems, c => c.SawAllItems || seesAllItems),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new BegunRun(user, replay, until, skipped.ToHashSet());
    }

    private static async Task<Failure?> TakeLeaseAsync(
        AiDbContext db, Actor actor, Guid conversationId, DateTimeOffset now, DateTimeOffset until, CancellationToken cancellationToken)
    {
        // 不算這則對話自己：它正在回覆時，使用者該看到的是「這則對話正在回覆」，不是「同時太多」。
        var others = await db.Conversations.CountAsync(
            c => c.OrganizationId == actor.OrganizationId && c.PersonId == actor.PersonId && c.Id != conversationId && c.RunningUntil > now,
            cancellationToken);
        if (others >= AiRules.MaxConcurrentRunsPerPerson)
        {
            return AssistantFailures.TooManyRuns();
        }

        var taken = await db.Conversations
            .Where(c => c.Id == conversationId
                && c.OrganizationId == actor.OrganizationId
                && c.PersonId == actor.PersonId
                && (c.RunningUntil == null || c.RunningUntil < now))
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.RunningUntil, until), cancellationToken);
        return taken == 0 ? AssistantFailures.Busy() : null;
    }

    private static bool TryReplay(IReadOnlyList<AiMessageRow> history, out IReadOnlyList<ChatMessage> messages)
    {
        try
        {
            messages = TranscriptReplay.ToChatMessages(history);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            messages = [];
            return false;
        }
    }

    private static AiMessageRow NewUserRow(Actor actor, Guid conversationId, int sequence, UserTurn turn, DateTimeOffset now)
    {
        // 沒打字（只附了檔案）時不放空的文字段落：Anthropic 不收空的文字區塊，網頁也不必畫空的泡泡。
        var parts = turn.Text.Length > 0 ? new AssistantPart[] { new AssistantTextPart(turn.Text) } : [];
        var contents = new List<TranscriptContent>();
        if (turn.Text.Length > 0)
        {
            contents.Add(new TranscriptText(turn.Text));
        }

        contents.AddRange(AttachmentSelection.ToTranscript(turn.Attachments));
        return new AiMessageRow
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            OrganizationId = actor.OrganizationId,
            Sequence = sequence,
            Role = AssistantMessageMapper.UserRoleName,
            Parts = AssistantMessageMapper.WriteParts(parts),
            Transcript = AssistantJson.Serialize(new[] { new TranscriptMessage(TranscriptMessage.UserRole, contents) }),
            CreatedAt = now,
            ItemKey = turn.ItemKey,
        };
    }

    /// <summary>標題：第一則訊息的前幾個字，換行與 Tab 換成空白；不把一個字（代理對）切成兩半。</summary>
    private static string TitleOf(string text)
    {
        var flat = string.Concat(text.Select(c => c is '\n' or '\r' or '\t' ? ' ' : c)).Trim();
        if (flat.Length <= AiRules.MaxTitleLength)
        {
            return flat;
        }

        return AiText.Cut(flat, AiRules.MaxTitleLength).TrimEnd();
    }

    /// <summary>使用者這一則：整理過的文字（可以是空的）、選的項目與已經檢查過的附件。</summary>
    private sealed record UserTurn(string Text, string? ItemKey, IReadOnlyList<AiAttachmentRow> Attachments);

    private sealed record BegunRun(
        AiMessageRow User, IReadOnlyList<ChatMessage> History, DateTimeOffset LeaseUntil, IReadOnlySet<Guid> SkippedAttachmentIds);
}
