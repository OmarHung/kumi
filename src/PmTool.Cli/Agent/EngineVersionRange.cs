using System.Globalization;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Agent;

/// <summary>
/// 本機代理認得的引擎版本範圍：下限是實測過的版本，上限到下一個主版本（不含）。引擎的旗標與輸出格式每個版本都可能變，
/// 沒測過的版本可能默默少了某道防護，所以範圍外預設不提供，使用者用 <c>--allow-untested-engine</c> 明講才放行。
/// </summary>
internal sealed partial record EngineVersionRange(string DisplayName, Version Minimum, Version ExclusiveMaximum)
{
    public static readonly EngineVersionRange ClaudeCode = new("Claude Code", new Version(2, 1, 330), new Version(3, 0, 0));

    public static readonly EngineVersionRange Codex = new("Codex", new Version(0, 163, 0), new Version(1, 0, 0));

    public static EngineVersionRange For(string kind) => kind == EngineKinds.Codex ? Codex : ClaudeCode;

    /// <summary>在範圍內回 null；不在（含版本字串讀不懂）回說明。</summary>
    public string? Check(string versionText)
    {
        var range = $"{Minimum} 以上、{ExclusiveMaximum} 以下（不含）";
        if (!TryParse(versionText, out var version))
        {
            return $"讀不出 {DisplayName} 的版本（{Shown(versionText)}），不在驗證過的範圍（{range}）內，預設不提供。確定它是可信的版本再加 --allow-untested-engine。";
        }

        return version >= Minimum && version < ExclusiveMaximum
            ? null
            : $"{DisplayName} {versionText} 不在驗證過的版本範圍（{range}）內，預設不提供。"
                + (version < Minimum ? $"請更新 {DisplayName}；" : "這一版還沒驗證過，等 kumi 更新；")
                + "要自己承擔風險的話加 --allow-untested-engine。";
    }

    /// <summary>取開頭的 <c>主.次.修</c> 三段數字（後面的預覽版標記、括號都不看）。少一段的補 0；不是數字開頭是 false。</summary>
    public static bool TryParse(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (VersionPattern().Match(text.Trim()) is not { Success: true } match)
        {
            return false;
        }

        int Part(string name) => match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;
        version = new Version(Part("major"), Part("minor"), Part("patch"));
        return true;
    }

    // 數字最多 6 位：夠用，也讓 int.Parse 不會溢位。
    [GeneratedRegex(@"^v?(?<major>\d{1,6})(?:\.(?<minor>\d{1,6}))?(?:\.(?<patch>\d{1,6}))?(?![\d.]*\d)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    private static string Shown(string text) => text.Length <= 40 ? text : text[..40];
}
