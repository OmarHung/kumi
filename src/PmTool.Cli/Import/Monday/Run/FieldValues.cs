using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>從 monday 的欄位值裡讀日期、網址、勾選這類常用的值。monday 的 value 是 JSON 字串，壞掉的當作沒有。</summary>
internal static partial class MondayValues
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex LeadingDate();

    public static JsonNode? Json(MondayColumnValue value)
    {
        if (string.IsNullOrWhiteSpace(value.ValueJson))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value.ValueJson);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    public static string? Text(MondayColumnValue value) =>
        string.IsNullOrWhiteSpace(value.Text) ? null : MarkdownText.StripInvisible(value.Text).Trim();

    /// <summary>date 欄的日期（YYYY-MM-DD）：先看 value 的 date，沒有再看 monday 給的文字。</summary>
    public static DateOnly? Date(MondayColumnValue value) =>
        ParseDate(MondayJson.Text(Json(value)?["date"])) ?? ParseDate(value.Text);

    /// <summary>timeline 欄：from 與 to。</summary>
    public static (DateOnly? From, DateOnly? To) Timeline(MondayColumnValue value)
    {
        var json = Json(value);
        return (ParseDate(MondayJson.Text(json?["from"])), ParseDate(MondayJson.Text(json?["to"])));
    }

    public static bool? Checked(MondayColumnValue value)
    {
        var json = Json(value);
        if (json is null)
        {
            return null;
        }

        return string.Equals(MondayJson.Text(json["checked"]), "true", StringComparison.OrdinalIgnoreCase)
            || json["checked"] is JsonValue flag && flag.TryGetValue<bool>(out var isChecked) && isChecked;
    }

    public static string? LinkUrl(MondayColumnValue value) =>
        MondayJson.Text(Json(value)?["url"]) is { Length: > 0 } url ? url.Trim() : Text(value);

    private static DateOnly? ParseDate(string? text) =>
        text is not null && LeadingDate().Match(text.Trim()) is { Success: true } match
        && DateOnly.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}

/// <param name="Value">要放進項目的值；null 是不放（沒有值，或值不能用）。</param>
/// <param name="Reminder">值不能用時的說明（略過了什麼）。</param>
/// <param name="PersonId">person 欄位選到的人（他要是專案成員）。</param>
internal sealed record FieldConversion(JsonNode? Value, string? Reminder = null, Guid? PersonId = null);

/// <summary>monday 的欄 → 自訂欄位（交接第 6.3 節）。</summary>
internal static class FieldConverter
{
    private static readonly IReadOnlyDictionary<string, string[]> Accepted = new Dictionary<string, string[]>
    {
        ["number"] = ["numbers", "text"],
        ["text"] = ["text", "long_text", "link", "numbers"],
        ["long_text"] = ["text", "long_text", "link", "numbers"],
        ["url"] = ["link", "text"],
        ["date"] = ["date", "timeline"],
        ["single_select"] = ["status", "dropdown", "board_relation"],
        ["multi_select"] = ["dropdown", "board_relation", "status"],
        ["person"] = ["people"],
        ["checkbox"] = ["checkbox"],
    };

    public static bool IsSupported(string fieldType, string columnType) =>
        Accepted.TryGetValue(fieldType, out var columns) && columns.Contains(columnType);

    public static string SupportedColumns(string fieldType) =>
        Accepted.TryGetValue(fieldType, out var columns) ? string.Join("、", columns) : "（不支援這種欄位）";

    /// <param name="eligiblePerson">monday 的人編號 → 可以當作值的人（對得上、而且是專案成員）；對不上是 null。</param>
    public static FieldConversion Convert(
        string fieldName, string fieldType, FieldMapping mapping, MondayColumnValue value, ISet<string> optionKeys,
        Func<string, ServerPerson?> eligiblePerson) => fieldType switch
        {
            "number" => Number(fieldName, value),
            "text" or "long_text" => Text(value),
            "url" => Url(fieldName, value),
            "date" => Date(value),
            "single_select" => Select(fieldName, mapping, value, optionKeys, multiple: false),
            "multi_select" => Select(fieldName, mapping, value, optionKeys, multiple: true),
            "person" => Person(fieldName, value, eligiblePerson),
            "checkbox" => MondayValues.Checked(value) is { } isChecked ? new FieldConversion(JsonValue.Create(isChecked)) : new FieldConversion(null),
            _ => new FieldConversion(null),
        };

