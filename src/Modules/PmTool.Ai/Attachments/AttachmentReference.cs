using Microsoft.Extensions.AI;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 還原歷史時留在訊息裡的附件參照：交易裡不讀檔，交易之後、呼叫模型之前由 <see cref="AttachmentReplay"/> 換成真正的內容
/// （圖片與 PDF 是 <see cref="DataContent"/>、文字檔是 <see cref="TextContent"/>，或一行說明）。
/// 沒換掉就送給供應商的話，轉接層會當成不認得的內容丟掉，所以每一條回覆的路都要經過 <see cref="AttachmentReplay"/>。
/// </summary>
internal sealed class AttachmentReference(Guid id, string fileName, string contentType, AssistantAttachmentKind kind, long sizeBytes) : AIContent
{
    public Guid Id { get; } = id;

    public string FileName { get; } = fileName;

    public string ContentType { get; } = contentType;

    public AssistantAttachmentKind Kind { get; } = kind;

    public long SizeBytes { get; } = sizeBytes;
}
