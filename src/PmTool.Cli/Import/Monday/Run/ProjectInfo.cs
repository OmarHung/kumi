using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Import.Monday.Run;

/// <summary>一個專案裡的週期。</summary>
internal sealed record CycleInfo(Guid Id, string Name);

/// <summary>
/// 伺服器上的一個專案：成員、藍圖（類型、流程、欄位）、呼叫者的權限。檢查階段用它對照對應檔，
/// 寫入階段用它知道誰已經是成員、週期有沒有建。
/// </summary>
internal sealed class ProjectInfo
{
    private const string ManagePermission = "project.manage";

    private readonly JsonElement _blueprint;

    private ProjectInfo(string key, JsonElement detail)
    {
        Key = key;
        _blueprint = detail.Get("blueprint") ?? default;
        IsArchived = detail.Get("project")?.Get("archivedAt") is not null;
        CanManage = detail.List("myPermissions").Any(permission => permission.ValueKind == JsonValueKind.String && permission.GetString() == ManagePermission);
        Members = [.. detail.List("members").Select(member => Guid.TryParse(member.Text("personId"), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)];
    }

    public string Key { get; }

    public bool IsArchived { get; }

    public bool CanManage { get; }

    public HashSet<Guid> Members { get; }

    public static ProjectInfo From(string key, JsonElement detail) => new(key, detail);

    /// <summary>
    /// 內建的四個角色（admin、editor、commenter、viewer）寫死在伺服器裡、不在藍圖的 roles；藍圖只列自訂角色。
    /// 只查藍圖的話，連預設的 editor 都會被當成沒有。
    /// </summary>
    public bool HasRole(string roleKey) =>
        BuiltInRoles.Contains(roleKey) || _blueprint.List("roles").Any(role => role.Text("key") == roleKey);

    private static readonly string[] BuiltInRoles = ["admin", "editor", "contributor", "commenter", "viewer"];

    public JsonElement? ItemType(string typeKey) => Find(_blueprint.List("itemTypes"), typeKey);

    /// <summary>這個類型的流程裡有哪些狀態；類型或流程不存在是空的。</summary>
    public IReadOnlySet<string> StateKeys(string typeKey)
    {
        var workflow = ItemType(typeKey)?.Text("workflow");
        var states = Find(_blueprint.List("workflows"), workflow)?.List("states") ?? [];
        return states.Select(state => state.Text("key")).OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>這個類型的流程裡某個狀態的類別（backlog、not_started、in_progress、done、canceled）；找不到是 null。</summary>
    public string? StateCategory(string typeKey, string stateKey)
    {
        var workflow = ItemType(typeKey)?.Text("workflow");
        var states = Find(_blueprint.List("workflows"), workflow)?.List("states") ?? [];
        return states.Where(state => state.Text("key") == stateKey).Select(state => state.Text("category")).FirstOrDefault();
    }

    /// <summary>報告給人看的名稱：藍圖裡的 name，找不到就用 key。</summary>
    public string TypeName(string typeKey) => ItemType(typeKey)?.Text("name") ?? typeKey;

    public string FieldName(string fieldKey) => Field(fieldKey)?.Text("name") ?? fieldKey;

    public JsonElement? Field(string fieldKey) => Find(_blueprint.List("fields"), fieldKey);

    /// <summary>這個欄位有沒有掛在這個類型上（沒掛的欄位，新增項目時伺服器不收）。</summary>
    public bool TypeHasField(string typeKey, string fieldKey) =>
        ItemType(typeKey)?.List("fields").Any(field => field.ValueKind == JsonValueKind.String && field.GetString() == fieldKey) == true;

    public static string? FieldType(JsonElement field) => field.Text("type");

    public static IReadOnlySet<string> OptionKeys(JsonElement field) =>
        field.List("options").Select(option => option.Text("key")).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static JsonElement? Find(IReadOnlyList<JsonElement> elements, string? key) =>
        key is null ? null : elements.Where(element => element.Text("key") == key).Select(element => (JsonElement?)element).FirstOrDefault();
}
