using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>
/// 上層項目：要在同一個專案、不能繞圈、類型要在藍圖的 parents 裡。
/// 繞圈先於類型檢查：藍圖保證類型的上層關係沒有圈，所以照著類型掛的項目本來就不會繞圈，
/// 但「把自己或自己底下的項目設為上層」這種操作，說它會繞圈比說類型不符更好懂。
/// </summary>
internal static class ParentRules
{
    // 類型最多 20 種而且上層關係沒有圈，正常的資料不會超過這個深度；超過表示資料壞了，當成會繞圈擋下來。
    private const int MaxDepth = 100;

    public static async Task<Checked<ItemParent?>> CheckAsync(
        WorkDbContext db,
        ProjectAccess access,
        ItemType itemType,
        ItemEntity? current,
        Optional<string?> parentKey,
        CancellationToken cancellationToken)
    {
        if (!parentKey.HasValue || parentKey.Value is null)
        {
            return Checked<ItemParent?>.Ok(parentKey.HasValue ? new Optional<ItemParent?>(null) : default);
        }

        var key = parentKey.Value;
        if (!ItemKeys.TryParse(key, out var projectKey, out var number) || projectKey != access.Project.Key)
        {
            return UnknownParent(key);
        }

        // 先不上鎖讀一次、檢查繞圈與類型，不合法的直接擋下，不去等上層那一列的鎖。互相設成上層的兩個請求各自鎖住自己再等對方，
        // 會互鎖；藍圖的類型上層關係沒有圈，所以兩邊之中一定有一邊在這裡就被擋下、放開自己那一列。
        var (organizationId, projectId) = (access.Project.OrganizationId, access.Project.Id);
        var unlocked = await ItemStore.FindAsync(db, organizationId, projectId, number, cancellationToken);
        if (await ProblemAsync(db, access, itemType, current, key, unlocked, cancellationToken) is { } early)
        {
            return early;
        }

        // 取共用鎖（刪除或改類型要等這裡提交才檢查下層，見 ItemStore.ShareAsync）之後再檢查一次才是最終的判斷：
        // 沒上鎖讀完到上鎖之間，它可能被刪除、改了類型，或被搬到這個項目底下。
        var parent = await ItemStore.ShareAsync(db, organizationId, projectId, number, cancellationToken);
        return await ProblemAsync(db, access, itemType, current, key, parent, cancellationToken)
            ?? Checked<ItemParent?>.Ok(new ItemParent(parent!.Id, parent.Number));
    }

    /// <summary>刪除的項目當成不存在。上層沒有問題時是 null。</summary>
    private static async Task<Checked<ItemParent?>?> ProblemAsync(
        WorkDbContext db, ProjectAccess access, ItemType itemType, ItemEntity? current, string parentKey, ItemEntity? parent,
        CancellationToken cancellationToken)
    {
        // 看不到的上層（只看自己的角色）和不存在一樣。
        if (parent is null || !access.Items.Contains(parent))
        {
            return UnknownParent(parentKey);
        }

        if (current is not null && await FormsCycleAsync(db, current, parent, cancellationToken))
        {
            return Fail(
                WorkErrorCodes.ItemParentCycle,
                $"不能把{Quote(parentKey)}設為上層：它就是這個項目，或是掛在這個項目底下，上層關係會繞成一圈。");
        }

        return itemType.Parents.Contains(parent.Type)
            ? null
            : Fail(WorkErrorCodes.ItemParentNotAllowed, NotAllowedMessage(access, itemType, parent.Type));
    }

    private static Checked<ItemParent?> UnknownParent(string parentKey) =>
        Fail(WorkErrorCodes.ItemUnknownParent, $"找不到上層項目{Quote(parentKey)}；上層項目要在同一個專案裡。");

    /// <summary>從新的上層往上走，走到自己就是繞圈。沒刪除的項目往上都是沒刪除的項目，不必另外排除刪除的。</summary>
    private static async Task<bool> FormsCycleAsync(WorkDbContext db, ItemEntity item, ItemEntity parent, CancellationToken cancellationToken)
    {
        Guid? cursor = parent.Id;
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (cursor is not { } id)
            {
                return false;
            }

            if (id == item.Id)
            {
                return true;
            }

            cursor = await db.Items
                .Where(i => i.OrganizationId == item.OrganizationId && i.Id == id)
                .Select(i => i.ParentId)
                .SingleOrDefaultAsync(cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// <paramref name="parentVisible"/> 是 false（上層是呼叫者看不到的項目）時不說出上層的類型名稱，只說它不符合。
    /// </summary>
    public static string NotAllowedMessage(ProjectAccess access, ItemType itemType, string parentType, bool parentVisible = true)
    {
        var names = itemType.Parents.Select(p => Quote(access.Schema.ItemTypes.GetValueOrDefault(p)?.Name ?? p));
        var parentName = access.Schema.ItemTypes.GetValueOrDefault(parentType)?.Name ?? parentType;
        if (itemType.Parents.Count == 0)
        {
            return $"{Quote(itemType.Name)}只能放在最上層，不能掛在其他項目底下。";
        }

        return parentVisible
            ? $"{Quote(itemType.Name)}不能掛在{Quote(parentName)}底下，只能掛在{string.Join("、", names)}底下。"
            : $"{Quote(itemType.Name)}不能掛在你看不到的上層項目底下，只能掛在{string.Join("、", names)}底下。";
    }

    private static Checked<ItemParent?> Fail(string code, string message) =>
        Checked<ItemParent?>.Fail(code, CoreFields.Parent, message);
}
