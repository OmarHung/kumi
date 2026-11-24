using System.Collections.Frozen;

namespace PmTool.Blueprint.Schema;

internal static class SchemaKeywords
{
    public const string Type = "type";
    public const string Properties = "properties";
    public const string Required = "required";
    public const string AdditionalProperties = "additionalProperties";
    public const string Items = "items";
    public const string Enum = "enum";
    public const string Const = "const";
    public const string Ref = "$ref";

    public const string SchemaDialect = "$schema";
    public const string Id = "$id";
    public const string Defs = "$defs";

    /// <summary>會被檢查的關鍵字。清單以外的關鍵字一律拒絕，Schema 才能直接拿去約束語言模型的輸出。</summary>
    public static readonly FrozenSet<string> Checked =
        new[] { Type, Properties, Required, AdditionalProperties, Items, Enum, Const, Ref }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>只是說明的註記，值必須是字串，不影響驗證。format 也不檢查，例如 uri 交給語意驗證。</summary>
    public static readonly FrozenSet<string> TextAnnotations =
        new[] { "title", "description", "format" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// 只能寫在文件最上層的註記。標準裡寫在內層的 $id 會開出新的基準 URI，內層的 $defs 不在
    /// #/$defs/ 能引用到的範圍；這個子集不處理這些情況，所以直接拒絕。
    /// </summary>
    public static readonly FrozenSet<string> RootOnly =
        new[] { SchemaDialect, Id, Defs }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>標準裡這些關鍵字遇到非物件的值會被默默略過，所以要求它們只出現在 type 是 object 的地方。</summary>
    public static readonly IReadOnlyList<string> ObjectOnly = [Properties, Required, AdditionalProperties];
}
