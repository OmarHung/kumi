using System.Text.Json.Nodes;
using PmTool.Api.Json;

namespace PmTool.Api.Mcp.Tools;

/// <summary>
/// 組工具參數的 JSON Schema。只產生 CLAUDE.md 允許的關鍵字（type、properties、required、additionalProperties、enum、description），
/// 產出一律是 <c>additionalProperties: false</c> 的物件。
/// </summary>
internal static class McpSchemas
{
    public const string NoParameters = """{"type":"object","properties":{},"additionalProperties":false}""";

    public static string Object(string[] required, params (string Name, JsonObject Schema)[] properties)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(properties.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Name, p.Schema))),
        };
        if (required.Length > 0)
        {
            schema["required"] = new JsonArray([.. required.Select(name => (JsonNode)name)]);
        }

        schema["additionalProperties"] = false;
        return schema.ToJsonString();
    }

    public static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };

    public static JsonObject Integer(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Boolean(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject ArrayOf(string description, JsonObject items) =>
        new() { ["type"] = "array", ["description"] = description, ["items"] = items };

    /// <summary>必填的屬性；值不限型別（不寫 type）。</summary>
    public static JsonObject Any(string description) => new() { ["description"] = description };

    public static JsonObject ObjectOf(string[] required, params (string Name, JsonObject Schema)[] properties) =>
        JsonNode.Parse(Object(required, properties))!.AsObject();

    public static JsonObject FreeObject(string description) => new() { ["type"] = "object", ["description"] = description };

    public static JsonObject Enum(string description, params string[] values) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["enum"] = new JsonArray([.. values.Select(value => (JsonNode)value)]),
    };

    /// <summary>值照 API 的名字（snake_case），和回應裡看到的一樣。</summary>
    public static JsonObject Enum<TEnum>(string description)
        where TEnum : struct, Enum => Enum(description, [.. SnakeCaseEnums.AllNames<TEnum>()]);
}
