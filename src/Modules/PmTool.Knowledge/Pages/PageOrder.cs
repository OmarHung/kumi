using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

/// <summary>
/// 同一層頁面的位置：永遠是 0 起連續。受影響的那一層整層重新編號，看不到的頁面一起編（相對順序不變），不然位置會撞號。
/// </summary>
internal static class PageOrder
{
    /// <summary>還不在這個位置的頁面（新搬進來、剛復原的）用這個當目前的位置，一定會被寫入。</summary>
    public const int Unplaced = -1;

    public static IReadOnlyList<(Guid Id, int Current)> Of(IEnumerable<PageInfo> pages) => [.. pages.Select(p => (p.Id, p.Position))];

    /// <summary>在 <paramref name="index"/> 的位置放進一頁（超過尾端就放最後）。</summary>
    public static IReadOnlyList<(Guid Id, int Current)> Insert(
        IReadOnlyList<(Guid Id, int Current)> siblings, int index, Guid pageId, int current)
    {
        var list = siblings.ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), (pageId, current));
        return list;
    }

    /// <summary>依順序把位置改成 0、1、2…，只寫真的不同的。</summary>
    public static async Task RenumberAsync(
        KnowledgeDbContext db, Guid organizationId, Guid spaceId, IReadOnlyList<(Guid Id, int Current)> ordered, CancellationToken cancellationToken)
    {
        var changed = ordered.Select((entry, index) => (entry.Id, entry.Current, Index: index)).Where(e => e.Current != e.Index).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        var ids = changed.Select(e => e.Id).ToArray();
        var positions = changed.Select(e => e.Index).ToArray();
        await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE knowledge.pages AS p SET position = v.position
            FROM unnest({ids}::uuid[], {positions}::integer[]) AS v(id, position)
            WHERE p.id = v.id AND p.organization_id = {organizationId} AND p.space_id = {spaceId}
            """,
            cancellationToken);
    }
}
