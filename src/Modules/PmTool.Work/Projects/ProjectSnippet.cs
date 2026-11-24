using System.Text.RegularExpressions;

namespace PmTool.Work.Projects;

/// <summary>專案搜尋的摘錄（<see cref="ProjectSearchHit.Snippet"/>）：不碰資料庫，好單獨測邊界。</summary>
internal static partial class ProjectSnippet
{
    private const string Ellipsis = "…";

    // 符號後面要有空白才算 Markdown 的標記：「**粗體**」「#標籤」「-x」是內容，不能吃掉。
    [GeneratedRegex(@"^\s*(?:(?:#{1,6}|[-*+]|\d{1,9}[.)])\s+|>\s*)*", RegexOptions.CultureInvariant)]
    private static partial Regex LinePrefix();

    /// <summary>
    /// 第一個包含 <paramref name="query"/>（不分大小寫）的行，去掉行首符號與頭尾空白，超過上限時以比對到的字為中心截斷。
    /// 資料庫與這裡的大小寫規則不完全一致時可能找不到行：退回第一個非空白行，讓畫面至少看得出是說明比對到的。
    /// 沒有說明（或整段空白）是 null。
    /// </summary>
    public static string? From(string? text, string query)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        if (lines.FirstOrDefault(line => line.Contains(query, StringComparison.OrdinalIgnoreCase)) is not { } matched)
        {
            return Clip(Clean(lines.First(line => line.Trim().Length > 0)), 0, 0);
        }

        var content = Clean(matched, query);
        return Clip(content, IndexIn(content, query), query.Length);
    }

    private static string Clean(string line, string? query = null)
    {
        var content = line[LinePrefix().Match(line).Length..].Trim();

        // 整行只剩符號，或搜尋的字落在被去掉的符號裡（例如搜尋「1.」）：去掉符號後比對到的字就不在摘錄裡了，
        // 照整行（去頭尾空白）算位置與截斷，讓比對到的字看得到。
        var lostTheMatch = query is not null && !content.Contains(query, StringComparison.OrdinalIgnoreCase);
        return content.Length == 0 || lostTheMatch ? line.Trim() : content;
    }

    private static int IndexIn(string content, string query) =>
        Math.Max(0, content.IndexOf(query, StringComparison.OrdinalIgnoreCase));

    private static string Clip(string content, int matchStart, int matchLength)
    {
        var limit = ProjectDescriptionRules.MaxSnippetLength;
        if (content.Length <= limit)
        {
            return content;
        }

        var (start, end) = matchLength + 2 >= limit
            ? LongMatchWindow(content, matchStart, limit)
            : CenteredWindow(content, matchStart, matchLength, limit);
        (start, end) = AvoidSplittingPairs(content, start, end);
        return (start > 0 ? Ellipsis : string.Empty) + content[start..end] + (end < content.Length ? Ellipsis : string.Empty);
    }

    // 「…」也算在上限裡，所以兩邊被截時各讓出一個字的位置。比對的字在視窗中間，讓出的位置不會吃到它（見單元測試）。
    private static (int Start, int End) CenteredWindow(string content, int matchStart, int matchLength, int limit)
    {
        var center = matchStart + matchLength / 2;
        var start = Math.Clamp(center - limit / 2, 0, content.Length - limit);
        var end = start + limit;
        return (start > 0 ? start + 1 : start, end < content.Length ? end - 1 : end);
    }

    // 比對的字自己就塞滿視窗：從它的開頭看起，結尾截掉。
    private static (int Start, int End) LongMatchWindow(string content, int matchStart, int limit)
    {
        var room = limit - (matchStart > 0 ? 1 : 0);
        var end = matchStart + room;
        return end < content.Length ? (matchStart, end - 1) : (matchStart, content.Length);
    }

    // 表情符號這類字在 UTF-16 是兩個單位：切在中間會留下單獨的半個字。
    private static (int Start, int End) AvoidSplittingPairs(string content, int start, int end)
    {
        if (start > 0 && char.IsLowSurrogate(content[start]))
        {
            start++;
        }

        if (end < content.Length && char.IsHighSurrogate(content[end - 1]))
        {
            end--;
        }

        return (start, end);
    }
}
