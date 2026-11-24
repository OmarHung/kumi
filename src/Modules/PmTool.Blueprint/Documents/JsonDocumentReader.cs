using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PmTool.Blueprint.Documents;

/// <summary>
/// JSON 分兩步讀。先用 Utf8JsonReader 掃一遍：它是迭代式的，可以邊讀邊套用深度與節點數上限，超過就停。
/// 不先掃的話，太深的文件會在 JsonNode.Parse 撞到 .NET 自己的深度上限而丟 JsonException，和語法錯誤分不出來。
/// 掃描通過，才交給 JsonNode.Parse 建樹。
/// </summary>
internal static class JsonDocumentReader
{
    // 比我們的上限多一層，讓 NodeTally 一定比 .NET 讀取器先發現太深。
    private const int ParserMaxDepth = DocumentLimits.MaxDepth + 1;

    private static readonly JsonReaderOptions ScanOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = ParserMaxDepth,
    };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = ParserMaxDepth,
        AllowDuplicateProperties = false,
    };

    public static DocumentReadResult Read(string text)
    {
        // DocumentReader 已經確認沒有落單的代理字元，轉成 UTF-8 不會失真。
        var utf8 = Encoding.UTF8.GetBytes(text);
        var issue = Scan(utf8);
        if (issue is not null)
        {
            return DocumentReadResult.Failure(issue);
        }

        try
        {
            return DocumentReadResult.Success(JsonNode.Parse(utf8, nodeOptions: null, ParseOptions));
        }
        catch (JsonException)
        {
            // 語法、深度、字串跳脫在掃描時都檢查過了，這裡還會失敗的只剩重複的鍵。
            // .NET 的這個例外不帶行號，訊息又會引用鍵名，所以不轉出它的內容。
            return DocumentReadResult.Failure(DocumentIssues.JsonDuplicateKey());
        }
    }

    private static ValidationIssue? Scan(byte[] utf8)
    {
        var reader = new Utf8JsonReader(utf8, ScanOptions);
        var tally = default(NodeTally);
        try
        {
            while (reader.Read())
            {
                if (HasUnpairedEscapedSurrogate(ref reader))
                {
                    return DocumentIssues.JsonInvalidEscape(LineOf(utf8, reader.TokenStartIndex));
                }

                tally = Count(tally, reader.TokenType);
                var violation = tally.FindViolation();
                if (violation is not null)
                {
                    return violation;
                }
            }

            return null;
        }
        catch (JsonException exception)
        {
            return DocumentIssues.JsonSyntax(exception.LineNumber);
        }
    }

    private static NodeTally Count(NodeTally tally, JsonTokenType token) => token switch
    {
        JsonTokenType.StartObject or JsonTokenType.StartArray => tally.EnterContainer(),
        JsonTokenType.EndObject or JsonTokenType.EndArray => tally.ExitContainer(),
        JsonTokenType.PropertyName => tally,
        _ => tally.AddScalar(),
    };

    /// <summary>
    /// 讀取器只檢查 \u 後面是不是四個十六進位數字，不檢查代理字元有沒有成對；這種字串要到取值時才丟例外，
    /// 等於把問題留給後面的階段。所以有跳脫的字串在掃描時就先取一次值。
    /// </summary>
    private static bool HasUnpairedEscapedSurrogate(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.PropertyName) || !reader.ValueIsEscaped)
        {
            return false;
        }

        try
        {
            _ = reader.GetString();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static long LineOf(byte[] utf8, long offset) => utf8.AsSpan(0, (int)offset).Count((byte)'\n') + 1;
}
