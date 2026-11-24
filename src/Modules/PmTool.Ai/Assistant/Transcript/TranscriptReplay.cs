using System.Text.Json;
using Microsoft.Extensions.AI;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>把對話裡之前每一則的 transcript 還原成要送給模型的訊息。</summary>
internal static class TranscriptReplay
{
    public static IReadOnlyList<ChatMessage> ToChatMessages(IEnumerable<AiMessageRow> rows) =>
        [.. rows.OrderBy(row => row.Sequence).SelectMany(ToChatMessages)];

    private static IEnumerable<ChatMessage> ToChatMessages(AiMessageRow row)
    {
        var messages = AssistantJson.Deserialize<List<TranscriptMessage>>(row.Transcript) ?? [];
        return messages.Select(message => ToChatMessage(message, row.Sequence));
    }

    private static ChatMessage ToChatMessage(TranscriptMessage message, int sequence)
    {
        var role = message.Role switch
        {
            TranscriptMessage.UserRole => ChatRole.User,
            TranscriptMessage.AssistantRole => ChatRole.Assistant,
            TranscriptMessage.ToolRole => ChatRole.Tool,
            _ => throw new FormatException($"不認得的訊息角色：{message.Role}"),
        };
        return new ChatMessage(role, [.. message.Contents.Select(content => ToContent(content, sequence))]);
    }

    // 編號換成「這一則的序號加上呼叫的編號」：各家供應商的編號格式不同，也不能在整段對話裡重複。
    private static AIContent ToContent(TranscriptContent content, int sequence) => content switch
    {
        TranscriptText text => new TextContent(text.Text),
        TranscriptCall call => new FunctionCallContent(ReplayId(sequence, call.CallId), call.Name, ArgumentsOf(call.Arguments)),
        TranscriptResult result => new FunctionResultContent(ReplayId(sequence, result.CallId), result.Result),
        _ => throw new FormatException("不認得的訊息內容。"),
    };

    public static string ReplayId(int sequence, string callId) => $"m{sequence}{callId}";

    private static Dictionary<string, object?> ArgumentsOf(JsonElement arguments)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return values;
        }

        foreach (var property in arguments.EnumerateObject())
        {
            values[property.Name] = property.Value;
        }

        return values;
    }
}
