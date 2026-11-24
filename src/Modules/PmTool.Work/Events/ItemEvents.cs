using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Events;

/// <summary>
/// 每個寫入點在同一個交易裡留下的事件。內容直接存合約的 <see cref="ItemEventData"/>：合約只加不改名，
/// 舊的事件缺的欄位讀回來是預設值，讀的人可能落後很久也讀得回來。
/// 改類型與變更集套用時的搬移不寫事件：前者只動類型與跟著搬的狀態、後者不是人對某個項目做的事，都不是這一輪要通知的。
/// </summary>
internal static class ItemEvents
{
    private const string AttachmentsField = "attachments";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <param name="mentioned">說明裡提及的人，已經過濾（<see cref="Mentions.NotifiableAsync"/>）。</param>
    /// <param name="historyFollowers">不是 null 表示這是歷史匯入（<see cref="ItemEventData.History"/>），內容是要關注的人。</param>
    public static WorkEventEntity Created(
        Actor actor,
        ProjectAccess access,
        ItemType itemType,
        ItemEntity item,
        IReadOnlyList<Guid> mentioned,
        Guid activityId,
        bool imported = false,
        IReadOnlyList<Guid>? historyFollowers = null)
    {
        var first = access.Schema.StateOf(itemType, item.State);
        var data = Data(access, item, mentioned) with
        {
            AssigneeChanged = item.AssigneeId is not null,
            ToState = Snapshot(item.State, first?.Name),
            ActivityId = activityId,
            Imported = imported || historyFollowers is not null,
            History = historyFollowers is not null,
            Followers = historyFollowers ?? [],
        };
        return New(WorkEventKind.ItemCreated, actor, item.CreatedAt, data);
    }

    /// <param name="before">修改之前的那一列（鎖住之後讀的）。</param>
    /// <param name="after">修改之後的樣子。</param>
    /// <param name="described">這一次真正改到的欄位，名稱和活動紀錄的一樣。</param>
    public static WorkEventEntity Updated(
        Actor actor,
        ProjectAccess access,
        ItemEntity before,
        ItemEntity after,
        IReadOnlyList<FieldChange> described,
        IReadOnlyList<Guid> mentioned,
        Guid activityId)
    {
        var assigneeChanged = before.AssigneeId != after.AssigneeId;
        var data = Data(access, after, mentioned) with
        {
            AssigneeChanged = assigneeChanged,
            PreviousAssigneeId = assigneeChanged ? before.AssigneeId : null,
            ChangedFields = [.. described.Select(c => c.Field)],
            ActivityId = activityId,
        };
        return New(WorkEventKind.ItemUpdated, actor, after.UpdatedAt, data);
    }

    public static WorkEventEntity Transitioned(
        Actor actor, ProjectAccess access, ItemType itemType, ItemEntity before, ItemEntity after, Guid activityId)
    {
        var data = Data(access, after, []) with
        {
            FromState = Snapshot(before.State, access.Schema.StateOf(itemType, before.State)?.Name),
            ToState = Snapshot(after.State, access.Schema.StateOf(itemType, after.State)?.Name),
            ActivityId = activityId,
        };
        return New(WorkEventKind.ItemTransitioned, actor, after.UpdatedAt, data);
    }

    public static WorkEventEntity Commented(
        Actor actor,
        ProjectAccess access,
        ItemEntity item,
        DateTimeOffset at,
        string body, IReadOnlyList<Guid> mentioned,
        Guid activityId,
        Guid? replyToCommentId,
        Guid? replyToAuthorId,
        bool history = false)
    {
        var data = Data(access, item, mentioned) with
        {
            CommentExcerpt = Mentions.Excerpt(body),
            ActivityId = activityId,
            ReplyToCommentId = replyToCommentId,
            ReplyToAuthorId = replyToAuthorId,
            Imported = history,
            History = history,
        };
        return New(WorkEventKind.ItemCommented, actor, at, data);
    }

    /// <param name="newMentions">這一版新加的提及（前一版沒有的），已經過濾。摘要是編輯之後的內容。</param>
    public static WorkEventEntity CommentEdited(
        Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, string body, IReadOnlyList<Guid> newMentions, Guid activityId) =>
        New(WorkEventKind.ItemCommentEdited, actor, at, Data(access, item, newMentions) with
        {
            CommentExcerpt = Mentions.Excerpt(body),
            ActivityId = activityId,
        });

    public static WorkEventEntity CommentDeleted(Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, Guid activityId) =>
        New(WorkEventKind.ItemCommentDeleted, actor, at, Data(access, item, []) with { ActivityId = activityId });

    public static WorkEventEntity CommentRestored(Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, Guid activityId) =>
        New(WorkEventKind.ItemCommentRestored, actor, at, Data(access, item, []) with { ActivityId = activityId });

    public static WorkEventEntity Deleted(Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, Guid activityId) =>
        New(WorkEventKind.ItemDeleted, actor, at, Data(access, item, []) with { ActivityId = activityId });

    /// <summary>附件的新增與刪除：項目本身沒變，事件帶項目現在的樣子，改了什麼寫在 <see cref="ItemEventData.ChangedFields"/>。</summary>
    public static WorkEventEntity AttachmentAdded(
        Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, Guid activityId, bool history = false) =>
        New(WorkEventKind.ItemAttachmentAdded, actor, at, AttachmentData(access, item, activityId) with { Imported = history, History = history });

