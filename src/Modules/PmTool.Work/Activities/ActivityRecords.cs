using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Abstractions;
using PmTool.Work.ChangeSets;
using PmTool.Work.Persistence;

namespace PmTool.Work.Activities;

/// <summary>
/// 活動在 jsonb 裡的內容。和合約的型別分開：合約之後改了，已經存著的舊紀錄仍然讀得回來。
/// 沒有的部分不寫出來，種類決定有哪些。
/// </summary>
internal sealed record ActivityContent
{
    public IReadOnlyList<StoredChange>? Changes { get; init; }

    public string? FromState { get; init; }

    public string? ToState { get; init; }

    public string? Comment { get; init; }

    public long? BlueprintVersion { get; init; }
}

/// <summary>一個欄位的前後值，形狀見 <see cref="FieldChange"/>；沒有值的那一邊不寫出來。</summary>
internal sealed record StoredChange(string Field, JsonElement? From, JsonElement? To);

/// <summary>活動的資料表列與合約的 <see cref="ItemActivity"/> 之間的轉換。</summary>
internal static class ActivityRecords
{
    public const string AttachmentField = "attachment";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>建立的時間就是項目的建立時間，兩邊才對得上。<paramref name="importedFrom"/> 是歷史匯入的來源。</summary>
    public static ItemActivityEntity Created(Actor actor, ItemEntity item, string? importedFrom = null) =>
        New(actor, item, ActivityKind.Created, item.CreatedAt, new ActivityContent()) with { ImportedFrom = importedFrom };

    /// <param name="at">和項目新的 updatedAt 相同。</param>
    public static ItemActivityEntity Updated(Actor actor, ItemEntity item, DateTimeOffset at, IReadOnlyList<FieldChange> changes) =>
        New(actor, item, ActivityKind.Updated, at, new ActivityContent
        {
            Changes = [.. changes.Select(c => new StoredChange(c.Field, c.From, c.To))],
        });

    public static ItemActivityEntity Transitioned(Actor actor, ItemEntity item, DateTimeOffset at, string fromState, string toState) =>
        New(actor, item, ActivityKind.Transitioned, at, new ActivityContent { FromState = fromState, ToState = toState });

    /// <param name="replyToId">回覆的最上層留言；最上層的留言是 null。</param>
    /// <param name="importedFrom">歷史匯入的來源；這裡建立的是 null。</param>
    public static ItemActivityEntity Commented(
        Actor actor, ItemEntity item, DateTimeOffset at, string comment, Guid? replyToId, string? importedFrom = null) =>
        New(actor, item, ActivityKind.Commented, at, new ActivityContent { Comment = comment })
        with { ReplyToId = replyToId, ImportedFrom = importedFrom };

    /// <summary>資料表裡留言的種類文字；查回覆的對象時用。</summary>
    public static string CommentedKind => KindText(ActivityKind.Commented);

    /// <param name="at">套用的時刻，和項目新的 updatedAt 相同。</param>
    /// <param name="blueprintVersion">套用之後的藍圖版本。</param>
    public static ItemActivityEntity Migrated(
        Actor actor, MigratingItem item, DateTimeOffset at, long blueprintVersion, string? fromState, string? toState, IReadOnlyList<FieldChange> changes) =>
        New(actor, (item.OrganizationId, item.ProjectId, item.Id), ActivityKind.Migrated, at, new ActivityContent
        {
            Changes = changes.Count == 0 ? null : [.. changes.Select(c => new StoredChange(c.Field, c.From, c.To))],
            FromState = fromState,
            ToState = toState,
            BlueprintVersion = blueprintVersion,
        });

    /// <param name="at">和項目新的 updatedAt 相同。</param>
    /// <param name="changes">第一筆是 item_type 的前後值，接著是封存的自訂欄位。</param>
    /// <param name="fromState">狀態跟著搬了才有；沒變是 null，<paramref name="toState"/> 也一樣。</param>
    public static ItemActivityEntity TypeChanged(
        Actor actor, ItemEntity item, DateTimeOffset at, IReadOnlyList<FieldChange> changes, string? fromState, string? toState) =>
        New(actor, item, ActivityKind.TypeChanged, at, new ActivityContent
        {
            Changes = [.. changes.Select(c => new StoredChange(c.Field, c.From, c.To))],
            FromState = fromState,
            ToState = toState,
        });

    /// <param name="at">刪除的時刻，和項目的 deleted_at 相同。</param>
    public static ItemActivityEntity Deleted(Actor actor, ItemEntity item, DateTimeOffset at) =>
        New(actor, item, ActivityKind.Deleted, at, new ActivityContent());

    /// <param name="at">復原的時刻，和項目新的 updatedAt 相同。</param>
    /// <param name="changes">封存的自訂欄位與拿掉的上層；都沒有時是空的。</param>
    /// <param name="fromState">狀態跟著搬了才有；沒變是 null，<paramref name="toState"/> 也一樣。</param>
    public static ItemActivityEntity Restored(
        Actor actor, ItemEntity item, DateTimeOffset at, IReadOnlyList<FieldChange> changes, string? fromState, string? toState) =>
        New(actor, item, ActivityKind.Restored, at, new ActivityContent
        {
            Changes = changes.Count == 0 ? null : [.. changes.Select(c => new StoredChange(c.Field, c.From, c.To))],
            FromState = fromState,
            ToState = toState,
        });

