using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

internal sealed record PersonReference(Guid PersonId, string DisplayName);

/// <summary>
/// 項目在 API 上的形狀，清單與「我的工作」用這個：沒有說明，一頁兩百個項目不必帶著長文字。
/// 負責人與建立者帶顯示名稱，網頁不必另外查。
/// </summary>
internal record ItemData(
    string Key,
    string ProjectKey,
    long Number,
    string Type,
    string Title,
    string State,
    StateCategory StateCategory,
    PersonReference? Assignee,
    PersonReference CreatedBy,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? Estimate,
    string? ParentKey,
    Guid? CycleId,
    Guid? MilestoneId,
    IReadOnlyDictionary<string, JsonElement> Fields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ItemData From(WorkItem item, PeopleNames names) => new(
        item.Key,
        item.ProjectKey,
        item.Number,
        item.Type,
        item.Title,
        item.State,
        item.StateCategory,
        item.AssigneeId is { } assignee ? new PersonReference(assignee, names.NameOf(assignee)) : null,
        names.Reference(item.CreatedBy),
        item.Priority,
        item.StartDate,
        item.DueDate,
        item.Estimate,
        item.ParentKey,
        item.CycleId,
        item.MilestoneId,
        item.Fields,
        item.CreatedAt,
        item.UpdatedAt);

    /// <summary>一個回應裡所有項目的負責人一次查完。</summary>
    public static async Task<IReadOnlyList<ItemData>> FromManyAsync(
        IReadOnlyList<WorkItem> items, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(
            people, organizationId, items.Select(i => i.AssigneeId).OfType<Guid>().Concat(items.Select(i => i.CreatedBy)), cancellationToken);
        return [.. items.Select(item => From(item, names))];
    }
}

/// <summary>
/// 單一項目的回應（讀取、新增、修改、改狀態）多一個說明，沒有值時寫成 null；
/// 用另一個型別，清單的回應就不可能帶出這個屬性。
/// </summary>
internal sealed record ItemDetailData : ItemData
{
    public ItemDetailData(ItemData item, string? description)
        : base(item) => Description = description;

    public string? Description { get; }

    /// <summary>說明的版本。網頁改說明時帶回 <c>expectedDescriptionRevision</c>，伺服器才擋得下蓋掉別人修改的儲存。</summary>
    public string DescriptionRevision => DescriptionRevisions.Of(Description);
}

internal sealed record ItemPageData(IReadOnlyList<ItemData> Items, string? NextCursor);

internal sealed record MyWorkData(IReadOnlyList<ItemData> Items);

/// <summary>跨專案搜尋的回應：和清單同一個項目形狀，不分頁。</summary>
internal sealed record SearchItemsData(IReadOnlyList<ItemData> Items);

/// <summary>新增項目。沒給的優先順序是 none；狀態不能指定，由流程決定。</summary>
internal record CreateItemRequest(
    string Type,
    string Title,
    Guid? AssigneeId = null,
    Priority? Priority = null,
    DateOnly? StartDate = null,
    DateOnly? DueDate = null,
    decimal? Estimate = null,
    string? ParentKey = null,
    string? Description = null,
    IReadOnlyDictionary<string, JsonElement>? Fields = null,
    Guid? CycleId = null,
    Guid? MilestoneId = null)
{
    public NewWorkItem ToNewWorkItem() => new()
    {
        Type = Type,
        Title = Title,
        AssigneeId = AssigneeId,
        Priority = Priority ?? Work.Priority.None,
        StartDate = StartDate,
        DueDate = DueDate,
        Estimate = Estimate,
        ParentKey = ParentKey,
        Description = Description,
        CycleId = CycleId,
        MilestoneId = MilestoneId,
        Fields = Fields ?? new Dictionary<string, JsonElement>(),
    };
}

/// <summary>
/// 局部修改。沒有 type 與 state：類型不能改，狀態要走轉換才會套用流程的規則；
/// 送了這兩個屬性會因為「不認得的屬性」被擋下來。
/// </summary>
internal sealed record PatchItemRequest
{
    public Optional<string> Title { get; init; }

    public Optional<Guid?> AssigneeId { get; init; }

    /// <summary>給了 null 當成 none（清掉優先順序），和新增項目的收法一致。</summary>
    public Optional<Priority?> Priority { get; init; }

    public Optional<DateOnly?> StartDate { get; init; }

    public Optional<DateOnly?> DueDate { get; init; }

    public Optional<decimal?> Estimate { get; init; }

    public Optional<string?> ParentKey { get; init; }

    /// <summary>給 null 或空字串都是清掉，由工作引擎決定；這裡只分出沒給、null、值三種。</summary>
    public Optional<string?> Description { get; init; }

    /// <summary>和 description 一起給時檢查：開始編輯時拿到的 descriptionRevision。沒給是不檢查。</summary>
    public string? ExpectedDescriptionRevision { get; init; }

    /// <summary>給 null 是清掉週期，沒給是不動。</summary>
    public Optional<Guid?> CycleId { get; init; }

    public Optional<Guid?> MilestoneId { get; init; }

    public IReadOnlyDictionary<string, JsonElement>? Fields { get; init; }

    public WorkItemPatch ToPatch() => new()
    {
        Title = Title,
        AssigneeId = AssigneeId,
        Priority = Priority.HasValue ? new Optional<Priority>(Priority.Value ?? Work.Priority.None) : default,
        StartDate = StartDate,
        DueDate = DueDate,
        Estimate = Estimate,
        ParentKey = ParentKey,
        Description = Description,
        ExpectedDescriptionRevision = ExpectedDescriptionRevision,
        CycleId = CycleId,
        MilestoneId = MilestoneId,
        Fields = Fields ?? new Dictionary<string, JsonElement>(),
    };
}

internal sealed record TransitionRequest(string To);

/// <summary>
/// 改類型。沒給或給 null 和給了不存在的類型一樣，交給工作引擎回 422 item.unknown_type，網頁只要認一種錯誤。
/// 沒有 state：狀態由伺服器決定，送了會因為「不認得的屬性」被擋下來。
/// </summary>
internal sealed record ChangeTypeRequest(string? Type = null);

/// <summary>刪除（封存）的結果。</summary>
internal sealed record DeletedItemData(string Key, DateTimeOffset DeletedAt);
