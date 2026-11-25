using Microsoft.EntityFrameworkCore;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Events;

/// <summary>
/// 把一筆事件變成通知與自動關注，寫進同一個 <see cref="NotificationsDbContext"/>（呼叫端的交易）。
/// 一批裡的事件共用同一個 <see cref="ReaderCache"/> 與「已經有的通知」，所以處理順序要照事件的順序。
/// </summary>
internal sealed class EventApplier(NotificationsDbContext db, ReaderCache readers, HashSet<(Guid, Guid, NotificationKind)> existing)
{
    private readonly HashSet<NotifiedRecipient> _notified = [];

    /// <summary>這一批收件匣有變的人：新增了通知、或這則留言被刪掉而少了通知。一筆事件存成功之後才算進來：退回的事件沒有留下變化，不該通知別人去看。</summary>
    public IReadOnlyCollection<NotifiedRecipient> NotifiedRecipients => _notified;

    public async Task ApplyAsync(WorkEvent workEvent, CancellationToken cancellationToken)
    {
        var item = workEvent.Item;
        if (workEvent.Kind == WorkEventKind.ItemDeleted)
        {
            await db.Subscriptions
                .Where(s => s.ItemId == item.ItemId && s.OrganizationId == workEvent.OrganizationId)
                .ExecuteDeleteAsync(cancellationToken);
            await RemoveItemNotificationsAsync(workEvent, cancellationToken);
            return;
        }

        if (workEvent.Kind == WorkEventKind.ItemCommentDeleted)
        {
            await RemoveCommentNotificationsAsync(workEvent, cancellationToken);
            return;
        }

        if (workEvent.Kind == WorkEventKind.ItemCommentEdited)
        {
            await RefreshCommentExcerptAsync(workEvent, cancellationToken);
        }

        var subscriptions = await db.Subscriptions.AsNoTracking()
            .Where(s => s.ItemId == item.ItemId && s.OrganizationId == workEvent.OrganizationId)
            .ToListAsync(cancellationToken);
        var followers = subscriptions.Where(s => !s.IsUnsubscribed).Select(s => s.PersonId).ToHashSet();
        var plan = NotificationPlanner.Plan(workEvent, followers);

        var candidates = plan.Notifications.Select(n => n.RecipientId).Concat(plan.Follows.Select(f => f.PersonId)).ToHashSet();
        if (candidates.Count == 0)
        {
            return;
        }

        // 收件人要現在還是專案成員、而且看得到這個項目：被移出專案的人（或已經沒有權限的人）不通知、也不自動關注。
        // 只看自己的角色要是項目的負責人或建立者才看得到：被提及、留言、改狀態的通知都只給看得到這個項目的人。
        var allowed = await readers.ReadersAmongAsync(
            workEvent.OrganizationId, item.ProjectId, new ItemOwnership(item.CreatedBy, item.AssigneeId), candidates, cancellationToken);
        var added = await AddNotificationsAsync(workEvent, plan, allowed, cancellationToken);
        AddFollows(workEvent, plan, allowed, subscriptions);
        await db.SaveChangesAsync(cancellationToken);
        _notified.UnionWith(added);
    }

    // 已讀的也換：收件匣裡看到的要和留言現在的內容一致。編輯不改已讀狀態。
    // ActivityId 是 null 時 EF 會翻成 comment_id IS NULL，波及整個組織沒有留言編號的通知；consumer 已經擋過，這裡再擋一次。
    private async Task RefreshCommentExcerptAsync(WorkEvent workEvent, CancellationToken cancellationToken)
    {
        if (workEvent.Item.ActivityId is null)
        {
            return;
        }

        var excerpt = Truncate(Clean(workEvent.Item.CommentExcerpt));
        var editedAt = (DateTimeOffset?)workEvent.OccurredAt;
        await db.Notifications
            .Where(n => n.OrganizationId == workEvent.OrganizationId && n.CommentId == workEvent.Item.ActivityId)
            .ExecuteUpdateAsync(
                set => set.SetProperty(n => n.Excerpt, excerpt).SetProperty(n => n.ExcerptEditedAt, editedAt), cancellationToken);
    }

    // 項目刪除：它的通知（讀過沒讀過）一起刪掉，復原不找回。帳號通知沒有項目編號，條件比對不到。
    // 先讀出收件人再刪：這些人的未讀數變了，要交給觀察者推給他們的畫面。
    private async Task RemoveItemNotificationsAsync(WorkEvent workEvent, CancellationToken cancellationToken)
    {
        var scope = db.Notifications.Where(n => n.OrganizationId == workEvent.OrganizationId && n.ItemId == workEvent.Item.ItemId);
        var recipients = await scope.Select(n => n.RecipientId).Distinct().ToListAsync(cancellationToken);
        await scope.ExecuteDeleteAsync(cancellationToken);
        _notified.UnionWith(recipients.Select(id => new NotifiedRecipient(workEvent.OrganizationId, id)));
    }

    // 先讀出收件人再刪：這些人的未讀數變了，要交給觀察者推給他們的畫面。
    private async Task RemoveCommentNotificationsAsync(WorkEvent workEvent, CancellationToken cancellationToken)
    {
        if (workEvent.Item.ActivityId is null)
        {
            return;
        }

        var scope = db.Notifications.Where(n => n.OrganizationId == workEvent.OrganizationId && n.CommentId == workEvent.Item.ActivityId);
        var recipients = await scope.Select(n => n.RecipientId).Distinct().ToListAsync(cancellationToken);
        await scope.ExecuteDeleteAsync(cancellationToken);
        _notified.UnionWith(recipients.Select(id => new NotifiedRecipient(workEvent.OrganizationId, id)));
    }

