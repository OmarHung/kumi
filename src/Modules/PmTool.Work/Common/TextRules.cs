using System.Globalization;

namespace PmTool.Work.Common;

internal static class TextRules
{
    /// <summary>放進訊息的使用者輸入最多顯示這麼長；合法的 key 與代號都比它短，超過的才會被截斷。</summary>
    public const int MaxQuotedLength = 64;

    private const string Open = "「";
    private const string Close = "」";
    private const string CutMark = "…";

    /// <summary>
    /// 把一段文字放進訊息。輸入來自使用者，長度不受控；原樣放進訊息，一個請求就能換到很大的回應。
    /// </summary>
    public static string Quote(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxQuotedLength)
        {
            return Open + text + Close;
        }

        // 不要從代理字元對的中間切開，切出來的半個字元寫不進 JSON。
        var length = char.IsHighSurrogate(text[MaxQuotedLength - 1]) ? MaxQuotedLength - 1 : MaxQuotedLength;
        return Open + text[..length] + CutMark + Close;
    }

    /// <summary>
    /// 有沒有存不進資料庫或不該出現在名稱裡的字元。落單的代理字元轉不成 UTF-8；
    /// NUL 存不進 PostgreSQL 的 text 與 jsonb；其他控制字元出現在名稱裡只會造成顯示問題。
    /// </summary>
    public static bool HasForbiddenCharacters(string text, bool allowLineBreaks)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(c) || (char.IsControl(c) && !(allowLineBreaks && IsLineBreakOrTab(c))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 名稱不能用的字元：<see cref="HasForbiddenCharacters"/> 的那些，再加上看不見的格式字元（零寬空格、右到左覆寫這類）：
    /// 選單裡會出現看起來空白或顯示順序顛倒的名稱。
    /// </summary>
    public static bool HasUnsafeNameCharacters(string name) =>
        HasForbiddenCharacters(name, allowLineBreaks: false) || name.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.Format);

    /// <summary>
    /// \r\n 與單獨的 \r 換成 \n。網頁的文字框送出的是 \n，其他用戶端可能送 \r\n；不統一的話，
    /// 內容相同也會被當成有改，長度也會多算。只換這兩種：其他分行字元原本怎麼檢查就怎麼檢查。
    /// </summary>
    public static string? NormalizeLineBreaks(string? text) =>
        text?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static bool IsLineBreakOrTab(char c) => c is '\n' or '\r' or '\t';
}
