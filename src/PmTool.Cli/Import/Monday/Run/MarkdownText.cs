using System.Text;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>留言轉成 Markdown 之後的整理：不可見字元、跳脫、長度上限（internal/HANDOFF-monday-import.md 第 6.4 節）。</summary>
internal static partial class MarkdownText
{
    /// <summary>伺服器收的留言上限；超過就被擋。</summary>
    public const int MaxCommentLength = 10_000;

    /// <summary>截斷後留的長度：加上說明那一行之後還在上限以內。</summary>
    public const int TruncatedLength = 9_800;

    public const string TruncationNotice = "（內容太長，後面截掉了；全文見 monday 原連結）";

    public const string EmptyComment = "（空白的留言）";

    // 這些字元在 Markdown 裡可能變成格式；其他的符號（! # > - + 數字）只有在行首才有意義，另外處理。
    /// <summary>內文裡最長的一串連續反引號有幾個；圍欄要比它多，內文才逃不出程式碼。</summary>
    public static int LongestBacktickRun(string text)
    {
        int longest = 0, current = 0;
        foreach (var character in text)
        {
            current = character == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private const string InlineSpecials = "\\*_`[]<~";

    [GeneratedRegex("[\uFEFF\u200B-\u200D\u2060]")]
    private static partial Regex InvisibleCharacters();

    // 伺服器不收 NUL 與控制字元（整批 422，而且重跑永遠卡在同一則）；tab、換行、CR、換頁留給排版處理。
    // 落單的代理字元寫不成合法的 UTF-8，一樣會被擋。
    [GeneratedRegex(@"[\u0000-\u0008\u000B\u000E-\u001F\u007F-\u009F]|[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]")]
    private static partial Regex ForbiddenCharacters();

    [GeneratedRegex(@"[ \t]+(?=\n)")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankRuns();

    [GeneratedRegex(@"&(?=#?[A-Za-z0-9]+;)")]
    private static partial Regex EntityStart();

    [GeneratedRegex(@"^\d{1,9}(?=[.)])")]
    private static partial Regex OrderedMarker();

    // 連結與圖片：截斷時不能切在中間（[文字](網址)，文字裡的跳脫字元算在內）。
    [GeneratedRegex(@"!?\[(?:\\.|[^\]\\])*\]\([^)]*\)")]
    private static partial Regex LinkSpan();

    /// <summary>
    /// 去掉 U+FEFF、U+200B～U+200D、U+2060（monday 到處夾著它們，留著會讓標題與文字看起來有空白），
    /// 以及伺服器不收的控制字元與落單的代理字元。
    /// </summary>
    public static string StripInvisible(string text) =>
        ForbiddenCharacters().Replace(InvisibleCharacters().Replace(text, string.Empty), string.Empty);

    /// <summary>
    /// 文字裡的 Markdown 符號跳脫成不會意外變成格式。<paramref name="atLineStart"/> 時（這段文字是一行的開頭）
    /// 行首的 #、&gt;、-、+、「1.」也要跳脫，不然會變成標題、引用、清單。
    /// </summary>
    public static string EscapeInline(string text, bool atLineStart)
    {
        var entities = EntityStart().Matches(text).Select(match => match.Index).ToHashSet();
        var escaped = new StringBuilder(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            if (InlineSpecials.Contains(text[index]) || entities.Contains(index))
            {
                escaped.Append('\\');
            }

            escaped.Append(text[index]);
        }

        return atLineStart ? EscapeLineStart(escaped.ToString()) : escaped.ToString();
    }

    private static string EscapeLineStart(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (text[0] is '#' or '>' or '-' or '+' or '=')
        {
            return "\\" + text;
        }

        return OrderedMarker().Match(text) is { Success: true } marker
            ? text.Insert(marker.Length, "\\")
            : text;
    }

    /// <summary>方括號在連結文字與圖片說明裡要跳脫。</summary>
    public static string EscapeBrackets(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);

    /// <summary>連結的目的地：空白與括號會把 [文字](網址) 弄斷，換成百分比編碼。</summary>
    public static string EncodeDestination(string url)
    {
        var encoded = new StringBuilder(url.Length);
        foreach (var character in StripInvisible(url))
        {
            switch (character)
            {
                case ' ': encoded.Append("%20"); break;
                case '(': encoded.Append("%28"); break;
                case ')': encoded.Append("%29"); break;
                case '<': encoded.Append("%3C"); break;
                case '>': encoded.Append("%3E"); break;
                case '\\': encoded.Append("%5C"); break;
                case '\n' or '\r' or '\t': break;
                default: encoded.Append(character); break;
            }
        }

        return encoded.ToString();
    }

    /// <summary>整理最後的結果：不可見字元、行尾空白、連續的空行（三個以上的換行收成兩個）、頭尾空白。</summary>
    public static string Tidy(string markdown)
    {
        var text = StripInvisible(markdown).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\f', ' ');
        text = TrailingSpaces().Replace(text, string.Empty);
        return BlankRuns().Replace(text, "\n\n").Trim();
    }

    /// <summary>
    /// 超過 <see cref="MaxCommentLength"/> 就截到 <see cref="TruncatedLength"/>，最後接上說明。
    /// 不切在連結中間、跳脫字元後面或代理字元的中間。
    /// </summary>
    public static string Truncate(string markdown, out bool truncated)
    {
        truncated = markdown.Length > MaxCommentLength;
        if (!truncated)
        {
            return markdown;
        }

        var cut = TruncatedLength;
        foreach (Match span in LinkSpan().Matches(markdown))
        {
            if (span.Index < cut && cut < span.Index + span.Length)
            {
                cut = span.Index;
                break;
            }
        }

        if (cut > 0 && char.IsHighSurrogate(markdown[cut - 1]))
        {
            cut--;
        }

        if (cut > 0 && markdown[cut - 1] == '\\')
        {
            cut--;
        }

        var kept = markdown[..cut].TrimEnd();
        return kept + ClosingFence(kept) + "\n\n" + TruncationNotice;
    }

    /// <summary>截斷的地方還在程式碼圍欄裡時，補上收尾的圍欄，說明才不會被畫成程式碼。</summary>
    private static string ClosingFence(string text)
    {
        string? open = null;
        foreach (var line in text.Split('\n'))
        {
            var fence = new string([.. line.TrimStart().TakeWhile(character => character == '`')]);
            if (fence.Length < 3)
            {
                continue;
            }

            if (open is null)
            {
                open = fence;
            }
            else if (fence.Length >= open.Length && line.Trim().Length == fence.Length)
            {
                open = null;
            }
        }

        return open is null ? string.Empty : "\n" + open;
    }
}
