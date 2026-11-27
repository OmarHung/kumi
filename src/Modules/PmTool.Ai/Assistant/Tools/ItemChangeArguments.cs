using System.Text.Json;

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
}
