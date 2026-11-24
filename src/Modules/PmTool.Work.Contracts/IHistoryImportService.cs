using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 把別的系統（monday）的歷史搬進來：項目保留建立的人與時間、上下層、留言與回覆保留作者與時間、附件保留上傳的人與時間。
/// 和試算表匯入（<see cref="IItemImportService"/>）的分別：那是「現在由我建立一批項目」，這是「把過去發生的事照原樣記下來」。
/// <list type="bullet">
/// <item>可以重跑：每一筆都帶來源與外部編號（<see cref="HistorySource"/>），已經匯入過的略過（回報在 Skipped），不重複建立；
/// 第二次跑時只多建立新的項目、新的留言與附件。外部編號在組織裡是唯一的（同一個來源、同一種東西）。</item>
/// <item>不發任何通知（事件標 <see cref="ItemEventData.History"/>）；關注只照 <see cref="HistoryItem.Followers"/> 與負責人。</item>
/// <item>活動標 <see cref="ItemActivity.ImportedFrom"/>，時間軸看得出是搬過來的。</item>
/// <item>寫入的人（actor）要有 project.manage：可以替別人署名、指定過去的時間、直接設定狀態。封存的專案擋下（<see cref="WorkErrorCodes.ProjectArchived"/>）。</item>
/// <item>作者、建立的人、上傳的人、關注的人都要是這個專案的成員（任何角色；離職的人在網頁上建好、加進專案、再停用）。不是成員是那一筆的問題。</item>
/// </list>
/// </summary>
public interface IHistoryImportService
{
    /// <summary>
    /// 匯入一批項目，全部或全部不（已經匯入過的略過，不算問題）。每一列的規則和新增一樣（類型、標題、負責人是成員、週期與里程碑、自訂欄位的值…），
    /// 另外：狀態直接設定（要在那個類型的流程裡）、上層可以是同一批前面的列或之前匯入過的項目（<see cref="HistoryItem.ParentExternalId"/>）。
    /// 問題的路徑是 rows/&lt;索引&gt;/&lt;欄位&gt;，回 <see cref="WorkErrorCodes.ImportInvalid"/>。
    /// 每一列留一筆 Created 活動（作者是 CreatedBy、時間是 CreatedAt），寫一筆 item_created 事件（Imported 與 History 都是 true）；不留轉換的活動。
    /// 項目的 createdAt 與 updatedAt 都是 CreatedAt。
    /// </summary>
    Task<Result<HistoryItemsResult>> ImportItemsAsync(Actor actor, string projectKey, HistoryItems import, CancellationToken cancellationToken);

    /// <summary>
    /// 匯入一批留言與回覆，全部或全部不（已經匯入過的略過）。項目用外部編號指（要是之前匯入過、在這個專案裡的項目），
    /// 回覆指的那一則可以是同一批前面的或之前匯入過的（要在同一個項目）；指到回覆時存它的最上層那一則（回覆只有一層）。
    /// 內容的規則和一般留言一樣（長度、控制字元）。問題的路徑是 comments/&lt;索引&gt;/&lt;欄位&gt;，回 <see cref="WorkErrorCodes.ImportInvalid"/>。
    /// 每一則留一筆 Commented 活動（作者、時間照給的），寫一筆 item_commented 事件（History 是 true，不通知、不自動關注）。不改項目的 updatedAt。
    /// </summary>
    Task<Result<HistoryCommentsResult>> ImportCommentsAsync(Actor actor, string projectKey, HistoryComments import, CancellationToken cancellationToken);

    /// <summary>
    /// 匯入一個附件到項目（之前匯入過的項目，用項目編號指）。已經匯入過（同一個來源與外部編號）時不讀內容，直接回那一個（<see cref="HistoryAttachmentResult.Existing"/> 是 true）。
    /// 大小、數量、檔名的規則和一般上傳一樣（<see cref="AttachmentRules"/>）。留一筆 AttachmentAdded 活動（上傳的人與時間照給的），
    /// 寫一筆 item_attachment_added 事件（History 是 true）。
    /// </summary>
    Task<Result<HistoryAttachmentResult>> ImportAttachmentAsync(Actor actor, string itemKey, HistoryAttachment upload, CancellationToken cancellationToken);

    /// <summary>
    /// 查哪些外部編號已經匯入過：CLI 重跑時用它決定要不要再下載檔案、留言要接在哪個項目上。只回 actor 看得到的專案裡的；看不到的當作沒有。
    /// 一次最多 <see cref="HistoryLookup.MaxIds"/> 個。
    /// </summary>
    Task<Result<IReadOnlyList<HistoryRef>>> LookupAsync(Actor actor, HistoryLookup lookup, CancellationToken cancellationToken);
}

