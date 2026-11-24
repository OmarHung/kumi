using System.Text;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Agent;

/// <summary>
/// 要交給伺服器或印在終端機的錯誤文字：權杖、本機路徑換成 …，控制字元換成空白，截到伺服器收的長度（<c>AiRules.MaxErrorLength</c>）。
/// 引擎的錯誤可能帶著設定檔路徑、MCP 的網址與標頭，這裡是最後一道把關。
/// </summary>
internal static partial class ErrorText
{
    public const int MaxLength = 500;
    private const string Mask = "…";
    private const int MinLiteralLength = 6;

    public static string Sanitize(string? text, params string?[] literals)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text;
        foreach (var literal in LiteralsToHide(literals))
        {
            result = result.Replace(literal, Mask, StringComparison.Ordinal);
        }

        result = BearerPattern().Replace(result, $"Bearer {Mask}");
        result = TokenPattern().Replace(result, Mask);
        result = UncPathPattern().Replace(result, Mask);
        result = WindowsPathPattern().Replace(result, Mask);
        result = UnixPathPattern().Replace(result, Mask);
        result = RemoveControlCharacters(result).Trim();
        return result.Length <= MaxLength ? result : result[..(MaxLength - 1)] + Mask;
    }

    /// <summary>第一個非空的段落（空行隔開）；引擎的錯誤常常第一段是原因、後面是用法說明。</summary>
    public static string FirstParagraph(string? text) =>
        (text ?? string.Empty).Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

    private static IEnumerable<string> LiteralsToHide(string?[] literals)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        // 較長的先換，免得短的是長的一部分時，留下半截。
        return literals.Append(home).Append(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
            .Where(literal => literal is { Length: >= MinLiteralLength })
            .Select(literal => literal!)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(literal => literal.Length);
    }

    private static string RemoveControlCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"pmt_[A-Za-z0-9_\-]+")]
    private static partial Regex TokenPattern();

    // 網址（https://主機/a/b）裡的斜線前面是字母或冒號，不會被當成路徑；兩段以上才算，單獨的 /mcp 不動。
    [GeneratedRegex(@"(?<![\w:/.\-])(?:~|\.{1,2})?(?:/[\w.@+~\-]+){2,}/?")]
    private static partial Regex UnixPathPattern();

    [GeneratedRegex(@"\\\\[^\\\s'""]+(?:\\[^\\\s'""]+)+")]
    private static partial Regex UncPathPattern();

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\\s'""]+\\?)+")]
    private static partial Regex WindowsPathPattern();
}
