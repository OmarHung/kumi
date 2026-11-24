using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.SavedViews;

// 「我的工作」的檢視：project_id 是 null，名稱與上限都只算自己的。不需要看得到任何專案，所以沒有權限的檢查，
// 只有「自己的」這一層：別人的檢視與專案的檢視在這裡都查不到，呼叫端看到的和不存在一樣。
internal sealed partial class SavedViewService
{
    public async Task<IReadOnlyList<SavedView>> ListMyWorkAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await OwnMyWorkViews(db, actor).AsNoTracking().ToListAsync(cancellationToken);

        // 在記憶體裡排：最多 50 筆，順序也不受資料庫的排序規則影響。
        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id)
            .Select(r => SavedViewMapping.ToContract(r, logger))
            .ToList();
    }

    public async Task<Result<SavedView>> CreateMyWorkAsync(Actor actor, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var checkedDraft = SavedViewChecks.CheckMyWork(draft);
        if (checkedDraft.Failure is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockMyWorkAsync(db, actor, cancellationToken);
        if (await OwnMyWorkViews(db, actor).CountAsync(cancellationToken) >= SavedViewRules.MaxMyWork)
        {
            return SavedViewFailures.MyWorkLimitReached();
        }

        var name = checkedDraft.Value.Name;
        if (await MyWorkNameInUseAsync(db, actor, name, exceptId: null, cancellationToken))
        {
            return SavedViewFailures.NameTaken(name);
        }

        var now = WorkClock.Now(time);
        var row = new SavedViewEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = null,
            OwnerId = actor.PersonId,
            Name = name,
            Layout = SavedViewMapping.LayoutName(checkedDraft.Value.Layout),
            Query = SavedViewMapping.QueryJson(checkedDraft.Value.Query),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SavedViews.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.MyWorkViewNameIndex))
        {
            return SavedViewFailures.NameTaken(name);
        }

        return SavedViewMapping.ToContract(row, logger);
    }

    public async Task<Result<SavedView>> ReplaceMyWorkAsync(
        Actor actor, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        var checkedDraft = SavedViewChecks.CheckMyWork(draft);
        if (checkedDraft.Failure is { } invalid)
        {
            return invalid;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockMyWorkAsync(db, actor, cancellationToken);
        var current = await OwnMyWorkViews(db, actor).AsNoTracking().SingleOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (current is null)
        {
            return SavedViewFailures.NotFound();
        }

        var name = checkedDraft.Value.Name;
        if (await MyWorkNameInUseAsync(db, actor, name, viewId, cancellationToken))
        {
            return SavedViewFailures.NameTaken(name);
        }

        var updated = current with
        {
            Name = name,
            Layout = SavedViewMapping.LayoutName(checkedDraft.Value.Layout),
            Query = SavedViewMapping.QueryJson(checkedDraft.Value.Query),
            UpdatedAt = WorkClock.Now(time),
        };
        try
        {
            var affected = await OwnMyWorkViews(db, actor)
                .Where(v => v.Id == viewId)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(v => v.Name, updated.Name)
                        .SetProperty(v => v.Layout, updated.Layout)
                        .SetProperty(v => v.Query, updated.Query)
                        .SetProperty(v => v.UpdatedAt, updated.UpdatedAt),
                    cancellationToken);
            if (affected == 0)
            {
                // 讀到之後被另一個請求刪掉了；不能回成功，呼叫端會以為檢視還在。
                return SavedViewFailures.NotFound();
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.MyWorkViewNameIndex))
        {
            return SavedViewFailures.NameTaken(name);
        }

        return SavedViewMapping.ToContract(updated, logger);
    }

    public async Task<Result<Unit>> DeleteMyWorkAsync(Actor actor, Guid viewId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var deleted = await OwnMyWorkViews(db, actor).Where(v => v.Id == viewId).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? SavedViewFailures.NotFound() : Unit.Value;
    }

    private static IQueryable<SavedViewEntity> OwnMyWorkViews(WorkDbContext db, Actor actor) =>
        db.SavedViews.Where(v => v.OrganizationId == actor.OrganizationId && v.ProjectId == null && v.OwnerId == actor.PersonId);

    /// <summary>和專案的檢視同一種比法：不分大小寫都交給資料庫（lower），和唯一索引用同一套規則。</summary>
    private static Task<bool> MyWorkNameInUseAsync(
        WorkDbContext db, Actor actor, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        OwnMyWorkViews(db, actor).AnyAsync(v => v.Id != exceptId && v.Name.ToLower() == name.ToLower(), cancellationToken);

    /// <summary>上限與名稱都是「先數再寫」：同一個人同時送出時要排隊，留到交易結束。不同人各有自己的鎖。</summary>
    private static async Task LockMyWorkAsync(WorkDbContext db, Actor actor, CancellationToken cancellationToken)
    {
        var lockKey = $"work.saved_views:my_work:{actor.OrganizationId}:{actor.PersonId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }
}
