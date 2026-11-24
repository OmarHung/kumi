using PmTool.Work;

namespace PmTool.Notifications.Events;

internal sealed record PlannedNotification(Guid RecipientId, NotificationKind Kind);

internal sealed record PlannedFollow(Guid PersonId, SubscriptionReason Reason);

/// <param name="RemovesSubscriptions">項目刪除了：它的關注全部移除。</param>
internal sealed record EventPlan(
    IReadOnlyList<PlannedNotification> Notifications, IReadOnlyList<PlannedFollow> Follows, bool RemovesSubscriptions)
{
    public static readonly EventPlan Nothing = new([], [], false);
}

/// <summary>
/// 一個事件要通知誰、讓誰自動關注（規劃書的表）。純邏輯：還沒看專案看不看得到，那是讀者接著用工作引擎過濾的。
/// 做事的人自己不收通知；同一個人在同一個事件只收一則，被指派比被提及優先、被提及比回覆優先、回覆比留言優先。
/// </summary>
internal static class NotificationPlanner
{
    /// <param name="followers">現在關注這個項目的人（取消關注的不在裡面）。</param>
    public static EventPlan Plan(WorkEvent workEvent, IReadOnlySet<Guid> followers)
    {
        if (workEvent.Item.History)
        {
            return PlanHistory(workEvent);
        }

        var item = workEvent.Item;
        var actor = workEvent.ActorPersonId;
        var mentioned = item.MentionedPersonIds.Distinct().Take(WorkEventRules.MaxMentionsPerChange).ToList();

        return workEvent.Kind switch
        {
            WorkEventKind.ItemCreated => Created(item, actor, mentioned),
            WorkEventKind.ItemUpdated => Updated(item, actor, mentioned),
            WorkEventKind.ItemTransitioned => Transitioned(actor, followers),
            WorkEventKind.ItemCommented => Commented(item, actor, mentioned, followers),
            WorkEventKind.ItemCommentEdited => CommentEdited(actor, mentioned),
            WorkEventKind.ItemDeleted => EventPlan.Nothing with { RemovesSubscriptions = true },
            WorkEventKind.ItemRestored => Restored(item),

            // 附件不是對誰有新的要求，也不加關注；明確寫出來，才不會被當成不認得的種類。
            WorkEventKind.ItemAttachmentAdded or WorkEventKind.ItemAttachmentRemoved => EventPlan.Nothing,

            // 刪除留言在 applier 處理（把那則留言的通知清掉）；復原不找回通知也不重新通知。明確寫出來，理由同上。
            WorkEventKind.ItemCommentDeleted or WorkEventKind.ItemCommentRestored => EventPlan.Nothing,
            _ => EventPlan.Nothing,
        };
    }

    // 從別的系統搬過來的歷史：一則通知都不發（幾年前的留言、提及、指派不是現在發生的事）。
    // 只有建立項目會建立關注：負責人與匯入時指定的人；不自動關注建立的人，原本的建立者常常是 PM，一次匯入上千個項目會讓他關注全部。
    // 留言、附件與其他事件什麼都不做：留言的作者不因為匯入而關注。
    private static EventPlan PlanHistory(WorkEvent workEvent)
    {
        if (workEvent.Kind != WorkEventKind.ItemCreated)
        {
            return EventPlan.Nothing;
        }

        var item = workEvent.Item;
        var follows = new Follows();
        follows.Add(item.AssigneeId, SubscriptionReason.Assignee);
        follows.AddAll(item.Followers, SubscriptionReason.Manual);
        return new EventPlan([], follows.ToList(), false);
    }

    private static EventPlan Created(ItemEventData item, Guid actor, List<Guid> mentioned)
    {
        var notices = new Notices(actor);

        // 匯入一次建幾百個項目，指派給同一個人的話收件匣會被洗掉；自動關注照舊，要看的人從關注找得到。
        if (!item.Imported)
        {
            notices.Add(item.AssigneeId, NotificationKind.Assigned);
        }

        notices.AddAll(mentioned, NotificationKind.Mentioned);

        var follows = new Follows();
        follows.Add(item.CreatedBy, SubscriptionReason.Creator);
        follows.Add(item.AssigneeId, SubscriptionReason.Assignee);
        follows.AddAll(mentioned, SubscriptionReason.Mentioned);
        return new EventPlan(notices.ToList(), follows.ToList(), false);
    }

