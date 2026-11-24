using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 跨專案搜尋。「看得到哪些項目」用 <see cref="ReadableProjects.ItemsAsync"/>（專案與只看自己的範圍都在裡面），和專案清單、通知同一份規則；
/// 每個查詢都帶組織編號。ILIKE 的萬用字元照 <see cref="ItemQueryPlan"/> 的做法跳脫，使用者輸入的 % 與 _ 是普通字元。
/// </summary>
internal static class ItemSearch
{
    public const int MaxLimit = 20;

    // 快速跳轉的輸入，比標題的上限短：多出來的字不會有項目符合得了。
    public const int MaxTextLength = 200;

    // 排序的層級：編號完全符合、標題以它開頭、標題包含它。
    private const int ExactKeyRank = 0;
    private const int PrefixRank = 1;
    private const int ContainsRank = 2;

    public static Result<string> Validate(string? text, int limit)
    {
        if (limit is < 1 or > MaxLimit)
        {
            return WorkFailures.QueryInvalid(QueryPaths.Limit, $"數量要在 1 到 {MaxLimit} 之間。");
        }

        var trimmed = text?.Trim();
        return trimmed is not { Length: > 0 and <= MaxTextLength } || TextRules.HasForbiddenCharacters(trimmed, allowLineBreaks: false)
            ? WorkFailures.QueryInvalid(QueryPaths.Search, $"搜尋的文字要有 1 到 {MaxTextLength} 個字，而且不能有換行或控制字元。")
            : trimmed;
    }

    public static async Task<IReadOnlyList<WorkItem>> RunAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, string text, int limit, CancellationToken cancellationToken)
    {
        var readable = await ReadableProjects.ItemsAsync(db, cache, actor, cancellationToken);
        var contains = "%" + ItemQueryPlan.EscapeLike(text) + "%";
        var prefix = ItemQueryPlan.EscapeLike(text) + "%";
        var hasKey = ItemKeys.TryParse(text, out var keyProject, out var keyNumber);
        var escape = ItemQueryPlan.LikeEscape;

        var found = from item in readable
                    join project in db.Projects on new { item.OrganizationId, Id = item.ProjectId } equals new { project.OrganizationId, project.Id }
                    let isExact = hasKey && project.Key == keyProject && item.Number == keyNumber
                    where isExact || EF.Functions.ILike(item.Title, contains, escape)
                    orderby isExact ? ExactKeyRank : EF.Functions.ILike(item.Title, prefix, escape) ? PrefixRank : ContainsRank,
                        item.UpdatedAt descending, item.Id
                    select item;
        var rows = await found.Take(limit).Select(ItemReader.WithoutDescription).ToListAsync(cancellationToken);
        return await ItemReader.ToWorkItemsAsync(db, actor.OrganizationId, rows, cancellationToken);
    }
}
