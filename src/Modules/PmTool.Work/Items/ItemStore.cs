using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>呼叫者要操作的一個項目，連同專案、他的權限與項目的類型。</summary>
internal sealed record ItemTarget(ProjectAccess Access, ItemEntity Item, ItemType ItemType);

internal static class ItemStore
{
    /// <summary>
    /// 依編號找項目並確認權限。專案看不到時也回 item.not_found：問的是項目，不能讓人從代碼分辨出專案存不存在；
    /// 刪除（封存）的項目也一樣當成不存在。
    /// <paramref name="forUpdate"/> 時鎖住這一列直到交易結束，同時進來的修改會排隊，後到的讀到的是前一個改完的值；
    /// 讀藍圖之前也先對專案取共用鎖（<see cref="ProjectLocks"/>），套用藍圖的變更集進行中時會等它做完，讀到新的藍圖。
    /// </summary>
    public static async Task<Result<ItemTarget>> FindAsync(
        WorkDbContext db,
        BlueprintCache cache,
        Actor actor,
        string itemKey,
        string permission,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        if (!ItemKeys.TryParse(itemKey, out var projectKey, out var number))
        {
            return WorkFailures.ItemNotFound(itemKey);
        }

        if (forUpdate)
        {
            await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        }

        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ItemNotFound(itemKey);
        }

        if (!access.Can(permission))
        {
            return access.Missing(permission);
        }

        var item = forUpdate
            ? await LockAsync(db, actor.OrganizationId, access.Project.Id, number, cancellationToken)
            : await FindAsync(db, actor.OrganizationId, access.Project.Id, number, cancellationToken);
        // 只看自己的角色看不到的項目和不存在一樣：不透露它存在。
        if (item is null || !access.Items.Contains(item))
        {
            return WorkFailures.ItemNotFound(itemKey);
        }