    public static WorkEventEntity AttachmentRemoved(Actor actor, ProjectAccess access, ItemEntity item, DateTimeOffset at, Guid activityId) =>
        New(WorkEventKind.ItemAttachmentRemoved, actor, at, AttachmentData(access, item, activityId));

    /// <param name="after">復原之後的樣子；<paramref name="fromState"/> 只有狀態跟著搬了才有。</param>
    public static WorkEventEntity Restored(
        Actor actor, ProjectAccess access, ItemType itemType, ItemEntity after, string? fromState, Guid activityId)
    {
        var data = Data(access, after, []) with
        {
            FromState = fromState is null ? null : Snapshot(fromState, access.Schema.StateOf(itemType, fromState)?.Name),
            ToState = Snapshot(after.State, access.Schema.StateOf(itemType, after.State)?.Name),
            ActivityId = activityId,
        };
        return New(WorkEventKind.ItemRestored, actor, after.UpdatedAt, data);
    }

    /// <summary>讀回事件的內容（事件流與測試用）。</summary>
    public static ItemEventData ReadData(string content) =>
        JsonSerializer.Deserialize<ItemEventData>(content, Options)
            ?? throw new InvalidOperationException("事件的內容是空的。");

    /// <summary>
    /// 不認得的種類（新版寫的、這一版還不懂）讀成 <see cref="WorkEventKind.Unknown"/>，不丟例外：
    /// 事件流是整批讀的，丟例外會讓所有讀者永遠卡在這一筆前面。要不要略過、怎麼記錄是讀者的事（合約的註解寫了規矩）。
    /// </summary>
    public static WorkEventKind ParseKind(string text) => text switch
    {
        "item_created" => WorkEventKind.ItemCreated,
        "item_updated" => WorkEventKind.ItemUpdated,
        "item_transitioned" => WorkEventKind.ItemTransitioned,
        "item_commented" => WorkEventKind.ItemCommented,
        "item_deleted" => WorkEventKind.ItemDeleted,
        "item_restored" => WorkEventKind.ItemRestored,
        "item_attachment_added" => WorkEventKind.ItemAttachmentAdded,
        "item_attachment_removed" => WorkEventKind.ItemAttachmentRemoved,
        "item_comment_edited" => WorkEventKind.ItemCommentEdited,
        "item_comment_deleted" => WorkEventKind.ItemCommentDeleted,
        "item_comment_restored" => WorkEventKind.ItemCommentRestored,
        _ => WorkEventKind.Unknown,
    };

    /// <summary>不認得的種類或讀不懂的內容用的佔位資料；讀者看到 <see cref="WorkEventKind.Unknown"/> 就不會去用它。</summary>
    public static ItemEventData Placeholder { get; } = new(
        Guid.Empty, string.Empty, Guid.Empty, string.Empty, string.Empty, Guid.Empty, null, false, null, null, null, null, [], []);

    // 存的是固定的文字，不是列舉的數字或名稱：列舉改了順序或名字，舊事件的意思也不會跟著變。
    private static string KindText(WorkEventKind kind) => kind switch
    {
        WorkEventKind.ItemCreated => "item_created",
        WorkEventKind.ItemUpdated => "item_updated",
        WorkEventKind.ItemTransitioned => "item_transitioned",
        WorkEventKind.ItemCommented => "item_commented",
        WorkEventKind.ItemDeleted => "item_deleted",
        WorkEventKind.ItemRestored => "item_restored",
        WorkEventKind.ItemAttachmentAdded => "item_attachment_added",
        WorkEventKind.ItemAttachmentRemoved => "item_attachment_removed",
        WorkEventKind.ItemCommentEdited => "item_comment_edited",
        WorkEventKind.ItemCommentDeleted => "item_comment_deleted",
        WorkEventKind.ItemCommentRestored => "item_comment_restored",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的事件種類。"),
    };

    private static ItemEventData AttachmentData(ProjectAccess access, ItemEntity item, Guid activityId) =>
        Data(access, item, []) with { ChangedFields = [AttachmentsField], ActivityId = activityId };

    private static ItemEventData Data(ProjectAccess access, ItemEntity item, IReadOnlyList<Guid> mentioned) => new(
        access.Project.Id,
        access.Project.Key,
        item.Id,
        ItemKeys.Format(access.Project.Key, item.Number),
        item.Title,
        item.CreatedBy,
        item.AssigneeId,
        AssigneeChanged: false,
        PreviousAssigneeId: null,
        FromState: null,
        ToState: null,
        CommentExcerpt: null,
        mentioned,
        ChangedFields: []);

    // 藍圖之後可能把狀態改名或拿掉；當時的名稱找不到時退回 key，事件裡至少有一個看得懂的標示。
    private static StateSnapshot Snapshot(string key, string? name) => new(key, name ?? key);

    private static WorkEventEntity New(WorkEventKind kind, Actor actor, DateTimeOffset at, ItemEventData data) => new()
    {
        EventId = Guid.CreateVersion7(at),
        OrganizationId = actor.OrganizationId,
        Kind = KindText(kind),
        ActorId = actor.PersonId,
        OccurredAt = at,
        Content = JsonSerializer.Serialize(data, Options),
    };
}
