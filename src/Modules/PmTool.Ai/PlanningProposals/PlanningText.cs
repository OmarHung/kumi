using System.Globalization;

namespace PmTool.Ai.PlanningProposals;

/// <summary>
/// 週期、里程碑與專案的文字檢查，規則和工作引擎一樣（它的 TextRules 是 internal，這裡自己有一份）：
/// 建立建議時先擋，使用者按「套用」才不會被工作引擎退回來。
/// </summary>
internal static class PlanningText
{
    /// <summary>控制字元與單獨的代理字元都不行；<paramref name="allowLineBreaks"/> 時換行與 Tab 可以。</summary>
    public static bool HasForbiddenCharacters(string text, bool allowLineBreaks)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var c = text[index];
            if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(c) || (char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t')))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>名稱不能用的字元：上面那些，再加上看不見的格式字元（零寬空格、右到左覆寫這類）。</summary>
    public static bool HasUnsafeNameCharacters(string name) =>
        HasForbiddenCharacters(name, allowLineBreaks: false) || name.Any(c => char.GetUnicodeCategory(c) == UnicodeCategory.Format);

    /// <summary>換行統一成 \n、去掉頭尾空白；空的當成沒有。</summary>
    public static string? NormalizeMultiline(string? text)
    {
        var trimmed = text?.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
