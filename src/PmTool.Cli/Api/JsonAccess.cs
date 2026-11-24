using System.Globalization;
using System.Text.Json;

namespace PmTool.Cli.Api;

/// <summary>讀伺服器回應的小工具：少了屬性或型別不對時給空值，不丟例外，輸出時顯示成「-」。</summary>
internal static class JsonAccess
{
    public static JsonElement? Get(this JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;

    public static string? Text(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static long? Number(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    public static bool Flag(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.True };

    public static IReadOnlyList<JsonElement> List(this JsonElement element, string name) =>
        element.Get(name) is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];

    /// <summary>陣列本身（例如專案清單的 data）。</summary>
    public static IReadOnlyList<JsonElement> Items(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? [.. element.EnumerateArray()] : [];

    /// <summary>顯示用的文字：沒有值是「-」，布林寫成是／否，陣列用頓號連起來。</summary>
    public static string Display(this JsonElement? element) => element switch
    {
        null => "-",
        { ValueKind: JsonValueKind.String } text => text.GetString() ?? "-",
        { ValueKind: JsonValueKind.True } => "是",
        { ValueKind: JsonValueKind.False } => "否",
        { ValueKind: JsonValueKind.Number } number => number.GetRawText(),
        { ValueKind: JsonValueKind.Array } array => string.Join("、", array.EnumerateArray().Select(item => item.Display())),
        { } other => other.GetRawText(),
    };

    /// <summary>欄位不存在寫「-」。</summary>
    public static string Display(this JsonElement element, string name) => element.Get(name).Display();

    public static string Display(this JsonElement element) => ((JsonElement?)element).Display();

    /// <summary>時刻轉成本機時區的「年-月-日 時:分」。</summary>
    public static string Moment(string? iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            : "-";
}
