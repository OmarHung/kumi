using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Schema;

internal static class JsonElements
{
    /// <summary>
    /// 背後不是 JsonElement、也不是常用 CLR 型別（例如 int、decimal、Guid）的值，先寫成 JSON 文字再讀回來，
    /// 讓它走和 JSON 來源完全相同的判斷規則，不必為每一種 CLR 型別各寫一套。
    /// </summary>
    public static JsonElement From(JsonValue value)
    {
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }
}
