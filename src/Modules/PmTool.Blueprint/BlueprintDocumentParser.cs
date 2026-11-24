using System.Text.Json.Nodes;
using PmTool.Blueprint.Documents;

namespace PmTool.Blueprint;

/// <summary>讀文件的結果。沒有問題時 <see cref="Root"/> 是整棵樹（文件只有 null 時也是 null）。</summary>
public sealed record ParsedDocument(JsonNode? Root, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Succeeded => Issues.Count == 0;
}

/// <summary>
/// 把 JSON 或 YAML 文字讀成 JsonNode 樹，用的就是驗證器讀文件的那一份程式：
/// 純量的型別（0x10、很大的整數、有引號的數字）、上限、重複的鍵、anchor 與標籤的處理都一樣。
/// 給 CLI 這類要在送出之前自己改文件的呼叫端用，避免兩邊對同一份文字讀出不同的值。純邏輯，不碰資料庫與網路。
/// </summary>
public static class BlueprintDocumentParser
{
    public static ParsedDocument Parse(string? documentText, DocumentFormat format)
    {
        var result = DocumentReader.Read(documentText, format);
        return new ParsedDocument(result.Root, result.Issues);
    }
}
