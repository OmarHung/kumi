using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

internal sealed record ItemParent(Guid Id, long Number);

/// <summary>
/// 檢查並正規化過的修改：文字去掉頭尾空白、上層換成資料表的編號、自訂欄位的空值換成 null（表示拿掉）。
/// 只有 HasValue 的欄位會寫進資料表，沒提到的欄位維持原樣。
/// </summary>
internal sealed record ItemChanges
{
    public Optional<string> Title { get; init; }

    /// <summary>null 表示清掉說明。</summary>
    public Optional<string?> Description { get; init; }

    public Optional<Guid?> AssigneeId { get; init; }

    public Optional<Priority> Priority { get; init; }

    public Optional<DateOnly?> StartDate { get; init; }

    public Optional<DateOnly?> DueDate { get; init; }

    public Optional<decimal?> Estimate { get; init; }

    public Optional<ItemParent?> Parent { get; init; }

    public Optional<Guid?> CycleId { get; init; }

    public Optional<Guid?> MilestoneId { get; init; }

    /// <summary>要改的自訂欄位；值是 null 表示拿掉這個欄位。</summary>
    public IReadOnlyDictionary<string, JsonElement?> Fields { get; init; } = new Dictionary<string, JsonElement?>();

    public bool IsEmpty =>
        !Title.HasValue && !Description.HasValue && !AssigneeId.HasValue && !Priority.HasValue && !StartDate.HasValue && !DueDate.HasValue
        && !Estimate.HasValue && !Parent.HasValue && !CycleId.HasValue && !MilestoneId.HasValue && Fields.Count == 0;

    /// <summary>
    /// 只留下值和目前那一列真的不同的欄位。給了一樣的值不算有改：不寫、不記進活動，什麼都沒變時 updatedAt 也不動。
    /// 目前的值是鎖住之後讀的，比出來的就是這一次真正改到的。
    /// </summary>
    public ItemChanges WithoutUnchanged(ItemEntity current, ProjectSchema schema)
    {
        var fields = FieldValues.Parse(current.Fields);
        return this with
        {
            Title = Differs(Title, current.Title),
            Description = Differs(Description, current.Description),
            AssigneeId = Differs(AssigneeId, current.AssigneeId),
            Priority = Priority.HasValue && (short)Priority.Value != current.Priority ? Priority : default,
            StartDate = Differs(StartDate, current.StartDate),
            DueDate = Differs(DueDate, current.DueDate),
            Estimate = Differs(Estimate, current.Estimate),
            Parent = Parent.HasValue && Parent.Value?.Id != current.ParentId ? Parent : default,
            CycleId = Differs(CycleId, current.CycleId),
            MilestoneId = Differs(MilestoneId, current.MilestoneId),
            Fields = Fields
                .Where(pair => !FieldValues.SameValue(schema.Fields[pair.Key].Type, fields.TryGetValue(pair.Key, out var value) ? value : null, pair.Value))
                .ToDictionary(StringComparer.Ordinal),
        };
    }

    /// <summary>把修改套到目前的那一列，得到改完的樣子。目前的值是鎖住之後讀的，所以這裡算出來的就是最新的。</summary>
    public ItemEntity ApplyTo(ItemEntity item, DateTimeOffset now) => item with
    {
        Title = Title.HasValue ? Title.Value : item.Title,
        Description = Description.HasValue ? Description.Value : item.Description,
        AssigneeId = AssigneeId.HasValue ? AssigneeId.Value : item.AssigneeId,
        Priority = Priority.HasValue ? (short)Priority.Value : item.Priority,
        StartDate = StartDate.HasValue ? StartDate.Value : item.StartDate,
        DueDate = DueDate.HasValue ? DueDate.Value : item.DueDate,
        Estimate = Estimate.HasValue ? Estimate.Value : item.Estimate,
        ParentId = Parent.HasValue ? Parent.Value?.Id : item.ParentId,
        CycleId = CycleId.HasValue ? CycleId.Value : item.CycleId,
        MilestoneId = MilestoneId.HasValue ? MilestoneId.Value : item.MilestoneId,
        Fields = Fields.Count > 0 ? FieldValues.Serialize(FieldValues.Merge(FieldValues.Parse(item.Fields), Fields)) : item.Fields,
        UpdatedAt = now,
    };

    // decimal 的相等比的是大小，2.5 和 2.50 相等；字串是逐字元比。
    private static Optional<T> Differs<T>(Optional<T> change, T current) =>
        change.HasValue && !EqualityComparer<T>.Default.Equals(change.Value, current) ? change : default;

    /// <summary>新增和修改走同一套檢查：新增就是對一個空的項目把每個欄位都改一次。</summary>
    public static WorkItemPatch AsPatch(NewWorkItem item) => new()
    {
        Title = item.Title,
        Description = item.Description,
        AssigneeId = item.AssigneeId,
        Priority = item.Priority,
        StartDate = item.StartDate,
        DueDate = item.DueDate,
        Estimate = item.Estimate,
        ParentKey = item.ParentKey,
        CycleId = item.CycleId,
        MilestoneId = item.MilestoneId,
        Fields = item.Fields,
    };
}
