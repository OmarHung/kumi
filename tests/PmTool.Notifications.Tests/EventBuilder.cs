using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>造工作引擎的事件餵給讀者。位置照造的順序遞增，和真的事件流一樣。</summary>
internal sealed class EventBuilder(Guid organizationId, Guid projectId, Guid itemId)
{
    /// <summary>所有項目共用一個遞增的流水號，和真的事件流一樣：位置在整條流上是唯一的。</summary>
    private static long _sequence;

    public Guid OrganizationId { get; } = organizationId;

    public Guid ProjectId { get; } = projectId;

    public Guid ItemId { get; } = itemId;

    public static EventBuilder ForNewItem() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    public WorkEvent Created(Guid actor, Guid? assignee = null, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCreated, actor, actor, item => item with
        {
            AssigneeId = assignee,
            ToState = new StateSnapshot("todo", "待辦"),
            MentionedPersonIds = mentioned,
        });

    /// <summary>匯入建立的項目：<c>imported</c> 是 true。</summary>
    public WorkEvent CreatedByImport(Guid actor, Guid? assignee = null, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCreated, actor, actor, item => item with
        {
            AssigneeId = assignee,
            ToState = new StateSnapshot("todo", "待辦"),
            MentionedPersonIds = mentioned,
            Imported = true,
        });

    /// <summary>歷史匯入建立的項目（<c>History</c>）：<c>followers</c> 是匯入時指定要關注的人，<c>creator</c> 是原本的建立者。</summary>
    public WorkEvent CreatedByHistory(Guid creator, Guid? assignee, Guid[] followers, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCreated, creator, creator, item => item with
        {
            AssigneeId = assignee,
            ToState = new StateSnapshot("todo", "待辦"),
            MentionedPersonIds = mentioned,
            Imported = true,
            History = true,
            Followers = followers,
        });

    /// <summary>歷史匯入的留言：<c>replyToAuthor</c> 不是 null 時是回覆。</summary>
    public WorkEvent CommentedByHistory(Guid author, string excerpt, Guid? replyToAuthor = null, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCommented, author, author, item => item with
        {
            CommentExcerpt = excerpt,
            MentionedPersonIds = mentioned,
            ReplyToCommentId = replyToAuthor is null ? null : Guid.CreateVersion7(),
            ReplyToAuthorId = replyToAuthor,
            Imported = true,
            History = true,
        });

    public WorkEvent AttachmentAddedByHistory(Guid uploader, Guid creator, Guid? assignee = null) =>
        Build(WorkEventKind.ItemAttachmentAdded, uploader, creator, item => item with
        {
            AssigneeId = assignee,
            ChangedFields = ["attachments"],
            Imported = true,
            History = true,
        });

    public WorkEvent Updated(Guid actor, Guid? newAssignee = null, bool assigneeChanged = false, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemUpdated, actor, actor, item => item with
        {
            AssigneeId = newAssignee,
            AssigneeChanged = assigneeChanged,
            MentionedPersonIds = mentioned,
            ChangedFields = ["assignee"],
        });

    public WorkEvent Transitioned(Guid actor, string from = "待辦", string to = "進行中") =>
        Build(WorkEventKind.ItemTransitioned, actor, actor, item => item with
        {
            FromState = new StateSnapshot("from", from),
            ToState = new StateSnapshot("to", to),
        });

    public WorkEvent Commented(Guid actor, string excerpt, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCommented, actor, actor, item => item with
        {
            CommentExcerpt = excerpt,
            MentionedPersonIds = mentioned,
        });

    /// <summary>已知編號的留言：之後編輯、刪除的事件用同一個編號指到它。</summary>
    public WorkEvent CommentedAs(Guid commentId, Guid actor, string excerpt, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCommented, actor, actor, item => item with
        {
            ActivityId = commentId,
            CommentExcerpt = excerpt,
            MentionedPersonIds = mentioned,
        });

    /// <summary>編輯留言：<c>mentioned</c> 是這一版新加的提及，作者與回覆對象一律沒有。</summary>
    public WorkEvent CommentEdited(Guid commentId, Guid actor, string excerpt, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCommentEdited, actor, actor, item => item with
        {
            ActivityId = commentId,
            CommentExcerpt = excerpt,
            MentionedPersonIds = mentioned,
        });

    public WorkEvent CommentDeleted(Guid commentId, Guid actor) =>
        Build(WorkEventKind.ItemCommentDeleted, actor, actor, item => item with { ActivityId = commentId });

    public WorkEvent CommentRestored(Guid commentId, Guid actor) =>
        Build(WorkEventKind.ItemCommentRestored, actor, actor, item => item with { ActivityId = commentId });

    /// <summary>回覆：<c>replyToAuthor</c> 是最上層那一則留言的作者。</summary>
    public WorkEvent Replied(Guid actor, Guid replyToAuthor, string excerpt, params Guid[] mentioned) =>
        Build(WorkEventKind.ItemCommented, actor, actor, item => item with
        {
            CommentExcerpt = excerpt,
            MentionedPersonIds = mentioned,
            ReplyToCommentId = Guid.CreateVersion7(),
            ReplyToAuthorId = replyToAuthor,
        });

    public WorkEvent Deleted(Guid actor) => Build(WorkEventKind.ItemDeleted, actor, actor, item => item);

    /// <param name="creator">建立項目的人；復原事件帶的是項目原本的建立者，不是做復原的人。</param>
    public WorkEvent Restored(Guid actor, Guid creator, Guid? assignee = null) =>
        Build(WorkEventKind.ItemRestored, actor, creator, item => item with
        {
            AssigneeId = assignee,
            ToState = new StateSnapshot("todo", "待辦"),
        });

    public WorkEvent AttachmentAdded(Guid actor, Guid creator, Guid? assignee = null) =>
        Build(WorkEventKind.ItemAttachmentAdded, actor, creator, item => item with { AssigneeId = assignee, ChangedFields = ["attachments"] });

    public WorkEvent AttachmentRemoved(Guid actor, Guid creator, Guid? assignee = null) =>
        Build(WorkEventKind.ItemAttachmentRemoved, actor, creator, item => item with { AssigneeId = assignee, ChangedFields = ["attachments"] });

    /// <summary>加 <c>activity_id</c> 之前寫的事件：沒有活動的編號。</summary>
    public static WorkEvent Legacy(WorkEvent workEvent) => workEvent with { Item = workEvent.Item with { ActivityId = null } };

    private WorkEvent Build(WorkEventKind kind, Guid actor, Guid createdBy, Func<ItemEventData, ItemEventData> shape)
    {
        // 真的事件每一筆都帶一筆活動的編號（Legacy 拿掉它，模擬加這個欄位之前寫的事件）。
        var item = shape(new ItemEventData(
            ProjectId, "WEB", ItemId, "WEB-1", "範例項目", createdBy, null, false, null, null, null, null, [], [])
        {
            ActivityId = Guid.CreateVersion7(),
        });
        return new WorkEvent(
            Guid.CreateVersion7(),
            new WorkEventPosition(TransactionId: 1, Sequence: Interlocked.Increment(ref _sequence)),
            kind, OrganizationId, actor, DateTimeOffset.UtcNow, item);
    }
}
