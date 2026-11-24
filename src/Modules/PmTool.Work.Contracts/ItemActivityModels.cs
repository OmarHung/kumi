using System.Text.Json;

namespace PmTool.Work;

public enum ActivityKind
{
    Created,
    Updated,
    Transitioned,
    Commented,

    /// <summary>套用藍圖的變更集時，項目的資料被搬移：狀態搬到另一個狀態、欄位被刪除（值封存）或換了型別。</summary>
    Migrated,

    /// <summary>
    /// 改了類型。<see cref="ItemActivity.Changes"/> 的第一筆是 item_type 的前後值（類型的 key），
    /// 接著是新類型沒有、因此封存的自訂欄位（To 是 null，長文字兩邊都是 null）；狀態跟著搬了時有 FromState 與 ToState。
    /// </summary>
    TypeChanged,

    /// <summary>項目被刪除（封存）。刪除之後讀不到這個項目，這一筆只留在資料庫裡作為紀錄。</summary>
    Deleted,

    /// <summary>
    /// 刪除的項目被復原。刪除之後藍圖可能改過（變更集只搬沒刪除的項目）：狀態已經不在流程裡時搬到流程的第一個狀態（有 FromState 與 ToState），
    /// 類型已經沒有的自訂欄位與值不再合格的自訂欄位，值封存起來（<see cref="ItemActivity.Changes"/> 裡 To 是 null，和改類型的封存一樣）；
    /// 上層的類型已經不允許掛在底下時拿掉上層（changes 裡 parent 的 To 是 null）。都沒有時 Changes 是空的。
    /// </summary>
    Restored,

    /// <summary>上傳了一個附件。<see cref="ItemActivity.Changes"/> 一筆：Field 是 attachment，From 是 null，To 是檔名。</summary>
    AttachmentAdded,

    /// <summary>刪除了一個附件。<see cref="ItemActivity.Changes"/> 一筆：Field 是 attachment，From 是檔名，To 是 null。</summary>
    AttachmentRemoved,
}

/// <summary>一次修改裡一個欄位的前後值。</summary>
/// <param name="Field">
/// 核心欄位用文件裡的名字（title、description、assignee、priority、start_date、due_date、estimate、parent），
/// 自訂欄位用欄位的 key。兩者不會撞名：藍圖的語意規則不允許自訂欄位用核心欄位的名字。
/// </param>
/// <param name="From">
/// 改之前的值，形狀和 <see cref="WorkItem"/> 對外的一樣：負責人與成員欄位是人員編號的字串、上層項目是項目編號、
/// 優先度是 snake_case 的字串（none 算沒有值）、日期是 YYYY-MM-DD。沒有值是 null。
/// 說明與長文字欄位只記「有改」，兩邊一律是 null：內容可能很長，版本歷史之後另外做。
/// </param>
public sealed record FieldChange(string Field, JsonElement? From, JsonElement? To);

/// <summary>
/// 項目的一筆活動：建立、修改、改狀態、留言。和修改寫在同一個交易裡，資料改了就一定有紀錄。
/// </summary>
/// <param name="ActorId">做這件事的人（人員編號）。</param>
public sealed record ItemActivity(Guid Id, string ItemKey, ActivityKind Kind, Guid ActorId, DateTimeOffset At)
{
    /// <summary>
    /// <see cref="ActivityKind.Updated"/> 一定有，而且至少一筆；值真的有變的欄位才列。
    /// <see cref="ActivityKind.Migrated"/> 有欄位被刪除或轉換時才有。
    /// </summary>
    public IReadOnlyList<FieldChange> Changes { get; init; } = [];

    /// <summary>只有 <see cref="ActivityKind.Transitioned"/> 有：狀態的 key。</summary>
    public string? FromState { get; init; }

    public string? ToState { get; init; }

    /// <summary>
    /// 只有 <see cref="ActivityKind.Commented"/> 有：留言現在的內容，純文字，可以有換行。
    /// 刪除的留言是 null（看 <see cref="DeletedAt"/>）。
    /// </summary>
    public string? Comment { get; init; }

    /// <summary>
    /// 只有留言有：最後一次編輯的時間；沒編輯過是 null。<see cref="At"/> 照舊是第一次留言的時間，時間軸依它排，編輯不改變位置。
    /// 舊的內容用 <see cref="IItemActivityService.ListCommentRevisionsAsync"/> 讀。
    /// </summary>
    public DateTimeOffset? EditedAt { get; init; }

    /// <summary>
    /// 只有留言有：刪除的時間；沒刪除是 null。刪除的留言照樣在清單裡、位置不變（畫面顯示「留言已刪除」），
    /// 但內容不給任何人讀（<see cref="Comment"/> 是 null，版本也讀不到）；內容與版本留在資料庫，復原之後回來。
    /// </summary>
    public DateTimeOffset? DeletedAt { get; init; }

    /// <summary>刪除的人（人員編號）；作者自己刪的就是 <see cref="ActorId"/>。沒刪除是 null。</summary>
    public Guid? DeletedBy { get; init; }

    /// <summary>
    /// 只有回覆有：它回覆的那一則留言（一律是最上層的留言，回覆只有一層）。最上層的留言與其他活動是 null。
    /// 活動照樣依時間排；畫面把回覆收在它的那一則底下。
    /// </summary>
    public Guid? ReplyToId { get; init; }

    /// <summary>
    /// 從別的系統搬過來的歷史（<see cref="IHistoryImportService"/>）：來源的名字（例如 monday），時間軸顯示「從 monday 匯入」。
    /// 作者與時間是原本的。這裡建立的活動是 null。
    /// </summary>
    public string? ImportedFrom { get; init; }

    /// <summary>
    /// 只有 <see cref="ActivityKind.Migrated"/> 有：套用之後的藍圖版本。搬移的內容放在 <see cref="FromState"/>、
    /// <see cref="ToState"/>（狀態搬移）與 <see cref="Changes"/>（欄位刪除與轉換；刪除的 <c>To</c> 是 null）。
    /// </summary>
    public long? BlueprintVersion { get; init; }
}

/// <summary>活動依時間由舊到新排列。</summary>
public sealed record ActivityQuery
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>上一頁回傳的 <see cref="ItemActivityPage.NextCursor"/>；第一頁是 null。內容對呼叫端沒有意義。</summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// 只要這一種活動；null 是全部。網頁的「留言」用 <see cref="ActivityKind.Commented"/>（含回覆）。
    /// 在資料庫裡就篩掉再分頁：只在畫面上濾的話，一頁裡可能一則留言都沒有。游標和不篩的時候同一種，換了條件要從第一頁讀。
    /// </summary>
    public ActivityKind? Kind { get; init; }
}

/// <param name="NextCursor">還有更新的活動時的游標；沒有了就是 null。</param>
public sealed record ItemActivityPage(IReadOnlyList<ItemActivity> Items, string? NextCursor);

/// <summary>
/// 一則留言的一個版本。版本號從 1（第一次留言）起算，最大的那一個是現在的內容。
/// </summary>
/// <param name="Number">版本號，從 1 起算。</param>
/// <param name="Body">那個版本的內容（和留言一樣是 Markdown）。</param>
/// <param name="At">這個版本成為留言內容的時間：第 1 版是留言的時間，之後是那一次編輯的時間。</param>
/// <param name="IsCurrent">是不是現在的內容（版本號最大的那一個）。</param>
public sealed record CommentRevision(int Number, string Body, DateTimeOffset At, bool IsCurrent);
