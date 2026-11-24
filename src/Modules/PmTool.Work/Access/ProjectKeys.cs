using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace PmTool.Work.Access;

internal static partial class ProjectKeys
{
    /// <summary>
    /// 整段都要符合 <see cref="WorkRules.ProjectKeyPattern"/>。.NET 的 $ 也會配到結尾換行的前面，
    /// 所以 "WEB\n" 會通過 ^...$；要再確認配到的範圍正好是整個字串。
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? key)
    {
        if (key is null)
        {
            return false;
        }

        var match = Pattern().Match(key);
        return match.Success && match.Index == 0 && match.Length == key.Length;
    }

    /// <summary>查詢用：網址裡的代號可能是小寫，轉成大寫再比，不合格式的直接當成找不到。</summary>
    public static bool TryNormalize(string? key, [NotNullWhen(true)] out string? normalized)
    {
        normalized = key?.ToUpperInvariant();
        return IsValid(normalized);
    }

    [GeneratedRegex(WorkRules.ProjectKeyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
