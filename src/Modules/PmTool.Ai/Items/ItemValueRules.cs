using System.Globalization;
using System.Text.Json;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Ai.Items;

/// <summary>
/// 修改建議（<see cref="ItemChangeChecker"/>）與新增項目的建議（<c>NewItemChecker</c>）共用的值檢查：同一個欄位兩邊的規則與訊息一樣，
/// 模型從一邊學到的修正方式在另一邊也適用。回傳 null 是沒有問題，否則是給模型看的原因。
/// </summary>
internal static class ItemValueRules
{
    private const string DateFormat = "yyyy-MM-dd";

    public static readonly IReadOnlyList<string> PriorityNames = ["none", "low", "medium", "high", "urgent"];

    public static string? CheckTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "title 要是不空白的字串。";
        }

        return title.Trim().Length > WorkRules.MaxTitleLength ? $"title 最長 {WorkRules.MaxTitleLength} 個字元。" : null;
    }

    public static string? CheckDescriptionLength(string description) =>
        description.Length > WorkRules.MaxDescriptionLength ? $"description 最長 {WorkRules.MaxDescriptionLength} 個字元。" : null;

    public static string? CheckPriority(string? name) =>
        name is not null && PriorityNames.Contains(name)
            ? null
            : $"priority 要是 {string.Join("、", PriorityNames)} 其中之一。";

    public static string? CheckEstimate(decimal number) =>
        number >= 0 && number <= WorkRules.MaxNumberMagnitude ? null : EstimateMessage;

    public const string EstimateMessage = "estimate 要是不小於 0 的數字，null 是清掉。";

    public const string DateMessage = "日期要寫成 yyyy-MM-dd，null 是清掉。";

    public static bool IsDate(string? text) =>
        DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>自訂欄位的值（非 null）：JSON 種類要對、不能太大。</summary>
    public static string? CheckCustomValue(string key, FieldDefinition definition, JsonElement value)
    {
        if (!KindFits(definition.Type, value))
        {
            return $"欄位「{key}」是{definition.Type}，值的 JSON 種類不對。";
        }

        return OverLimit(value) is { } over ? $"欄位「{key}」的值太大：{over}" : null;
    }

    public static bool IsPlainText(string text, bool allowLineBreaks) =>
        !text.Any(c => char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t'));

    // 值裡任何一層的字串都要乾淨：自訂欄位的值可以是陣列。
    public static bool IsPlainText(JsonElement value, bool allowLineBreaks) => value.ValueKind switch
    {
        JsonValueKind.String => IsPlainText(value.GetString()!, allowLineBreaks),
        JsonValueKind.Array => value.EnumerateArray().All(e => IsPlainText(e, allowLineBreaks)),
        JsonValueKind.Object => value.EnumerateObject().All(p => IsPlainText(p.Name, allowLineBreaks: false) && IsPlainText(p.Value, allowLineBreaks)),
        _ => true,
    };

    private static string? OverLimit(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when value.GetString()!.Length > ItemProposalLimits.MaxCustomStringLength =>
            $"文字最長 {ItemProposalLimits.MaxCustomStringLength} 個字元。",
        JsonValueKind.Array when value.GetArrayLength() > ItemProposalLimits.MaxCustomArrayItems =>
            $"最多 {ItemProposalLimits.MaxCustomArrayItems} 項。",
        JsonValueKind.Array => value.EnumerateArray().Select(OverLimit).FirstOrDefault(problem => problem is not null),
        _ => null,
    };

    private static bool KindFits(FieldType type, JsonElement value) => type switch
    {
        FieldType.Text or FieldType.LongText or FieldType.Url or FieldType.SingleSelect or FieldType.Person =>
            value.ValueKind == JsonValueKind.String,
        FieldType.Date => value.ValueKind == JsonValueKind.String && IsDate(value.GetString()),
        FieldType.Number or FieldType.Currency => value.ValueKind == JsonValueKind.Number,
        FieldType.Checkbox => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FieldType.MultiSelect => value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String),
        _ => false,
    };
}