/// <summary>來源與外部編號的規則。</summary>
public static class HistorySource
{
    /// <summary>來源的名字：小寫英文開頭，小寫英文、數字、_ 或 -，2 到 30 個字（例如 monday）。存在活動上，時間軸顯示「從 monday 匯入」。</summary>
    public const string Pattern = @"\A[a-z][a-z0-9_-]{1,29}\z";

    /// <summary>外部編號最多這麼長；不能有空白與控制字元。</summary>
    public const int MaxExternalIdLength = 100;
}

/// <summary>外部編號指的是哪一種東西：同一個來源裡，項目、留言、附件的編號可能重複，各自唯一。</summary>
public enum HistoryRefKind
{
    Item,
    Comment,
    Attachment,
}

public sealed record HistoryItems
{
    public const int MaxRows = 1_000;

    public required string Source { get; init; }

    public required IReadOnlyList<HistoryItem> Rows { get; init; }

    /// <summary>只檢查、不建立。</summary>
    public bool DryRun { get; init; }
}

/// <summary>匯入的一個項目。</summary>
public sealed record HistoryItem
{
    public required string ExternalId { get; init; }

    /// <summary>項目的內容。上層不用這裡的 ParentKey（給了是問題），用 <see cref="ParentExternalId"/>。</summary>
    public required NewWorkItem Item { get; init; }

    /// <summary>狀態的 key；null 是流程的第一個狀態。直接設定，不走轉換。</summary>
    public string? State { get; init; }

    /// <summary>上層的外部編號：同一批前面的列，或之前匯入過、在這個專案裡的項目。</summary>
    public string? ParentExternalId { get; init; }

    public required Guid CreatedBy { get; init; }

    /// <summary>原本的建立時間。不能比現在晚（容許 5 分鐘的誤差），不能早於 2000-01-01。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>要關注這個項目的人（負責人不用列，一定關注）。都要是專案成員；最多 50 個。</summary>
    public IReadOnlyList<Guid> Followers { get; init; } = [];
}

/// <param name="Created">這一次建立的，照列的順序；只檢查時是空的。</param>
/// <param name="Skipped">之前匯入過、這一次略過的（項目編號是之前那一個，可能在別的專案）。</param>
public sealed record HistoryItemsResult(bool DryRun, IReadOnlyList<HistoryItemRef> Created, IReadOnlyList<HistoryItemRef> Skipped);

public sealed record HistoryItemRef(string ExternalId, string ItemKey);

public sealed record HistoryComments
{
    public const int MaxComments = 500;

    public required string Source { get; init; }

    public required IReadOnlyList<HistoryComment> Comments { get; init; }
}

public sealed record HistoryComment
{
    public required string ExternalId { get; init; }

    /// <summary>項目的外部編號（之前匯入過、在這個專案裡）。</summary>
    public required string ItemExternalId { get; init; }

    public required Guid AuthorId { get; init; }

    /// <summary>原本的時間；規則和 <see cref="HistoryItem.CreatedAt"/> 一樣。</summary>
    public required DateTimeOffset At { get; init; }

    public required string Body { get; init; }

    /// <summary>回覆的那一則的外部編號；最上層的留言是 null。</summary>
    public string? ReplyToExternalId { get; init; }
}

/// <param name="Created">這一次建立的：外部編號與活動的編號。</param>
public sealed record HistoryCommentsResult(IReadOnlyList<HistoryCommentRef> Created, IReadOnlyList<HistoryCommentRef> Skipped);

public sealed record HistoryCommentRef(string ExternalId, Guid ActivityId);

/// <summary>要匯入的附件。<see cref="Content"/> 由呼叫端提供、負責 Dispose；已經匯入過時服務不讀它。</summary>
public sealed record HistoryAttachment
{
    public required string Source { get; init; }

    public required string ExternalId { get; init; }

    public required string FileName { get; init; }

    public string? ContentType { get; init; }

    public required Stream Content { get; init; }

    public required Guid UploadedBy { get; init; }

    public required DateTimeOffset UploadedAt { get; init; }
}

/// <param name="Existing">之前匯入過：這是那一個，這一次沒有建立。</param>
public sealed record HistoryAttachmentResult(Attachment Attachment, bool Existing);

public sealed record HistoryLookup
{
    public const int MaxIds = 1_000;

    public required string Source { get; init; }

    public required HistoryRefKind Kind { get; init; }

    public required IReadOnlyList<string> ExternalIds { get; init; }
}

/// <summary>
/// 一筆已經匯入過的東西。<see cref="TargetId"/>：項目是項目的編號（UUID），留言是活動的編號，附件是附件的編號。
/// </summary>
public sealed record HistoryRef(string ExternalId, HistoryRefKind Kind, string ProjectKey, string ItemKey, Guid TargetId);
