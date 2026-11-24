using System.Collections.Frozen;

namespace PmTool.Blueprint.Schema;

/// <summary>
/// 載入 JSON Schema 文件並依 $id 建立索引。只支援刻意挑選的關鍵字子集，
/// 遇到子集以外的關鍵字要在載入時就丟例外。規格見交接文件的工作包 B。
/// </summary>
/// <remarks>
/// 載入時就把 Schema 編成不可變的 <see cref="SchemaNode"/>，之後可以讓多個請求同時使用。
/// </remarks>
internal sealed class SchemaRegistry
{
    private readonly FrozenDictionary<string, SchemaNode> _roots;

    private SchemaRegistry(FrozenDictionary<string, SchemaNode> roots) => _roots = roots;

    /// <exception cref="System.Text.Json.JsonException">某一份不是合法的 JSON，或有重複的鍵。</exception>
    /// <exception cref="NotSupportedException">某一份用了子集以外的寫法；訊息指出關鍵字與位置。</exception>
    /// <exception cref="ArgumentException">$id 重複。</exception>
    public static SchemaRegistry Load(IEnumerable<string> schemaTexts)
    {
        ArgumentNullException.ThrowIfNull(schemaTexts);
        var documents = schemaTexts
            .Select((text, index) => SchemaDocument.Parse(
                text ?? throw new ArgumentNullException(nameof(schemaTexts), $"第 {index + 1} 份 Schema 是 null。"),
                index + 1))
            .ToList();

        var duplicate = documents
            .GroupBy(document => document.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"$id「{duplicate.Key}」出現在不只一份 Schema 裡。", nameof(schemaTexts));
        }

        return new SchemaRegistry(SchemaCompiler.CompileAll(documents));
    }

    internal bool Contains(string schemaId) => _roots.ContainsKey(schemaId);

    /// <summary>
    /// 某份文件根節點編好的 Schema。沒載入過的 id 是程式的錯誤，不是使用者的輸入，所以直接丟例外。
    /// </summary>
    internal SchemaNode GetRoot(string schemaId)
    {
        ArgumentNullException.ThrowIfNull(schemaId);
        return _roots.TryGetValue(schemaId, out var root)
            ? root
            : throw new ArgumentException($"沒有載入 $id 為「{schemaId}」的 Schema。", nameof(schemaId));
    }
}