        // 藍圖保證類型存在；找不到表示資料和藍圖對不上，是要修的錯，不是使用者的問題。
        var itemType = access.Schema.ItemTypes.GetValueOrDefault(item.Type)
            ?? throw new InvalidOperationException($"項目 {itemKey} 的類型 {item.Type} 不在專案目前的藍圖裡。");
        return new ItemTarget(access, item, itemType);
    }

    /// <summary>
    /// 沒刪除的項目。刪除的項目資料留著，但每一個讀項目的地方都要當它不存在：清單、上層、下層、計數、變更集的影響與搬移。
    /// 照編號或編號以外的條件讀項目時都從這裡開始，不直接用 <see cref="WorkDbContext.Items"/>。
    /// </summary>
    public static IQueryable<ItemEntity> Live(WorkDbContext db) => db.Items.Where(i => i.DeletedAt == null);

    public static Task<ItemEntity?> FindAsync(
        WorkDbContext db, Guid organizationId, Guid projectId, long number, CancellationToken cancellationToken) =>
        Live(db)
            .AsNoTracking()
            .SingleOrDefaultAsync(
                i => i.OrganizationId == organizationId && i.ProjectId == projectId && i.Number == number, cancellationToken);

    /// <summary>
    /// 要掛在它底下的上層：讀它的同時取共用鎖（FOR SHARE）直到交易結束。刪除與改類型會先鎖住自己那一列再檢查下層，
    /// 兩邊互斥，「沒有下層」或「下層的類型都允許」檢查完之後，不會有新的下層在中間掛上來；
    /// 等到鎖時讀到的是對方提交後的那一列，刪除了就找不到、改了類型就照新的類型檢查。
    /// 只擋刪除、改類型與修改這一列，同時新增好幾個下層彼此不衝突。
    /// </summary>
    public static async Task<ItemEntity?> ShareAsync(
        WorkDbContext db, Guid organizationId, Guid projectId, long number, CancellationToken cancellationToken)
    {
        var rows = await db.Items
            .FromSql($"""
                SELECT * FROM work.items
                WHERE organization_id = {organizationId} AND project_id = {projectId} AND number = {number} AND deleted_at IS NULL
                FOR SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>和 <see cref="ShareAsync"/> 一樣取共用鎖，用在已經知道上層的資料表編號時（改類型檢查目前的上層）。</summary>
    public static async Task<ItemEntity?> ShareByIdAsync(WorkDbContext db, Guid organizationId, Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.Items
            .FromSql($"""
                SELECT * FROM work.items
                WHERE organization_id = {organizationId} AND id = {id} AND deleted_at IS NULL
                FOR SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    /// <summary>
    /// 在交易裡先加一再讀回來。UPDATE 鎖住專案那一列直到交易結束，同時新增的另一個交易會等它結束後
    /// 再從加過的值往上加，所以流水號不會重複；新增失敗時交易退回，號碼也一起退回。
    /// </summary>
    public static async Task<long> NextNumberAsync(WorkDbContext db, ProjectEntity project, CancellationToken cancellationToken)
    {
        await db.Projects
            .Where(p => p.OrganizationId == project.OrganizationId && p.Id == project.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.LastItemNumber, p => p.LastItemNumber + 1), cancellationToken);
        return await db.Projects
            .Where(p => p.OrganizationId == project.OrganizationId && p.Id == project.Id)
            .Select(p => p.LastItemNumber)
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// 只寫有改的欄位。呼叫端在同一個交易裡先鎖住這一列再算出 <paramref name="updated"/>，
    /// 所以兩個人同時改不同的欄位（包含不同的自訂欄位）時，後寫的那一個是在前一個改完的值上合併的。
    /// </summary>
    public static Task WriteAsync(WorkDbContext db, ItemEntity updated, ItemChanges changes, CancellationToken cancellationToken) =>
        db.Items
            .Where(i => i.OrganizationId == updated.OrganizationId && i.Id == updated.Id)
            .ExecuteUpdateAsync(
                setters =>
                {
                    setters.SetProperty(i => i.UpdatedAt, updated.UpdatedAt);
                    if (changes.Title.HasValue)
                    {
                        setters.SetProperty(i => i.Title, updated.Title);
                    }

                    if (changes.Description.HasValue)
                    {
                        setters.SetProperty(i => i.Description, updated.Description);
                    }

                    if (changes.AssigneeId.HasValue)
                    {
                        setters.SetProperty(i => i.AssigneeId, updated.AssigneeId);
                    }

                    if (changes.Priority.HasValue)
                    {
                        setters.SetProperty(i => i.Priority, updated.Priority);
                    }

                    WriteSchedule(setters, updated, changes);
                },
                cancellationToken);

    /// <summary>新的狀態與它的類別一起寫，兩者不會對不上。</summary>
    public static Task WriteStateAsync(WorkDbContext db, ItemEntity updated, CancellationToken cancellationToken) =>
        db.Items
            .Where(i => i.OrganizationId == updated.OrganizationId && i.Id == updated.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.State, updated.State)
                    .SetProperty(i => i.StateCategory, updated.StateCategory)
                    .SetProperty(i => i.UpdatedAt, updated.UpdatedAt),
                cancellationToken);

    /// <summary>改類型：類型、狀態與類別、自訂欄位一起寫，不會只寫了一半。</summary>
    public static Task WriteTypeAsync(WorkDbContext db, ItemEntity updated, CancellationToken cancellationToken) =>
        db.Items
            .Where(i => i.OrganizationId == updated.OrganizationId && i.Id == updated.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.Type, updated.Type)
                    .SetProperty(i => i.State, updated.State)
                    .SetProperty(i => i.StateCategory, updated.StateCategory)
                    .SetProperty(i => i.Fields, updated.Fields)
                    .SetProperty(i => i.UpdatedAt, updated.UpdatedAt),
                cancellationToken);

    /// <summary>
    /// 刪除＝封存：只記下刪除的人與時刻，其他欄位原樣留著。updatedAt 不動：它是內容最後一次改變的時間，
    /// 刪除沒有改內容。
    /// </summary>
    public static Task WriteDeletedAsync(WorkDbContext db, ItemEntity item, Guid deletedBy, DateTimeOffset deletedAt, CancellationToken cancellationToken) =>
        db.Items
            .Where(i => i.OrganizationId == item.OrganizationId && i.Id == item.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.DeletedAt, deletedAt)
                    .SetProperty(i => i.DeletedBy, deletedBy),
                cancellationToken);

    private static void WriteSchedule(UpdateSettersBuilder<ItemEntity> setters, ItemEntity updated, ItemChanges changes)
    {
        if (changes.StartDate.HasValue)
        {
            setters.SetProperty(i => i.StartDate, updated.StartDate);
        }

        if (changes.DueDate.HasValue)
        {
            setters.SetProperty(i => i.DueDate, updated.DueDate);
        }

        if (changes.Estimate.HasValue)
        {
            setters.SetProperty(i => i.Estimate, updated.Estimate);
        }

        if (changes.Parent.HasValue)
        {
            setters.SetProperty(i => i.ParentId, updated.ParentId);
        }

        if (changes.CycleId.HasValue)
        {
            setters.SetProperty(i => i.CycleId, updated.CycleId);
        }

        if (changes.MilestoneId.HasValue)
        {
            setters.SetProperty(i => i.MilestoneId, updated.MilestoneId);
        }

        if (changes.Fields.Count > 0)
        {
            setters.SetProperty(i => i.Fields, updated.Fields);
        }
    }

    /// <summary>
    /// FOR NO KEY UPDATE：擋同一列的其他修改，與要掛在它底下的新增或修改（它們對上層取 FOR SHARE，見 <see cref="ShareAsync"/>）。
    /// 等到鎖時條件會照對方提交後的那一列重新判斷，同時被刪除的就找不到。
    /// </summary>
    private static async Task<ItemEntity?> LockAsync(
        WorkDbContext db, Guid organizationId, Guid projectId, long number, CancellationToken cancellationToken)
    {
        var rows = await db.Items
            .FromSql($"""
                SELECT * FROM work.items
                WHERE organization_id = {organizationId} AND project_id = {projectId} AND number = {number} AND deleted_at IS NULL
                FOR NO KEY UPDATE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}
