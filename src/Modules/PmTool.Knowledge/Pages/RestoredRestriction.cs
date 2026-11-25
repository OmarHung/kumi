using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

/// <summary>
/// 復原時保住原本繼承的限制。一頁原本可能靠上層的名單保護（自己沒有名單）；放不回原本的上層時（上層還在垃圾桶、
/// 或搬得太深放不下），那些上層不再是它的祖先，繼承就斷了。這時把「不再是祖先的那幾層」的名單取交集，
/// 寫成這一頁自己的限制，復原之後能看到的人不會比復原之前多。不拒絕復原：拿回頁面的人照樣拿得回來。
/// 原本的上層鏈是沿著 <c>parent_id</c> 往上走（含垃圾桶裡的，軟刪除的資料列還在），不需要另外記錄。
/// </summary>
internal static class RestoredRestriction
{
    /// <summary>復原後這一頁自己的名單，與設定它的人：參與交集的每一層都是同一個人設的才用他，否則是 null（只有管理者能放寬）。</summary>
    public sealed record Inherited(IReadOnlyList<Guid> People, Guid? RestrictedBy);

    /// <summary>
    /// 復原之後這一頁自己該有的名單；不需要加任何限制時是 null。順序依這一頁自己的名單、再由近到遠的上層。
    /// 交集是空的時只留執行復原的人。擁有者不是執行復原的人：不然他就能拿掉原本別人設的限制。
    /// </summary>
    public static async Task<Inherited?> ComputeAsync(
        KnowledgeDbContext db, Actor actor, PageScope scope, PageInfo top, PageInfo? newParent, CancellationToken cancellationToken)
    {
        var kept = scope.ChainTo(newParent?.Id).Select(p => p.Id).ToHashSet();
        var lost = scope.ChainTo(top.ParentId).Where(p => p.IsRestricted && !kept.Contains(p.Id)).Reverse().ToList();
        if (lost.Count == 0)
        {
            return null;
        }

        var layers = (top.IsRestricted ? lost.Prepend(top) : lost).ToList();
        var layerIds = layers.Select(p => p.Id).ToList();
        var rows = await db.PageRestrictions.AsNoTracking()
            .Where(r => r.OrganizationId == actor.OrganizationId && layerIds.Contains(r.PageId))
            .OrderBy(r => r.Position)
            .Select(r => new { r.PageId, r.PersonId })
            .ToListAsync(cancellationToken);
        var lists = rows.ToLookup(r => r.PageId, r => r.PersonId);
        var common = lists[layerIds[0]].Where(person => layerIds.Skip(1).All(id => lists[id].Contains(person))).ToList();
        var owners = layers.Select(p => p.RestrictedBy).Distinct().ToList();
        return new Inherited(common.Count > 0 ? common : [actor.PersonId], owners.Count == 1 ? owners[0] : null);
    }

    /// <summary>整份取代這一頁自己的名單，並標成有限制。</summary>
    public static async Task WriteAsync(
        KnowledgeDbContext db, Guid organizationId, Guid pageId, Inherited inherited, CancellationToken cancellationToken)
    {
        await db.PageRestrictions.Where(r => r.OrganizationId == organizationId && r.PageId == pageId).ExecuteDeleteAsync(cancellationToken);
        db.PageRestrictions.AddRange(inherited.People.Select((person, position) => new PageRestrictionRow
        {
            OrganizationId = organizationId,
            PageId = pageId,
            PersonId = person,
            Position = position,
        }));
        await db.Pages.Where(p => p.OrganizationId == organizationId && p.Id == pageId)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.IsRestricted, true).SetProperty(p => p.RestrictedBy, inherited.RestrictedBy), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }
}
