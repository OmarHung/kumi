using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
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
        var rows = await db.Conversations
            .AsNoTracking()
            .Where(c => c.OrganizationId == actor.OrganizationId && c.ProjectId == project.Value.Project.Id && c.PersonId == actor.PersonId)
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

        // 組織沒開 AI 但允許個人連線時，成員也能開對話、用自己的連線送訊息；選的連線能不能用，送訊息時由 AiSelectionResolver 把關。
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        // 組織擁有者開了主機來源時，組織的有效成員也能開對話。
        if (!await db.Settings.AnyAsync(s => s.OrganizationId == actor.OrganizationId && (s.Enabled || s.AllowPersonalConnections), cancellationToken)
            && !await hostSources.AnyAsync(actor, cancellationToken))
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
        var conversation = await FindConversationAsync(db, actor, project.Value.Project.Id, conversationId, cancellationToken);
        if (conversation is null)
        {
            return AssistantFailures.ConversationNotFound();
        }

        var messages = await db.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.OrganizationId == actor.OrganizationId)
            .OrderBy(m => m.Sequence)
            .ToListAsync(cancellationToken);
        return new AssistantConversation(ToSummary(conversation, time.GetUtcNow()), [.. messages.Select(AssistantMessageMapper.ToMessage)]);
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

    /// <summary>看得到專案、而且有讀設定的權限。助理的所有操作都先過這裡。</summary>
    private async Task<Result<ProjectDetail>> OpenProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var project = await projects.GetAsync(actor, projectKey, cancellationToken);
        if (project.Failure is { } failure)
        {
            return failure;
        }

        return project.Value.MyPermissions.Contains(WorkPermissions.BlueprintRead) ? project : AssistantFailures.Forbidden();
    }

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
