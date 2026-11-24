using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Activities;

/// <summary>
/// 一次修改裡每個欄位的前後值，形狀和 <see cref="WorkItem"/> 對外的一樣（見 <see cref="FieldChange"/>）。
/// 舊值取自鎖住之後讀到的那一列，所以同時進來的修改排隊時，每一筆的舊值都是前一筆改完的值。
/// </summary>
internal static class ActivityChanges
{
    private const string DateFormat = "yyyy-MM-dd";

    /// <param name="current">鎖住之後讀到的、修改之前的那一列。</param>
    /// <param name="changes">只有值真的有變的欄位（<see cref="ItemChanges.WithoutUnchanged"/>）。</param>
    public static async Task<IReadOnlyList<FieldChange>> DescribeAsync(
        WorkDbContext db, ProjectAccess access, ItemType itemType, ItemEntity current, ItemChanges changes, CancellationToken cancellationToken)
    {
        var projectKey = access.Project.Key;
        var parentFrom = changes.Parent.HasValue ? await ParentKeyAsync(db, current, projectKey, cancellationToken) : null;
        FieldChange?[] core =
        [
            changes.Title.HasValue ? new(CoreFields.Title, Text(current.Title), Text(changes.Title.Value)) : null,
            changes.Description.HasValue ? new(CoreFields.Description, null, null) : null,
            changes.AssigneeId.HasValue ? new(CoreFields.Assignee, Reference(current.AssigneeId), Reference(changes.AssigneeId.Value)) : null,
            changes.Priority.HasValue ? new(CoreFields.Priority, PriorityText((Priority)current.Priority), PriorityText(changes.Priority.Value)) : null,
            changes.StartDate.HasValue ? new(CoreFields.StartDate, Date(current.StartDate), Date(changes.StartDate.Value)) : null,
            changes.DueDate.HasValue ? new(CoreFields.DueDate, Date(current.DueDate), Date(changes.DueDate.Value)) : null,
            changes.Estimate.HasValue ? new(CoreFields.Estimate, Number(current.Estimate), Number(changes.Estimate.Value)) : null,
            changes.Parent.HasValue ? new(CoreFields.Parent, parentFrom, ItemKey(projectKey, changes.Parent.Value?.Number)) : null,

            // 值是編號：名稱不存進活動，週期改名之後顯示用目前的名稱，刪掉的顯示「已刪除的週期」。
            changes.CycleId.HasValue ? new(CoreFields.Cycle, Reference(current.CycleId), Reference(changes.CycleId.Value)) : null,
            changes.MilestoneId.HasValue ? new(CoreFields.Milestone, Reference(current.MilestoneId), Reference(changes.MilestoneId.Value)) : null,
        ];

        // 自訂欄位依類型裡欄位的順序列，和畫面上看到的順序一樣。
        var fields = FieldValues.Parse(current.Fields);
        var custom = itemType.Fields
            .Where(changes.Fields.ContainsKey)
            .Select(key => Custom(access.Schema.Fields[key], ValueOf(fields, key), changes.Fields[key]));
        return [.. core.OfType<FieldChange>(), .. custom];
    }

    // 長文字只記有改：內容可能很長，版本歷史之後另外做。
    private static FieldChange Custom(FieldDefinition field, JsonElement? from, JsonElement? to) =>
        field.Type == FieldType.LongText ? new(field.Key, null, null) : new(field.Key, from, to);

    private static async Task<JsonElement?> ParentKeyAsync(WorkDbContext db, ItemEntity current, string projectKey, CancellationToken cancellationToken)
    {
        if (current.ParentId is not { } parentId)
        {
            return null;
        }

        // 目前的上層一定沒刪除（有沒刪除的下層時刪不掉），不必另外排除。
        var number = await db.Items
            .Where(i => i.OrganizationId == current.OrganizationId && i.Id == parentId)
            .Select(i => i.Number)
            .SingleAsync(cancellationToken);
        return ItemKey(projectKey, number);
    }

    private static JsonElement? ValueOf(IReadOnlyDictionary<string, JsonElement> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : null;

    private static JsonElement? Text(string? text) => text is null ? null : JsonSerializer.SerializeToElement(text);

    private static JsonElement? Reference(Guid? id) => Text(id?.ToString("D"));

    /// <summary>優先度「無」算沒有值，和負責人沒填一樣是 null。</summary>
    private static JsonElement? PriorityText(Priority priority) =>
        priority == Priority.None ? null : Text(JsonNamingPolicy.SnakeCaseLower.ConvertName(priority.ToString()));

    private static JsonElement? Date(DateOnly? date) => Text(date?.ToString(DateFormat, CultureInfo.InvariantCulture));

    /// <summary>存進去之前已經正規化過；再正規化一次，資料庫讀回來的寫法（例如尾端的 0）不會跑進紀錄。</summary>
    private static JsonElement? Number(decimal? number) =>
        number is { } value ? JsonSerializer.SerializeToElement(NumberRules.TryNormalize(value, out var normalized) ? normalized : value) : null;

    private static JsonElement? ItemKey(string projectKey, long? number) =>
        number is { } value ? Text(ItemKeys.Format(projectKey, value)) : null;
}
