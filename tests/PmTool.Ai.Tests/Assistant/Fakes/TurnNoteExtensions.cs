using Microsoft.Extensions.AI;
using PmTool.Ai.Assistant;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>最新一則使用者訊息前面的 Kumi 附註（日期與建議現況）是另一段文字；只看使用者自己送的內容時把它拿掉。</summary>
internal static class TurnNoteExtensions
{
    public static IReadOnlyList<AIContent> WithoutNote(this IEnumerable<AIContent> contents) =>
        [.. contents.Where(c => c is not TextContent text || !text.Text.StartsWith(AssistantPrompt.TurnNotePrefix, StringComparison.Ordinal))];

    public static string PlainText(this ChatMessage message) =>
        string.Concat(message.Contents.WithoutNote().OfType<TextContent>().Select(t => t.Text));
}
