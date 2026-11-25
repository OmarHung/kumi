using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>
/// 直接掛在某個項目底下的一個項目，只有說明它是誰用得到的欄位。
/// <paramref name="Hidden"/>：呼叫者看不到它（只看自己的角色）。它照樣擋刪除與改類型，但編號與標題不能出現在回覆裡。
/// </summary>
internal sealed record ChildItem(long Number, string Title, string Type, bool Hidden = false);

/// <summary>
/// 下層項目：刪除與改類型要看的「底下還掛著什麼」。刪除的下層不算。
/// 呼叫端先鎖住上層那一列再讀，要掛上來的新增或修改會等它提交（見 <see cref="ItemStore.ShareAsync"/>），讀到的就是全部。
/// </summary>
internal static class ChildItems
{
    /// <summary>Details 裡一個下層的路徑：children/下層的編號。</summary>
    public const string PathPrefix = "children/";

    /// <summary>依編號，和清單的預設順序一樣。看不到的下層也在裡面（要擋刪除與改類型），只是標成 <see cref="ChildItem.Hidden"/>。</summary>
    public static async Task<List<ChildItem>> OfAsync(WorkDbContext db, ItemEntity parent, ItemScope scope, CancellationToken cancellationToken)
    {
        var children = ItemStore.Live(db)
            .AsNoTracking()
            .Where(i => i.OrganizationId == parent.OrganizationId && i.ParentId == parent.Id);
        var all = await children.OrderBy(i => i.Number).Select(i => new ChildItem(i.Number, i.Title, i.Type)).ToListAsync(cancellationToken);
        if (!scope.OwnOnly || all.Count == 0)
        {
            return all;
        }

        var visible = (await scope.Apply(children).Select(i => i.Number).ToListAsync(cancellationToken)).ToHashSet();
        return [.. all.Select(child => visible.Contains(child.Number) ? child : child with { Number = 0, Title = string.Empty, Hidden = true })];
    }

    /// <summary>還有下層時不能刪除；每個下層一筆，使用者看得出要先處理哪些。</summary>
    public static Failure HasChildren(string projectKey, string itemKey, IReadOnlyList<ChildItem> children) =>
        new(FailureKind.Conflict, WorkErrorCodes.ItemHasChildren, $"項目 {itemKey} 底下還有{CountText(children)}，要先把它們移開或刪除，才能刪除這個項目。")
        {
            Details =
            [
                .. children.Where(child => !child.Hidden).Select(child =>
                    Detail(WorkErrorCodes.ItemHasChildren, projectKey, child, $"{Describe(projectKey, child)}還掛在這個項目底下，要先把它移開或刪除。")),
                .. children.Any(child => child.Hidden)
                    ? [HiddenDetail(WorkErrorCodes.ItemHasChildren, "還有你看不到的項目掛在這個項目底下，要請能看到的人先把它移開或刪除。")]
                    : Array.Empty<FailureDetail>(),
            ],
        };

    /// <summary>訊息裡的下層數量：只算呼叫者看得到的，看不到的只說有，不給數量（數量也是資訊）。</summary>
    private static string CountText(IReadOnlyList<ChildItem> children)
    {
        var visible = children.Count(child => !child.Hidden);
        if (visible == children.Count)
        {
            return $" {visible} 個項目";
        }

        return visible == 0 ? "你看不到的項目" : $" {visible} 個項目，以及你看不到的項目";
    }

    public static FailureDetail Detail(string code, string projectKey, ChildItem child, string message) =>
        new(code, PathPrefix + ItemKeys.Format(projectKey, child.Number), message);

    /// <summary>看不到的下層：路徑只到 children，不帶編號，不讓人從這裡知道它是誰。</summary>
    public static FailureDetail HiddenDetail(string code, string message) => new(code, PathPrefix.TrimEnd('/'), message);

    /// <summary>訊息裡怎麼稱呼一個下層：標題加編號，同名的項目也分得出來。</summary>
    public static string Describe(string projectKey, ChildItem child) => $"{Quote(child.Title)}（{ItemKeys.Format(projectKey, child.Number)}）";
}
