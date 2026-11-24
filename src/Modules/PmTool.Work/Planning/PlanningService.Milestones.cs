using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Planning;

internal sealed partial class PlanningService
{
    public async Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, WorkPermissions.ItemRead, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var projectId = access.Value.Project.Id;
        var rows = await OwnMilestones(db, actor, projectId).AsNoTracking().ToListAsync(cancellationToken);
        var progress = await PlanningProgress.OfMilestonesAsync(db, access.Value, null, cancellationToken);

        // 同一天依名稱：在記憶體裡排，順序才不受資料庫的排序規則影響（和檢視一樣）。
        return rows
            .OrderBy(m => m.DueDate)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ThenBy(m => m.Id)
            .Select(m => ToContract(m, progress.GetValueOrDefault(m.Id, PlanningProgress.Empty)))
            .ToList();
    }

    public async Task<Result<Milestone>> CreateMilestoneAsync(
        Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareWriteAsync(db, actor, projectKey, () => PlanningChecks.Check(draft), MilestoneLockScope, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (access, checkedDraft) = prepared.Value;
        var project = access.Project;
        if (await OwnMilestones(db, actor, project.Id).CountAsync(cancellationToken) >= PlanningRules.MaxPerProject)
        {
            return PlanningFailures.MilestoneLimitReached();
        }

        if (await NameInUseAsync(db, actor, project.Id, checkedDraft.Name, exceptId: null, cancellationToken))
        {
            return PlanningFailures.MilestoneNameTaken(checkedDraft.Name);
        }

        var now = WorkClock.Now(time);
        var row = new MilestoneEntity
        {
            Id = WorkClock.NewId(time),
            OrganizationId = actor.OrganizationId,
            ProjectId = project.Id,
            Name = checkedDraft.Name,
            DueDate = checkedDraft.DueDate,
            Description = checkedDraft.Description,
            CreatedBy = actor.PersonId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Milestones.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.MilestoneNameIndex))
        {
            return PlanningFailures.MilestoneNameTaken(checkedDraft.Name);
        }

        return ToContract(row, PlanningProgress.Empty);
    }

    public async Task<Result<Milestone>> UpdateMilestoneAsync(
        Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var prepared = await PrepareWriteAsync(db, actor, projectKey, () => PlanningChecks.Check(draft), MilestoneLockScope, cancellationToken);
        if (prepared.Failure is { } failure)
        {
            return failure;
        }

        var (access, checkedDraft) = prepared.Value;
        var project = access.Project;
        var current = await OwnMilestones(db, actor, project.Id).AsNoTracking().SingleOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        if (current is null)
        {
            return PlanningFailures.MilestoneNotFound();
        }

        if (await NameInUseAsync(db, actor, project.Id, checkedDraft.Name, milestoneId, cancellationToken))
        {
            return PlanningFailures.MilestoneNameTaken(checkedDraft.Name);
        }

        var updated = current with
        {
            Name = checkedDraft.Name,
            DueDate = checkedDraft.DueDate,
            Description = checkedDraft.Description,
            UpdatedAt = WorkClock.Now(time),
        };
        try
        {
            var affected = await OwnMilestones(db, actor, project.Id)
                .Where(m => m.Id == milestoneId)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(m => m.Name, updated.Name)
                        .SetProperty(m => m.DueDate, updated.DueDate)
                        .SetProperty(m => m.Description, updated.Description)
                        .SetProperty(m => m.UpdatedAt, updated.UpdatedAt),
                    cancellationToken);
            if (affected == 0)
            {
                return PlanningFailures.MilestoneNotFound();
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgresErrors.IsUniqueViolation(exception, WorkDbContext.MilestoneNameIndex))
        {
            return PlanningFailures.MilestoneNameTaken(checkedDraft.Name);
        }

        var progress = await PlanningProgress.OfMilestonesAsync(db, access, milestoneId, cancellationToken);
        return ToContract(updated, progress.GetValueOrDefault(milestoneId, PlanningProgress.Empty));
    }

    // 和刪除週期一樣：順序和修改項目相反，被選為互鎖的犧牲者就整個重做一次。
    public Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return DeadlockRetry.OnceAsync(() => DeleteMilestoneOnceAsync(actor, projectKey, milestoneId, cancellationToken));
    }

    private async Task<Result<Unit>> DeleteMilestoneOnceAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 和新增、修改一樣先取專案的共用鎖，才不會在封存之後還刪掉一筆。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, WorkPermissions.ItemUpdate, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        // 項目的欄位由資料庫清成空，不寫活動與事件（已知限制）。
        var deleted = await OwnMilestones(db, actor, access.Value.Project.Id)
            .Where(m => m.Id == milestoneId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted == 0 ? PlanningFailures.MilestoneNotFound() : Unit.Value;
    }

    private static IQueryable<MilestoneEntity> OwnMilestones(WorkDbContext db, Actor actor, Guid projectId) =>
        db.Milestones.Where(m => m.OrganizationId == actor.OrganizationId && m.ProjectId == projectId);

    private static Milestone ToContract(MilestoneEntity row, ItemProgress progress) =>
        new(row.Id, row.Name, row.DueDate, row.Description, progress);

    private static Task<bool> NameInUseAsync(
        WorkDbContext db, Actor actor, Guid projectId, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLowerInvariant();
        return OwnMilestones(db, actor, projectId).AnyAsync(m => m.Id != exceptId && m.Name.ToLower() == lowered, cancellationToken);
    }
}
