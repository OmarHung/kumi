using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>Codex 的 <c>exec --json</c>：一行一個事件，工具與文字都包在 <c>item.*</c> 裡。</summary>
internal sealed class CodexTranslator : IEngineTranslator
{
    private const string KumiServer = "kumi";
    private const string ForbiddenToolMessage = "本機的 Codex 想用 Kumi 以外的工具，已經停下。";
    // 白名單：只認這幾種項目，其他（現在的 command_execution、file_change、web_search，與以後新增的）一律停下。
    private static readonly HashSet<string> AllowedItems = ["agent_message", "reasoning", "mcp_tool_call", "todo_list", "error"];

    private readonly Dictionary<string, string> _messageTexts = [];
    private readonly HashSet<string> _toolsStarted = [];
    private long _inputTokens;
    private long _outputTokens;
    private bool _usageSeen;
    private bool _textSeen;
    private bool _eventSeen;
    private string? _lastError;
    private JobOutcome? _outcome;

    public TranslatorStep Feed(string line)
    {
        if (_outcome is not null)
        {
            return TranslatorStep.None;
        }

        using var document = TranslatorSupport.TryParse(line);
        if (document is null)
        {
            return TranslatorStep.None;
        }

        _eventSeen = true;
        var root = document.RootElement;
        return root.Text("type") switch
        {
            "item.started" or "item.updated" or "item.completed" => root.Get("item") is { } item ? OnItem(root.Text("type")!, item) : TranslatorStep.None,
            "turn.completed" => OnTurnCompleted(root),
            "turn.failed" => OnTurnFailed(root),
            "error" => RememberError(root.Text("message")),
            _ => TranslatorStep.None,
        };
    }

    public JobOutcome Conclude(int exitCode, string standardError)
    {
        if (_outcome is not null)
        {
            return _outcome;
        }

        if (_lastError is not null)
        {
            return JobOutcome.Failed($"本機的 Codex 回報錯誤：{_lastError}");
        }

        if (exitCode == 0 && _textSeen)
        {
            // 結束代碼 0、有文字卻沒有 turn.completed：版本不同的 Codex 可能不送，內容是完整的。
            return new JobOutcome(JobStatus.Complete, null, _usageSeen ? _inputTokens : null, _usageSeen ? _outputTokens : null);
        }

        if (exitCode == 0)
        {
            return JobOutcome.Failed("本機的 Codex 沒有回報結果就結束了。");
        }

        var reason = ErrorText.Sanitize(StderrReason(standardError));
        return JobOutcome.Failed(!_eventSeen
            ? $"本機的 Codex 沒辦法執行（代碼 {exitCode}）{(reason.Length > 0 ? $"：{reason}" : "。")}"
            : $"本機的 Codex 結束了（代碼 {exitCode}）{(reason.Length > 0 ? $"：{reason}" : "。")}");
    }

    private TranslatorStep OnItem(string eventType, JsonElement item)
    {
        var type = item.Text("type");
        if (type is null || !AllowedItems.Contains(type) || (type == "mcp_tool_call" && item.Text("server") != KumiServer))
        {
            return TranslatorStep.Stop(ForbiddenToolMessage);
        }

        return type switch
        {
            "mcp_tool_call" => OnToolCall(eventType, item),
            "agent_message" => OnMessage(item),
            _ => TranslatorStep.None,
        };
    }

    private TranslatorStep OnToolCall(string eventType, JsonElement item)
    {
        if (item.Text("id") is not { } id || item.Text("tool") is not { } name)
        {
            return TranslatorStep.None;
        }

        var events = new List<AgentEvent>();
        if (_toolsStarted.Add(id))
        {
            events.Add(new ToolEvent(id, name, ToolStatus.Running));
        }

        if (eventType == "item.completed")
        {
            events.Add(item.Text("status") == "failed" || item.Get("error") is not null
                ? new ToolEvent(id, name, ToolStatus.Failed, Error: TranslatorSupport.ToolError(item.Get("error")?.Text("message") ?? string.Empty))
                : new ToolEvent(id, name, ToolStatus.Done, name == TranslatorSupport.ProposeChangeSet ? ChangeSetNumberOf(item) : null,
                    ItemProposalId: name == TranslatorSupport.ProposeItemChanges ? TranslatorSupport.ItemProposalId(TranslatorSupport.TextOf(item.Get("result")?.Get("content"))) : null));
        }

        return TranslatorStep.Emit([.. events]);
    }

