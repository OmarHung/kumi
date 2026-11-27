using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 修改建議的權限預檢（單一項目與批次修改共用）：建議的每一步套用時都會以使用者的權限檢查；
/// 先擋在建立的時候，使用者只能讀的時候不必看到一張永遠套用不了的卡片。
/// </summary>
internal static class ItemChangePermissions
{
    public static Failure? Missing(IEnumerable<ItemFieldChange> changes, string? comment, IReadOnlyCollection<string> permissions)
    {
        var list = changes.ToList();
        var denied = (list.Any(c => c.Field != ItemFieldNames.Status) && !permissions.Contains(WorkPermissions.ItemUpdate))
            || (list.Any(c => c.Field == ItemFieldNames.Status) && !permissions.Contains(WorkPermissions.ItemTransition))
            || (!string.IsNullOrWhiteSpace(comment) && !permissions.Contains(WorkPermissions.CommentCreate));
        return denied
            ? new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "你沒有修改項目的權限，只能讀。")
            : null;
    }
}
