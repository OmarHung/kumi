using System.Text.Json;
using PmTool.Cli.Api;

namespace PmTool.Cli.Agent;

/// <summary>
/// Claude Code 的 <c>--output-format stream-json --include-partial-messages</c>。
/// 一行一個事件：system/init（檢查工具與 MCP）、stream_event（文字）、assistant（工具開始）、user（工具結束）、result（結束）。
/// </summary>
internal sealed class ClaudeCodeTranslator : IEngineTranslator
{
    public const string McpPrefix = "mcp__kumi__";
    public const string NotLoggedInMessage = "這台電腦的 Claude Code 沒有登入，請在終端機執行 claude 登入。";
    private const string BuiltInToolsMessage = "本機的 Claude Code 沒有照預期關掉內建工具，已經停下。";
    private const string NoInitMessage = "本機的 Claude Code 沒有先回報工具清單就開始動作，已經停下。";
    private const string McpMessage = "本機的 Claude Code 連不上 Kumi 的 MCP，已經停下。";

    private readonly Dictionary<string, string> _toolNames = [];
    private JobOutcome? _outcome;
    private bool _textSeen;
    private bool _breakBeforeNextText;
    private bool _initAccepted;

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

        var root = document.RootElement;
        var type = root.Text("type");
        // 沒看到合格的 init 之前，引擎做什麼都不可信：init 是唯一確認「內建工具真的關了」的地方。
        if (!_initAccepted && type is "assistant" or "stream_event" or "result" or "user")
        {
            return TranslatorStep.Stop(NoInitMessage);
        }

