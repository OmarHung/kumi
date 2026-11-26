using System.Text.Json;
using System.Text.Json.Serialization;

namespace PmTool.Ai.Assistant.Transcript;

/// <summary>
/// 存進 <c>ai.messages.transcript</c> 的一則訊息，和供應商無關（internal/HANDOFF-ai-assistant.md 第 5.4 節）。
/// role 是 user、assistant、tool。不存模型的思考。
/// </summary>
internal sealed record TranscriptMessage(string Role, IReadOnlyList<TranscriptContent> Contents)
{
    public const string UserRole = "user";

    public const string AssistantRole = "assistant";

    public const string ToolRole = "tool";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TranscriptText), "text")]
[JsonDerivedType(typeof(TranscriptCall), "call")]
[JsonDerivedType(typeof(TranscriptResult), "result")]
[JsonDerivedType(typeof(TranscriptAttachment), "attachment")]
internal abstract record TranscriptContent;

internal sealed record TranscriptText(string Text) : TranscriptContent;

/// <param name="CallId">這則回覆裡自己的編號（c1、c2…），不是供應商給的：各家格式不同，換了供應商再送回去會被拒。</param>
/// <param name="Arguments">模型給的參數（JSON 物件），原樣送回去。</param>
internal sealed record TranscriptCall(string CallId, string Name, JsonElement Arguments) : TranscriptContent;

/// <param name="Result">工具回的 JSON 字串。</param>
internal sealed record TranscriptResult(string CallId, string Result) : TranscriptContent;

/// <summary>
/// 使用者的訊息帶的附件，放在文字之後。只存參照：內容在儲存空間，重送給模型時才讀（<c>AttachmentReplay</c>）。
/// 檔名、類型與種類存一份，之後附件的資料列刪了（對話刪掉才會）也不影響這裡；種類是 <c>AttachmentKinds</c> 的固定文字。
/// </summary>
internal sealed record TranscriptAttachment(Guid Id, string FileName, string ContentType, string Kind, long SizeBytes) : TranscriptContent;
