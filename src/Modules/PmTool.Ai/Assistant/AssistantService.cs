using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Attachments;
using PmTool.Ai.HostAgents;
using PmTool.Ai.Persistence;
using PmTool.Ai.Providers;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

/// <summary>
/// 專案的 AI 助理的對話服務（internal/HANDOFF-ai-assistant.md 第 5 節）。這個檔案是讀、開、刪對話；送訊息在 <c>AssistantService.Start.cs</c>。
/// 權限一律先看專案：看不到回工作引擎的 NotFound，沒有 blueprint.read 回 <see cref="AiErrorCodes.AssistantForbidden"/>；
/// 對話一定要是自己的（組織、專案、人都對），不是的一律 <see cref="AiErrorCodes.ConversationNotFound"/>。
/// </summary>
internal sealed partial class AssistantService(
    IDbContextFactory<AiDbContext> contexts,
    IProjectService projects,
    IWorkItemService items,
    AiSelectionResolver resolver,
    AssistantReplyRunner runner,
    IMcpUrlProvider mcpUrls,
    HostSourceOptions hostSources,
    HostReservations hostReservations,
    ConnectionUsageLedger usageLedger,
    TimeProvider time,
    AssistantTimings timings,
    HostTimings hostTimings) : IAssistantService
{
    public async Task<Result<IReadOnlyList<AssistantConversationSummary>>> ListAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var project = await OpenProjectAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var canSeeAll = CanReadAllItems(project.Value);
        if (!canSeeAll)
        {
            await DeleteSeenAllAsync(db, actor, project.Value.Project.Id, null, now, cancellationToken);
        }

        var rows = await db.Conversations
            .AsNoTracking()
            .Where(c => c.OrganizationId == actor.OrganizationId && c.ProjectId == project.Value.Project.Id && c.PersonId == actor.PersonId
                && (canSeeAll || !c.SawAllItems))
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Take(AiRules.MaxConversationsListed)
            .ToListAsync(cancellationToken);
        return rows.Select(row => ToSummary(row, now)).ToList();
    }

    public async Task<Result<AssistantConversationSummary>> CreateAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var project = await OpenProjectAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        if (!await AssistantAccess.IsAvailableAsync(db, hostSources, actor, cancellationToken))
        {
            return AssistantFailures.Unavailable();
        }

        var now = time.GetUtcNow();
        var row = new AiConversationRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Value.Project.Id,
            PersonId = actor.PersonId,
            MessageCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Conversations.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToSummary(row, now);
    }

    public async Task<Result<AssistantConversation>> GetAsync(
        Actor actor, string projectKey, Guid conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var project = await OpenProjectAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var conversation = await FindVisibleConversationAsync(db, actor, project.Value, conversationId, cancellationToken);
        if (conversation is null)
        {
            return AssistantFailures.ConversationNotFound();
        }

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.OrganizationId == actor.OrganizationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);

        // 附件只讀還綁在訊息上的（對話還在，所以不會有孤兒），依訊息分好。
        var messageIds = messages.Select(m => m.Id).ToList();
        var attachments = (await db.Attachments
            .AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.MessageId != null && messageIds.Contains(a.MessageId.Value))
            .OrderBy(a => a.Position)
            .ToListAsync(cancellationToken))
            .ToLookup(a => a.MessageId!.Value, AttachmentMapper.ToContract);
        return new AssistantConversation(
            ToSummary(conversation, time.GetUtcNow()),
            [.. messages.Select(m => AssistantMessageMapper.ToMessage(m, [.. attachments[m.Id]]))]);
    }

    public async Task<Result<Unit>> DeleteAsync(
        Actor actor, string projectKey, Guid conversationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var project = await OpenProjectAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var conversation = await FindConversationAsync(db, actor, project.Value.Project.Id, conversationId, cancellationToken);
        if (conversation is null)
        {
            return AssistantFailures.ConversationNotFound();
        }

        // 條件寫在刪除裡：檢查和刪除之間回覆才開始的話，不能把正在寫入的對話刪掉。訊息由資料庫的外鍵連帶刪除。
        var now = time.GetUtcNow();
        var deleted = await db.Conversations
            .Where(c => c.Id == conversationId && c.OrganizationId == actor.OrganizationId && (c.RunningUntil == null || c.RunningUntil < now))
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? AssistantFailures.Busy() : Unit.Value;
    }

    private Task<Result<ProjectDetail>> OpenProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        AssistantAccess.OpenProjectAsync(projects, actor, projectKey, cancellationToken);

    private static bool CanReadAllItems(ProjectDetail project) => AssistantAccess.CanReadAllItems(project);

    /// <summary>
    /// 找對話，並套用 AI-06：這則對話是在「看得到全部項目」時回覆過的，而他現在只剩自己的項目，歷史裡可能有他現在看不到的內容，
    /// 所以整則刪掉、當作不存在（不回內容）。正在回覆的刪不掉（回覆還要存），這次一樣當作不存在，之後再刪。
    /// </summary>
    private async Task<AiConversationRow?> FindVisibleConversationAsync(
        AiDbContext db, Actor actor, ProjectDetail project, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await FindConversationAsync(db, actor, project.Project.Id, conversationId, cancellationToken);
        if (conversation is null || !conversation.SawAllItems || CanReadAllItems(project))
        {
            return conversation;
        }

        await DeleteSeenAllAsync(db, actor, project.Project.Id, conversationId, time.GetUtcNow(), cancellationToken);
        return null;
    }

    private static Task<int> DeleteSeenAllAsync(
        AiDbContext db, Actor actor, Guid projectId, Guid? conversationId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Conversations
            .Where(c => c.OrganizationId == actor.OrganizationId
                && c.ProjectId == projectId
                && c.PersonId == actor.PersonId
                && c.SawAllItems
                && (conversationId == null || c.Id == conversationId)
                && (c.RunningUntil == null || c.RunningUntil < now))
            .ExecuteDeleteAsync(cancellationToken);

    private static Task<AiConversationRow?> FindConversationAsync(
        AiDbContext db, Actor actor, Guid projectId, Guid conversationId, CancellationToken cancellationToken) =>
        db.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == conversationId
                    && c.OrganizationId == actor.OrganizationId
                    && c.ProjectId == projectId
                    && c.PersonId == actor.PersonId,
                cancellationToken);

    private static AssistantConversationSummary ToSummary(AiConversationRow row, DateTimeOffset now) => new(
        row.Id, row.Title, row.MessageCount, row.RunningUntil > now, row.CreatedAt, row.UpdatedAt);
}
