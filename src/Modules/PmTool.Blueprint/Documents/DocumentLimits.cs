namespace PmTool.Blueprint.Documents;

/// <summary>讀入文件時的上限。文件來自使用者或模型，超過就直接拒絕，不嘗試解析。</summary>
internal static class DocumentLimits
{
    public const int MaxCharacters = 262_144;
    public const int MaxDepth = 32;
    public const int MaxNodes = 20_000;
}
