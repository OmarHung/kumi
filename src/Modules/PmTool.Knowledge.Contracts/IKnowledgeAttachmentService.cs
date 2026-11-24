using PmTool.Abstractions;

namespace PmTool.Knowledge;

/// <summary>
/// 頁面的附件（internal/HANDOFF-knowledge-base.md 第 4.6 節）。檔案和項目的附件放在同一個儲存空間（<c>PmTool.Storage</c>，設定照舊是 <c>Attachments:*</c>），
/// 鍵是附件的編號；資料庫只記檔案的資訊（<c>knowledge.page_attachments</c>）。
/// 看得到頁面就能列出與下載；上傳要能改那一頁。頁面在垃圾桶裡時讀不到（NotFound），拿回來後照舊。
/// 沒有刪除：舊的版本還參照著它們，刪了版本歷史就壞了。
/// </summary>
public interface IKnowledgeAttachmentService
{
    /// <summary>
    /// 上傳一個檔案到頁面。先確認權限與數量（不合格就不讀內容），再寫進儲存空間、邊寫邊算大小與 SHA-256，
    /// 超過 <see cref="KnowledgeRules.MaxAttachmentBytes"/> 時中止並刪掉寫了一半的檔案（<see cref="KnowledgeErrorCodes.AttachmentTooLarge"/>）；
    /// 寫完才記下附件，記錄失敗時盡量刪掉已經寫進去的檔案。一頁最多 <see cref="KnowledgeRules.MaxAttachmentsPerPage"/> 個
    /// （<see cref="KnowledgeErrorCodes.AttachmentLimitReached"/>）。檔名與類型的規則和項目的附件相同（<see cref="KnowledgeErrorCodes.AttachmentNameInvalid"/>，路徑 fileName）。
    /// 上傳不改頁面的 revision 與更新時刻：參照是使用者存檔時才寫進內文。
    /// </summary>
    Task<Result<PageAttachment>> AddAsync(
        Actor actor, string spaceKey, int pageNumber, NewPageAttachment upload, CancellationToken cancellationToken);

    /// <summary>頁面的附件，先上傳的在前。</summary>
    Task<Result<IReadOnlyList<PageAttachment>>> ListAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken);

    /// <summary>打開附件來讀。呼叫端負責 Dispose。儲存空間裡找不到檔案時是 NotFound（並記錯誤：資料庫與儲存空間對不上）。</summary>
    Task<Result<PageAttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken);
}
