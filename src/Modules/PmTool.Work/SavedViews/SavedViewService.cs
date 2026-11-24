using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.SavedViews;

internal sealed partial class SavedViewService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time, ILogger<SavedViewService> logger) : ISavedViewService
{
    public async Task<Result<IReadOnlyList<SavedView>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var rows = await OwnViews(db, actor, access.Value.Project.Id).AsNoTracking().ToListAsync(cancellationToken);

        // 在記憶體裡排：最多 50 筆，順序也不受資料庫的排序規則影響。
        return rows
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Id)
            .Select(r => SavedViewMapping.ToContract(r, logger))
            .ToList();
    }

    public async Task<Result<SavedView>> CreateAsync(
        Actor actor, string projectKey, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareAsync(db, actor, projectKey, draft, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (project, checkedDraft) = prepared.Value;
        if (await OwnViews(db, actor, project.Id).CountAsync(cancellationToken) >= SavedViewRules.MaxPerProject)
        {
            return SavedViewFailures.LimitReached();
        }

        if (await NameInUseAsync(db, actor, project.Id, checkedDraft.Name, exceptId: null, cancellationToken))
        {
            return SavedViewFailures.NameTaken(checkedDraft.Name);
        }

        var now = WorkClock.Now(time);
        var row = new SavedViewEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Id,
            OwnerId = actor.PersonId,
            Name = checkedDraft.Name,
            Layout = SavedViewMapping.LayoutName(checkedDraft.Layout),
            Query = SavedViewMapping.QueryJson(checkedDraft.Query),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.SavedViews.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.SavedViewNameIndex))
        {
            return SavedViewFailures.NameTaken(checkedDraft.Name);
        }

        return SavedViewMapping.ToContract(row, logger);
    }

    public async Task<Result<SavedView>> ReplaceAsync(
        Actor actor, string projectKey, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(draft);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareAsync(db, actor, projectKey, draft, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (project, checkedDraft) = prepared.Value;
        var current = await OwnViews(db, actor, project.Id).AsNoTracking().SingleOrDefaultAsync(v => v.Id == viewId, cancellationToken);
        if (current is null)
        {
            return SavedViewFailures.NotFound();
        }

        if (await NameInUseAsync(db, actor, project.Id, checkedDraft.Name, viewId, cancellationToken))
        {
            return SavedViewFailures.NameTaken(checkedDraft.Name);
        }

        var updated = current with
        {
            Name = checkedDraft.Name,
            Layout = SavedViewMapping.LayoutName(checkedDraft.Layout),
            Query = SavedViewMapping.QueryJson(checkedDraft.Query),
            UpdatedAt = WorkClock.Now(time),
        };
        try
        {
            var affected = await db.SavedViews
                .Where(v => v.OrganizationId == actor.OrganizationId && v.Id == viewId)
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
        catch (Exception exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.SavedViewNameIndex))
        {
            return SavedViewFailures.NameTaken(checkedDraft.Name);
        }

        return SavedViewMapping.ToContract(updated, logger);
    }

    public async Task<Result<Unit>> DeleteAsync(Actor actor, string projectKey, Guid viewId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var deleted = await OwnViews(db, actor, access.Value.Project.Id)
            .Where(v => v.Id == viewId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? SavedViewFailures.NotFound() : Unit.Value;
    }

    /// <summary>只有自己的：別人的檢視在這裡就查不到，所以呼叫端看到的和不存在一樣。</summary>
    private static IQueryable<SavedViewEntity> OwnViews(WorkDbContext db, Actor actor, Guid projectId) =>
        db.SavedViews.Where(v => v.OrganizationId == actor.OrganizationId && v.ProjectId == projectId && v.OwnerId == actor.PersonId);

    /// <summary>
    /// 不分大小寫比對兩邊都交給資料庫（lower），和唯一索引用同一套規則。
    /// 同一個人在同一個專案的寫入已經被鎖排成一條，所以這裡看到的就是提交後的結果；索引是最後一道防線。
    /// </summary>
    private static Task<bool> NameInUseAsync(
        WorkDbContext db, Actor actor, Guid projectId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        OwnViews(db, actor, projectId).AnyAsync(v => v.Id != exceptId && v.Name.ToLower() == name.ToLower(), cancellationToken);

    private async Task<Result<ProjectAccess>> CheckAccessAsync(
        WorkDbContext db, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        return access.Can(WorkPermissions.ItemRead) ? access : access.Missing(WorkPermissions.ItemRead);
    }

    /// <summary>
    /// 寫入前的共同步驟：先確認看得到專案，再檢查欄位，最後鎖住（專案、人）。
    /// 上限與名稱都是「先數再寫」，同一個人同時送出時要排隊，數字才不會超過；
    /// 不同人與不同專案各有自己的鎖，不會互相等。鎖留到交易結束。
    /// </summary>
    private async Task<Result<(ProjectEntity Project, SavedViewDraft Draft)>> PrepareAsync(
        WorkDbContext db, Actor actor, string projectKey, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var project = access.Value.Project;
        var checkedDraft = SavedViewChecks.Check(draft);
        if (checkedDraft.Failure is { } invalid)
        {
            return invalid;
        }

        var lockKey = $"work.saved_views:{project.Id}:{actor.PersonId}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        return (project, checkedDraft.Value);
    }
}
