namespace PmTool.Work;

/// <summary>
/// 事件流裡的位置：寫入事件的交易編號（PostgreSQL 的 <c>pg_current_xact_id()</c>）與事件的流水號。
/// 只用流水號不夠：先拿到號碼的交易可能比後拿到的晚提交，讀的人往前跳過之後就永遠讀不到它。
/// 依（交易編號、流水號）的順序讀、而且只讀比「還在進行的最舊交易」更早的交易，才不會漏。
/// </summary>
public sealed record WorkEventPosition(long TransactionId, long Sequence) : IComparable<WorkEventPosition>
{
    /// <summary>從頭開始。</summary>
    public static WorkEventPosition Start { get; } = new(0, 0);

    public int CompareTo(WorkEventPosition? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byTransaction = TransactionId.CompareTo(other.TransactionId);
        return byTransaction != 0 ? byTransaction : Sequence.CompareTo(other.Sequence);
    }
}

/// <summary>工作引擎的事件。對外是合約：之後只加不改名（讀的人可能落後很久）。</summary>
/// <remarks>
/// 讀者要容忍不認得的種類：新版的工作引擎可能寫了舊版讀者不認得的事件（滾動更新時兩個版本會同時在跑）。
/// 事件流把它們讀成 <see cref="Unknown"/>；讀者略過並記 Warning、位置照樣往前走，不可以丟例外讓整批卡住。
/// </remarks>
public enum WorkEventKind
{
    ItemCreated,
    ItemUpdated,
    ItemTransitioned,
    ItemCommented,
    ItemDeleted,

    /// <summary>項目多了一個附件（資料表裡的文字是 item_attachment_added）。<see cref="ItemEventData.ChangedFields"/> 是 ["attachments"]。</summary>
    ItemAttachmentAdded,

    /// <summary>項目的一個附件被刪除（item_attachment_removed）。<see cref="ItemEventData.ChangedFields"/> 是 ["attachments"]。</summary>
    ItemAttachmentRemoved,

    /// <summary>刪除的項目被復原。事件裡是復原之後的樣子（狀態可能因為藍圖改過而搬了，見 <see cref="ActivityKind.Restored"/>）。</summary>
    ItemRestored,

    /// <summary>
    /// 一則留言（含回覆）被編輯（資料表裡的文字是 item_comment_edited）。不新增活動：<see cref="ItemEventData.ActivityId"/> 是那一則留言，
    /// <see cref="ItemEventData.CommentExcerpt"/> 是編輯之後的摘要，<see cref="ItemEventData.MentionedPersonIds"/> 是這一次新加的提及
    /// （編輯之前那一版沒有、這一版有的）。<see cref="ItemEventData.ReplyToCommentId"/> 與作者一律是 null：編輯不是新的回覆。
    /// </summary>
    ItemCommentEdited,

    /// <summary>
    /// 一則留言（含回覆）被刪除（item_comment_deleted）。<see cref="ItemEventData.ActivityId"/> 是那一則留言，沒有摘要。
    /// 通知模組刪掉這則留言的所有通知（收件匣的未讀數跟著變，還沒寄的信不寄）。
    /// </summary>
    ItemCommentDeleted,

    /// <summary>
    /// 刪除的留言被復原（item_comment_restored）。<see cref="ItemEventData.ActivityId"/> 是那一則留言，沒有摘要。
    /// 讀的人什麼都不用發：刪除時清掉的通知不找回來，也不重新通知（即時更新照樣推，讓別的分頁重抓）。
    /// </summary>
    ItemCommentRestored,

    /// <summary>
    /// 這個版本不認得的種類（或內容讀不懂）。只由事件流產生、不會被寫進資料表；<see cref="WorkEvent.Item"/> 只是佔位的空值，不能用。
    /// 事件種類在資料表裡存的是文字，列舉的數字不是合約，新種類加在它前面也可以。
    /// </summary>
    Unknown,
}

/// <summary>狀態的 key 與發生當時的名稱（藍圖之後可能改名或刪掉它）。</summary>
public sealed record StateSnapshot(string Key, string Name);

