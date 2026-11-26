using PmTool.Ai.Persistence;

namespace PmTool.Ai.Attachments;

internal static class AttachmentMapper
{
    public static AssistantAttachment ToContract(AiAttachmentRow row) =>
        new(row.Id, row.FileName, row.ContentType, row.SizeBytes, AttachmentKinds.Parse(row.Kind), row.UploadedAt);

    /// <summary>PostgreSQL 的 timestamptz 只存到微秒；不先截掉的話，回給呼叫端的時間會和之後讀回來的差一點點。</summary>
    public static DateTimeOffset Truncate(DateTimeOffset now) =>
        new(now.UtcTicks - (now.UtcTicks % TimeSpan.TicksPerMicrosecond), TimeSpan.Zero);

    /// <summary>儲存空間裡的鍵：附件的編號。</summary>
    public static string KeyOf(Guid id) => id.ToString("D");
}
