using PmTool.Ai.Items;
using PmTool.Work;

namespace PmTool.Ai.CommentProposals;

/// <summary>
/// 建立留言建議時，照工作引擎的規則先擋下做不到的那一筆（套用時工作引擎照樣檢查，這裡是讓使用者不必看到永遠套不了的卡片）。
/// 回傳 null 是沒有問題，否則是給模型看的原因。
/// </summary>
internal static class CommentRules
{
    public static string? Check(CommentChangeDraft change, ItemActivity comment, Guid me, bool canComment, bool canManage) => change.Action switch
    {
        CommentAction.Edit => CheckEdit(change, comment, me, canComment),
        CommentAction.Delete => CheckDelete(change, comment, me, canComment, canManage),
        CommentAction.Restore => CheckRestore(change, comment, me, canComment, canManage),
        _ => "action 要是 edit、delete、restore 其中之一。",
    };

    /// <summary>編輯的內容正規化：換行統一、去掉頭尾空白（和工作引擎比對「內容一樣」的方式相同）。</summary>
    public static string Normalize(string body) => body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static string? CheckEdit(CommentChangeDraft change, ItemActivity comment, Guid me, bool canComment)
    {
        if (comment.DeletedAt is not null)
        {
            return "這則留言已經刪除，要先復原（restore）才能編輯。";
        }

        if (comment.ImportedFrom is not null)
        {
            return "這是從別的系統匯入的留言，不能編輯。";
        }

        if (comment.ActorId != me)
        {
            return "這是別人的留言，只有作者能編輯。";
        }

        if (!canComment)
        {
            return "你沒有留言的權限，不能編輯留言。";
        }

        return CheckBody(change.Body, comment.Comment);
    }

    private static string? CheckBody(string? body, string? current)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "edit 要給 body（新的留言內容，不能是空的）。";
        }

        var text = Normalize(body);
        if (text.Length > WorkRules.MaxCommentLength)
        {
            return $"body 最長 {WorkRules.MaxCommentLength} 個字元。";
        }

        if (!ItemValueRules.IsPlainText(text, allowLineBreaks: true))
        {
            return "body 不能含有 NUL 或其他控制字元。";
        }

        return current is not null && Normalize(current) == text ? "新的內容和現在的一樣，不需要編輯。" : null;
    }

    private static string? CheckDelete(CommentChangeDraft change, ItemActivity comment, Guid me, bool canComment, bool canManage)
    {
        if (change.Body is not null)
        {
            return "delete 不能給 body。";
        }

        if (comment.DeletedAt is not null)
        {
            return "這則留言已經刪除了。";
        }

        if (comment.ActorId != me || comment.ImportedFrom is not null)
        {
            return canManage ? null : "這是別人的（或匯入的）留言，只有專案管理者能刪除。";
        }

        return canComment ? null : "你沒有留言的權限，不能刪除留言。";
    }

    private static string? CheckRestore(CommentChangeDraft change, ItemActivity comment, Guid me, bool canComment, bool canManage)
    {
        if (change.Body is not null)
        {
            return "restore 不能給 body。";
        }

        if (comment.DeletedAt is null)
        {
            return "這則留言沒有刪除，不需要復原。";
        }

        if (canManage)
        {
            return null;
        }

        var authorDeleted = comment.DeletedBy == comment.ActorId && comment.ActorId == me && comment.ImportedFrom is null;
        return authorDeleted && canComment ? null : "這則留言是別人（或專案管理者）刪除的，只有專案管理者能復原。";
    }
}
