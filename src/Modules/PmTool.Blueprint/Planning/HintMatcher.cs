namespace PmTool.Blueprint.Planning;

/// <summary>
/// 把「需要交代的處理」和變更集寫的搬移指示對起來。每個需要的處理用第一條對得上的指示；
/// 沒有指示可用的回報缺少；沒被用到的指示算不算寫錯，由各個護欄自己決定。
/// </summary>
internal static class HintMatcher
{
    /// <param name="required">需要交代的處理，依回報的順序。</param>
    /// <param name="unused">
    /// 沒被用到的指示要回報什麼；回傳 null 表示這條指示雖然用不到，但沒有寫錯。
    /// 參數是指示的位置，以及它是不是和前面某一條重複。
    /// </param>
    public static MigrationOutcome Match<TKey, THint>(
        IEnumerable<TKey> required,
        IReadOnlyList<THint> hints,
        Func<THint, TKey> keyOf,
        Func<TKey, THint, DataImpact> toImpact,
        Func<TKey, ValidationIssue> missing,
        Func<int, bool, ValidationIssue?> unused)
        where TKey : notnull
    {
        var positions = KeyPositions.First(hints, keyOf);
        var impacts = new List<DataImpact>();
        var issues = new List<ValidationIssue>();
        var used = new HashSet<int>();
        foreach (var key in required)
        {
            if (positions.TryGetValue(key, out var position))
            {
                used.Add(position);
                impacts.Add(toImpact(key, hints[position]));
            }
            else
            {
                issues.Add(missing(key));
            }
        }

        issues.AddRange(Unused(hints.Count, used)
            .Select(index => unused(index, positions[keyOf(hints[index])] != index))
            .OfType<ValidationIssue>());
        return new MigrationOutcome(impacts, issues);
    }

    public static IEnumerable<int> Unused(int count, IReadOnlySet<int> used) =>
        Enumerable.Range(0, count).Where(index => !used.Contains(index));
}