    private async Task<List<NotifiedRecipient>> AddNotificationsAsync(
        WorkEvent workEvent, EventPlan plan, IReadOnlySet<Guid> allowed, CancellationToken cancellationToken)
    {
        var planned = plan.Notifications.Where(n => allowed.Contains(n.RecipientId)).ToList();
        var alreadyMentioned = await AlreadyMentionedAsync(workEvent, planned, cancellationToken);
        List<NotifiedRecipient> added = [];
        foreach (var notification in planned)
        {
            var isRepeatMention = notification.Kind == NotificationKind.Mentioned && alreadyMentioned.Contains(notification.RecipientId);
            if (!isRepeatMention && existing.Add((workEvent.EventId, notification.RecipientId, notification.Kind)))
            {
                db.Notifications.Add(ToRow(workEvent, notification));
                added.Add(new NotifiedRecipient(workEvent.OrganizationId, notification.RecipientId));
            }
        }

        return added;
    }

    /// <summary>
    /// 這一批「提及了你」裡，已經有同樣通知的人：反覆編輯同一則留言、反覆改說明，不該一次次洗收件匣。
    /// 留言：同一則留言只要有過就算（讀過也算，摘要編輯時已經換新）。說明：同一個人對同一個項目、不屬於任何留言、還沒讀的那一則才算，
    /// 讀過之後再提及照常通知；不同的人提及同一個人，各自通知（不然後來的人提及會被先前別人的未讀通知吞掉）。
    /// </summary>
    private async Task<HashSet<Guid>> AlreadyMentionedAsync(
        WorkEvent workEvent, IReadOnlyList<PlannedNotification> planned, CancellationToken cancellationToken)
    {
        Guid[] recipients = [.. planned.Where(n => n.Kind == NotificationKind.Mentioned).Select(n => n.RecipientId)];
        if (recipients.Length == 0)
        {
            return [];
        }

        var mentions = db.Notifications.AsNoTracking()
            .Where(n => n.OrganizationId == workEvent.OrganizationId && n.Kind == NotificationKind.Mentioned && recipients.Contains(n.RecipientId));
        if (workEvent.Kind is WorkEventKind.ItemCommented or WorkEventKind.ItemCommentEdited)
        {
            if (workEvent.Item.ActivityId is not { } commentId)
            {
                return [];
            }

            return [.. await mentions.Where(n => n.CommentId == commentId).Select(n => n.RecipientId).Distinct().ToListAsync(cancellationToken)];
        }

        if (workEvent.Kind is WorkEventKind.ItemCreated or WorkEventKind.ItemUpdated)
        {
            var itemId = workEvent.Item.ItemId;
            var actorId = workEvent.ActorPersonId;
            return [.. await mentions.Where(n => n.ItemId == itemId && n.CommentId == null && n.ReadAt == null && n.ActorPersonId == actorId)
                .Select(n => n.RecipientId).Distinct().ToListAsync(cancellationToken)];
        }

        return [];
    }

    // 有列就不動：取消關注的人不會被自動加回來。
    private void AddFollows(WorkEvent workEvent, EventPlan plan, IReadOnlySet<Guid> allowed, IReadOnlyList<SubscriptionRow> subscriptions)
    {
        var known = subscriptions.Select(s => s.PersonId).ToHashSet();
        foreach (var follow in plan.Follows.Where(f => allowed.Contains(f.PersonId) && !known.Contains(f.PersonId)))
        {
            db.Subscriptions.Add(new SubscriptionRow
            {
                OrganizationId = workEvent.OrganizationId,
                ProjectId = workEvent.Item.ProjectId,
                ItemId = workEvent.Item.ItemId,
                PersonId = follow.PersonId,
                Reason = follow.Reason,
                IsUnsubscribed = false,
                CreatedAt = workEvent.OccurredAt,
            });
        }
    }

    private static NotificationRow ToRow(WorkEvent workEvent, PlannedNotification planned)
    {
        var item = workEvent.Item;
        var isStateChange = planned.Kind == NotificationKind.StateChanged;
        // 和留言有關的三種都帶摘要：收件匣看得到對方說了什麼，不必點進去。
        var hasExcerpt = planned.Kind is NotificationKind.Commented or NotificationKind.Mentioned or NotificationKind.Replied;
        return new NotificationRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = workEvent.OrganizationId,
            RecipientId = planned.RecipientId,
            Kind = planned.Kind,
            ProjectId = item.ProjectId,
            ProjectKey = item.ProjectKey,
            ItemId = item.ItemId,
            ItemKey = item.ItemKey,
            ItemTitle = Clean(item.Title)!,
            ActorPersonId = workEvent.ActorPersonId,
            FromStateName = isStateChange ? Clean(item.FromState?.Name) : null,
            ToStateName = isStateChange ? Clean(item.ToState?.Name) : null,
            Excerpt = hasExcerpt ? Truncate(Clean(item.CommentExcerpt)) : null,
            // 只有留言與編輯留言的事件，活動是一則留言；其他事件的活動不是留言，網頁沒有東西可以捲過去。
            CommentId = workEvent.Kind is WorkEventKind.ItemCommented or WorkEventKind.ItemCommentEdited ? item.ActivityId : null,
            EventId = workEvent.EventId,
            CreatedAt = workEvent.OccurredAt,
        };
    }

    /// <summary>PostgreSQL 的文字欄位不收 NUL；工作引擎已經擋過，這裡再擋一次，免得一則壞資料讓整批卡住。</summary>
    private static string? Clean(string? text) => text?.Replace("\0", string.Empty, StringComparison.Ordinal);

    private static string? Truncate(string? text) =>
        text is { Length: > WorkEventRules.MaxExcerptLength } ? text[..WorkEventRules.MaxExcerptLength] : text;
}
