using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>資料表的列轉成合約的 <see cref="WorkItem"/>。項目編號要專案代號，上層要上層的流水號，一次查齊。</summary>
internal static class ItemReader
{
    /// <summary>
    /// 清單用的欄位：除了說明以外的每一欄。一頁兩百個項目，說明每個可能有五萬字，查詢時就不讀，不是讀了再丟掉。
    /// 可以是 null 的欄位不是 required，漏寫了編譯器不會提醒；資料表多了欄位，這裡也要跟著加。
    /// </summary>
    public static readonly Expression<Func<ItemEntity, ItemEntity>> WithoutDescription = i => new ItemEntity
    {
        Id = i.Id,
        OrganizationId = i.OrganizationId,
        ProjectId = i.ProjectId,
        Number = i.Number,
        Type = i.Type,
        Title = i.Title,
        State = i.State,
        StateCategory = i.StateCategory,
        AssigneeId = i.AssigneeId,
        Priority = i.Priority,
        StartDate = i.StartDate,
        DueDate = i.DueDate,
        Estimate = i.Estimate,
        ParentId = i.ParentId,
        CycleId = i.CycleId,
        MilestoneId = i.MilestoneId,
        Fields = i.Fields,
        CreatedBy = i.CreatedBy,
        CreatedAt = i.CreatedAt,
        UpdatedAt = i.UpdatedAt,
        DeletedAt = i.DeletedAt,
        DeletedBy = i.DeletedBy,
    };

    public static async Task<IReadOnlyList<WorkItem>> ToWorkItemsAsync(
        WorkDbContext db, Guid organizationId, IReadOnlyList<ItemEntity> items, CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var projectIds = items.Select(i => i.ProjectId).Distinct().ToArray();
        var parentIds = items.Where(i => i.ParentId is not null).Select(i => i.ParentId!.Value).Distinct().ToArray();
        var projectKeys = await db.Projects
            .Where(p => p.OrganizationId == organizationId && projectIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Key })
            .ToDictionaryAsync(p => p.Id, p => p.Key, cancellationToken);
        // 上層不必排除刪除的：有沒刪除的下層時刪不掉，所以沒刪除的項目的上層一定也沒刪除。
        var parentNumbers = parentIds.Length == 0
            ? []
            : await db.Items
                .Where(i => i.OrganizationId == organizationId && parentIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Number })
                .ToDictionaryAsync(i => i.Id, i => i.Number, cancellationToken);
        return [.. items.Select(i => ToWorkItem(i, projectKeys[i.ProjectId], i.ParentId is { } parent ? parentNumbers[parent] : null))];
    }

    public static WorkItem ToWorkItem(ItemEntity item, string projectKey, long? parentNumber) => new(
        ItemKeys.Format(projectKey, item.Number),
        projectKey,
        item.Number,
        item.Type,
        item.Title,
        item.State,
        StateCategoryText.Parse(item.StateCategory),
        item.AssigneeId,
        (Priority)item.Priority,
        item.StartDate,
        item.DueDate,
        item.Estimate,
        parentNumber is { } number ? ItemKeys.Format(projectKey, number) : null,
        FieldValues.Parse(item.Fields),
        item.CreatedAt,
        item.UpdatedAt)
    {
        Description = item.Description,
        CycleId = item.CycleId,
        MilestoneId = item.MilestoneId,
    };
}
