using System.Globalization;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// kumi（本機代理、主機的 worker）的版本比較。照語意版本：主、次、修訂依數字比，同樣的數字帶預發行標籤（<c>-beta</c>）的比正式版小，
/// 建置資訊（<c>+abc</c>）不算。解析不出來的一律當作不夠新：寧可請使用者更新，也不要把圖片交給不認得的 kumi。
/// </summary>
internal static class AgentVersions
{
    public static bool IsAtLeast(string? version, string minimum) =>
        TryParse(version, out var actual) && TryParse(minimum, out var wanted) && actual.CompareTo(wanted) >= 0;

    private static bool TryParse(string? text, out Parsed parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var core = text.Trim().TrimStart('v', 'V');
        var plus = core.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            core = core[..plus];
        }

        var isPreRelease = false;
        var dash = core.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            isPreRelease = true;
            core = core[..dash];
        }

        var parts = core.Split('.');
        if (parts.Length is < 1 or > 3 || parts.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return false;
        }

        var numbers = parts.Select(p => int.Parse(p, CultureInfo.InvariantCulture)).Concat([0, 0]).Take(3).ToArray();
        parsed = new Parsed(numbers[0], numbers[1], numbers[2], isPreRelease);
        return true;
    }

    private readonly record struct Parsed(int Major, int Minor, int Patch, bool IsPreRelease)
    {
        public int CompareTo(Parsed other)
        {
            var byNumbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
            if (byNumbers != 0)
            {
                return byNumbers;
            }

            // 同樣的數字：正式版比預發行版大。
            return IsPreRelease == other.IsPreRelease ? 0 : IsPreRelease ? -1 : 1;
        }
    }
}
