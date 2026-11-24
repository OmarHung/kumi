using System.Text.Json.Nodes;
using YamlDotNet.Core.Events;

namespace PmTool.Blueprint.Documents;

/// <summary>
/// 把 YAML 解析器的事件一個一個接成 JsonNode 樹，同時檢查所有限制；回傳問題時，呼叫端就停止讀取。
/// 不用 YamlStream 載入：它是遞迴的，深層巢狀會讓堆疊溢位，而堆疊溢位抓不到，整個程序會死。
/// 這裡用明確的堆疊，事件流本身又是迭代式的，巢狀多深都不會遞迴。
/// </summary>
internal sealed class YamlTreeBuilder
{
    private readonly Stack<JsonNode> _open = new();
    private NodeTally _tally;
    private int _documentCount;
    private JsonNode? _root;

    // mapping 讀到鍵、還在等值的時候，鍵暫放在這裡，值一接上去就清掉。只要一個欄位就夠：
    // 子容器在開始時就接到父 mapping 上，等子容器結束，父 mapping 一定又在等下一個鍵。
    private string? _pendingKey;

    private bool IsExpectingKey => _pendingKey is null && _open.TryPeek(out var top) && top is JsonObject;

    public ValidationIssue? Accept(ParsingEvent parsingEvent) => parsingEvent switch
    {
        DocumentStart start => StartDocument(start),
        AnchorAlias alias => DocumentIssues.YamlAliasNotAllowed(alias.Start.Line),
        NodeEvent node when !node.Anchor.IsEmpty => DocumentIssues.YamlAliasNotAllowed(node.Start.Line),
        NodeEvent node when !node.Tag.IsEmpty => DocumentIssues.YamlTagNotSupported(node.Start.Line),
        Scalar scalar => AcceptScalar(scalar),
        MappingStart mapping => Open(new JsonObject(), mapping),
        SequenceStart sequence => Open(new JsonArray(), sequence),
        MappingEnd or SequenceEnd => Close(),
        _ => null,
    };

    public DocumentReadResult Finish() =>
        _documentCount == 0
            ? DocumentReadResult.Failure(DocumentIssues.NoYamlDocument())
            : DocumentReadResult.Success(_root);

    private ValidationIssue? StartDocument(DocumentStart start)
    {
        _documentCount++;
        return _documentCount > 1 ? DocumentIssues.YamlMultipleDocuments(start.Start.Line) : null;
    }

    private ValidationIssue? AcceptScalar(Scalar scalar)
    {
        if (IsExpectingKey)
        {
            return AcceptKey(scalar);
        }

        _tally = _tally.AddScalar();
        var violation = _tally.FindViolation();
        if (violation is null)
        {
            Attach(YamlScalarResolver.Resolve(scalar));
        }

        return violation;
    }

    private ValidationIssue? AcceptKey(Scalar key)
    {
        // 鍵一律用寫出來的文字當屬性名稱，所以 1 與 "1" 會變成同一個名稱；重複要用轉換後的名稱判斷，
        // 也順便避免 JsonObject.Add 因為重複而丟例外。
        if (((JsonObject)_open.Peek()).ContainsKey(key.Value))
        {
            return DocumentIssues.YamlDuplicateKey(key.Start.Line);
        }

        _pendingKey = key.Value;
        return null;
    }

    private ValidationIssue? Open(JsonNode container, NodeEvent start)
    {
        if (IsExpectingKey)
        {
            return DocumentIssues.YamlKeyNotScalar(start.Start.Line);
        }

        _tally = _tally.EnterContainer();
        var violation = _tally.FindViolation();
        if (violation is null)
        {
            Attach(container);
            _open.Push(container);
        }

        return violation;
    }

    private ValidationIssue? Close()
    {
        _open.Pop();
        _tally = _tally.ExitContainer();
        _pendingKey = null;
        return null;
    }

    private void Attach(JsonNode? node)
    {
        if (!_open.TryPeek(out var parent))
        {
            _root = node;
        }
        else if (parent is JsonArray array)
        {
            array.Add(node);
        }
        else
        {
            ((JsonObject)parent).Add(_pendingKey!, node);
            _pendingKey = null;
        }
    }
}
