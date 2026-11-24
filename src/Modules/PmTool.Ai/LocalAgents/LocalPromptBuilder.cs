using System.Text;
using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;

namespace PmTool.Ai.LocalAgents;

/// <summary>
/// 本機的引擎收到的系統提示與對話（internal/HANDOFF-local-agent.md 第 2、4.3 節）。每一則都把整段對話重送，
/// 不靠引擎自己記的工作階段：換電腦、換引擎、中間用過 API 模型都一樣。
/// </summary>
internal static class LocalPromptBuilder
{
    private const string Header = "以下是這則對話到目前為止的內容（舊的在前）。請只回答最後一則「使用者」的訊息。";

    private const string UserLabel = "使用者：";

    private const string AssistantLabel = "助理：";

    /// <summary>內建助理的系統提示，加上這個專案與權限的一句說明、本機用 MCP 工具的說法。</summary>
    public static string SystemPrompt(
        string projectName, string projectKey, IReadOnlyList<string> permissions, string? itemKey = null) =>
        string.Join(
            "\n\n",
            AssistantPrompt.SystemPrompt,
            AssistantPrompt.ProjectNote(projectName, projectKey, permissions),
            AssistantPrompt.ReadResource("local-agent.md").Trim(),
            itemKey is not null ? AssistantPrompt.ItemNote(itemKey) : string.Empty).TrimEnd();

    /// <summary>主機代理（組織來源）的版本：工具的說法和本機相同，只是引擎跑在 Kumi 主機上、代替發問的人操作，不提「你的電腦」。</summary>
    public static string HostSystemPrompt(
        string projectName, string projectKey, IReadOnlyList<string> permissions, string? itemKey = null) =>
        string.Join(
            "\n\n",
            AssistantPrompt.SystemPrompt,
            AssistantPrompt.ProjectNote(projectName, projectKey, permissions),
            AssistantPrompt.ReadResource("host-agent.md").Trim(),
            itemKey is not null ? AssistantPrompt.ItemNote(itemKey) : string.Empty).TrimEnd();

    /// <summary>
    /// 對話排成文字：「使用者：…」「助理：…」一則一段，工具只寫「（用了 get_blueprint）」，最後一則是這次的訊息。
    /// 超過 <see cref="AiRules.MaxToolResultLength"/> 時從最舊的截掉並註明。
    /// </summary>
    public static string Prompt(IReadOnlyList<ChatMessage> history, string userText)
    {
        var turns = TurnsOf(history);
        var last = UserLabel + userText;
        var dropped = 0;
        while (turns.Count > 0 && Length(turns, last) > AiRules.MaxToolResultLength)
        {
            turns.RemoveAt(0);
            dropped++;
        }

        var builder = new StringBuilder(Header).Append("\n\n");
        if (dropped > 0)
        {
            builder.Append($"（較早的 {dropped} 段對話因為太長，省略了。）\n\n");
        }

        foreach (var turn in turns)
        {
            builder.Append(turn).Append("\n\n");
        }

        return builder.Append(last).ToString();
    }

    private static int Length(List<string> turns, string last) => turns.Sum(t => t.Length + 2) + last.Length + Header.Length + 2;

    // 工具的來回（助理的呼叫、工具的結果）在 transcript 裡是好幾則訊息，連續的助理訊息併成一段。
    private static List<string> TurnsOf(IReadOnlyList<ChatMessage> history)
    {
        var turns = new List<string>();
        string? currentLabel = null;
        var current = new StringBuilder();
        foreach (var message in history)
        {
            if (message.Role == ChatRole.Tool)
            {
                continue;
            }

            var label = message.Role == ChatRole.User ? UserLabel : AssistantLabel;
            var text = TextOf(message);
            if (text.Length == 0)
            {
                continue;
            }

            if (label != currentLabel)
            {
                Close(turns, currentLabel, current);
                currentLabel = label;
            }

            current.Append(current.Length == 0 ? string.Empty : "\n").Append(text);
        }

        Close(turns, currentLabel, current);
        return turns;
    }

    private static void Close(List<string> turns, string? label, StringBuilder current)
    {
        if (label is not null && current.Length > 0)
        {
            turns.Add(label + current);
        }

        current.Clear();
    }

    private static string TextOf(ChatMessage message)
    {
        var parts = new List<string>();
        foreach (var content in message.Contents)
        {
            switch (content)
            {
                case TextContent { Text.Length: > 0 } text:
                    parts.Add(text.Text);
                    break;
                case FunctionCallContent call:
                    parts.Add($"（用了 {call.Name}）");
                    break;
            }
        }

        return string.Join("\n", parts);
    }
}
