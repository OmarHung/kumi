using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using PmTool.Abstractions;
using PmTool.Ai.Assistant.Transcript;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Items;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

internal sealed partial class AssistantService
{
    public async Task<Result<AssistantRun>> StartAsync(
        Actor actor, string projectKey, Guid conversationId, AssistantMessageInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);

        // 檢查的順序見第 5.2 節；任何一項不過就什麼都不存。
        var text = CheckText(input.Text);
        if (text.Failure is { } invalidText)
        {
            return invalidText;
        }

        var selection = await resolver.ResolveAsync(actor, input.Selection, cancellationToken);
        if (selection.Failure is { } invalidSelection)
        {
            return invalidSelection;
        }

        var project = await OpenProjectAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } noProject)
        {
            return noProject;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var conversation = await FindVisibleConversationAsync(db, actor, project.Value, conversationId, cancellationToken);
        if (conversation is null)
        {
            return AssistantFailures.ConversationNotFound();
        }

        // 選的項目要是這個專案、看得到的；不然什麼都不存。
        WorkItem? item = null;
        if (input.ItemKey is not null)
        {
            var found = await ItemLookup.FindAsync(items, actor, project.Value.Project.Key, input.ItemKey, cancellationToken);
            if (found.Failure is { } noItem)
            {
                return noItem;
            }

            item = found.Value;
        }

        if (conversation.MessageCount + 2 > AiRules.MaxMessagesPerConversation)
        {
            return AssistantFailures.TooLong();
        }

        var hostSourceId = selection.Value.Endpoint.HostSourceId;
        var isLocal = hostSourceId is null && selection.Value.Endpoint.Kind.IsLocalEngine();
        var mcpUrl = isLocal ? mcpUrls.GetMcpUrl() : null;
        if (isLocal && mcpUrl is null)
        {
            return AssistantFailures.McpUrlMissing();
        }

        // 主機來源：在存使用者的訊息之前預留名額（排隊數、每天與每月的配額）；預留失敗什麼都不存。
        var assistantMessageId = Guid.CreateVersion7();
        Guid? hostJobId = null;
        if (hostSourceId is { } sourceId)
        {
            var reserved = await hostReservations.ReserveAsync(
                actor, sourceId, project.Value.Project.Key, conversationId, assistantMessageId, cancellationToken);
            if (reserved.Failure is { } notReserved)
            {
                return notReserved;
            }

            hostJobId = reserved.Value;
        }

        // 組織的連線：在存使用者的訊息之前預留用量（每人每天、全組織每月）；到了上限什麼都不存。
        var countsUsage = hostSourceId is null && !isLocal && !selection.Value.Endpoint.Personal;
        if (countsUsage
            && await usageLedger.ReserveAsync(
                actor, selection.Value.Selection.ConnectionId, project.Value.Project.Key, assistantMessageId, cancellationToken) is { } overLimit)
        {
            return overLimit;
        }

        Result<BegunRun> begun;
        try
        {
            begun = await BeginRunAsync(
                db, actor, conversation, text.Value, item?.Key, hostSourceId is not null, CanReadAllItems(project.Value), cancellationToken);
        }
        catch
        {
            await ReleaseReservationsAsync(hostJobId, countsUsage ? assistantMessageId : null);
            throw;
        }

        if (begun.Failure is { } notBegun)
        {
            await ReleaseReservationsAsync(hostJobId, countsUsage ? assistantMessageId : null);
            return notBegun;
        }

        var started = begun.Value;
        var session = runner.Start(new ReplyRequest(
            actor,
            project.Value.Project.Key,
            project.Value.MyPermissions,
            conversationId,
            assistantMessageId,
            started.User.Sequence + 1,
            started.LeaseUntil,
            selection.Value,
            started.History,
            text.Value)
        {
            McpUrl = mcpUrl,
            ItemKey = item?.Key,
            HostJobId = hostJobId,
            CountsUsage = countsUsage,
        });
        return new AssistantRun(AssistantMessageMapper.ToMessage(started.User), assistantMessageId, session);
    }

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

    private static Result<string> CheckText(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
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
        AiDbContext db, Actor actor, AiConversationRow conversation, string text, string? itemKey, bool queued, bool seesAllItems,
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

        var user = NewUserRow(actor, conversation.Id, current.MessageCount + 1, text, itemKey, now);
        db.Messages.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        var title = TitleOf(text);
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
        return new BegunRun(user, replay, until);
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

    private static AiMessageRow NewUserRow(Actor actor, Guid conversationId, int sequence, string text, string? itemKey, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ConversationId = conversationId,
        OrganizationId = actor.OrganizationId,
        Sequence = sequence,
        Role = AssistantMessageMapper.UserRoleName,
        Parts = AssistantMessageMapper.WriteParts([new AssistantTextPart(text)]),
        Transcript = AssistantJson.Serialize(new[]
        {
            new TranscriptMessage(TranscriptMessage.UserRole, [new TranscriptText(text)]),
        }),
        CreatedAt = now,
        ItemKey = itemKey,
    };

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

    private sealed record BegunRun(
        AiMessageRow User, IReadOnlyList<ChatMessage> History, DateTimeOffset LeaseUntil);
}
