using System.Globalization;

namespace PmTool.Ai.Proposals;

/// <summary>
/// 項目編號清單的短寫法：同一個專案連續的編號收成「WEB-38～WEB-44」，其餘用頓號隔開，太多時只列前面幾組。
/// 摘要只放編號與數字，不放標題（標題是使用者寫的內容）。
/// </summary>
internal static class KeyLists
{
    private const int MaxGroups = 8;

    private const int MinRunLength = 3;

    public static string Describe(IReadOnlyList<string> keys)
    {
        var groups = Group(keys);
        var shown = groups.Take(MaxGroups).ToList();
        var text = string.Join("、", shown);
        return groups.Count > MaxGroups ? $"{text}…（共 {keys.Count} 個）" : text;
    }

    private static List<string> Group(IReadOnlyList<string> keys)
    {
        var groups = new List<string>();
        var parsed = keys.Select(Parse).ToList();
        for (var i = 0; i < parsed.Count;)
        {
            var end = i;
            while (end + 1 < parsed.Count && IsNext(parsed[end], parsed[end + 1]))
            {
                end++;
            }

            if (end - i + 1 >= MinRunLength)
            {
                groups.Add($"{keys[i]}～{keys[end]}");
                i = end + 1;
            }
            else
            {
                groups.Add(keys[i]);
                i++;
            }
        }

        return groups;
    }

    private static bool IsNext((string Prefix, long Number)? left, (string Prefix, long Number)? right) =>
        left is { } a && right is { } b && a.Prefix == b.Prefix && b.Number == a.Number + 1;

    private static (string Prefix, long Number)? Parse(string key)
    {
        var dash = key.LastIndexOf('-');
        return dash > 0 && long.TryParse(key[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? (key[..dash], number)
            : null;
    }
}
