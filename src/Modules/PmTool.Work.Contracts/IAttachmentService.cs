using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 項目的附件（PM-5，internal/HANDOFF-pm5-attachments.md）。資料庫只記檔案的資訊，檔案本身放在儲存空間
/// （本機是一個資料夾，雲端是 Google Cloud Storage；設定 Attachments:Provider）。
/// 權限跟著項目：看得到項目（item.read）就能列出與下載；新增與刪除要 item.update（封存的專案擋下，<see cref="WorkErrorCodes.ProjectArchived"/>）。
/// 刪除的項目讀不到它的附件（NotFound）；項目復原後附件照舊。看不到的項目、別的組織的附件一律 NotFound。
/// </summary>
public interface IAttachmentService
{
    /// <summary>
    /// 上傳一個檔案到項目。先確認權限與數量（不合格就不讀內容），再把內容寫進儲存空間、邊寫邊算大小與 SHA-256，
    /// 超過 <see cref="AttachmentRules.MaxBytes"/> 時中止並刪掉寫了一半的檔案（<see cref="WorkErrorCodes.AttachmentTooLarge"/>）；
    /// 寫完才在一個交易裡記下附件、留一筆 <see cref="ActivityKind.AttachmentAdded"/> 活動、寫一筆 item_attachment_added 事件。
    /// 交易失敗時盡量刪掉已經寫進儲存空間的檔案。
    /// 一個項目最多 <see cref="AttachmentRules.MaxPerItem"/> 個（<see cref="WorkErrorCodes.AttachmentLimitReached"/>）。
    /// 檔名的規則見 <see cref="AttachmentRules"/>，不合格是驗證錯誤（<see cref="WorkErrorCodes.AttachmentNameInvalid"/>，路徑 fileName）。
    /// </summary>
    Task<Result<Attachment>> AddAsync(Actor actor, string itemKey, NewAttachment upload, CancellationToken cancellationToken);

    /// <summary>項目的附件，先上傳的在前。</summary>
    Task<Result<IReadOnlyList<Attachment>>> ListAsync(Actor actor, string itemKey, CancellationToken cancellationToken);

    /// <summary>打開附件的內容來讀。呼叫端負責 Dispose 回傳的串流。儲存空間裡找不到檔案時是 NotFound（並記錯誤：資料庫與儲存空間對不上）。</summary>
    Task<Result<AttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除附件：不能復原（資料列與儲存空間的檔案都刪掉；備份裡的照舊留著）。需要 item.update。
    /// 留一筆 <see cref="ActivityKind.AttachmentRemoved"/> 活動、寫一筆 item_attachment_removed 事件；刪儲存空間的檔案在交易提交之後，失敗只記 Warning。
    /// </summary>
    Task<Result<Attachment>> DeleteAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken);
}

public static class AttachmentRules
{
    /// <summary>一個檔案最大 25 MiB：Cloud Run 的請求上限是 32 MiB，留給 multipart 的其他內容。</summary>
    public const long MaxBytes = 25L * 1024 * 1024;

    /// <summary>一個項目最多幾個附件。</summary>
    public const int MaxPerItem = 100;

    /// <summary>
    /// 檔名去掉前後空白後 1 到 255 個字；不能有控制字元、格式字元（零寬、方向覆寫）、路徑分隔（/ \）與 NUL；不能是 . 或 ..。
    /// 檔名只是顯示與下載時用的名字，儲存空間裡的鍵是附件的編號，不會用到檔名。
    /// </summary>
    public const int MaxFileNameLength = 255;

    /// <summary>瀏覽器回報的類型最多這麼長；不像類型（type/subtype）的當成 application/octet-stream。</summary>
    public const int MaxContentTypeLength = 200;
}

/// <summary>要上傳的檔案。<see cref="Content"/> 由呼叫端提供、負責 Dispose；服務只讀到結束或超過上限。</summary>
public sealed record NewAttachment
{
    public required string FileName { get; init; }

    /// <summary>瀏覽器或 CLI 報的類型，只拿來決定下載時怎麼呈現（見 API 的下載端點），不信任它。</summary>
    public string? ContentType { get; init; }

    public required Stream Content { get; init; }
}

/// <param name="Id">附件的編號（UUIDv7），也是儲存空間裡的鍵。</param>
/// <param name="ItemKey">附件所在的項目編號，例如 WEB-12。</param>
/// <param name="ContentType">記下來的類型（已經整理過，不像類型的是 application/octet-stream）。</param>
/// <param name="Sha256">內容的 SHA-256（小寫十六進位），給 CLI 與備份核對用。</param>
public sealed record Attachment(
    Guid Id,
    string ItemKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid UploadedBy,
    DateTimeOffset UploadedAt);

/// <summary>附件與它的內容。呼叫端讀完要 Dispose <see cref="Content"/>。</summary>
public sealed record AttachmentContent(Attachment Attachment, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
