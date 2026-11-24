using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work;

/// <summary>已刪除的項目清單的條件。</summary>
public sealed record DeletedItemQuery
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>上一頁回傳的 <see cref="DeletedItemPage.NextCursor"/>；第一頁是 null。內容對呼叫端沒有意義。</summary>
    public string? Cursor { get; init; }
}

/// <summary>一個已刪除的項目（清單用，沒有說明與自訂欄位）。</summary>
/// <param name="Key">項目編號，例如 WEB-12。</param>
/// <param name="State">刪除時的狀態 key；藍圖之後改過時可能已經不在流程裡（復原時才搬）。</param>
/// <param name="ParentKey">刪除時的上層編號；上層可能也是刪除的。</param>
/// <param name="DeletedBy">刪除的人；舊資料沒有記錄時是 null。</param>
public sealed record DeletedItemSummary(
    string Key,
    string ProjectKey,
    long Number,
    string Type,
    string Title,
    string State,
    StateCategory StateCategory,
    string? ParentKey,
    Guid? DeletedBy,
    DateTimeOffset DeletedAt);

/// <param name="NextCursor">還有下一頁時的游標；沒有了就是 null。</param>
public sealed record DeletedItemPage(IReadOnlyList<DeletedItemSummary> Items, string? NextCursor);

/// <summary>
/// 批次修改的內容：<see cref="State"/> 與其他屬性只能擇一（改狀態要照流程，混在一起時哪個先做、做到一半失敗算什麼都說不清楚）。
/// 只改有給的屬性，語意和 <see cref="WorkItemPatch"/> 相同（null 是清掉）。
/// </summary>
public sealed record BulkItemUpdate
{
    /// <summary>一次最多幾個項目：和清單一頁的上限相同，網頁上選得到的就是已經載入的那些。</summary>
    public const int MaxItems = 200;

    public required IReadOnlyList<string> Keys { get; init; }

    /// <summary>要改成的狀態 key。不在某一筆的流程裡、或流程不允許這條轉換，那一筆失敗。</summary>
    public Optional<string> State { get; init; }

    public Optional<Guid?> AssigneeId { get; init; }

    public Optional<Priority> Priority { get; init; }

    public Optional<DateOnly?> StartDate { get; init; }

    public Optional<DateOnly?> DueDate { get; init; }

    public Optional<Guid?> CycleId { get; init; }

    public Optional<Guid?> MilestoneId { get; init; }
}

/// <summary>一筆沒做成的原因：和單一項目的操作回傳的失敗相同（代碼、訊息、細項）。</summary>
public sealed record BulkItemFailure(string Key, Failure Failure);

public sealed record BulkUpdateResult(IReadOnlyList<WorkItem> Updated, IReadOnlyList<BulkItemFailure> Failed);

public sealed record BulkDeleteResult(IReadOnlyList<DeletedWorkItem> Deleted, IReadOnlyList<BulkItemFailure> Failed);

public sealed record BulkRestoreResult(IReadOnlyList<WorkItem> Restored, IReadOnlyList<BulkItemFailure> Failed);
