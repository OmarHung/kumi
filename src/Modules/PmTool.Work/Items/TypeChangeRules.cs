using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>
/// 改類型時上下層要符合新類型：上層的類型要在新類型的 parents 裡，每個沒刪除的下層的類型都要允許掛在新類型底下。
/// 所有問題一起回報，使用者一次看得到要先處理哪些。呼叫端已經鎖住這個項目那一列。
/// </summary>
internal static class TypeChangeRules
{
    public static async Task<IReadOnlyList<FailureDetail>> CheckAsync(
        WorkDbContext db, ProjectAccess access, ItemEntity item, ItemType next, CancellationToken cancellationToken)
    {
        var parent = await ParentProblemAsync(db, access, item, next, cancellationToken);
        var children = await ChildItems.OfAsync(db, item, access.Items, cancellationToken);
        return [.. new[] { parent }.OfType<FailureDetail>(), .. ChildProblems(access, next, children)];
    }

    /// <summary>
    /// 上層取共用鎖再看它的類型：它同時在改類型的話，等它提交後照新的類型檢查。
    /// 有沒刪除的下層時上層刪不掉，所以一定找得到；找不到表示資料壞了，是要修的錯。
    /// </summary>
    private static async Task<FailureDetail?> ParentProblemAsync(
        WorkDbContext db, ProjectAccess access, ItemEntity item, ItemType next, CancellationToken cancellationToken)
    {
        if (item.ParentId is not { } parentId)
        {
            return null;
        }

        var parent = await ItemStore.ShareByIdAsync(db, item.OrganizationId, parentId, cancellationToken)
            ?? throw new InvalidOperationException($"項目 {item.Id} 的上層 {parentId} 不存在或已經刪除。");
        return next.Parents.Contains(parent.Type)
            ? null
            : new FailureDetail(WorkErrorCodes.ItemParentNotAllowed, CoreFields.Parent, ParentRules.NotAllowedMessage(access, next, parent.Type, access.Items.Contains(parent)));
    }

    private static IEnumerable<FailureDetail> ChildProblems(ProjectAccess access, ItemType next, IReadOnlyList<ChildItem> children)
    {
        var projectKey = access.Project.Key;
        var hiddenReported = false;
        foreach (var child in children)
        {
            if (child.Hidden)
            {
                // 看不到的下層不論類型一律合併成同一筆：允許與否不同的話，換幾種類型試就推得出它是什麼類型。
                if (hiddenReported)
                {
                    continue;
                }

                hiddenReported = true;
                yield return ChildItems.HiddenDetail(
                    WorkErrorCodes.ItemChildrenNotAllowed,
                    "有你看不到的下層項目掛在這個項目底下，要請看得到全部的人來改類型。");
                continue;
            }

            // 下層的類型一定在藍圖裡：套用變更集時已經擋下被刪除類型底下還有沒刪除的項目。
            var childType = access.Schema.ItemTypes[child.Type];
            if (childType.Parents.Contains(next.Key))
            {
                continue;
            }

            yield return ChildItems.Detail(
                WorkErrorCodes.ItemChildrenNotAllowed,
                projectKey,
                child,
                $"{ChildItems.Describe(projectKey, child)}是{Quote(childType.Name)}，不能掛在{Quote(next.Name)}底下；要先把它移開，才能改成這個類型。");
        }
    }
}
