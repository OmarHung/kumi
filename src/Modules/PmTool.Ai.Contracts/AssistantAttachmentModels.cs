using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// AI 助理訊息的附件（internal/HANDOFF-ai-attachments.md）。先上傳（還沒送出的是「待送」），送訊息時用
/// <see cref="AssistantMessageInput.AttachmentIds"/> 帶上，存下使用者的訊息時綁到那一則。
/// 只有上傳的人看得到；綁好之後跟著對話（對話看不到、刪掉了，附件一律當作不存在）。
/// 檔案放在共用的儲存空間（<c>IFileStore</c>），容量算進組織與每人每日的上限（<c>StorageArea.AssistantAttachments</c>）。
/// 專案看不到、沒有用助理的權限時和開對話一樣回失敗。
/// </summary>
public interface IAssistantAttachmentService
{
    /// <summary>
    /// 上傳一個待送的附件。類型看內容判斷（不信任檔名與瀏覽器報的類型）：四種點陣圖、PDF、UTF-8 的文字檔，其他回
    /// <see cref="AiErrorCodes.AttachmentTypeNotAllowed"/>；依類型的大小上限見 <see cref="AssistantAttachmentRules"/>
    /// （<see cref="AiErrorCodes.AttachmentTooLarge"/>）。檔名的規則和項目的附件相同（<see cref="AiErrorCodes.AttachmentNameInvalid"/>）。
    /// 一個人的待送附件最多 <see cref="AssistantAttachmentRules.MaxPendingPerPerson"/> 個（<see cref="AiErrorCodes.TooManyAttachments"/>）。
    /// 先在自己的交易裡拿儲存空間的鎖再檢查容量（<see cref="AiErrorCodes.AttachmentQuotaExceeded"/>、<see cref="AiErrorCodes.AttachmentDailyQuotaExceeded"/>）。
    /// </summary>
    Task<Result<AssistantAttachment>> UploadAsync(
        Actor actor, string projectKey, NewAssistantAttachment upload, CancellationToken cancellationToken);

    /// <summary>刪掉自己一個待送的附件（使用者在輸入框按 ×）。已經送出的不能刪（跟著對話刪），一律 <see cref="AiErrorCodes.AttachmentNotFound"/>。</summary>
    Task<Result<Unit>> DeletePendingAsync(Actor actor, string projectKey, Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>打開附件來讀（自己待送的，或自己看得到的對話裡的）。呼叫端負責 Dispose。看不到一律 <see cref="AiErrorCodes.AttachmentNotFound"/>。</summary>
    Task<Result<AssistantAttachmentContent>> OpenAsync(Actor actor, string projectKey, Guid attachmentId, CancellationToken cancellationToken);
}

public enum AssistantAttachmentKind
{
    /// <summary>PNG、JPEG、GIF、WebP。送給 API 的模型時是圖片；本機與主機上的引擎讀不到。</summary>
    Image,

    /// <summary>PDF。送給 API 的模型時是文件；本機與主機上的引擎讀不到。</summary>
    Pdf,

    /// <summary>UTF-8 的文字檔（程式碼、Markdown、CSV、JSON、記錄…）。內容以文字接在訊息後面送出，所有來源都讀得到。</summary>
    Text,
}

/// <param name="Id">附件的編號（UUIDv7），也是儲存空間裡的鍵。</param>
/// <param name="FileName">整理過的檔名（只拿來顯示與下載）。</param>
/// <param name="ContentType">看內容判斷出來的類型：image/png、image/jpeg、image/gif、image/webp、application/pdf、text/plain。</param>
public sealed record AssistantAttachment(
    Guid Id, string FileName, string ContentType, long SizeBytes, AssistantAttachmentKind Kind, DateTimeOffset UploadedAt);

/// <summary>要上傳的檔案。<see cref="Content"/> 由呼叫端提供、負責 Dispose；服務只讀到結束或超過上限。</summary>
public sealed record NewAssistantAttachment
{
    public required string FileName { get; init; }

    /// <summary>瀏覽器報的類型，不信任它（類型看內容判斷）。</summary>
    public string? ContentType { get; init; }

    public required Stream Content { get; init; }
}

/// <summary>附件與它的內容。呼叫端讀完要 Dispose。</summary>
public sealed record AssistantAttachmentContent(AssistantAttachment Attachment, string Sha256, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>附件的上限。數字的理由見 internal/HANDOFF-ai-attachments.md 第 2 節。</summary>
public static class AssistantAttachmentRules
{
    /// <summary>一則訊息最多幾個附件。</summary>
    public const int MaxPerMessage = 5;

    /// <summary>一張圖片最大 5 MiB（Anthropic 的單張上限）。</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>一個 PDF 最大 10 MiB。</summary>
    public const long MaxPdfBytes = 10L * 1024 * 1024;

    /// <summary>一個文字檔最大 256 KiB（整份接在訊息裡送出，太大只是燒 token）。</summary>
    public const long MaxTextBytes = 256L * 1024;

    /// <summary>上傳時讀內容的上限（三種類型裡最大的）：讀完才知道是哪一種。</summary>
    public const long MaxUploadBytes = MaxPdfBytes;

    /// <summary>一則訊息的附件合計最大 15 MiB。</summary>
    public const long MaxBytesPerMessage = 15L * 1024 * 1024;

    /// <summary>
    /// 每一輪重送整段對話時，圖片與 PDF 合計最多送幾個位元組（新的優先）；超過的換成一行「之前附的檔案，這次沒有再送」的說明。
    /// 不然對話越長請求越大，最後超過供應商的請求上限（Anthropic 32 MB，base64 還要多三分之一）。
    /// </summary>
    public const long MaxReplayBinaryBytes = 15L * 1024 * 1024;

    /// <summary>每一輪重送整段對話時，文字檔的內容合計最多幾個字元（新的優先）；超過的同樣換成一行說明。</summary>
    public const int MaxReplayTextCharacters = 400_000;

    /// <summary>一個人最多同時有幾個待送的附件（上傳了還沒送出）。</summary>
    public const int MaxPendingPerPerson = 20;

    /// <summary>待送的附件多久沒送出就刪掉（AiMaintenanceWorker 每小時清）。</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(24);

    /// <summary>檔名去掉前後空白後 1 到 255 個字；規則和項目的附件相同（不能有控制字元、格式字元、路徑分隔、NUL，不能是 . 或 ..）。</summary>
    public const int MaxFileNameLength = 255;
}
