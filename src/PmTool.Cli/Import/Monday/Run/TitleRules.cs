namespace PmTool.Cli.Import.Monday.Run;

/// <summary>依標題的關鍵字決定類型與欄位的值（<see cref="TitleRule"/>）。標題不分大小寫、先去掉不可見字元與前後空白。</summary>
internal static class TitleRules
{
    /// <summary>適用於這個專案的規則（沒給 project 的對每個專案都適用），保持原來的順序。</summary>
    public static IReadOnlyList<TitleRule> ForProject(IReadOnlyList<TitleRule> rules, string projectKey) =>
        [.. rules.Where(rule => AppliesTo(rule, projectKey))];

    public static bool AppliesTo(TitleRule rule, string projectKey) =>
        rule.Project is null || string.Equals(rule.Project, projectKey, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string name) => MarkdownText.StripInvisible(name).Trim();

    public static bool Matches(TitleRule rule, string title) =>
        rule.Match.Any(word => title.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>第一條符合、而且有 type 的規則的類型；沒有是 null。</summary>
    public static string? TypeFor(IReadOnlyList<TitleRule> rules, string title) =>
        rules.FirstOrDefault(rule => rule.Type is not null && Matches(rule, title))?.Type;

    /// <summary>每個欄位各自取第一條符合、而且給了那個欄位的規則的值。</summary>
    public static IReadOnlyDictionary<string, string> FieldsFor(IReadOnlyList<TitleRule> rules, string title)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rule in rules.Where(rule => Matches(rule, title)))
        {
            foreach (var (key, value) in rule.Fields)
            {
                result.TryAdd(key, value);
            }
        }

        return result;
    }
}
