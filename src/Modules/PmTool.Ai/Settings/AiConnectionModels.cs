using PmTool.Abstractions;
using PmTool.Ai.Providers;

namespace PmTool.Ai.Settings;

/// <summary>連線上的模型清單怎麼由「供應商讀到的」「手動加的」「開放的」組出來，與重新讀清單時怎麼合併。</summary>
internal static class AiConnectionModels
{
    /// <summary>
    /// 以 <paramref name="catalog"/> 為準，加上手動的。開放的模型不在其中時回失敗（路徑 <c>enabledModels[i]</c>）。
    /// 手動加的代號和清單重複時，算清單裡的，不算手動。
    /// </summary>
    public static Result<IReadOnlyList<AiModel>> Build(
        AiProviderKind kind,
        IReadOnlyList<AiDiscoveredModel> catalog,
        IReadOnlyList<string> customIds,
        IReadOnlyList<string> enabledIds)
    {
        var enabled = new HashSet<string>(enabledIds, StringComparer.Ordinal);
        var models = catalog
            .Select(m => new AiModel(m.Id, m.DisplayName, m.Efforts, enabled.Contains(m.Id), Custom: false))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var id in customIds.Where(id => !models.ContainsKey(id)))
        {
            models[id] = new AiModel(id, id, AiEffortCatalog.ForCustom(kind, id), enabled.Contains(id), Custom: true);
        }

        var missing = enabledIds
            .Select((id, index) => (id, index))
            .Where(x => !models.ContainsKey(x.id))
            .Select(x => new FailureDetail(
                AiErrorCodes.ModelNotAvailable, $"enabledModels[{x.index}]", "這個模型不在清單裡，要用的話請先加成手動的模型。"))
            .ToList();
        if (missing.Count > 0)
        {
            return new Failure(FailureKind.Invalid, AiErrorCodes.SettingsInvalid, "連線的設定有幾個欄位不對。") { Details = missing };
        }

        return Sorted(models.Values);
    }

    /// <summary>供應商的清單：存起來的模型裡不是手動加的那些。</summary>
    public static IReadOnlyList<AiDiscoveredModel> CatalogOf(IEnumerable<AiModel> stored) =>
        [.. stored.Where(m => !m.Custom).Select(m => new AiDiscoveredModel(m.Id, m.DisplayName, m.Efforts))];

    /// <summary>
    /// 重新讀清單後合併：還在的保留開放與否，新出現的不開放；不見了的，沒開放的拿掉，開放中的保留並標成手動
    /// （清單可能只是沒列出來，不讓預設或成員正在用的模型無聲無息地消失）；手動加的不動，但出現在清單裡就改算清單的。
    /// </summary>
    public static IReadOnlyList<AiModel> Refresh(IReadOnlyList<AiModel> stored, IReadOnlyList<AiDiscoveredModel> discovered)
    {
        var previous = stored.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var merged = discovered
            .Select(d => new AiModel(d.Id, d.DisplayName, d.Efforts, previous.TryGetValue(d.Id, out var old) && old.Enabled, Custom: false))
            .ToDictionary(m => m.Id, StringComparer.Ordinal);
        foreach (var old in stored.Where(m => !merged.ContainsKey(m.Id) && (m.Custom || m.Enabled)))
        {
            merged[old.Id] = old with { Custom = true };
        }

        return Sorted(merged.Values);
    }

    private static List<AiModel> Sorted(IEnumerable<AiModel> models) =>
        [.. models.OrderBy(m => m.Id, StringComparer.Ordinal)];
}
