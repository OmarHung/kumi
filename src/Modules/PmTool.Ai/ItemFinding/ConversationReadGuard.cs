using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Work;

namespace PmTool.Ai.ItemFinding;

/// <summary>
/// 對話讀過的專案（<c>ai.conversation_reads</c>）的檢查：這則對話讀過的任何一個專案，他現在看不到、或只剩看自己的項目（沒有 item.read），
/// 歷史裡就可能有他現在看不到的內容，所以整則刪掉。和既有的 <c>saw_all_items</c>（對話所屬的專案）同一套時機與「回覆進行中的不刪」。
/// </summary>
internal sealed class ConversationReadGuard(IServiceProvider services)
{
    /// <summary>
    /// 找出這個人（或指定的一則）的對話裡，讀過的專案現在不能再看的，沒在回覆的整則刪掉。
    /// 回傳所有這樣的對話編號（含回覆進行中、這次沒刪掉的）：呼叫的人要把它們當作不存在。
    /// 一次問出看得到的專案與權限，不是一個專案問一次；沒有任何記錄時連工作引擎都不問。
    /// </summary>
    public async Task<IReadOnlyList<Guid>> DeleteStaleAsync(
        AiDbContext db, Actor actor, Guid? conversationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reads = await db.ConversationReads
            .AsNoTracking()
            .Where(r => r.OrganizationId == actor.OrganizationId && r.PersonId == actor.PersonId
                && (conversationId == null || r.ConversationId == conversationId))
            .Select(r => new { r.ConversationId, r.ProjectId })
            .ToListAsync(cancellationToken);
        if (reads.Count == 0)
        {
            return [];
        }

        var access = services.GetRequiredService<IWorkAccessQueries>();
        // 只問記過的那幾個專案：組織管理員看得到整個組織，全部載入（含每個專案的藍圖）會讓每次開、送、列都多一趟大查詢。
        // 不在結果裡的就是看不到，語意和全部載入相同。
        var visible = await access.FindVisibleProjectsAsync(actor, [.. reads.Select(r => r.ProjectId).Distinct()], cancellationToken);
        var stale = reads
            .Where(r => !visible.TryGetValue(r.ProjectId, out var project) || !project.Permissions.Contains(WorkPermissions.ItemRead))
            .Select(r => r.ConversationId)
            .Distinct()
            .ToList();
        if (stale.Count == 0)
        {
            return stale;
        }

        await db.Conversations
            .Where(c => stale.Contains(c.Id)
                && c.OrganizationId == actor.OrganizationId
                && c.PersonId == actor.PersonId
                && (c.RunningUntil == null || c.RunningUntil < now))
            .ExecuteDeleteAsync(cancellationToken);
        return stale;
    }
}
