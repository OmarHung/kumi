using System.Globalization;
using PmTool.Abstractions;

namespace PmTool.Ai.Attachments;

/// <summary>檔名的整理規則，和項目與知識庫的附件相同（每個模組各有一份是這個 repo 的慣例，模組之間不引用 internal 型別）。</summary>
internal static class AttachmentNames
{
    /// <summary>Details 裡的路徑，和 HTTP 上傳的欄位同名。</summary>
    public const string FileNamePath = "fileName";

    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// 去掉前後空白後 1 到 255 個字；不能有控制字元、格式字元、路徑分隔與 NUL、落單的代理字元，不能是 . 或 ..。
    /// 檔名只拿來顯示與下載，儲存空間的鍵是編號；擋路徑分隔是為了下載時 Content-Disposition 與使用者存檔的位置。
    /// </summary>
    public static Result<string> Check(string? fileName)
    {
        var name = fileName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Invalid("請給檔案一個名稱。");
        }

        if (name.Length > AssistantAttachmentRules.MaxFileNameLength)
        {
            return Invalid($"檔名最多 {AssistantAttachmentRules.MaxFileNameLength} 個字，現在有 {name.Length} 個字；請改短一點。");
        }

        if (name is "." or "..")
        {
            return Invalid("檔名不能是「.」或「..」。");
        }

        if (name.IndexOfAny(PathSeparators) >= 0)
        {
            return Invalid("檔名不能有路徑符號（/ 或 \\）。");
        }

        return HasUnsafeCharacters(name)
            ? Invalid("檔名裡有無法儲存或看不見的特殊字元（例如控制字元、零寬字元），請改掉後再上傳。")
            : name;
    }

    // 落單的代理字元轉不成 UTF-8、NUL 存不進 PostgreSQL；格式字元（零寬、右到左覆寫）與 U+2028／U+2029 會讓檔名在畫面與標頭裡看起來不一樣或斷行。
    private static bool HasUnsafeCharacters(string name)
    {
        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];
            if (char.IsHighSurrogate(c) && index + 1 < name.Length && char.IsLowSurrogate(name[index + 1]))
            {
                index++;
                continue;
            }

            if (char.IsSurrogate(c) || char.IsControl(c)
                || char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                return true;
            }
        }

        return false;
    }

    private static Failure Invalid(string message) =>
        new(FailureKind.Invalid, AiErrorCodes.AttachmentNameInvalid, message)
        {
            Details = [new FailureDetail(AiErrorCodes.AttachmentNameInvalid, FileNamePath, message)],
        };
}