    private static long? ChangeSetNumberOf(JsonElement item)
    {
        var result = item.Get("result");
        var text = TranslatorSupport.TextOf(result?.Get("content"));
        return TranslatorSupport.ChangeSetNumber(text)
            ?? result?.Get("structured_content")?.Number("number")
            ?? result?.Get("structuredContent")?.Number("number");
    }

    /// <summary>同一個 id 的訊息每次都帶全文：只送比上一次多出來的部分；不是接在後面的話，換行後送整段。</summary>
    private TranslatorStep OnMessage(JsonElement item)
    {
        if (item.Text("id") is not { } id || item.Text("text") is not { } text)
        {
            return TranslatorStep.None;
        }

        var isNewMessage = !_messageTexts.TryGetValue(id, out var previous);
        previous ??= string.Empty;
        _messageTexts[id] = text;
        if (text.Length == 0 || text == previous)
        {
            return TranslatorStep.None;
        }

        string delta;
        if (text.StartsWith(previous, StringComparison.Ordinal))
        {
            delta = text[previous.Length..];
            // 新的一則訊息接在前一則後面時隔一段，不要黏在一起。
            if (isNewMessage && _textSeen)
            {
                delta = "\n\n" + delta;
            }
        }
        else
        {
            delta = "\n" + text;
        }

        _textSeen = true;
        return TranslatorStep.Emit(new TextEvent(delta));
    }

    private TranslatorStep OnTurnCompleted(JsonElement root)
    {
        var usage = root.Get("usage");
        _usageSeen = usage is not null;
        _inputTokens += usage?.Number("input_tokens") ?? 0;
        _outputTokens += usage?.Number("output_tokens") ?? 0;
        _outcome = new JobOutcome(JobStatus.Complete, null, _usageSeen ? _inputTokens : null, _usageSeen ? _outputTokens : null);
        return new TranslatorStep([], _outcome);
    }

    private TranslatorStep OnTurnFailed(JsonElement root)
    {
        var message = root.Get("error")?.Text("message") ?? root.Text("message") ?? _lastError;
        _outcome = JobOutcome.Failed(ReasonOf(message));
        return new TranslatorStep([], _outcome);
    }

    /// <summary>
    /// error 事件不一定是結束：連線不穩時 Codex 會送「Reconnecting…」再重試。所以只記下，
    /// 真正失敗是 turn.failed，或是引擎結束了還沒有結果（見 <see cref="Conclude"/>）。
    /// </summary>
    private TranslatorStep RememberError(string? message)
    {
        var clean = ErrorText.Sanitize(ErrorText.FirstParagraph(message));
        _lastError = clean.Length > 0 ? clean : _lastError;
        return TranslatorStep.None;
    }

    private static string ReasonOf(string? message) =>
        ErrorText.Sanitize(ErrorText.FirstParagraph(message)) is { Length: > 0 } clean
            ? $"本機的 Codex 回報錯誤：{clean}"
            : "本機的 Codex 回報失敗。";

    /// <summary>Codex 的標準錯誤是一長串記錄；「Error:」開頭的最後一行才是原因，沒有就取第一行。</summary>
    private static string StderrReason(string standardError)
    {
        var lines = standardError.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.LastOrDefault(line => line.StartsWith("Error", StringComparison.OrdinalIgnoreCase)) ?? lines.FirstOrDefault() ?? string.Empty;
    }
}
