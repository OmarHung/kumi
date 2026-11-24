using System.Text.Json;
using System.Text.Json.Serialization;
using PmTool.Api.Json;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 歷史匯入的一列項目：新增項目的內容（不收 parentKey，上層用 parentExternalId），再加外部編號、狀態、建立的人與時間、關注的人。
/// 欄位的意義與規則見 <see cref="IHistoryImportService"/>。
/// </summary>
internal sealed record HistoryRowRequest(
    string ExternalId,
    string Type,
    string Title,
    Guid CreatedBy,
    [property: JsonConverter(typeof(ZonedDateTimeOffsetConverter))] DateTimeOffset CreatedAt,
    Guid? AssigneeId = null,
    Priority? Priority = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    decimal? Estimate = null,
    string? Description = null,
    IReadOnlyDictionary<string, JsonElement>? Fields = null,
    Guid? CycleId = null,
    Guid? MilestoneId = null,
    string? State = null,
    string? ParentExternalId = null,
    IReadOnlyList<Guid>? Followers = null)
{
    public HistoryItem ToHistoryItem() => new()
    {
        ExternalId = ExternalId,
        Item = new CreateItemRequest(Type, Title, AssigneeId, Priority, StartDate, DueDate, Estimate, null, Description, Fields, CycleId, MilestoneId)
            .ToNewWorkItem(),
        State = State,
        ParentExternalId = ParentExternalId,
        CreatedBy = CreatedBy,
        CreatedAt = CreatedAt,
        Followers = Followers ?? [],
    };
}

internal sealed record HistoryItemsRequest(string? Source = null, IReadOnlyList<HistoryRowRequest>? Rows = null, bool DryRun = false)
{
    public HistoryItems ToHistoryItems() => new()
    {
        Source = Source ?? string.Empty,
        Rows = [.. (Rows ?? []).Select(row => row.ToHistoryItem())],
        DryRun = DryRun,
    };
}

internal sealed record HistoryCommentRequest(
    string ExternalId, string ItemExternalId, Guid AuthorId, [property: JsonConverter(typeof(ZonedDateTimeOffsetConverter))] DateTimeOffset At, string Body, string? ReplyToExternalId = null)
{
    public HistoryComment ToHistoryComment() => new()
    {
        ExternalId = ExternalId,
        ItemExternalId = ItemExternalId,
        AuthorId = AuthorId,
        At = At,
        Body = Body,
        ReplyToExternalId = ReplyToExternalId,
    };
}

internal sealed record HistoryCommentsRequest(string? Source = null, IReadOnlyList<HistoryCommentRequest>? Comments = null)
{
    public HistoryComments ToHistoryComments() => new()
    {
        Source = Source ?? string.Empty,
        Comments = [.. (Comments ?? []).Select(comment => comment.ToHistoryComment())],
    };
}

internal sealed record HistoryLookupRequest(string? Source = null, HistoryRefKind Kind = HistoryRefKind.Item, IReadOnlyList<string>? ExternalIds = null)
{
    public HistoryLookup ToHistoryLookup() => new() { Source = Source ?? string.Empty, Kind = Kind, ExternalIds = ExternalIds ?? [] };
}

internal sealed record HistoryItemRefData(string ExternalId, string ItemKey)
{
    public static HistoryItemRefData From(HistoryItemRef reference) => new(reference.ExternalId, reference.ItemKey);
}

internal sealed record HistoryItemsData(bool DryRun, IReadOnlyList<HistoryItemRefData> Created, IReadOnlyList<HistoryItemRefData> Skipped)
{
    public static HistoryItemsData From(HistoryItemsResult result) => new(
        result.DryRun, [.. result.Created.Select(HistoryItemRefData.From)], [.. result.Skipped.Select(HistoryItemRefData.From)]);
}

internal sealed record HistoryCommentRefData(string ExternalId, Guid ActivityId)
{
    public static HistoryCommentRefData From(HistoryCommentRef reference) => new(reference.ExternalId, reference.ActivityId);
}

internal sealed record HistoryCommentsData(IReadOnlyList<HistoryCommentRefData> Created, IReadOnlyList<HistoryCommentRefData> Skipped)
{
    public static HistoryCommentsData From(HistoryCommentsResult result) => new(
        [.. result.Created.Select(HistoryCommentRefData.From)], [.. result.Skipped.Select(HistoryCommentRefData.From)]);
}

internal sealed record HistoryRefData(string ExternalId, HistoryRefKind Kind, string ProjectKey, string ItemKey, Guid TargetId)
{
    public static HistoryRefData From(HistoryRef reference) =>
        new(reference.ExternalId, reference.Kind, reference.ProjectKey, reference.ItemKey, reference.TargetId);
}
