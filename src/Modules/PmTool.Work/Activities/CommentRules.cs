using PmTool.Abstractions;
using PmTool.Work.Common;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Activities;

/// <summary>
/// 留言的內容：純文字，可以換行。存去掉頭尾空白後的內容，長度也用它算，前後多按的 Enter 不佔字數。
/// 換行先統一成 \n，\r\n 只算一個字。換行與 Tab 以外的控制字元擋下來：NUL 存不進資料庫，其他的只會造成顯示問題。
/// </summary>
internal static class CommentRules
{
    /// <summary>Details 裡的路徑，和 HTTP 請求內文的屬性名稱一樣。</summary>
    public const string BodyPath = "body";

    public static Result<string> Check(string? body)
    {
        var trimmed = NormalizeLineBreaks(body)?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return WorkFailures.Invalid(WorkErrorCodes.CommentBodyRequired, BodyPath, "請先寫下留言的內容。");
        }

        if (trimmed.Length > WorkRules.MaxCommentLength)
        {
            return WorkFailures.Invalid(
                WorkErrorCodes.CommentBodyTooLong,
                BodyPath,
                $"留言最多 {WorkRules.MaxCommentLength} 個字，現在有 {trimmed.Length} 個字；請刪短一點，或分成幾則留言。");
        }

        return HasForbiddenCharacters(trimmed, allowLineBreaks: true)
            ? WorkFailures.Invalid(
                WorkErrorCodes.CommentBodyInvalid, BodyPath, "留言裡有無法儲存的特殊字元（例如控制字元），請刪掉後再送出；換行與 Tab 可以使用。")
            : trimmed;
    }
}
