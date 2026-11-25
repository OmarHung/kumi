using PmTool.Abstractions;

namespace PmTool.Notifications;

/// <summary>
/// 自己的收件匣：別人做的、和自己有關的事（被指派、被提及、關注的項目有留言或改了狀態）。
/// 只列現在還看得到的專案的通知；看不到了（被移出專案）的不列、不算未讀。
/// </summary>
public interface INotificationInbox
{
    /// <summary>
    /// 新的在前。<see cref="NotificationQuery.UnreadOnly"/> 時只列未讀的。
    /// limit 超出範圍或游標看不懂時回 <see cref="NotificationErrorCodes.QueryInvalid"/>，細項的路徑是 <c>limit</c> 或 <c>cursor</c>。
    /// </summary>
    Task<Result<NotificationPage>> ListAsync(Actor actor, NotificationQuery query, CancellationToken cancellationToken);

    /// <summary>未讀的數量（側欄的徽章用）。</summary>
    Task<int> CountUnreadAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>標成已讀。別人的、不存在的編號略過。回傳這一次從未讀變成已讀的數量。</summary>
    Task<int> MarkReadAsync(Actor actor, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken);

    /// <summary>全部標成已讀（只算看得到的專案的）。回傳這一次從未讀變成已讀的數量。</summary>
    Task<int> MarkAllReadAsync(Actor actor, CancellationToken cancellationToken);
}

/// <summary>關注項目：關注的項目有留言或改狀態時收到通知。</summary>
public interface IItemSubscriptionService
{
    /// <summary>
    /// 自己對這個項目的關注狀態。呼叫端（API）先用工作引擎確認看得到這個項目，再把它和專案的編號傳進來。
    /// 不是專案成員時 <see cref="ItemSubscription.CanFollow"/> 是 false、<see cref="ItemSubscription.Following"/> 一定是 false。
    /// </summary>
    Task<ItemSubscription> GetAsync(Actor actor, Guid projectId, Guid itemId, CancellationToken cancellationToken);

    /// <summary>
    /// 關注或取消關注。取消之後，就算之後留言、被指派也不會自動恢復（被指派與被提及照樣直接通知）。
    /// 只有專案成員收得到通知，所以不是成員（例如沒加入專案的組織管理員）回 409
    /// <see cref="NotificationErrorCodes.NotProjectMember"/>，不留下關注。
    /// </summary>
    Task<Result<ItemSubscription>> SetAsync(Actor actor, Guid projectId, Guid itemId, bool following, CancellationToken cancellationToken);
}

public enum NotificationKind
{
    /// <summary>被指派為負責人。</summary>
    Assigned,

    /// <summary>在留言或說明裡被提及。</summary>
    Mentioned,

    /// <summary>關注的項目有新留言。</summary>
    Commented,

    /// <summary>關注的項目改了狀態。</summary>
    StateChanged,

    /// <summary>
    /// 有人回覆了你的留言（你是那一則最上層留言的作者）。比「有新留言」優先、比「被提及」次之：
    /// 同一則回覆裡也提及了你，只收「被提及」一則。
    /// </summary>
    Replied,

    /// <summary>
    /// 帳號的安全通知（建立存取權杖、命令列登入、授權 AI 用戶端、登入碼被重複使用）。只給本人、沒有專案與項目，
    /// 內容在 <see cref="NotificationItem.Account"/>；不受專案的看得到與否影響。
    /// </summary>
    AccountSecurity,
}

/// <param name="UnreadOnly">只列未讀的。</param>
/// <param name="Cursor">上一頁回傳的 <see cref="NotificationPage.NextCursor"/>；第一頁是 null。</param>
/// <param name="Limit">1～<see cref="NotificationRules.MaxPageSize"/>。</param>
public sealed record NotificationQuery(bool UnreadOnly, string? Cursor, int Limit);

public sealed record NotificationPage(IReadOnlyList<NotificationItem> Items, string? NextCursor);

