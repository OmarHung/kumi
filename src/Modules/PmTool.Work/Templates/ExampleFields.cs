using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Templates;

/// <param name="Values">通過檢查、已經正規化的值；日期欄位已經換成 YYYY-MM-DD。</param>
internal sealed record ExampleFieldCheck(IReadOnlyDictionary<string, JsonElement> Values, IReadOnlyList<FailureDetail> Problems);

/// <summary>
/// 範例的自訂欄位：先把相對天數的日期換成真正的日期，再交給一般新增項目用的欄位檢查，兩邊的規則才會一樣。
/// 範例裡沒有真的人，所以成員欄位一律不收。
/// </summary>
internal static class ExampleFields
{
    /// <summary>日期、天數的範圍：十年以內。超過的多半是寫錯，也避免日期運算溢位。</summary>
    public const int MaxDays = 3_650;

    public static ExampleFieldCheck Check(
        ProjectSchema schema, ItemType type, IReadOnlyDictionary<string, JsonElement> fields, DateOnly today)
    {
        var problems = new List<FailureDetail>();
        var prepared = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in fields)
        {
            var definition = type.Fields.Contains(key) ? schema.Fields.GetValueOrDefault(key) : null;
            switch (definition?.Type)
            {
                case FieldType.Person:
                    problems.Add(Problem(key, $"範例不放成員欄位{Quote(definition.Name)}：範例裡沒有真的人。"));
                    break;
                case FieldType.Date:
                    AddDate(prepared, problems, definition, value, today);
                    break;
                default:
                    if (definition is not null && value.ValueKind == JsonValueKind.Null)
                    {
                        problems.Add(Problem(key, $"欄位{Quote(definition.Name)}不能寫 null：範例是新建的，沒有值可以清掉，不想填就整個拿掉。"));
                        break;
                    }

                    prepared[key] = value;
                    break;
            }
        }

        var checkedFields = CustomFieldRules.Check(schema, type, prepared);
        var values = checkedFields.Changes
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Value, StringComparer.Ordinal);
        return new ExampleFieldCheck(values, [.. problems, .. checkedFields.Problems]);
    }

    private static void AddDate(
        Dictionary<string, JsonElement> prepared, List<FailureDetail> problems, FieldDefinition definition, JsonElement value, DateOnly today)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var days) && days is >= -MaxDays and <= MaxDays)
        {
            prepared[definition.Key] = JsonSerializer.SerializeToElement(today.AddDays(days).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            return;
        }

        problems.Add(Problem(definition.Key, $"日期欄位{Quote(definition.Name)}要寫成相對建立當天的天數（整數，絕對值不超過 {MaxDays}）。"));
    }

    private static FailureDetail Problem(string path, string message) => new(WorkErrorCodes.ItemInvalidValue, path, message);
}
