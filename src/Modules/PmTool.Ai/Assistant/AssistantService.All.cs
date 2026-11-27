using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.Assistant;

internal sealed partial class AssistantService
{
    /// <summary>
    /// 自己所有的對話（各專案與不選專案的），新的在前，最多 <see cref="AiRules.MaxConversationsListed"/> 則。
    /// 每個專案照開那個專案的對話時同一套規則（<see cref="OpenScopeAsync"/>）：看不到專案、沒有讀設定的權限的不列；
    /// 看過全部項目而現在只剩自己的，和專案的清單一樣整則刪掉。
    /// 先算出能列的專案、在查詢裡過濾，最後才取前幾則：不然看不到的專案裡較新的對話會把看得到的擠掉。
    /// </summary>
    public async Task<Result<IReadOnlyList<AssistantConversationListing>>> ListAllAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = time.GetUtcNow();
        var scopes = await OpenProjectScopesAsync(db, actor, cancellationToken);
        await DeleteSeenAllWhereOnlyOwnAsync(db, actor, scopes.Values, now, cancellationToken);
        var stale = await readGuard.DeleteStaleAsync(db, actor, null, now, cancellationToken);

        var seesAll = scopes.Where(s => s.Value.CanReadAllItems).Select(s => s.Key).ToList();
        var onlyOwn = scopes.Where(s => !s.Value.CanReadAllItems).Select(s => s.Key).ToList();
        var rows = await db.Conversations
            .AsNoTracking()
            .Where(c => c.OrganizationId == actor.OrganizationId && c.PersonId == actor.PersonId
                && !stale.Contains(c.Id)
                && (c.ProjectId == null
                    || seesAll.Contains(c.ProjectId.Value)
                    || (onlyOwn.Contains(c.ProjectId.Value) && !c.SawAllItems)))
            .OrderByDescending(c => c.UpdatedAt)
            .ThenByDescending(c => c.Id)
            .Take(AiRules.MaxConversationsListed)
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new AssistantConversationListing(
                row.ProjectId is { } projectId ? scopes[projectId].ProjectKey : null, ToSummary(row, now)))
            .ToList();
    }

    /// <summary>
    /// 自己有對話的專案各開一次（和專案的對話同一份檢查）；看不到的、沒有權限的不在結果裡。
    /// 專案的清單是工作引擎回的（看得到的），先用它把編號換成代號：不在裡面的就不必再問。
    /// 問的次數是「有對話、而且看得到的專案數」，和對話的則數無關。
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ConversationScope>> OpenProjectScopesAsync(
        AiDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var withConversations = await db.Conversations
            .AsNoTracking()
            .Where(c => c.OrganizationId == actor.OrganizationId && c.PersonId == actor.PersonId && c.ProjectId != null)
            .Select(c => c.ProjectId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var scopes = new Dictionary<Guid, ConversationScope>();
        if (withConversations.Count == 0)
        {
            return scopes;
        }

        var visible = await projects.ListAsync(actor, cancellationToken);
        foreach (var project in visible.Where(p => withConversations.Contains(p.Id)))
        {
            var scope = await OpenScopeAsync(actor, project.Key, cancellationToken);
            if (scope.Failure is null && scope.Value.ProjectId == project.Id)
            {
                scopes[project.Id] = scope.Value;
            }
        }

        return scopes;
    }

    /// <summary>只剩自己的項目的專案：看過全部項目的對話整則刪掉（AI-06，和專案的清單一樣），正在回覆的這次不刪（查詢照樣不列）。</summary>
    private static async Task DeleteSeenAllWhereOnlyOwnAsync(
        AiDbContext db, Actor actor, IEnumerable<ConversationScope> scopes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var scope in scopes.Where(s => !s.CanReadAllItems))
        {
            await DeleteSeenAllAsync(db, actor, scope.ProjectId, null, now, cancellationToken);
        }
    }
}