    // 刪除時關注全部清掉了（連取消關注的紀錄），復原只加回建立的人與目前的負責人，和新增時自動關注的規則一樣；不通知：
    // 復原是把東西放回原處，不是對誰有新的要求。
    private static EventPlan Restored(ItemEventData item)
    {
        var follows = new Follows();
        follows.Add(item.CreatedBy, SubscriptionReason.Creator);
        follows.Add(item.AssigneeId, SubscriptionReason.Assignee);
        return new EventPlan([], follows.ToList(), false);
    }

    private static EventPlan Updated(ItemEventData item, Guid actor, List<Guid> mentioned)
    {
        var newAssignee = item.AssigneeChanged ? item.AssigneeId : null;
        var notices = new Notices(actor);
        notices.Add(newAssignee, NotificationKind.Assigned);
        notices.AddAll(mentioned, NotificationKind.Mentioned);

        var follows = new Follows();
        follows.Add(newAssignee, SubscriptionReason.Assignee);
        follows.AddAll(mentioned, SubscriptionReason.Mentioned);
        return new EventPlan(notices.ToList(), follows.ToList(), false);
    }

    private static EventPlan Transitioned(Guid actor, IReadOnlySet<Guid> followers)
    {
        var notices = new Notices(actor);
        notices.AddAll(followers, NotificationKind.StateChanged);
        return new EventPlan(notices.ToList(), [], false);
    }

    private static EventPlan Commented(ItemEventData item, Guid actor, List<Guid> mentioned, IReadOnlySet<Guid> followers)
    {
        // 先加提及，同一個人之後當關注的人再加一次時會被略過，所以只收「提及」一則。
        var notices = new Notices(actor);
        notices.AddAll(mentioned, NotificationKind.Mentioned);

        // 回覆的對象不論有沒有關注都收到；自己回覆自己不通知（Notices 會略過做事的人）。
        notices.Add(item.ReplyToAuthorId, NotificationKind.Replied);
        notices.AddAll(followers, NotificationKind.Commented);

        var follows = new Follows();
        follows.Add(actor, SubscriptionReason.Commenter);
        follows.AddAll(mentioned, SubscriptionReason.Mentioned);
        return new EventPlan(notices.ToList(), follows.ToList(), false);
    }

    // 編輯不是新的留言：不通知關注的人、不發「回覆了你」、不加關注，只有這一版新加的提及照留言的規則通知。
    private static EventPlan CommentEdited(Guid actor, List<Guid> mentioned)
    {
        var notices = new Notices(actor);
        notices.AddAll(mentioned, NotificationKind.Mentioned);
        return new EventPlan(notices.ToList(), [], false);
    }

    private sealed class Notices(Guid actor)
    {
        private readonly Dictionary<Guid, NotificationKind> _byRecipient = [];

        public void Add(Guid? recipient, NotificationKind kind)
        {
            if (recipient is { } id && id != actor)
            {
                _byRecipient.TryAdd(id, kind);
            }
        }

        public void AddAll(IEnumerable<Guid> recipients, NotificationKind kind)
        {
            foreach (var recipient in recipients)
            {
                Add(recipient, kind);
            }
        }

        public IReadOnlyList<PlannedNotification> ToList() =>
            [.. _byRecipient.Select(pair => new PlannedNotification(pair.Key, pair.Value))];
    }

    private sealed class Follows
    {
        private readonly Dictionary<Guid, SubscriptionReason> _byPerson = [];

        public void Add(Guid? person, SubscriptionReason reason)
        {
            if (person is { } id)
            {
                _byPerson.TryAdd(id, reason);
            }
        }

        public void AddAll(IEnumerable<Guid> people, SubscriptionReason reason)
        {
            foreach (var person in people)
            {
                Add(person, reason);
            }
        }

        public IReadOnlyList<PlannedFollow> ToList() => [.. _byPerson.Select(pair => new PlannedFollow(pair.Key, pair.Value))];
    }
}
