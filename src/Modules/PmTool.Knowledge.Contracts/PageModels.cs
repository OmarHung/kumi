namespace PmTool.Knowledge;

/// <summary>樹上的一頁（側欄的頁面樹用）：只有標題與位置，沒有內容。</summary>
/// <param name="Number">空間裡的編號（從 1 起，不重複使用），網址用它（/knowledge/HR/12）。</param>
/// <param name="ParentId">上層頁面；最上層是 null。</param>
/// <param name="Position">呼叫者看得到的同層頁面裡的順序，從 0 起連續（看不到的頁面不佔位置，所以不會洩漏有隱藏的頁面）。</param>
/// <param name="IsRestricted">這一頁自己設了限制（不含從上層繼承的）。</param>
public sealed record PageNode(
    Guid Id,
    int Number,
    Guid? ParentId,
    int Position,
    string Title,
    bool IsRestricted,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy);

/// <summary>
/// 一個空間裡呼叫者看得到的頁面（不含垃圾桶裡的）。排列是先序：上層在它的子頁面前面，同一層依 <see cref="PageNode.Position"/>。
/// 看得到的頁面它的上層一定也看得到（限制會往下繼承），所以這份清單一定接得成一棵樹。
/// </summary>
public sealed record PageTree(SpaceSummary Space, IReadOnlyList<PageNode> Pages);

/// <summary>指到一頁（麵包屑、垃圾桶、限制的來源）。</summary>
public sealed record PageReference(Guid Id, int Number, string Title);

/// <summary>一頁的完整內容。</summary>
/// <param name="Body">Markdown；空的頁面是空字串。</param>
/// <param name="Revision">內容（標題或內文）每改一次加一，從 1 起。修改與還原版本要帶它（<see cref="PageChanges.ExpectedRevision"/>）。搬移與改限制不算。</param>
/// <param name="Ancestors">從最上層到上一層，不含自己。</param>
/// <param name="RestrictedAncestors">上層裡自己設了限制的頁面（從最上層往下），這一頁的限制從它們繼承。</param>
/// <param name="VersionCount">版本數（第二段的版本歷史用）。</param>
public sealed record PageDetail(
    Guid Id,
    SpaceSummary Space,
    int Number,
    string Title,
    string Body,
    long Revision,
    IReadOnlyList<PageReference> Ancestors,
    bool IsRestricted,
    IReadOnlyList<PageReference> RestrictedAncestors,
    DateTimeOffset CreatedAt,
    Guid CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy,
    int VersionCount,
    PagePermissions MyPermissions);

/// <param name="CanEdit">同 <see cref="SpacePermissions.CanEdit"/>（看得到這一頁就適用）。</param>
/// <param name="CanComment">同 <see cref="SpacePermissions.CanComment"/>。</param>
/// <param name="CanDelete">CanEdit，而且底下沒有呼叫者看不到的頁面（<see cref="KnowledgeErrorCodes.PageHasHiddenDescendants"/>）。</param>
public sealed record PagePermissions(bool CanEdit, bool CanComment, bool CanDelete);

/// <param name="ParentNumber">上層頁面的編號；null 是最上層。</param>
/// <param name="Title">去掉頭尾空白後 1 到 <see cref="KnowledgeRules.MaxPageTitleLength"/> 個字，不能有換行與控制字元。</param>
/// <param name="Body">Markdown；null 是空的頁面。和修改同一套檢查。</param>
public sealed record NewPage(int? ParentNumber, string Title, string? Body)
{
    /// <summary>放在這個同層頁面的前面；null 是放在最後。</summary>
    public int? BeforeNumber { get; init; }
}

/// <summary>改內容。標題與內文都是 null 表示什麼都不改（照樣檢查 <see cref="ExpectedRevision"/>）。</summary>
/// <param name="Title">null 是不改。</param>
/// <param name="Body">null 是不改；空字串是清空。</param>
/// <param name="ExpectedRevision">呼叫者看到的 <see cref="PageDetail.Revision"/>；和現在的不同是 <see cref="KnowledgeErrorCodes.PageConflict"/>。</param>
public sealed record PageChanges(string? Title, string? Body, long ExpectedRevision);

/// <param name="ParentNumber">新的上層；null 是最上層。</param>
/// <param name="BeforeNumber">放在這個同層頁面的前面；null 是最後。</param>
public sealed record PageMove(int? ParentNumber, int? BeforeNumber);

/// <summary>刪除的結果：這一頁連同底下的頁面一起進垃圾桶，同一批。</summary>
/// <param name="PageCount">這一批有幾頁（含自己）。</param>
public sealed record PageDeletion(Guid DeletionId, PageReference Page, int PageCount);

