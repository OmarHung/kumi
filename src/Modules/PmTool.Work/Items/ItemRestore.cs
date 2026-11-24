using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Items;

/// <summary>復原要操作的一列（可能沒刪除），連同專案與呼叫者的權限。</summary>
internal sealed record RestoreTarget(ProjectAccess Access, ItemEntity Item);

/// <summary>復原的讀寫：找到刪除的那一列、檢查上層，把復原後的樣子寫回去。整理成什麼樣子見 <see cref="RestorePlan"/>。</summary>
internal static class ItemRestore
{
    /// <summary>
    /// 和 <see cref="ItemStore.FindAsync(WorkDbContext, BlueprintCache, Actor, string, string, bool, CancellationToken)"/> 一樣先對專案取共用鎖、
    /// 再鎖住這一列，差別是刪除的列也找得到：復原的對象本來就是刪除的，沒刪除的（重送）也回傳，由呼叫端什麼都不做。
    /// </summary>
    public static async Task<Result<RestoreTarget>> FindAsync(
        WorkDbContext db, BlueprintCache cache, Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        if (!ItemKeys.TryParse(itemKey, out var projectKey, out var number))
        {
            return WorkFailures.ItemNotFound(itemKey);
        }

        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ItemNotFound(itemKey);
        }

        if (!access.Can(WorkPermissions.ItemDelete))
        {
            return access.Missing(WorkPermissions.ItemDelete);
        }

        var item = await LockAsync(db, actor.OrganizationId, access.Project.Id, number, cancellationToken);
        return item is null || !access.Items.Contains(item) ? WorkFailures.ItemNotFound(itemKey) : new RestoreTarget(access, item);
    }

    /// <summary>
    /// 上層也是刪除的就擋下；沒刪除的取共用鎖（和新增、修改掛上來時一樣，見 <see cref="ItemStore.ShareAsync"/>），
    /// 刪除上層要等這裡提交才檢查下層，不會在復原的同時把上層刪掉、留下掛在刪除的上層底下的項目。
    /// </summary>
    public static async Task<Result<ItemEntity?>> LiveParentAsync(
        WorkDbContext db, ProjectAccess access, ItemEntity item, CancellationToken cancellationToken)
    {
        if (item.ParentId is not { } parentId)
        {
            return (ItemEntity?)null;
        }

        var parent = await ItemStore.ShareByIdAsync(db, item.OrganizationId, parentId, cancellationToken);
        if (parent is not null)
        {
            return parent;
        }

        var number = await db.Items
            .Where(i => i.OrganizationId == item.OrganizationId && i.Id == parentId)
            .Select(i => i.Number)
            .SingleAsync(cancellationToken);
        var parentKey = ItemKeys.Format(access.Project.Key, number);
        return new Failure(FailureKind.Conflict, WorkErrorCodes.ItemParentDeleted, $"上層 {parentKey} 也已刪除，先復原它。")
        {
            Details = [new FailureDetail(WorkErrorCodes.ItemParentDeleted, CoreFields.Parent, $"上層 {parentKey} 也已刪除，先復原它。")],
        };
    }

    public static Failure TypeRemoved(string typeKey) =>
        new(FailureKind.Conflict, WorkErrorCodes.ItemTypeRemoved, $"藍圖已經沒有{Quote(typeKey)}這個類型，無法復原。");

    /// <summary>清掉刪除的紀錄，連同整理過的狀態、自訂欄位與上層一起寫，不會只寫了一半。</summary>
    public static Task WriteAsync(WorkDbContext db, ItemEntity restored, CancellationToken cancellationToken) =>
        db.Items
            .Where(i => i.OrganizationId == restored.OrganizationId && i.Id == restored.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.DeletedAt, (DateTimeOffset?)null)
                    .SetProperty(i => i.DeletedBy, (Guid?)null)
                    .SetProperty(i => i.State, restored.State)
                    .SetProperty(i => i.StateCategory, restored.StateCategory)
                    .SetProperty(i => i.Fields, restored.Fields)
                    .SetProperty(i => i.ParentId, restored.ParentId)
                    .SetProperty(i => i.UpdatedAt, restored.UpdatedAt),
                cancellationToken);

    // 和刪除一樣用 FOR NO KEY UPDATE，兩個人同時復原或一邊復原一邊修改會排隊；不看 deleted_at。
    private static async Task<ItemEntity?> LockAsync(
        WorkDbContext db, Guid organizationId, Guid projectId, long number, CancellationToken cancellationToken)
    {
        var rows = await db.Items
            .FromSql($"""
                SELECT * FROM work.items
                WHERE organization_id = {organizationId} AND project_id = {projectId} AND number = {number}
                FOR NO KEY UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}
