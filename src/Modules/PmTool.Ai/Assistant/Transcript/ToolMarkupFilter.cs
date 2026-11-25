using System.Text;
using System.Text.RegularExpressions;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>
/// 拿掉模型當成文字吐出來的工具呼叫標記（<c>&lt;invoke name="get_item"&gt;…&lt;/invoke&gt;</c>）。模型同時呼叫好幾個工具時，
/// 偶爾第一個正常、後面幾個變成回覆的文字（2026-11-26 在本機代理的 Claude Code 上看到）：使用者看到亂碼，存進 transcript 之後
/// 模型下一則又讀到自己吐過這種東西。提示管不到這種生成時的格式錯亂，只能在收文字的地方濾掉。
/// 只認 Kumi 自己的工具（<see cref="LocalAgentTools.Allowed"/>，可以帶 <c>mcp__kumi__</c> 前綴、名稱用單或雙引號），加上
/// <c>function_calls</c> 的外框與零散的 <c>&lt;/invoke&gt;</c>：使用者貼的、或模型拿來解釋的其他 XML 不動。
/// 思考寫進回覆那一類（PI-03）分不出正常的句子，照舊只靠提示。
/// 串流時文字一小段一小段來，可能是標記開頭的 <c>&lt;</c> 先留著，確定了才交出去或丟掉；一段文字結束（接著是工具或整則結束）時呼叫
/// <see cref="Flush"/>。一則回覆用一個，不是執行緒安全的。
/// </summary>
internal sealed partial class ToolMarkupFilter
{
    /// <summary>
    /// 看得出是 Kumi 的工具、卻一直沒有 <c>&lt;/invoke&gt;</c> 時最多留多少字。正常的標記只有幾百字；超過就當作模型只吐了開頭，
    /// 拿掉開頭的標籤、後面照常交出去：不然串流會卡住，之後寫的真正回答也會跟著被丟掉。
    /// </summary>
    public const int MaxHeldLength = 4000;

    private const string McpPrefix = "mcp__kumi__";

    /// <summary>一個標籤最長多少字；超過還沒看到 <c>&gt;</c> 就當成一般的文字。</summary>
    private const int MaxTagLength = 200;

    /// <summary>命名空間（<c>x:invoke</c> 的 <c>x</c>）最長多少字：只有識別字的一串，超過就不可能是標記。</summary>
    private const int MaxNamespaceLength = 32;

    private static readonly HashSet<string> KumiTools = new(LocalAgentTools.Allowed, StringComparer.Ordinal);

    private readonly StringBuilder _held = new();
    private bool _skippingWhitespace;
    private bool _skippedNewline;

    // 照原樣交出去的 <invoke …>（不是 Kumi 的工具）還沒看到結尾的有幾個：它們的 </invoke> 要留著。
    private int _keptInvokes;

    /// <summary>收一段文字，回傳現在可以交出去的部分（可能是空的）。</summary>
    public string Push(string delta)
    {
        _held.Append(delta);
        return Drain(isFinal: false);
    }

    /// <summary>這一段文字結束：留著的交出去（沒有結尾的 Kumi 工具標記丟掉），下一段從頭開始。</summary>
    public string Flush()
    {
        var output = Drain(isFinal: true);
        _held.Clear();
        _skippingWhitespace = false;
        _keptInvokes = 0;
        return output;
    }

    private string Drain(bool isFinal)
    {
        var text = _held.ToString();
        var output = new StringBuilder();
        var index = 0;
        while (index < text.Length)
        {
            if (_skippingWhitespace && SkipsAfterMarkup(text[index]))
            {
                index++;
                continue;
            }

            _skippingWhitespace = false;
            var open = text.IndexOf('<', index);
            if (open < 0)
            {
                output.Append(text, index, text.Length - index);
                index = text.Length;
                break;
            }

            output.Append(text, index, open - index);
            index = open;
            var decision = Decide(text, index, isFinal);
            if (decision.Kind == DecisionKind.Hold)
            {
                break;
            }

            if (decision.Kind == DecisionKind.Keep)
            {
                output.Append('<');
                index++;
                continue;
            }

            index += decision.Length;
            _skippingWhitespace = true;
            _skippedNewline = false;
        }

        _held.Clear().Append(text, index, text.Length - index);
        return output.ToString();
    }

    /// <summary>
    /// 拿掉的標記後面緊接的空格與一個換行一起拿掉，前後的話才接得起來；第二個換行留著，段落之間的空行不會不見。
    /// </summary>
    private bool SkipsAfterMarkup(char c)
    {
        if (c is ' ' or '\t' or '\r')
        {
            return true;
        }

        if (c != '\n' || _skippedNewline)
        {
            return false;
        }

        _skippedNewline = true;
        return true;
    }