/// <summary>垃圾桶裡的一批：一次刪除的那一頁（連同它底下的頁面）。</summary>
/// <param name="Page">刪除的那一頁（這一批的最上面）。</param>
/// <param name="PageCount">這一批有幾頁（含自己）。</param>
/// <param name="FormerParent">刪除時的上層；最上層是 null。</param>
public sealed record TrashEntry(
    Guid DeletionId,
    PageReference Page,
    int PageCount,
    PageReference? FormerParent,
    DateTimeOffset DeletedAt,
    Guid DeletedBy);

/// <summary>一頁的限制。</summary>
/// <param name="People">這一頁自己限制給哪些人看（依加入的順序）；沒有設是 null。</param>
/// <param name="RestrictedAncestors">從上層繼承來的限制（從最上層往下）；要同時在每一份名單裡才看得到。</param>
public sealed record PageRestriction(IReadOnlyList<Guid>? People, IReadOnlyList<PageReference> RestrictedAncestors)
{
    /// <summary>設定這個限制的人；沒有限制、或舊資料查不出來時是 null。只有他與空間的管理者能放寬。</summary>
    public Guid? RestrictedBy { get; init; }

    /// <summary>呼叫的人能不能放寬這一頁自己的限制（他是設定的人，或是空間的管理者）。</summary>
    public bool CanRelax { get; init; }
}

/// <summary>頁面上傳的一個附件。內文用 <c>![檔名](attachment:編號)</c> 或 <c>[檔名](attachment:編號)</c> 參照它。</summary>
/// <param name="Id">附件的編號（UUIDv7），也是儲存空間裡的鍵。</param>
/// <param name="ContentType">整理過的類型，不像類型的是 application/octet-stream。</param>
/// <param name="Sha256">小寫十六進位。</param>
public sealed record PageAttachment(
    Guid Id,
    Guid PageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    Guid UploadedBy,
    DateTimeOffset UploadedAt);

/// <summary>要上傳的檔案。<see cref="Content"/> 由呼叫端提供、負責 Dispose。</summary>
public sealed record NewPageAttachment
{
    public required string FileName { get; init; }

    /// <summary>只拿來決定下載時怎麼呈現，不信任它。</summary>
    public string? ContentType { get; init; }

    public required Stream Content { get; init; }
}

/// <summary>附件與它的內容。呼叫端讀完要 Dispose。</summary>
public sealed record PageAttachmentContent(PageAttachment Attachment, Stream Content) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <param name="Cursor">上一頁回傳的 <see cref="TrashPage.NextCursor"/>；第一頁是 null。</param>
/// <param name="Limit">1～<see cref="KnowledgeRules.MaxTrashPageSize"/>。</param>
public sealed record TrashQuery(string? Cursor = null, int Limit = KnowledgeRules.DefaultTrashPageSize);

/// <param name="Batches">垃圾桶裡的批數。</param>
/// <param name="MaxBatches">批數的上限。</param>
public sealed record TrashCapacity(int Batches, int MaxBatches);

/// <param name="Capacity">只有空間的管理者才有（他看得到全部）；其他人是 null，免得從批數知道有看不到的批。</param>
public sealed record TrashPage(IReadOnlyList<TrashEntry> Items, string? NextCursor, TrashCapacity? Capacity);

/// <summary>
/// 永久刪除的範圍一定要明講：<see cref="Batch"/> 是立刻永久刪除這一批；<see cref="Expired"/> 是清掉刪除超過
/// <see cref="KnowledgeRules.TrashPurgeAge"/> 的批（一次最多 <see cref="KnowledgeRules.MaxBatchesPerPurge"/> 批、
/// <see cref="KnowledgeRules.MaxPagesPerPurge"/> 頁）。沒有「什麼都不帶」的預設。
/// </summary>
public sealed record TrashPurgeRequest
{
    private TrashPurgeRequest(Guid? deletionId) => DeletionId = deletionId;

    /// <summary>要永久刪除的那一批；null 是「清掉到期的」。</summary>
    public Guid? DeletionId { get; }

    public static TrashPurgeRequest Batch(Guid deletionId) => new(deletionId);

    public static TrashPurgeRequest Expired() => new((Guid?)null);
}

/// <param name="HasMoreExpired">這次沒清到、但已經到期而且清得掉的批還有沒有（一次的量有上限）。</param>
public sealed record TrashPurge(int PurgedBatches, int PurgedPages, int PurgedAttachments, int RemainingBatches, bool HasMoreExpired = false);