    /// <param name="at">上傳的時刻，和附件的 uploaded_at 相同。</param>
    /// <param name="importedFrom">歷史匯入的來源；這裡建立的是 null。</param>
    public static ItemActivityEntity AttachmentAdded(Actor actor, ItemEntity item, DateTimeOffset at, string fileName, string? importedFrom = null) =>
        New(actor, item, ActivityKind.AttachmentAdded, at, new ActivityContent { Changes = [AttachmentChange(null, fileName)] })
        with { ImportedFrom = importedFrom };

    /// <param name="at">刪除的時刻。</param>
    public static ItemActivityEntity AttachmentRemoved(Actor actor, ItemEntity item, DateTimeOffset at, string fileName) =>
        New(actor, item, ActivityKind.AttachmentRemoved, at, new ActivityContent { Changes = [AttachmentChange(fileName, null)] });

    /// <param name="itemKey">項目的正式編號（專案代號大寫），不是呼叫端給的寫法。</param>
    public static ItemActivity ToActivity(ItemActivityEntity entity, string itemKey)
    {
        // 內容是這個模組自己寫進去的；讀不回來表示資料被動過，是要修的錯，不是使用者的問題。
        var content = JsonSerializer.Deserialize<ActivityContent>(entity.Content, Options)
            ?? throw new InvalidOperationException($"活動 {entity.Id} 的內容是空的。");
        return new ItemActivity(entity.Id, itemKey, ParseKind(entity.Kind), entity.ActorId, entity.At)
        {
            Changes = [.. (content.Changes ?? []).Select(c => new FieldChange(c.Field, c.From, c.To))],
            FromState = content.FromState,
            ToState = content.ToState,
            // 刪除的留言內容留在資料庫，但只有這裡一個地方藏起來：所有讀活動的路（API、MCP、AI、CLI）都經過它。
            Comment = entity.DeletedAt is null ? content.Comment : null,
            EditedAt = entity.EditedAt,
            DeletedAt = entity.DeletedAt,
            DeletedBy = entity.DeletedBy,
            ReplyToId = entity.ReplyToId,
            ImportedFrom = entity.ImportedFrom,
            BlueprintVersion = content.BlueprintVersion,
        };
    }

    /// <summary>留言存著的內容（不看有沒有刪除）；編輯與看版本要比對、留舊版本時用，不對外給。</summary>
    public static string StoredComment(ItemActivityEntity entity) =>
        JsonSerializer.Deserialize<ActivityContent>(entity.Content, Options)?.Comment
            ?? throw new InvalidOperationException($"留言 {entity.Id} 沒有內容。");

    /// <summary>編輯之後要寫回 content 的文字。</summary>
    public static string CommentContent(string comment) => JsonSerializer.Serialize(new ActivityContent { Comment = comment }, Options);

    /// <summary>附件的活動只有一筆變更：欄位叫 attachment，前後值是檔名。</summary>
    private static StoredChange AttachmentChange(string? from, string? to) => new(
        AttachmentField,
        from is null ? null : JsonSerializer.SerializeToElement(from),
        to is null ? null : JsonSerializer.SerializeToElement(to));

    private static ItemActivityEntity New(Actor actor, ItemEntity item, ActivityKind kind, DateTimeOffset at, ActivityContent content) =>
        New(actor, (item.OrganizationId, item.ProjectId, item.Id), kind, at, content);

    private static ItemActivityEntity New(
        Actor actor, (Guid OrganizationId, Guid ProjectId, Guid Id) item, ActivityKind kind, DateTimeOffset at, ActivityContent content) => new()
        {
            // 主鍵的時間取自活動本身的時間：同一個項目的活動依主鍵排，大致就是發生的順序。
            Id = Guid.CreateVersion7(at),
            OrganizationId = item.OrganizationId,
            ProjectId = item.ProjectId,
            ItemId = item.Id,
            ActorId = actor.PersonId,
            Kind = KindText(kind),
            At = at,
            Content = JsonSerializer.Serialize(content, Options),
        };

    // 存的是固定的文字，不是列舉的數字或名稱：列舉改了順序或名字，舊紀錄的意思也不會跟著變。
    internal static string KindText(ActivityKind kind) => kind switch
    {
        ActivityKind.Created => "created",
        ActivityKind.Updated => "updated",
        ActivityKind.Transitioned => "transitioned",
        ActivityKind.Commented => "commented",
        ActivityKind.Migrated => "migrated",
        ActivityKind.TypeChanged => "type_changed",
        ActivityKind.Deleted => "deleted",
        ActivityKind.Restored => "restored",
        ActivityKind.AttachmentAdded => "attachment_added",
        ActivityKind.AttachmentRemoved => "attachment_removed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的活動種類。"),
    };

    private static ActivityKind ParseKind(string text) => text switch
    {
        "created" => ActivityKind.Created,
        "updated" => ActivityKind.Updated,
        "transitioned" => ActivityKind.Transitioned,
        "commented" => ActivityKind.Commented,
        "migrated" => ActivityKind.Migrated,
        "type_changed" => ActivityKind.TypeChanged,
        "deleted" => ActivityKind.Deleted,
        "restored" => ActivityKind.Restored,
        "attachment_added" => ActivityKind.AttachmentAdded,
        "attachment_removed" => ActivityKind.AttachmentRemoved,
        _ => throw new InvalidOperationException($"資料表裡有不認得的活動種類 {text}。"),
    };
}
