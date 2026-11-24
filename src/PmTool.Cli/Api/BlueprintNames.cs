using System.Text.Json;

namespace PmTool.Cli.Api;

/// <summary>
/// 從專案的藍圖查名稱與欄位型別：狀態、項目類型、欄位。給人看的輸出用名稱，不用內部的代號。
/// 藍圖取不到（沒有權限、連線有問題）時是空的，所有查詢原樣回傳代號，輸出照樣印得出來。
/// </summary>
internal sealed class BlueprintNames
{
    public static readonly BlueprintNames Empty = new(default);

    private readonly JsonElement _blueprint;

    private BlueprintNames(JsonElement blueprint) => _blueprint = blueprint;

    public static BlueprintNames From(JsonElement? blueprint) => blueprint is { ValueKind: JsonValueKind.Object } found ? new(found) : Empty;

    /// <summary>項目類型的名稱。</summary>
    public string TypeName(string? typeKey) =>
        typeKey is null ? "-" : FindByKey(_blueprint.List("itemTypes"), typeKey)?.Text("name") ?? typeKey;

    /// <summary>狀態的名稱：先找這個類型的流程，找不到再找所有流程裡同代號的第一個。</summary>
    public string StateName(string? typeKey, string? stateKey)
    {
        if (stateKey is null)
        {
            return "-";
        }

        var workflows = _blueprint.List("workflows");
        var workflowKey = typeKey is null ? null : FindByKey(_blueprint.List("itemTypes"), typeKey)?.Text("workflow");
        var preferred = workflows.Where(workflow => workflow.Text("key") == workflowKey);
        var state = preferred.Concat(workflows)
            .Select(workflow => FindByKey(workflow.List("states"), stateKey))
            .FirstOrDefault(found => found is not null);
        return state?.Text("name") ?? stateKey;
    }

    public JsonElement? Field(string fieldKey) => FindByKey(_blueprint.List("fields"), fieldKey);

    public string FieldName(string fieldKey) => Field(fieldKey)?.Text("name") ?? fieldKey;

    private static JsonElement? FindByKey(IReadOnlyList<JsonElement> elements, string key) =>
        elements.Any(element => element.Text("key") == key) ? elements.First(element => element.Text("key") == key) : null;
}