/// <summary>
/// 事件發生當時項目的樣子，加上這一次改了什麼。和寫入在同一個交易裡存下來，讀的人不必回頭查（那時可能已經變了）。
/// </summary>
/// <param name="CreatedBy">建立項目的人。</param>
/// <param name="AssigneeId">事件之後的負責人。</param>
/// <param name="PreviousAssigneeId">這一次換了負責人時，之前的負責人（之前沒有就是 null）；沒換時是 null，看 <paramref name="AssigneeChanged"/>。</param>
/// <param name="FromState">改狀態時的前一個狀態；其他事件是 null。</param>
/// <param name="ToState">改狀態時的新狀態；建立時是起始狀態；其他事件是 null。</param>
/// <param name="CommentExcerpt">
/// 留言的前 <see cref="WorkEventRules.MaxExcerptLength"/> 個字，提及換成「@名稱」；只有留言與編輯留言的事件有（編輯的是新的內容）。
/// 摘要是事件當時的內容，不代表留言現在的樣子：之後可能被編輯或刪除。讀的人要自己處理 <see cref="WorkEventKind.ItemCommentEdited"/> 與
/// <see cref="WorkEventKind.ItemCommentDeleted"/>；重播歷史時不能把摘要直接交出去（例如送給外部的 webhook）；留言永久刪除時要一起清掉。
/// </param>
/// <param name="MentionedPersonIds">
/// 這一次新提及、而且看得到這個專案的人（留言裡的、說明裡新加的；改說明時原本就提及的不算）。不包含做這件事的人。
/// </param>
/// <param name="ChangedFields">改了哪些欄位（核心欄位的名稱如 <c>title</c>、<c>assignee</c>、<c>description</c>，自訂欄位的 key）。只有更新事件有。</param>
public sealed record ItemEventData(
    Guid ProjectId,
    string ProjectKey,
    Guid ItemId,
    string ItemKey,
    string Title,
    Guid CreatedBy,
    Guid? AssigneeId,
    bool AssigneeChanged,
    Guid? PreviousAssigneeId,
    StateSnapshot? FromState,
    StateSnapshot? ToState,
    string? CommentExcerpt,
    IReadOnlyList<Guid> MentionedPersonIds,
    IReadOnlyList<string> ChangedFields)
{
    /// <summary>
    /// 這一次寫入時留下的那一筆活動（項目時間軸上的一筆，<see cref="ItemActivity.Id"/>）。每個寫入點都剛好寫一筆活動和一筆事件；
    /// 例外是留言的編輯、刪除與復原：不新增活動，這裡是那一則留言。
    /// 加這個欄位之前寫的事件是 null。
    /// </summary>
    public Guid? ActivityId { get; init; }

    /// <summary>
    /// 這個項目是匯入建立的（<see cref="IItemImportService"/>，只有 ItemCreated 會是 true）。讀的人可以據此少做一點：
    /// 通知模組不為匯入的項目發「指派給你」，一次匯入幾百個指派給同一個人時，收件匣不會被洗掉；自動關注照舊。
    /// 加這個欄位之前寫的事件是 false。
    /// </summary>
    public bool Imported { get; init; }

    /// <summary>
    /// 只有回覆的留言事件有：它回覆的那一則最上層留言（活動的編號）與那則留言的作者。
    /// 通知模組用作者發「回覆了你的留言」，不必回頭查 Work 的資料（讀的人不該依賴別的模組的資料表）。
    /// 加這兩個欄位之前寫的事件都是 null。
    /// </summary>
    public Guid? ReplyToCommentId { get; init; }

    public Guid? ReplyToAuthorId { get; init; }

    /// <summary>
    /// 這是從別的系統搬過來的歷史（<see cref="IHistoryImportService"/>：保留原作者與時間）。讀的人：通知模組**一則通知都不發**
    /// （幾年前的留言、提及、指派不是現在發生的事）；只有 ItemCreated 會建立關注：負責人與 <see cref="Followers"/>，不自動關注建立的人
    /// （原本的建立者常常是 PM，一次匯入上千個項目會讓他關注全部）。留言、附件的歷史事件不建立關注。
    /// History 是 true 時 Imported 也是 true。加這個欄位之前寫的事件是 false。
    /// </summary>
    public bool History { get; init; }

    /// <summary>只有歷史的 ItemCreated 有：匯入時指定要關注的人（不含負責人）。其他事件是空的。</summary>
    public IReadOnlyList<Guid> Followers { get; init; } = [];
}

