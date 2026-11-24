using PmTool.Abstractions;
using PmTool.Api.Responses;
using PmTool.Blueprint.Model;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>已刪除的項目在 API 上的形狀（清單用，沒有說明與自訂欄位）。刪除的人帶顯示名稱，舊資料沒有記錄時是 null。</summary>
internal sealed record DeletedItemSummaryData(
    string Key,
    string ProjectKey,
    long Number,
    string Type,
    string Title,
    string State,
    StateCategory StateCategory,
    string? ParentKey,
    PersonReference? DeletedBy,
    DateTimeOffset DeletedAt)
{
    public static DeletedItemSummaryData From(DeletedItemSummary item, PeopleNames names) => new(
        item.Key,
        item.ProjectKey,
        item.Number,
        item.Type,
        item.Title,
        item.State,
        item.StateCategory,
        item.ParentKey,
        item.DeletedBy is { } person ? new PersonReference(person, names.NameOf(person)) : null,
        item.DeletedAt);
}

internal sealed record DeletedItemPageData(IReadOnlyList<DeletedItemSummaryData> Items, string? NextCursor);

/// <summary>
/// 批次修改。<see cref="State"/> 與其他屬性只能擇一（模組檢查，回 item.bulk_invalid）；
/// 沒寫的屬性是不改，寫 null 是清掉，和單一項目的修改同一套讀法。
/// </summary>
internal sealed record BulkUpdateRequest
{
    public IReadOnlyList<string>? Keys { get; init; }

    public Optional<string> State { get; init; }

    public Optional<Guid?> AssigneeId { get; init; }

    /// <summary>給 null 當成 none，和單一項目的修改一致。</summary>
    public Optional<Priority?> Priority { get; init; }

    public Optional<DateOnly?> StartDate { get; init; }

    public Optional<DateOnly?> DueDate { get; init; }

    public Optional<Guid?> CycleId { get; init; }

    public Optional<Guid?> MilestoneId { get; init; }

    public BulkItemUpdate ToUpdate() => new()
    {
        Keys = Keys ?? [],
        State = State,
        AssigneeId = AssigneeId,
        Priority = Priority.HasValue ? new Optional<Priority>(Priority.Value ?? Work.Priority.None) : default,
        StartDate = StartDate,
        DueDate = DueDate,
        CycleId = CycleId,
        MilestoneId = MilestoneId,
    };
}

/// <summary>批次刪除與復原的請求：編號。</summary>
internal sealed record BulkKeysRequest(IReadOnlyList<string>? Keys = null);

/// <summary>一筆沒做成的原因，<c>error</c> 和信封裡的錯誤同一個形狀。</summary>
internal sealed record BulkFailureData(string Key, ApiError Error)
{
    public static BulkFailureData From(BulkItemFailure failure) => new(
        failure.Key,
        new ApiError(
            failure.Failure.Code,
            failure.Failure.Message,
            [.. failure.Failure.Details.Select(detail => new ApiErrorDetail(detail.Code, detail.Path, detail.Message))]));

    public static IReadOnlyList<BulkFailureData> FromMany(IReadOnlyList<BulkItemFailure> failures) => [.. failures.Select(From)];
}

internal sealed record BulkUpdateData(IReadOnlyList<ItemData> Updated, IReadOnlyList<BulkFailureData> Failed);

internal sealed record BulkDeleteData(IReadOnlyList<DeletedItemData> Deleted, IReadOnlyList<BulkFailureData> Failed);

internal sealed record BulkRestoreData(IReadOnlyList<ItemData> Restored, IReadOnlyList<BulkFailureData> Failed);
