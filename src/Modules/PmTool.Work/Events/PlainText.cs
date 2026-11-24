using System.Text.RegularExpressions;

namespace PmTool.Work.Events;

/// <summary>
/// 把 Markdown 轉成通知摘要用的純文字：收件匣照純文字顯示，不能露出 <c>**</c>、<c>-</c>、<c>[]()</c> 這些符號。
/// 不是完整的 Markdown 解析器，只處理留言裡常見的寫法：強調（粗體、斜體、刪除線）、標題、清單與待辦勾選、引用、
/// 分隔線、行內與區塊的程式碼、連結、圖片、角括號的網址。
/// 限制：不處理表格（直線符號照留）、參照式連結、HTML、跳脫字元、巢狀引用裡的清單；
/// 看不懂的寫法留原樣。輸入先截到 <see cref="MaxInputLength"/> 個字再處理，長度與時間都有上限。
/// </summary>
internal static partial class PlainText
{
    /// <summary>符號最多佔多少倍的字：先截再轉，摘要上限是 200 字，留 10 倍給符號。</summary>
    internal const int MaxInputLength = WorkEventRules.MaxExcerptLength * 10;

    /// <summary>去掉 Markdown 符號、連續空白壓成一個空白、頭尾去空白。不截到摘要的長度，那是呼叫端的事。</summary>
    public static string From(string markdown)
    {
        var text = CapInput(markdown.Replace("\r\n", "\n", StringComparison.Ordinal));
        try
        {
            return Collapse(Inline(Lines(text)));
        }
        catch (RegexMatchTimeoutException)
        {
            // 逾時就放棄轉換：摘要多幾個符號，比整個儲存失敗好。
            return Collapse(text);
        }
    }

    private static string CapInput(string text)
    {
        if (text.Length <= MaxInputLength)
        {
            return text;
        }

        // 不要從代理字元對的中間切開。
        var length = char.IsHighSurrogate(text[MaxInputLength - 1]) ? MaxInputLength - 1 : MaxInputLength;
        return text[..length];
    }

    // 以行為單位的語法：分隔線、程式碼區塊的圍欄、標題、引用、清單。
    private static string Lines(string text)
    {
        var withoutRules = Rule().Replace(text, string.Empty);
        var withoutFences = Fence().Replace(withoutRules, string.Empty);
        var withoutQuotes = Quote().Replace(withoutFences, string.Empty);
        var withoutHeadings = Heading().Replace(withoutQuotes, string.Empty);
        return ListMarker().Replace(withoutHeadings, string.Empty);
    }

    // 行內的語法。圖片要在連結之前：它比連結多一個驚嘆號。
    private static string Inline(string text)
    {
        var plain = Image().Replace(text, "$1");
        plain = Link().Replace(plain, "$1");
        plain = AutoLink().Replace(plain, "$1");
        plain = plain.Replace("`", string.Empty, StringComparison.Ordinal);
        plain = Strong().Replace(plain, "$2");
        return Emphasis().Replace(plain, "$1$2");
    }

    private static string Collapse(string text) => Whitespace().Replace(text, " ").Trim();

    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.Multiline;

    private const int TimeoutMilliseconds = 1000;

    [GeneratedRegex(@"^[ \t]{0,3}(?:[-*_][ \t]*){3,}$", Options, TimeoutMilliseconds)]
    private static partial Regex Rule();

    [GeneratedRegex(@"^[ \t]{0,3}(?:`{3,}|~{3,})[^`~\r\n]*$", Options, TimeoutMilliseconds)]
    private static partial Regex Fence();

    [GeneratedRegex(@"^[ \t]{0,3}(?:>[ \t]?)+", Options, TimeoutMilliseconds)]
    private static partial Regex Quote();

    [GeneratedRegex(@"^[ \t]{0,3}#{1,6}[ \t]+", Options, TimeoutMilliseconds)]
    private static partial Regex Heading();

    // 無序或有序清單的符號，後面接著的待辦勾選 [ ] 與 [x] 一起去掉。
    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d{1,9}[.)])[ \t]+(?:\[[ xX]\][ \t]+)?", Options, TimeoutMilliseconds)]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"!\[([^\]\r\n]{0,300})\]\([^)\r\n]{0,1000}\)", Options, TimeoutMilliseconds)]
    private static partial Regex Image();

    [GeneratedRegex(@"\[([^\]\r\n]{0,300})\]\([^)\r\n]{0,1000}\)", Options, TimeoutMilliseconds)]
    private static partial Regex Link();

    [GeneratedRegex(@"<((?:https?://|mailto:)[^>\s]{1,1000})>", Options, TimeoutMilliseconds)]
    private static partial Regex AutoLink();

    // 兩個符號的強調（粗體、刪除線）：夾住的內容不以空白開頭或結尾，才不會把乘號或波浪線當成強調。
    [GeneratedRegex(@"(\*\*|__|~~)(?=\S)([^\r\n]+?)(?<=\S)\1", Options, TimeoutMilliseconds)]
    private static partial Regex Strong();

    // 單一符號的斜體。星號可以貼在字中間（中文沒有空白）；底線前後不能是文字，snake_case 才不會被吃掉。
    // 乘號通常前後有空白，夾住的內容以空白開頭就不算（2 * 3 * 4）。
    [GeneratedRegex(@"(?<!\*)\*(?=\S)([^\r\n*]+?)(?<=\S)\*(?!\*)|(?<![\w_])_(?=\S)([^\r\n_]+?)(?<=\S)_(?![\w_])", Options, TimeoutMilliseconds)]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+", Options, TimeoutMilliseconds)]
    private static partial Regex Whitespace();
}
