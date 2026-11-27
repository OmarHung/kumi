using System.Text.Json;
using Microsoft.Extensions.AI;

namespace PmTool.Ai.Assistant.Tools;

/// <summary>changes（[{field, value}]）的讀法：單一項目的修改建議與批次修改建議每一筆都用它。型別不對照實告訴模型，不默默丟掉。</summary>
internal static class ItemChangeArguments
{
    /// <param name="elements">changes 陣列的元素；不是陣列是 null。</param>
    /// <param name="path">錯誤訊息裡的路徑，例如 <c>changes</c> 或 <c>items[2].changes</c>。</param>
    public static (IReadOnlyList<ItemFieldChange>? Value, string? Error) Read(IReadOnlyList<JsonElement>? elements, string path)
    {
        if (elements is null)
        {
            return (null, $"參數 {path} 要是 [{{field, value}}] 的陣列。");
        }

        var changes = new List<ItemFieldChange>(elements.Count);
        foreach (var element in elements)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("field", out var field)
                || field.ValueKind != JsonValueKind.String)
            {
                return (null, $"{path} 的每一項都要是 {{field, value}}，field 是字串。");
            }

            changes.Add(new ItemFieldChange(field.GetString()!, element.TryGetProperty("value", out var value) ? value : default));
        }

        return (changes, null);
    }

    /// <summary>replyTo（留言 id 的字串）：沒給或 null 是新的一則；給了但不是 GUID 照實告訴模型。</summary>
    public static (Guid? Value, string? Error) ReadReplyTo(AIFunctionArguments arguments, string path)
    {
        if (!arguments.TryGetValue(path, out var raw) || raw is null or JsonElement { ValueKind: JsonValueKind.Null })
        {
            return (null, null);
        }

        return ToolArguments.String(arguments, path) is { } text && Guid.TryParse(text, out var id)
            ? (id, null)
            : (null, $"參數 {path} 要是留言的 id（GUID）。");
    }

    /// <summary>批次修改某一筆裡的 replyTo。</summary>
    public static (Guid? Value, string? Error) ReadReplyTo(JsonElement entry, string path)
    {
        if (!entry.TryGetProperty("replyTo", out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return (null, null);
        }

        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id)
            ? (id, null)
            : (null, $"{path}.replyTo 要是留言的 id（GUID）。");
    }
}