        return type switch
        {
            "system" => OnSystem(root),
            "stream_event" => OnStreamEvent(root),
            "assistant" => OnAssistant(root),
            "user" => OnUser(root),
            "result" => OnResult(root),
            _ => TranslatorStep.None,
        };
    }

    public JobOutcome Conclude(int exitCode, string standardError)
    {
        if (_outcome is not null)
        {
            return _outcome;
        }

        if (standardError.Contains("Not logged in", StringComparison.OrdinalIgnoreCase))
        {
            return JobOutcome.Failed(NotLoggedInMessage);
        }

        var reason = ErrorText.Sanitize(LastLine(standardError));
        return JobOutcome.Failed(exitCode == 0
            ? "本機的 Claude Code 沒有回報結果就結束了。"
            : $"本機的 Claude Code 結束了（代碼 {exitCode}）{(reason.Length > 0 ? $"：{reason}" : "。")}");
    }

    private TranslatorStep OnSystem(JsonElement root)
    {
        if (root.Text("subtype") != "init")
        {
            return TranslatorStep.None;
        }

        // 規格：工具只能有 Kumi 的（或空的），Kumi 的 MCP 要連上了；不是就不讓它繼續，免得它拿著使用者電腦上的工具讀到設定裡的提示注入。
        // 欄位不存在、型別不對都當不合格：不能因為格式變了就放行。技能與斜線指令在 --disable-slash-commands 之後應該是空的。
        if (root.Get("tools") is not { ValueKind: JsonValueKind.Array } tools || root.Get("mcp_servers") is not { ValueKind: JsonValueKind.Array } servers)
        {
            return TranslatorStep.Stop(BuiltInToolsMessage);
        }

        if (tools.EnumerateArray().Any(tool => !(ToolName(tool) ?? string.Empty).StartsWith(McpPrefix, StringComparison.Ordinal))
            || !IsEmptyIfPresent(root, "skills") || !IsEmptyIfPresent(root, "slash_commands"))
        {
            return TranslatorStep.Stop(BuiltInToolsMessage);
        }

        if (!servers.EnumerateArray().Any(server => server.Text("name") == "kumi" && server.Text("status") == "connected"))
        {
            return TranslatorStep.Stop(McpMessage);
        }

        _initAccepted = true;
        return TranslatorStep.None;
    }

    /// <summary>欄位有就要是空陣列；沒有（舊版本不送）算合格，型別不對或有東西都不行。</summary>
    private static bool IsEmptyIfPresent(JsonElement root, string name) =>
        !root.TryGetProperty(name, out var value) || (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);

    private static string? ToolName(JsonElement tool) =>
        tool.ValueKind == JsonValueKind.String ? tool.GetString() : tool.Text("name");

    private TranslatorStep OnStreamEvent(JsonElement root)
    {
        if (root.Get("parent_tool_use_id") is not null || root.Get("event") is not { } inner)
        {
            return TranslatorStep.None;
        }

        if (inner.Text("type") == "message_start" && _textSeen)
        {
            _breakBeforeNextText = true;
            return TranslatorStep.None;
        }

        if (inner.Text("type") != "content_block_delta" || inner.Get("delta") is not { } delta || delta.Text("type") != "text_delta"
            || delta.Text("text") is not { Length: > 0 } text)
        {
            return TranslatorStep.None;
        }

        // 每一輪的文字之間隔一段，不然「我先看藍圖」接著「結果是…」會黏在一起。
        var prefix = _breakBeforeNextText ? "\n\n" : string.Empty;
        _breakBeforeNextText = false;
        _textSeen = true;
        return TranslatorStep.Emit(new TextEvent(prefix + text));
    }

    private TranslatorStep OnAssistant(JsonElement root)
    {
        var events = new List<AgentEvent>();
        foreach (var block in root.Get("message")?.List("content") ?? [])
        {
            if (block.Text("type") != "tool_use" || block.Text("id") is not { } id || !_toolNames.TryAdd(id, StripPrefix(block.Text("name") ?? "unknown")))
            {
                continue;
            }

            events.Add(new ToolEvent(id, _toolNames[id], ToolStatus.Running));
        }

        return TranslatorStep.Emit([.. events]);
    }

    private TranslatorStep OnUser(JsonElement root)
    {
        var events = new List<AgentEvent>();
        foreach (var block in root.Get("message")?.List("content") ?? [])
        {
            if (block.Text("type") != "tool_result" || block.Text("tool_use_id") is not { } id || !_toolNames.TryGetValue(id, out var name))
            {
                continue;
            }

            var text = TranslatorSupport.TextOf(block.Get("content"));
            events.Add(block.Flag("is_error")
                ? new ToolEvent(id, name, ToolStatus.Failed, Error: TranslatorSupport.ToolError(text))
                : new ToolEvent(id, name, ToolStatus.Done, name == TranslatorSupport.ProposeChangeSet ? TranslatorSupport.ChangeSetNumber(text) : null,
                    ItemProposalId: name == TranslatorSupport.ProposeItemChanges ? TranslatorSupport.ItemProposalId(text) : null,
                    ProjectProposalId: name == TranslatorSupport.ProposeProject ? TranslatorSupport.ProjectProposalId(text) : null,
                    NewItemProposalId: name == TranslatorSupport.ProposeNewItems ? TranslatorSupport.NewItemProposalId(text) : null));
        }

        return TranslatorStep.Emit([.. events]);
    }

    private TranslatorStep OnResult(JsonElement root)
    {
        var usage = root.Get("usage");
        var input = (usage?.Number("input_tokens") ?? 0) + (usage?.Number("cache_read_input_tokens") ?? 0)
            + (usage?.Number("cache_creation_input_tokens") ?? 0);
        var output = usage?.Number("output_tokens") ?? 0;
        var text = root.Text("result") ?? string.Empty;

        if (root.Flag("is_error") || root.Text("subtype") is not (null or "success"))
        {
            var reason = FailureReason(root, text);
            _outcome = new JobOutcome(JobStatus.Failed, reason, input, output);
            return new TranslatorStep([], _outcome);
        }

        _outcome = new JobOutcome(JobStatus.Complete, null, input, output);
        // 沒有逐字的串流（引擎版本不同）時，最後的全文是唯一的內容，不能漏掉。
        return new TranslatorStep(!_textSeen && text.Length > 0 ? [new TextEvent(text)] : [], _outcome);
    }

    private static string FailureReason(JsonElement root, string text)
    {
        var raw = text.Length > 0 ? text : root.List("errors").Select(error => error.ValueKind == JsonValueKind.String ? error.GetString() : error.Text("message")).FirstOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? string.Empty;
        if (raw.Contains("Not logged in", StringComparison.OrdinalIgnoreCase) || raw.Contains("/login", StringComparison.Ordinal))
        {
            return NotLoggedInMessage;
        }

        var clean = ErrorText.Sanitize(ErrorText.FirstParagraph(raw));
        if (clean.Length > 0)
        {
            return clean;
        }

        return root.Text("subtype") == "error_max_turns"
            ? "引擎來回的次數超過上限，已經停下。"
            : "本機的 Claude Code 回報失敗。";
    }

    private static string StripPrefix(string name) =>
        name.StartsWith(McpPrefix, StringComparison.Ordinal) ? name[McpPrefix.Length..] : name;

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
}