    private Decision Decide(string text, int start, bool isFinal)
    {
        var available = text.Length - start;
        var close = text.IndexOf('>', start, Math.Min(available, MaxTagLength));
        if (close < 0)
        {
            var mayGrow = !isFinal && available < MaxTagLength && CouldBeMarkup(text.AsSpan(start));
            return mayGrow ? Decision.Hold : Decision.Keep;
        }

        var openLength = close - start + 1;
        var tag = TagPattern().Match(text, start, openLength);
        if (!tag.Success)
        {
            return Decision.Keep;
        }

        var isClosing = tag.Groups["slash"].Length > 0;
        var attributes = tag.Groups["attributes"].Value;
        return tag.Groups["element"].Value switch
        {
            "function_calls" => Decision.Remove(openLength),
            "invoke" when isClosing => DecideStrayClose(openLength),
            "invoke" => DecideInvoke(text, start, openLength, attributes, isFinal),
            _ => Decision.Keep,
        };
    }

    private Decision DecideStrayClose(int length)
    {
        if (_keptInvokes == 0)
        {
            return Decision.Remove(length);
        }

        _keptInvokes--;
        return Decision.Keep;
    }

    private Decision DecideInvoke(string text, int start, int openLength, string attributes, bool isFinal)
    {
        var isSelfClosing = attributes.TrimEnd().EndsWith('/');
        if (!IsKumiTool(attributes))
        {
            // 自己結尾的沒有對應的 </invoke>，不能讓它保護後面零散的那個。
            _keptInvokes += isSelfClosing ? 0 : 1;
            return Decision.Keep;
        }

        if (isSelfClosing)
        {
            return Decision.Remove(openLength);
        }

        var searchFrom = start + openLength;
        var searchLength = Math.Min(text.Length - searchFrom, MaxHeldLength);
        var end = InvokeClosePattern().Match(text, searchFrom, searchLength);
        if (end.Success)
        {
            return Decision.Remove(end.Index + end.Length - start);
        }

        // 看得出是 Kumi 的工具呼叫、只是還沒看到結尾：等。整段結束了還沒有就整個丟掉；等太久就只拿掉開頭的標籤。
        if (text.Length - start > MaxHeldLength)
        {
            return Decision.Remove(openLength);
        }

        return isFinal ? Decision.Remove(text.Length - start) : Decision.Hold;
    }

    private static bool IsKumiTool(string attributes)
    {
        var name = NamePattern().Match(attributes);
        if (!name.Success)
        {
            return false;
        }

        var tool = name.Groups["name"].Value;
        return KumiTools.Contains(tool.StartsWith(McpPrefix, StringComparison.Ordinal) ? tool[McpPrefix.Length..] : tool);
    }

    /// <summary>還沒看到 <c>&gt;</c> 的一段，再多收幾個字有沒有可能變成要拿掉的標籤。</summary>
    private static bool CouldBeMarkup(ReadOnlySpan<char> rest)
    {
        var body = rest[1..];
        if (body.StartsWith("/"))
        {
            body = body[1..];
        }

        var colon = body.IndexOf(':');
        if (colon < 0 && body.Length <= MaxNamespaceLength && IsIdentifier(body))
        {
            return true;
        }

        if (colon > 0 && colon <= MaxNamespaceLength && IsIdentifier(body[..colon]))
        {
            body = body[(colon + 1)..];
        }

        return "function_calls".AsSpan().StartsWith(body, StringComparison.Ordinal)
            || "invoke".AsSpan().StartsWith(body, StringComparison.Ordinal)
            || body.StartsWith("invoke ", StringComparison.Ordinal);
    }

    private static bool IsIdentifier(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"\G<(?<slash>/?)(?:[A-Za-z_][A-Za-z0-9_-]*:)?(?<element>function_calls|invoke|parameter)(?![A-Za-z0-9_-])(?<attributes>[^>]*)>$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("name\\s*=\\s*(?<quote>[\"'])(?<name>[^\"']*)\\k<quote>", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"</(?:[A-Za-z_][A-Za-z0-9_-]*:)?invoke>", RegexOptions.CultureInvariant)]
    private static partial Regex InvokeClosePattern();

    private enum DecisionKind
    {
        Hold,
        Keep,
        Remove,
    }

    private readonly record struct Decision(DecisionKind Kind, int Length)
    {
        public static Decision Hold => new(DecisionKind.Hold, 0);

        public static Decision Keep => new(DecisionKind.Keep, 0);

        public static Decision Remove(int length) => new(DecisionKind.Remove, length);
    }
}
