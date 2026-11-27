using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.ItemFinding;

/// <summary>
/// 寫 <c>ai.conversation_reads</c> 的唯一一處（find_items 與組織對話的專案工具共用）。同一個專案只記一次（唯一鍵，重複的略過）；
/// 只在對話是這個人的時候寫：條件放在 SELECT 裡，對話不存在或是別人的就一筆都不寫。
/// </summary>
internal static class ConversationReadWriter
{
    public static async Task WriteAsync(
        IDbContextFactory<AiDbContext> contexts,
        Actor actor,
        Guid conversationId,
        IReadOnlyCollection<Guid> projectIds,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (projectIds.Count == 0)
        {
            return;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        foreach (var projectId in projectIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO ai.conversation_reads (conversation_id, project_id, organization_id, person_id, created_at)
                SELECT c.id, {projectId}, c.organization_id, c.person_id, {now}
                FROM ai.conversations c
                WHERE c.id = {conversationId} AND c.organization_id = {actor.OrganizationId} AND c.person_id = {actor.PersonId}
                ON CONFLICT (conversation_id, project_id) DO NOTHING
                """,
                cancellationToken);
        }
    }
}
