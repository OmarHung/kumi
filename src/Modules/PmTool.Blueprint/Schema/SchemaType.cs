namespace PmTool.Blueprint.Schema;

/// <summary>子集支援的 type。JSON Schema 的 null 型別刻意不支援：沒寫的屬性就是沒有值。</summary>
internal enum SchemaType
{
    Object,
    Array,
    String,
    Integer,
    Number,
    Boolean,
}
