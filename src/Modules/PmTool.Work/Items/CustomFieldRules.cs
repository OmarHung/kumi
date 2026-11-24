using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Blueprints;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <param name="Changes">通過檢查的欄位；值是 null 表示拿掉。</param>
internal sealed record CustomFieldCheck(
    IReadOnlyDictionary<string, JsonElement?> Changes,
    IReadOnlyList<FailureDetail> Problems,
    IReadOnlyList<PersonReference> People);

/// <summary>自訂欄位要屬於這個類型（藍圖的 itemTypes[].fields），值要符合欄位型別。</summary>
internal static class CustomFieldRules
{
    /// <summary>自訂欄位整體在 Details 裡的路徑。</summary>
    public const string FieldsPath = "fields";

    /// <param name="fields">合約的預設是空的字典，但直接呼叫模組的入口（CLI、MCP）可能明確給 null。</param>
    public static CustomFieldCheck Check(ProjectSchema schema, ItemType itemType, IReadOnlyDictionary<string, JsonElement>? fields)
    {
        if (fields is null)
        {
            var problem = new FailureDetail(WorkErrorCodes.ItemInvalidValue, FieldsPath, "自訂欄位不能是 null；沒有要改的欄位時給空的物件。");
            return new CustomFieldCheck(new Dictionary<string, JsonElement?>(), [problem], []);
        }

        var results = fields.Select(pair => CheckOne(schema, itemType, pair.Key, pair.Value)).ToArray();
        return new CustomFieldCheck(
            results.Where(r => r.Problem is null).ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal),
            [.. results.Select(r => r.Problem).OfType<FailureDetail>()],
            [.. results.Select(r => r.Person).OfType<PersonReference>()]);
    }

    private static FieldResult CheckOne(ProjectSchema schema, ItemType itemType, string key, JsonElement value)
    {
        if (!itemType.Fields.Contains(key) || !schema.Fields.TryGetValue(key, out var field))
        {
            var problem = new FailureDetail(
                WorkErrorCodes.ItemUnknownField, key, $"{Quote(itemType.Name)}沒有{Quote(key)}這個欄位。");
            return new FieldResult(key, null, problem);
        }

        return FieldValueRules.Check(field, value);
    }
}
