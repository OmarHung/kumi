namespace PmTool.Blueprint;

internal static class KeyPositions
{
    /// <summary>
    /// 每個 key 第一次出現的位置。key 可能重複：語意規則就是負責回報重複的那一關，之後的階段雖然照理不會再看到，
    /// 也不能因此丟例外。所以一律只認第一個。
    /// </summary>
    public static Dictionary<TKey, int> First<T, TKey>(IReadOnlyList<T> items, Func<T, TKey> keyOf)
        where TKey : notnull
    {
        var positions = new Dictionary<TKey, int>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            positions.TryAdd(keyOf(items[index]), index);
        }

        return positions;
    }
}
