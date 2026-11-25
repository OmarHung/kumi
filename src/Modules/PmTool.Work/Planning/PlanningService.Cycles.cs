using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Planning;

internal sealed partial class PlanningService
{
    public async Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, WorkPermissions.ItemRead, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var projectId = access.Value.Project.Id;
        var rows = await OwnCycles(db, actor, projectId).AsNoTracking().OrderBy(c => c.StartDate).ThenBy(c => c.Id).ToListAsync(cancellationToken);
        var progress = await PlanningProgress.OfCyclesAsync(db, access.Value, null, cancellationToken);
        return rows.Select(c => ToContract(c, progress.GetValueOrDefault(c.Id, PlanningProgress.Empty))).ToList();
    }

    public async Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareWriteAsync(db, actor, projectKey, () => PlanningChecks.Check(draft), CycleLockScope, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (access, checkedDraft) = prepared.Value;
        var project = access.Project;
        if (await OwnCycles(db, actor, project.Id).CountAsync(cancellationToken) >= PlanningRules.MaxPerProject)
        {
            return PlanningFailures.CycleLimitReached();
        }

        if (await RuleBreakAsync(db, actor, project.Id, checkedDraft, exceptId: null, cancellationToken) is { } broken)
        {
            return broken;
        }

        var now = WorkClock.Now(time);
        var row = new CycleEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Id,
            Name = checkedDraft.Name,
            StartDate = checkedDraft.StartDate,
            EndDate = checkedDraft.EndDate,
            CreatedBy = actor.PersonId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Cycles.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.CycleNameIndex))
        {
            return PlanningFailures.CycleNameTaken(checkedDraft.Name);
        }

        return ToContract(row, PlanningProgress.Empty);
    }

    public async Task<Result<Cycle>> UpdateCycleAsync(
        Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareWriteAsync(db, actor, projectKey, () => PlanningChecks.Check(draft), CycleLockScope, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (access, checkedDraft) = prepared.Value;
        var project = access.Project;
        var current = await OwnCycles(db, actor, project.Id).AsNoTracking().SingleOrDefaultAsync(c => c.Id == cycleId, cancellationToken);
        if (current is null)
        {
            return PlanningFailures.CycleNotFound();
        }

        if (await RuleBreakAsync(db, actor, project.Id, checkedDraft, cycleId, cancellationToken) is { } broken)
        {
            return broken;
        }

        var updated = current with
        {
            Name = checkedDraft.Name,
            StartDate = checkedDraft.StartDate,
            EndDate = checkedDraft.EndDate,
            UpdatedAt = WorkClock.Now(time),
        };
        try
        {
            var affected = await OwnCycles(db, actor, project.Id)
                .Where(c => c.Id == cycleId)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(c => c.Name, updated.Name)
                        .SetProperty(c => c.StartDate, updated.StartDate)
                        .SetProperty(c => c.EndDate, updated.EndDate)
                        .SetProperty(c => c.UpdatedAt, updated.UpdatedAt),
                    cancellationToken);
            if (affected == 0)
            {
                // 讀到之後被另一個請求刪掉了；不能回成功，呼叫端會以為週期還在。
                return PlanningFailures.CycleNotFound();
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.CycleNameIndex))
        {
            return PlanningFailures.CycleNameTaken(checkedDraft.Name);
        }

        var progress = await PlanningProgress.OfCyclesAsync(db, access, cycleId, cancellationToken);
        return ToContract(updated, progress.GetValueOrDefault(cycleId, PlanningProgress.Empty));
    }

    // 修改項目先鎖項目再對週期取共用鎖，這裡先鎖週期再由外鍵清空項目，順序相反；被選為犧牲者就整個重做一次。
    public Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return DeadlockRetry.OnceAsync(() => DeleteCycleOnceAsync(actor, projectKey, cycleId, cancellationToken));
    }

    private async Task<Result<Unit>> DeleteCycleOnceAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和新增、修改一樣先取專案的共用鎖，才不會在封存之後還刪掉一筆。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await CheckWriteAccessAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        // 項目的外鍵是 SET NULL：項目留著，週期欄位由資料庫清成空。這一輪不為它們寫活動與事件（已知限制）：
        // 一次刪除可能牽動很多項目，逐一留紀錄要另外設計。
        var deleted = await OwnCycles(db, actor, access.Value.Project.Id).Where(c => c.Id == cycleId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted == 0 ? PlanningFailures.CycleNotFound() : Unit.Value;
    }

    private static IQueryable<CycleEntity> OwnCycles(WorkDbContext db, Actor actor, Guid projectId) =>
        db.Cycles.Where(c => c.OrganizationId == actor.OrganizationId && c.ProjectId == projectId);

    private static Cycle ToContract(CycleEntity row, ItemProgress progress) =>
        new(row.Id, row.Name, row.StartDate, row.EndDate, progress);

    /// <summary>
    /// 名稱不分大小寫的比對交給資料庫（lower，EF 只翻譯得了 ToLower），和唯一索引用同一套規則；比對的值用 ToLowerInvariant，不受執行環境的語系影響；日期含頭尾，只要有一天相同就算重疊。
    /// 同一個專案的寫入已經被鎖排成一條，所以這裡看到的就是前一個提交後的結果。
    /// </summary>
    private static async Task<Failure?> RuleBreakAsync(
        WorkDbContext db, Actor actor, Guid projectId, CycleDraft draft, Guid? exceptId, CancellationToken cancellationToken)
    {
        var others = OwnCycles(db, actor, projectId).AsNoTracking().Where(c => c.Id != exceptId);
        var name = draft.Name.ToLowerInvariant();
        if (await others.AnyAsync(c => c.Name.ToLower() == name, cancellationToken))
        {
            return PlanningFailures.CycleNameTaken(draft.Name);
        }

        var overlapping = await others
            .Where(c => c.StartDate <= draft.EndDate && c.EndDate >= draft.StartDate)
            .OrderBy(c => c.StartDate)
            .FirstOrDefaultAsync(cancellationToken);
        return overlapping is null ? null : PlanningFailures.CycleOverlaps(overlapping);
    }
}
