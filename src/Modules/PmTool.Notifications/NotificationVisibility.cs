using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications;

/// <summary>
/// 一個人的通知現在還看得到哪些。收件匣與寄 Email 共用這一份：哪些專案、哪些項目看得到由工作引擎決定（<see cref="IWorkAccessQueries"/>），
/// 看不到的專案、看不到的項目的通知，既不列在收件匣、不算未讀，也不寄信。帳號通知沒有專案，只要是本人的就一直看得到。
/// </summary>
internal sealed class NotificationVisibility(IWorkAccessQueries access)
{
    // 一次問工作引擎的項目數：整個清單塞進一個查詢的參數會太長。
    private const int ItemCheckChunkSize = 1000;

    /// <summary>
    /// 這個人的通知，限於看得到的專案、看得到的項目。項目看不看得到要問工作引擎（它知道誰負責、誰建立）：
    /// 先問他只看自己項目的專案（沒有就不過濾），再找出那些專案的通知指到哪些項目、哪些現在看不到，再把這些項目排除在查詢外，分頁與計數照舊在資料庫裡做。
    /// 看不到的項目通常很少（負責人換掉之後的那幾個），所以排除的清單很短。
    /// </summary>
    public async Task<IQueryable<NotificationRow>> VisibleToAsync(NotificationsDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        Guid[] readableProjectIds = [.. await access.ReadableProjectIdsAsync(actor, cancellationToken)];
        var inProjects = db.Notifications.AsNoTracking().Where(n =>
            n.OrganizationId == actor.OrganizationId && n.RecipientId == actor.PersonId
            && (n.ProjectId == null || readableProjectIds.Contains(n.ProjectId.Value)));
        if (readableProjectIds.Length == 0)
        {
            return inProjects;
        }

        // 絕大多數人沒有只看自己的專案：不逐項確認，已刪除項目的通知也照舊留著。
        var ownOnly = (await access.OwnOnlyProjectIdsAsync(actor, cancellationToken)).ToArray();
        if (ownOnly.Length == 0)
        {
            return inProjects;
        }

        var itemIds = await inProjects
            .Where(n => n.ProjectId != null && n.ItemId != null && ownOnly.Contains(n.ProjectId.Value))
            .Select(n => n.ItemId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var hidden = new List<Guid>();
        foreach (var chunk in itemIds.Chunk(ItemCheckChunkSize))
        {
            var readable = await access.ReadableItemIdsAsync(actor, chunk, cancellationToken);
            hidden.AddRange(chunk.Where(id => !readable.Contains(id)));
        }

        var hiddenIds = hidden.ToArray();
        return hiddenIds.Length == 0 ? inProjects : inProjects.Where(n => n.ItemId == null || !hiddenIds.Contains(n.ItemId.Value));
    }
}
