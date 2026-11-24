using System.Text.Json;

namespace PmTool.Blueprint.Schema;

/// <summary>一份 Schema 文件的原始內容。只在載入期間使用，驗證時不會碰到。</summary>
internal sealed record SchemaDocument(string Id, JsonElement Root)
{
    // 註解與結尾逗號預設就不允許。重複的鍵要明確關掉：重複的關鍵字只會留下其中一個，等於悄悄改了規則。
    private static readonly JsonDocumentOptions Options = new() { AllowDuplicateProperties = false };

    /// <param name="text">Schema 的 JSON 文字。</param>
    /// <param name="position">第幾份（從 1 開始），給錯誤訊息用；這時還不知道 $id。</param>
    public static SchemaDocument Parse(string text, int position)
    {
        using var document = JsonDocument.Parse(text, Options);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new NotSupportedException($"第 {position} 份 Schema 不是 JSON 物件。");
        }

        if (!root.TryGetProperty(SchemaKeywords.Id, out var id)
            || id.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(id.GetString()))
        {
            throw new NotSupportedException($"第 {position} 份 Schema 沒有 $id，或 $id 不是非空的字串。");
        }

        return new SchemaDocument(id.GetString()!, root.Clone());
    }

    /// <summary>$defs 的名稱，依文件順序。$defs 不是物件時丟例外。</summary>
    public IReadOnlyList<string> DefinitionNames()
    {
        if (!Root.TryGetProperty(SchemaKeywords.Defs, out var definitions))
        {
            return [];
        }

        if (definitions.ValueKind != JsonValueKind.Object)
        {
            throw new NotSupportedException($"Schema 位置 {SchemaLocation.RootOf(Id)}：$defs 必須是 JSON 物件。");
        }

        return [.. definitions.EnumerateObject().Select(definition => definition.Name)];
    }

    public bool HasDefinition(string name) =>
        Root.TryGetProperty(SchemaKeywords.Defs, out var definitions)
        && definitions.ValueKind == JsonValueKind.Object
        && definitions.TryGetProperty(name, out _);
}
