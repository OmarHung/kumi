namespace PmTool.Cli.Agent;

/// <summary>
/// 在 <c>--help</c> 的輸出裡找旗標：整個旗標才算。用子字串的話，<c>--tools</c> 會被 <c>--tools-extra</c>、<c>--no-tools</c> 的一部分、
/// 甚至說明文字裡提到的字眼滿足，少一個安全用的旗標卻沒發現。旗標前面不能是旗標名稱的字元，後面也不能是。
/// </summary>
internal static class HelpFlags
{
    /// <summary>help 裡找得到這個旗標。Claude Code 把 <c>--append-system-prompt</c> 與 <c>-file</c> 合寫成 <c>--append-system-prompt[-file]</c>。</summary>
    public static bool Contains(string help, string flag) =>
        ContainsWhole(help, flag)
        || (flag.EndsWith("-file", StringComparison.Ordinal) && ContainsWhole(help, flag[..^"-file".Length] + "[-file]"));

    private static bool ContainsWhole(string help, string flag)
    {
        var from = 0;
        while (from <= help.Length - flag.Length && help.IndexOf(flag, from, StringComparison.Ordinal) is var index and >= 0)
        {
            var before = index == 0 ? ' ' : help[index - 1];
            var after = index + flag.Length >= help.Length ? ' ' : help[index + flag.Length];
            if (!IsFlagCharacter(before) && !IsFlagCharacter(after))
            {
                return true;
            }

            from = index + 1;
        }

        return false;
    }

    private static bool IsFlagCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
