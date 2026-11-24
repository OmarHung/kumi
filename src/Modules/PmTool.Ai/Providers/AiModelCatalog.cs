namespace PmTool.Ai.Providers;

/// <summary>從供應商讀到的清單先整理過才存：去掉不合規定的、重複的，依代號排序，超過上限的截掉。</summary>
internal static class AiModelCatalog
{
    public static IReadOnlyList<AiDiscoveredModel> Normalize(IEnumerable<AiDiscoveredModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        return models
            .Where(m => IsStorable(m.Id, AiRules.MaxModelIdLength) && IsStorable(m.DisplayName, AiRules.MaxDisplayNameLength))
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .Take(AiRules.MaxCatalogModels)
            .ToList();
    }

    private static bool IsStorable(string? text, int maxLength) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= maxLength && !text.Any(char.IsControl);
}
