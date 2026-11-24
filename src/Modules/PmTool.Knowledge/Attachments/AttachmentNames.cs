using System.Globalization;
using System.Text.RegularExpressions;
using PmTool.Abstractions;
using PmTool.Knowledge.Common;

namespace PmTool.Knowledge.Attachments;

/// <summary>檔名與類型的整理規則，和項目的附件相同（每個模組各有一份是這個 repo 的慣例）。</summary>
internal static partial class AttachmentNames
{
    /// <summary>Details 裡的路徑，和 HTTP 上傳的欄位同名。</summary>
    public const string FileNamePath = "fileName";

    public const string FallbackContentType = "application/octet-stream";

    private const int MaxFileNameLength = 255;

    private const int MaxContentTypeLength = 200;

    private static readonly char[] PathSeparators = ['/', '\\'];

    /// <summary>
    /// 去掉前後空白後 1 到 255 個字；不能有控制字元、格式字元、路徑分隔與 NUL，不能是 . 或 ..。
    /// 檔名只拿來顯示與下載，儲存空間的鍵是編號；擋路徑分隔是為了下載時 Content-Disposition 與使用者存檔的位置。
    /// </summary>
    public static Result<string> CheckFileName(string? fileName)
    {
        var name = fileName?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return Invalid("請給檔案一個名稱。");
        }

        if (name.Length > MaxFileNameLength)
        {
            return Invalid($"檔名最多 {MaxFileNameLength} 個字，現在有 {name.Length} 個字；請改短一點。");
        }

        if (name is "." or "..")
        {
            return Invalid("檔名不能是「.」或「..」。");
        }

        if (name.IndexOfAny(PathSeparators) >= 0)
        {
            return Invalid("檔名不能有路徑符號（/ 或 \\）。");
        }

        return KnowledgeText.HasUnsafeNameCharacters(name) || name.Any(IsLineOrParagraphSeparator)
            ? Invalid("檔名裡有無法儲存或看不見的特殊字元（例如控制字元、零寬字元），請改掉後再上傳。")
            : name;
    }

    /// <summary>
    /// 只留 <c>type/subtype</c>：去掉參數、轉小寫。空白、太長、不像類型的一律是 application/octet-stream。
    /// 它只拿來決定下載時怎麼呈現，不信任它。
    /// </summary>
    public static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > MaxContentTypeLength)
        {
            return FallbackContentType;
        }

        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        var essence = (semicolon < 0 ? contentType : contentType[..semicolon]).Trim().ToLowerInvariant();
        return MediaType().IsMatch(essence) ? essence : FallbackContentType;
    }

    // U+2028、U+2029 不是控制字元，卻會讓檔名在畫面與標頭裡斷行。
    private static bool IsLineOrParagraphSeparator(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    private static Failure Invalid(string message) =>
        KnowledgeFailures.Invalid(KnowledgeErrorCodes.AttachmentNameInvalid, FileNamePath, message);

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]*/[a-z0-9][a-z0-9!#$&^_.+-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaType();
}
