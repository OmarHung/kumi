using YamlDotNet.Core;

namespace PmTool.Blueprint.Documents;

/// <summary>
/// YAML 只用低階的 Parser 讀事件流，邊讀邊檢查邊建樹，任何一項不合格就停。
/// 不再用 YamlStream 載入第二次：檢查通過之後，事件流已經完整決定了這棵樹，YamlStream 只會多一次遞迴的解析。
/// </summary>
internal static class YamlDocumentReader
{
    public static DocumentReadResult Read(string text)
    {
        var parser = new Parser(new StringReader(text));
        var builder = new YamlTreeBuilder();
        while (true)
        {
            var failure = TryMoveNext(parser, out var hasEvent);
            if (failure is not null)
            {
                return DocumentReadResult.Failure(failure);
            }

            if (!hasEvent)
            {
                return builder.Finish();
            }

            var issue = builder.Accept(parser.Current!);
            if (issue is not null)
            {
                return DocumentReadResult.Failure(issue);
            }
        }
    }

    /// <summary>try 只包住解析器這一步，建樹的程式有錯時不會被誤報成語法錯誤。</summary>
    private static ValidationIssue? TryMoveNext(Parser parser, out bool hasEvent)
    {
        hasEvent = false;
        try
        {
            hasEvent = parser.MoveNext();
            return null;
        }
        catch (YamlException exception)
        {
            return DocumentIssues.YamlSyntax(exception.Start.Line, exception.Start.Column);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            // YamlDotNet 18.1.0 對某些不合法的輸入丟的不是 YamlException：\U 跳脫碼超出範圍丟 ArgumentOutOfRangeException，
            // 標籤不是合法的 URI 丟 ArgumentException，單獨的 \r 換行會讓掃描器丟 InvalidOperationException。
            // 都是模糊測試找到的（見 DocumentReaderFuzzTests）；只抓這兩類，其他例外照常往外丟。
            return DocumentIssues.YamlUnreadable();
        }
    }
}