/// <summary>一筆事件。</summary>
/// <param name="EventId">全域唯一，讀的人用它去重。</param>
public sealed record WorkEvent(
    Guid EventId,
    WorkEventPosition Position,
    WorkEventKind Kind,
    Guid OrganizationId,
    Guid ActorPersonId,
    DateTimeOffset OccurredAt,
    ItemEventData Item);

public static class WorkEventRules
{
    public const int MaxExcerptLength = 200;

    /// <summary>一則留言或一次說明的修改，最多算幾個提及（超過的不通知，內容照樣存）。</summary>
    public const int MaxMentionsPerChange = 20;

    /// <summary>
    /// 提及的寫法：Markdown 的連結，網址是 <c>mention:</c> 加人員編號，例如 <c>@[王小明](mention:3f2b…)</c>。
    /// 網頁的編輯器插入這個寫法，顯示時換成提及的樣子；不認得的人或看不到專案的人照文字顯示，不通知。
    /// </summary>
    public const string MentionScheme = "mention:";
}

/// <summary>
/// 事件流：工作引擎每次寫入都在同一個交易裡留下事件。讀的人（通知、之後的 webhook、自動化）各自記住讀到哪裡，
/// 一個落後或故障不影響其他的（規劃書第 10 節）。
/// </summary>
public interface IWorkEventStream
{
    /// <summary>
    /// <paramref name="after"/> 之後的事件，依（交易編號、流水號）排序，最多 <paramref name="limit"/> 筆。
    /// 只回已經確定的：交易編號比目前還在進行的最舊交易更小的。沒有新的就是空清單。
    /// </summary>
    Task<IReadOnlyList<WorkEvent>> ReadAfterAsync(WorkEventPosition after, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// 把讀的人自己存的位置換算成現在這個資料庫的位置。備份還原到新的資料庫之後，新資料庫的交易編號從頭算起，
    /// 讀的人存的是舊資料庫的編號，會排在所有新事件後面、新事件永遠讀不到：交易編號不小於目前快照的 xmax 時回（1、原本的流水號），
    /// 否則原樣回傳。規則只在工作引擎寫一次。
    /// 讀的人每次讀出自己存的位置，都要先經過這個方法再拿去比較或傳給 <see cref="ReadAfterAsync"/>；換算後不同，就把換算後的存回去。
    /// </summary>
    Task<WorkEventPosition> ResolvePositionAsync(WorkEventPosition stored, CancellationToken cancellationToken);

    /// <summary>
    /// 現在已經確定的最後一個位置（<see cref="ReadAfterAsync"/> 讀得到的最後一筆；沒有事件時是 <see cref="WorkEventPosition.Start"/>）。
    /// 不記位置、只在乎「從現在起」的讀者用它當起點（即時推送），不必從頭讀過所有的歷史。
    /// </summary>
    Task<WorkEventPosition> GetLatestPositionAsync(CancellationToken cancellationToken);
}

/// <summary>
/// 讀事件流的一方。API 的背景工作定期把新的事件交給每一個讀的人。
/// 事件裡的留言摘要（<see cref="ItemEventData.CommentExcerpt"/>）是事件當時的內容：留言之後被編輯或刪除，
/// 讀的人要自己處理 ItemCommentEdited 與 ItemCommentDeleted，重播歷史時不能把舊的摘要直接交給外部。
/// </summary>
public interface IWorkEventConsumer
{
    /// <summary>名稱，也是它存讀到哪裡的鍵。</summary>
    string Name { get; }

    /// <summary>上一次處理到的位置；第一次是 <see cref="WorkEventPosition.Start"/>。</summary>
    Task<WorkEventPosition> GetPositionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 處理一批事件，並把位置記到最後一筆，兩件事在同一個交易裡。好幾個程序同時叫到時只有一個做；
    /// 已經處理過的位置（例如別的程序剛做完）略過，不重複產生結果。
    /// </summary>
    Task HandleAsync(IReadOnlyList<WorkEvent> events, CancellationToken cancellationToken);
}
