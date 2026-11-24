namespace PmTool.Api.Composition;

/// <summary>
/// 讀部署設定的共用方法。值不合規定時丟出說得出是哪個設定的例外：設定錯了要在部署時就知道，
/// 不要等到第一個請求才發現，也不要默默當成預設值。
/// </summary>
internal static class SettingValues
{
    /// <summary>沒設或空白時用 <paramref name="defaultValue"/>；只收 true／false（不分大小寫）。</summary>
    public static bool ReadFlag(IConfiguration configuration, string key, bool defaultValue)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        return bool.TryParse(raw.Trim(), out var value)
            ? value
            : throw new InvalidOperationException($"{key} 只能是 true 或 false，現在是「{raw}」。");
    }

    /// <summary>沒設或空白時是 null，其他的去掉前後空白。</summary>
    public static string? ReadText(IConfiguration configuration, string key) =>
        string.IsNullOrWhiteSpace(configuration[key]) ? null : configuration[key]!.Trim();

    /// <summary>
    /// 可以有多個值的設定。環境變數寫成 <c>Key__0</c>、<c>Key__1</c>；只有一個時也可以直接寫 <c>Key</c>。
    /// 空白的項目略過。
    /// </summary>
    public static IReadOnlyList<string> ReadList(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var single = string.IsNullOrWhiteSpace(section.Value) ? [] : new[] { section.Value.Trim() };
        var items = section.GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim());
        return [.. single, .. items];
    }
}
