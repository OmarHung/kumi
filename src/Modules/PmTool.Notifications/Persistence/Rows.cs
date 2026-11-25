using PmTool.Work;

namespace PmTool.Notifications.Persistence;

/// <summary>
/// <c>notifications.subscriptions</c> 的一列：某個人對某個項目的關注。取消關注不刪列、只做標記，
/// 這樣之後留言、被指派也不會自動把人加回來（規劃書：取消了就是取消了）。
/// </summary>
internal sealed record SubscriptionRow
{
    public required Guid OrganizationId { get; init; }

    public required Guid ProjectId { get; init; }

    public required Guid ItemId { get; init; }

    public required Guid PersonId { get; init; }

    public required SubscriptionReason Reason { get; init; }

    public required bool IsUnsubscribed { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// <c>notifications.notifications</c> 的一列：一則通知。項目的編號、標題與狀態名稱是事件發生當時的樣子，
/// 不回頭查工作引擎（那時可能已經改名或刪掉）。
/// </summary>
internal sealed record NotificationRow
{
    public required Guid Id { get; init; }

    public required Guid OrganizationId { get; init; }

    public required Guid RecipientId { get; init; }

    public required NotificationKind Kind { get; init; }

    /// <summary>帳號通知（<see cref="NotificationKind.AccountSecurity"/>）沒有專案與項目，這五個欄位都是 null；其他種類一定有。</summary>
    public Guid? ProjectId { get; init; }

    public string? ProjectKey { get; init; }

    public Guid? ItemId { get; init; }

    public string? ItemKey { get; init; }

    public string? ItemTitle { get; init; }

    public required Guid ActorPersonId { get; init; }

    /// <summary>只有帳號通知有：發生了什麼事，存成 jsonb；資料庫的檢查限制保證它和種類一起出現。</summary>
    public AccountNotice? AccountNotice { get; init; }

    public string? FromStateName { get; init; }

    public string? ToStateName { get; init; }

    public string? Excerpt { get; init; }

    /// <summary>由留言產生的通知（留言、留言裡的提及）是那一則留言（活動）的編號；其他是 null。</summary>
    public Guid? CommentId { get; init; }

    /// <summary>留言編輯之後摘要換成新內容的時刻；沒換過是 null。畫面據此註明「已編輯」。</summary>
    public DateTimeOffset? ExcerptEditedAt { get; init; }

    /// <summary>產生這則通知的事件；（事件、收件人、種類）唯一，重複處理同一個事件不會多出通知。</summary>
    public required Guid EventId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>
    /// Email 的寄送處理完了：寄出了、或決定不寄（已經讀了、太舊、組織沒開、他關掉了、沒有 Email）。
    /// null 是還在等。加這個欄位之前的通知在遷移時填成建立的時間，不會補寄。
    /// </summary>
    public DateTimeOffset? EmailHandledAt { get; init; }
}

/// <summary>
/// <c>notifications.email_settings</c> 的一列：一個組織的寄信設定與背景寄送的狀況。沒設定過的組織沒有這一列。
/// 種類與加密方式存成 snake_case 的字串。密碼用 Data Protection 加密後存，讀出來只說有沒有。
/// </summary>
internal sealed record EmailSettingsRow
{
    public required Guid OrganizationId { get; init; }

    public required bool Enabled { get; init; }

    public required EmailProvider Provider { get; init; }

    public string? Host { get; init; }

    public int? Port { get; init; }

    public SmtpSecurity? Security { get; init; }

    public string? Username { get; init; }

    /// <summary>加密過的密碼（<c>IDataProtector.Protect</c> 的結果）；沒有密碼是 null。</summary>
    public string? ProtectedPassword { get; init; }

    public string? FromAddress { get; init; }

    public string? FromName { get; init; }

    public string? SiteUrl { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required Guid UpdatedBy { get; init; }

    public DateTimeOffset? LastSentAt { get; init; }

    public DateTimeOffset? LastFailedAt { get; init; }

    public string? LastError { get; init; }

    /// <summary>連續失敗了幾次（成功一次就歸零），用來算下一次什麼時候再試。</summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>失敗之後，這個時間以前不再試；null 是隨時可以寄。</summary>
    public DateTimeOffset? RetryAfter { get; init; }
}

/// <summary><c>notifications.email_preferences</c> 的一列：某個人要不要收 Email 通知。沒有這一列就是要收。</summary>
internal sealed record EmailPreferenceRow
{
    public required Guid OrganizationId { get; init; }

    public required Guid PersonId { get; init; }

    public required bool Enabled { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// <c>notifications.reader_positions</c> 的一列：事件的讀者讀到哪裡。
/// 這張表是整個系統共用的（不屬於哪個組織），所以沒有 organization_id。
/// </summary>
internal sealed record ReaderPositionRow
{
    public required string Name { get; init; }

    public required long TransactionId { get; init; }

    public required long Sequence { get; init; }
}

/// <summary>
/// <c>notifications.failed_events</c> 的一列：處理失敗的事件。同一筆事件每確定失敗一次次數加一
/// （距離上一次算進次數的失敗至少 <see cref="NotificationsDbContext.MinEventRetryInterval"/>，暫時性的錯誤不記）；
/// 到了 <see cref="NotificationsDbContext.MaxEventAttempts"/> 次就略過它，位置往前走，免得一筆壞事件讓所有人的通知都停住。
/// 錯誤只存種類與訊息的摘要（不存堆疊），事件的內容回工作引擎的事件流查。
/// </summary>
internal sealed record FailedEventRow
{
    public required Guid EventId { get; init; }

    public required Guid OrganizationId { get; init; }

    public required long TransactionId { get; init; }

    public required long Sequence { get; init; }

    public required string ErrorType { get; init; }

    public required string ErrorMessage { get; init; }

    public required int Attempts { get; init; }

    /// <summary>上一次算進次數的失敗的時間（間隔內的失敗只更新錯誤訊息，不動它）。</summary>
    public required DateTimeOffset LastFailedAt { get; init; }
}
