namespace PmTool.Ai.Persistence;

/// <summary>
/// <c>ai.attachments</c> 的一列：助理訊息的一個附件（internal/HANDOFF-ai-attachments.md 第 4 節）。檔案本身在 <c>IFileStore</c>，鍵是編號。
/// 待送：<see cref="BoundAt"/> 是 null；已送出：綁在使用者的那一則訊息上；孤兒：<see cref="BoundAt"/> 有值而 <see cref="MessageId"/> 是 null
/// （對話刪掉了，資料列留著等維護的工作先刪檔再刪列，容量才不會在檔案還在的時候就被放掉）。
/// </summary>
internal sealed record AiAttachmentRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    /// <summary>上傳的人。</summary>
    public required Guid PersonId { get; init; }

    public string? ProjectKey { get; init; }

    /// <summary>送出之後才有；對話刪掉時變回 null。</summary>
    public Guid? ConversationId { get; init; }

    public Guid? MessageId { get; init; }

    /// <summary>在那一則訊息裡的順序，從 0 開始。</summary>
    public int Position { get; init; }

    public required string FileName { get; init; }

    public required string ContentType { get; init; }

    /// <summary>image、pdf、text（<see cref="Attachments.AttachmentKinds"/>）。</summary>
    public required string Kind { get; init; }

    public required long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }

    public DateTimeOffset? BoundAt { get; init; }

    /// <summary>
    /// 供應商不收這個檔案（重送歷史時請求被判不合法、拿掉歷史裡的圖片與 PDF 重試才成功）：有值就不再重送，直接用一行說明。
    /// 不知道是哪一個壞，所以那一次請求裡的歷史圖片與 PDF 一起記。
    /// </summary>
    public DateTimeOffset? ReplaySkippedAt { get; init; }

    /// <summary>
    /// 維護清檔案失敗的時間：每批 500 筆依這個排序，刪不掉的排到最後，不會一直佔著前面的位置、卡住後面刪得掉的。
    /// </summary>
    public DateTimeOffset? SweepFailedAt { get; init; }
}