    /// <summary>
    /// 對應檔裡寫的固定值（群組的 fields、titleRules 的 fields）→ 自訂欄位的值：單選是選項的 key，多選用逗號隔開，
    /// 勾選是 true／false，數字是數字，其他照字串。值不能用時 <see cref="FieldConversion.Reminder"/> 是原因（檢查階段把它列成問題）。
    /// </summary>
    public static FieldConversion FromText(string fieldName, string fieldType, string text, ISet<string> optionKeys)
    {
        var value = text.Trim();
        return fieldType switch
        {
            "number" => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? new FieldConversion(JsonValue.Create(number))
                : new FieldConversion(null, $"欄位「{fieldName}」的值「{value}」不是數字"),
            "checkbox" => bool.TryParse(value, out var isChecked)
                ? new FieldConversion(JsonValue.Create(isChecked))
                : new FieldConversion(null, $"欄位「{fieldName}」的值「{value}」要寫 true 或 false"),
            "single_select" => optionKeys.Contains(value)
                ? new FieldConversion(JsonValue.Create(value))
                : new FieldConversion(null, $"欄位「{fieldName}」沒有選項「{value}」"),
            "multi_select" => FixedMultiSelect(fieldName, value, optionKeys),
            "url" => IsWebUrl(value)
                ? new FieldConversion(JsonValue.Create(value))
                : new FieldConversion(null, $"欄位「{fieldName}」的值「{value}」不是 http 或 https 網址"),
            "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? new FieldConversion(JsonValue.Create(value))
                : new FieldConversion(null, $"欄位「{fieldName}」的值「{value}」不是日期，格式要是 yyyy-MM-dd"),
            "person" => new FieldConversion(null, $"欄位「{fieldName}」是人員欄位，不能寫固定的值"),
            _ => new FieldConversion(JsonValue.Create(value)),
        };
    }

    private static FieldConversion FixedMultiSelect(string fieldName, string value, ISet<string> optionKeys)
    {
        var keys = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        var unknown = keys.Where(key => !optionKeys.Contains(key)).ToArray();
        return unknown.Length > 0 || keys.Length == 0
            ? new FieldConversion(null, $"欄位「{fieldName}」沒有選項「{(keys.Length == 0 ? value : string.Join("、", unknown))}」")
            : new FieldConversion(new JsonArray([.. keys.Select(key => (JsonNode?)JsonValue.Create(key))]));
    }

    private static FieldConversion Number(string fieldName, MondayColumnValue value)
    {
        // 先試 value 的原始數值（text 可能是被格式化過的，例如帶千分位或單位），再退回 monday 給的文字。
        var raw = MondayJson.Text(MondayValues.Json(value));
        if (raw is not null && decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact))
        {
            return new FieldConversion(JsonValue.Create(exact));
        }

        if (MondayValues.Text(value) is not { } text)
        {
            return new FieldConversion(null);
        }

        return decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var number)
            ? new FieldConversion(JsonValue.Create(number))
            : new FieldConversion(null, $"欄位「{fieldName}」的值「{text}」不是數字，略過");
    }

    private static FieldConversion Text(MondayColumnValue value)
    {
        var text = value.Type == "link" ? MondayValues.LinkUrl(value) : MondayValues.Text(value);
        return new FieldConversion(text is null ? null : JsonValue.Create(text));
    }

    private static FieldConversion Url(string fieldName, MondayColumnValue value)
    {
        var url = value.Type == "link" ? MondayValues.LinkUrl(value) : MondayValues.Text(value);
        if (url is null)
        {
            return new FieldConversion(null);
        }

        return IsWebUrl(url)
            ? new FieldConversion(JsonValue.Create(url))
            : new FieldConversion(null, $"欄位「{fieldName}」的值「{url}」不是 http 或 https 網址，略過");
    }

    public static bool IsWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static FieldConversion Date(MondayColumnValue value)
    {
        var date = value.Type == "timeline" ? MondayValues.Timeline(value).To : MondayValues.Date(value);
        return new FieldConversion(date is null ? null : JsonValue.Create(HistoryApi.Date(date.Value)));
    }

    private static FieldConversion Select(string fieldName, FieldMapping mapping, MondayColumnValue value, ISet<string> optionKeys, bool multiple)
    {
        var candidates = Candidates(value);
        var matched = candidates
            .Select(candidate => mapping.Values.TryGetValue(candidate, out var key) && optionKeys.Contains(key) ? key : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var reminder = candidates.Count > 0 && matched.Length == 0
            ? $"欄位「{fieldName}」的值「{string.Join("、", candidates)}」沒有對應的選項，略過"
            : null;
        if (matched.Length == 0)
        {
            return new FieldConversion(null, reminder);
        }

        return multiple
            ? new FieldConversion(new JsonArray([.. matched.Select(key => (JsonNode?)JsonValue.Create(key))]))
            : new FieldConversion(JsonValue.Create(matched[0]));
    }

    private static IReadOnlyList<string> Candidates(MondayColumnValue value) => value.Type switch
    {
        "status" => value.Label is { Length: > 0 } label ? [label] : [],
        "board_relation" => [.. value.LinkedItems.Select(item => item.Name).Where(name => name.Length > 0)],
        // dropdown 的 text 是用「, 」把標籤接起來的。
        _ => MondayValues.Text(value)?.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
    };

    private static FieldConversion Person(string fieldName, MondayColumnValue value, Func<string, ServerPerson?> eligiblePerson)
    {
        var person = value.PersonIds.Select(eligiblePerson).FirstOrDefault(found => found is not null);
        if (person is null)
        {
            return new FieldConversion(null, value.PersonIds.Count > 0 ? $"欄位「{fieldName}」的人對不上這裡的成員，略過" : null);
        }

        return new FieldConversion(JsonValue.Create(person.Id.ToString()), null, person.Id);
    }
}
