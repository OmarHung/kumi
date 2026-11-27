using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemFinding;

/// <summary>記下對話讀過的專案（<see cref="IAssistantConversationReads"/>）。</summary>
internal sealed class AssistantConversationReads(
    IWorkAccessQueries access,
    IDbContextFactory<AiDbContext> contexts,
    TimeProvider time) : IAssistantConversationReads
{
    public async Task RememberAsync(
        Actor actor, Guid? conversationId, IReadOnlyCollection<string> projectKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(projectKeys);
        if (conversationId is not { } conversation || projectKeys.Count == 0)
        {
            return;
        }

        // 只記現在看得到全部項目的專案：只看自己的人本來就只讀得到自己的，記了反而會讓對話馬上被刪。
        var projectIds = new List<Guid>();
        foreach (var key in projectKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var project = await access.FindVisibleProjectAsync(actor, key, cancellationToken);
            if (project is not null && project.Permissions.Contains(WorkPermissions.ItemRead))
            {
                projectIds.Add(project.ProjectId);
            }
        }

        await ConversationReadWriter.WriteAsync(contexts, actor, conversation, projectIds, time.GetUtcNow(), cancellationToken);
    }
}
