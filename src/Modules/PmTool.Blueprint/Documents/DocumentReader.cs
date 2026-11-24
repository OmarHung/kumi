using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Documents;

internal sealed record DocumentReadResult(JsonNode? Root, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Succeeded => Issues.Count == 0;

    public static DocumentReadResult Success(JsonNode? root) => new(root, []);

    public static DocumentReadResult Failure(ValidationIssue issue) => new(null, [issue]);
}

/// <summary>把 JSON 或 YAML 文字讀成 JsonNode。規格見 internal/HANDOFF-week1-blueprint-validation.md 的工作包 A。</summary>
internal static class DocumentReader
{
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// 不合法的文件一律回傳問題，不丟例外。只有 format 不是定義過的值時會丟，那是呼叫端的程式錯誤，不是文件的問題。
    /// </summary>
    public static DocumentReadResult Read(string? text, DocumentFormat format)
    {
        Func<string, DocumentReadResult> parse = format switch
        {
            DocumentFormat.Json => JsonDocumentReader.Read,
            DocumentFormat.Yaml => YamlDocumentReader.Read,
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "不支援的文件格式。"),
        };

        if (text is null)
        {
            return DocumentReadResult.Failure(DocumentIssues.Empty());
        }

        // 先看長度，超過上限就不做任何掃描。
        if (text.Length > DocumentLimits.MaxCharacters)
        {
            return DocumentReadResult.Failure(DocumentIssues.TooLarge(text.Length));
        }

        var body = text.StartsWith(ByteOrderMark) ? text[1..] : text;
        if (string.IsNullOrWhiteSpace(body))
        {
            return DocumentReadResult.Failure(DocumentIssues.Empty());
        }

        return HasUnpairedSurrogate(body)
            ? DocumentReadResult.Failure(DocumentIssues.UnpairedSurrogate())
            : parse(body);
    }

    /// <summary>
    /// 落單的代理字元不是合法的 Unicode。JSON 轉 UTF-8 時會丟例外；YAML 解析器則會把它原樣放進字串，
    /// 之後序列化時被悄悄換成 U+FFFD。兩種格式在這裡一起擋下。
    /// </summary>
    private static bool HasUnpairedSurrogate(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                index++;
            }
            else if (char.IsSurrogate(text[index]))
            {
                return true;
            }
        }

        return false;
    }
}