/// <summary>
/// 一則通知。項目的編號、標題、狀態名稱是發生當時的樣子（之後改了名也照舊，點進去看最新的）。
/// </summary>
/// <param name="Excerpt">留言或提及的那一段（最多 200 字，提及換成「@名稱」）；其他種類是 null。</param>
/// <param name="CommentId">由留言產生的通知（留言、留言裡的提及）是那一則留言的編號（也就是活動的編號）；其他種類與加這個欄位之前的通知是 null。</param>
/// <param name="ProjectKey">專案、項目的編號與標題：<see cref="NotificationKind.AccountSecurity"/> 沒有，是 null；其他種類一定有。</param>
public sealed record NotificationItem(
    Guid NotificationId,
    NotificationKind Kind,
    string? ProjectKey,
    string? ItemKey,
    string? ItemTitle,
    Guid ActorPersonId,
    string? FromStateName,
    string? ToStateName,
    string? Excerpt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    Guid? CommentId)
{
    /// <summary>只有 <see cref="NotificationKind.AccountSecurity"/> 有：發生了什麼事。</summary>
    public AccountNotice? Account { get; init; }
}

/// <summary>帳號通知的事件種類（和身分模組的事件種類各自定義，通知的合約不引用身分的合約）。</summary>
public enum AccountNoticeEvent
{
    AccessTokenCreated,
    CliLoginTokensIssued,
    OauthAppAuthorized,
    CliLoginCodeReused,
}

/// <summary>
/// 帳號通知的內容。範圍用對外的名稱（<c>items:read</c> 這些）；每個種類只填用得到的欄位，其他是空的或 null。
/// </summary>
/// <param name="Tokens">建立的權杖：手動建立 1 把、瀏覽器登入 1 到 2 把。</param>
/// <param name="Scopes">OAuth 授權的範圍。</param>
/// <param name="SourceAddress">伺服器看到的來源位址。</param>
/// <param name="RevokedTokens">登入碼被重複使用時撤銷的權杖數。</param>
public sealed record AccountNotice(
    AccountNoticeEvent Event,
    IReadOnlyList<AccountNoticeToken> Tokens,
    IReadOnlyList<string> Scopes,
    string? SourceAddress,
    string? MachineName,
    string? ClientName,
    string? ClientHost,
    int RevokedTokens);

/// <param name="ExpiresAt">null 是不會過期。</param>
public sealed record AccountNoticeToken(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);

/// <param name="Following">現在有沒有在關注。</param>
/// <param name="Reason">為什麼在關注；沒有在關注時是 null。</param>
/// <param name="CanFollow">是不是這個專案的成員。只有成員收得到通知；不是的話不能關注，<paramref name="Following"/> 一定是 false。</param>
public sealed record ItemSubscription(bool Following, SubscriptionReason? Reason, bool CanFollow);

/// <summary>開始關注的原因：自己按的，或因為建立、被指派、留言、被提及而自動關注。</summary>
public enum SubscriptionReason
{
    Manual,
    Creator,
    Assignee,
    Commenter,
    Mentioned,
}

public static class NotificationRules
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 100;

    /// <summary>一次標成已讀最多幾個編號。</summary>
    public const int MaxMarkReadIds = 200;
}

/// <summary>
/// 想知道「誰的收件匣剛有變化」的一方（API 的即時推送）。通知模組在產生通知的交易提交之後呼叫；可以註冊零到多個。
/// 實作不可以丟例外拖垮通知的處理（通知已經存好了，推送失敗只是畫面晚一點更新）；通知模組也會接住並記 Warning。
/// </summary>
public interface INotificationObserver
{
    /// <param name="recipients">這一批收件匣有變的人（不重複）：有新通知的，或留言刪除時通知被刪掉的。</param>
    Task NotifiedAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken);
}

/// <summary>收到新通知的人。</summary>
public sealed record NotifiedRecipient(Guid OrganizationId, Guid PersonId);

