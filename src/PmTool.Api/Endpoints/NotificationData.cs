using PmTool.Notifications;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 一則通知在 API 上的形狀。項目的編號、標題與狀態名稱是事件發生當時的樣子；做事的人帶顯示名稱，網頁不必另外查。
/// 帳號通知（<c>account_security</c>）沒有專案與項目：<c>projectKey</c>、<c>itemKey</c>、<c>itemTitle</c> 是 null，
/// 發生了什麼事在 <c>account</c>，做事的人就是本人。<c>edited</c> 是摘要來自留言編輯之後的內容。
/// </summary>
internal sealed record NotificationData(
    Guid NotificationId,
    NotificationKind Kind,
    string? ProjectKey,
    string? ItemKey,
    string? ItemTitle,
    PersonReference Actor,
    string? FromStateName,
    string? ToStateName,
    string? Excerpt,
    Guid? CommentId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    AccountNotice? Account,
    bool Edited)
{
    /// <summary>通知裡做事的人已經查不到時的名字（已離開組織或被刪掉）。</summary>
    public const string UnknownActorName = "查不到的人";

    public static NotificationData From(NotificationItem item, PeopleNames names) => new(
        item.NotificationId,
        item.Kind,
        item.ProjectKey,
        item.ItemKey,
        item.ItemTitle,
        new PersonReference(item.ActorPersonId, names.Find(item.ActorPersonId)?.DisplayName ?? UnknownActorName),
        item.FromStateName,
        item.ToStateName,
        item.Excerpt,
        item.CommentId,
        item.CreatedAt,
        item.ReadAt,
        item.Account,
        item.Edited);
}

internal sealed record NotificationPageData(IReadOnlyList<NotificationData> Items, string? NextCursor);

internal sealed record UnreadCountData(int Count);

internal sealed record UpdatedCountData(int Updated);

/// <param name="CanFollow">是不是這個專案的成員；不是的話不能關注（PUT 回 409），<paramref name="Following"/> 一定是 false。</param>
internal sealed record SubscriptionData(bool Following, SubscriptionReason? Reason, bool CanFollow)
{
    public static SubscriptionData From(ItemSubscription subscription) =>
        new(subscription.Following, subscription.Reason, subscription.CanFollow);
}

internal sealed record MarkReadRequest(IReadOnlyList<Guid> NotificationIds);

internal sealed record SubscriptionRequest(bool Following);
