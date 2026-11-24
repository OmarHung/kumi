using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Planning;

/// <summary>
/// 週期與里程碑。讀要 item.read，寫要 item.update（組織管理員都有）。
/// 寫入是「先數、先比對再寫」，所以同一個專案的同類寫入靠專案的 advisory lock 排成一條，鎖留到交易結束；
/// 唯一索引與資料庫的外鍵是最後一道防線。
/// </summary>
internal sealed partial class PlanningService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time) : IPlanningService
{
    private const string CycleLockScope = "work.cycles";
    private const string MilestoneLockScope = "work.milestones";

    private async Task<Result<ProjectAccess>> CheckAccessAsync(
        WorkDbContext db, Actor actor, string projectKey, string permission, CancellationToken cancellationToken)
    {
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        return access.Can(permission) ? access : access.Missing(permission);
    }

    /// <summary>
    /// 寫入前的共同步驟：先確認能改專案的項目，再檢查欄位，最後鎖住專案。欄位檢查在鎖之前：
    /// 不合格的請求不必排隊。
    /// </summary>
    private async Task<Result<(ProjectAccess Access, TDraft Draft)>> PrepareWriteAsync<TDraft>(
        WorkDbContext db,
        Actor actor,
        string projectKey,
        Func<Result<TDraft>> check,
        string lockScope,
        CancellationToken cancellationToken)
    {
        // 呼叫端已開交易：先取專案的共用鎖，封存（要排他鎖）要等這次寫入提交，封存之後才進來的寫入讀到的就是封存的專案。
        await ProjectLocks.ShareAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await CheckAccessAsync(db, actor, projectKey, WorkPermissions.ItemUpdate, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        // 週期與里程碑是整個專案的事：只看得到自己項目的人（item.read_own，例如執行者）有 item.update 也不能改，要看得到全部才行。
        if (access.Value.Items.OwnOnly)
        {
            return access.Value.Missing(WorkPermissions.ItemRead);
        }

        var checkedDraft = check();
        if (checkedDraft.Failure is { } invalid)
        {
            return invalid;
        }

        var project = access.Value.Project;
        var lockKey = $"{lockScope}:{project.Id}";
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        return (access.Value, checkedDraft.Value);
    }
}
