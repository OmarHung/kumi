using System.Text.Json;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>一個欄位的前後值。值照模組給的 JSON 原樣輸出（人員編號、項目編號、snake_case 的優先順序），API 不轉換。</summary>
internal sealed record FieldChangeData(string Field, JsonElement? From, JsonElement? To);

/// <summary>
/// 活動在 API 上的形狀。每一種活動共用一個形狀，用不到的屬性寫成 null（changes 是空陣列），
/// 網頁只要看 kind，不必先判斷屬性在不在。做事的人帶顯示名稱，網頁不必另外查。
/// </summary>
/// <param name="ReplyToId">只有回覆有：它回覆的最上層留言。</param>
/// <param name="ImportedFrom">從別的系統搬過來的歷史：來源的名字（例如 monday）；這裡建立的是 null。</param>
/// <param name="BlueprintVersion">只有 migrated 有：套用之後的藍圖版本。</param>
/// <param name="EditedAt">只有留言有：最後一次編輯的時間；沒編輯過是 null。</param>
/// <param name="DeletedAt">只有留言有：刪除的時間；刪除的留言 <c>comment</c> 是 null。</param>
/// <param name="DeletedBy">刪除的人，和 actor 一樣帶顯示名稱；沒刪除是 null。</param>
internal sealed record ActivityData(
    Guid Id,
    ActivityKind Kind,
    PersonReference Actor,
    DateTimeOffset At,
    IReadOnlyList<FieldChangeData> Changes,
    string? FromState,
    string? ToState,
    string? Comment,
    Guid? ReplyToId,
    string? ImportedFrom,
    long? BlueprintVersion,
    DateTimeOffset? EditedAt,
    DateTimeOffset? DeletedAt,
    PersonReference? DeletedBy)
{
    public static ActivityData From(ItemActivity activity, PeopleNames names) => new(
        activity.Id,
        activity.Kind,
        new PersonReference(activity.ActorId, names.NameOf(activity.ActorId)),
        activity.At,
        [.. activity.Changes.Select(change => new FieldChangeData(change.Field, change.From, change.To))],
        activity.FromState,
        activity.ToState,
        activity.Comment,
        activity.ReplyToId,
        activity.ImportedFrom,
        activity.BlueprintVersion,
        activity.EditedAt,
        activity.DeletedAt,
        activity.DeletedBy is { } deleter ? new PersonReference(deleter, names.NameOf(deleter)) : null);

    /// <summary>一個回應裡所有做事的人一次查完。</summary>
    public static async Task<IReadOnlyList<ActivityData>> FromManyAsync(
        IReadOnlyList<ItemActivity> activities, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            people,
            organizationId,
            activities.Select(activity => activity.ActorId).Concat(activities.Select(activity => activity.DeletedBy).OfType<Guid>()),
            cancellationToken);
        return [.. activities.Select(activity => From(activity, names))];
    }
}

internal sealed record ActivityPageData(IReadOnlyList<ActivityData> Items, string? NextCursor);

/// <summary>留言。空白或太長由工作引擎判斷，這裡只要求有一個字串；replyTo 不是 GUID 時內文解析就失敗（400）。</summary>
internal sealed record CommentRequest(string Body, Guid? ReplyTo = null);

/// <summary>編輯留言的新內容。空白或太長由工作引擎判斷，這裡只要求有一個字串。</summary>
internal sealed record CommentEditRequest(string Body);

/// <summary>一則留言的一個版本；新的在前，第一筆是現在的內容。</summary>
internal sealed record CommentRevisionData(int Number, string Body, DateTimeOffset At, bool IsCurrent)
{
    public static CommentRevisionData From(CommentRevision revision) => new(revision.Number, revision.Body, revision.At, revision.IsCurrent);
}

internal sealed record CommentRevisionListData(IReadOnlyList<CommentRevisionData> Items);
