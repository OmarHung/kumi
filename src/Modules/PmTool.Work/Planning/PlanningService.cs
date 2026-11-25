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
    /// 新增、修改、刪除共用的寫入檢查：週期與里程碑是整個專案的事，除了 item.update，還要看得到全部項目；
    /// 只看自己的、或完全沒有讀取權限的角色，有 item.update 也不能動。
    /// </summary>
    private async Task<Result<ProjectAccess>> CheckWriteAccessAsync(
        WorkDbContext db, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(db, actor, projectKey, WorkPermissions.ItemUpdate, cancellationToken);
        if (access.Failure is not null)
        {
            return access;
        }

        // 只看得到自己項目的人讀得到項目，說「沒有查看項目的權限」會讓他摸不著頭緒：直接說缺的是什麼。
        return access.Value.ReadsAllItems
            ? access
            : WorkFailures.Forbidden("週期與里程碑是整個專案共用的：要看得到這個專案的全部項目，才能新增、修改或刪除。");
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
        var access = await CheckWriteAccessAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
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
