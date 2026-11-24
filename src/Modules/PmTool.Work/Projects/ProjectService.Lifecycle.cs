using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Common;
using PmTool.Work.Persistence;

namespace PmTool.Work.Projects;

/// <summary>專案的改名、封存與取消封存。三個都要 project.manage，封存的專案也一樣可以做。</summary>
internal sealed partial class ProjectService
{
    public async Task<Result<ProjectSummary>> RenameAsync(Actor actor, string projectKey, string name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ManageableAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        // 權限先於名稱的檢查：沒有權限的人不該從驗證訊息得知任何事。
        var checkedName = NewProjectRules.CheckName(name);
        if (checkedName.Failure is { } invalid)
        {
            return invalid;
        }

        if (checkedName.Value == access.Value.Project.Name)
        {
            return access.Value.Summary;
        }

        await db.Projects
            .Where(p => p.OrganizationId == actor.OrganizationId && p.Id == access.Value.Project.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Name, checkedName.Value), cancellationToken);
        return await ReloadSummaryAsync(db, actor, projectKey, cancellationToken);
    }

    public Task<Result<ProjectSummary>> ArchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        SetArchivedAsync(actor, projectKey, archive: true, cancellationToken);

    public Task<Result<ProjectSummary>> UnarchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        SetArchivedAsync(actor, projectKey, archive: false, cancellationToken);

    private async Task<Result<ProjectSummary>> SetArchivedAsync(
        Actor actor, string projectKey, bool archive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        try
        {
            return await SetArchivedInTransactionAsync(db, actor, projectKey, archive, cancellationToken);
        }
        catch (Exception exception) when (PostgresErrors.IsLockTimeout(exception))
        {
            // 例外離開交易的範圍時交易已經退回，什麼都沒改。
            return WorkFailures.ProjectBusy(archive ? "封存" : "取消封存");
        }
    }

    private async Task<Result<ProjectSummary>> SetArchivedInTransactionAsync(
        WorkDbContext db, Actor actor, string projectKey, bool archive, CancellationToken cancellationToken)
    {

        // 專案那一列鎖到交易結束：進行中的寫入（先拿共用鎖）做完才輪到這裡，封存之後進來的寫入讀到的就是封存的專案。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await ProjectLocks.LimitWaitAsync(db, limits.ApplyLockTimeout, cancellationToken);
        await ProjectLocks.ExclusiveAsync(db, actor.OrganizationId, projectKey, cancellationToken);
        var access = await ManageableAsync(db, actor, projectKey, cancellationToken);
        if (access.Failure is { } failure)
        {
            return failure;
        }

        var project = access.Value.Project;
        if (access.Value.IsArchived == archive)
        {
            return access.Value.Summary;
        }

        var (at, by) = archive ? (WorkClock.Now(time), (Guid?)actor.PersonId) : ((DateTimeOffset?)null, (Guid?)null);
        await db.Projects
            .Where(p => p.OrganizationId == actor.OrganizationId && p.Id == project.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.ArchivedAt, at).SetProperty(p => p.ArchivedBy, by), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ReloadSummaryAsync(db, actor, projectKey, cancellationToken);
    }

    /// <summary>看得到專案、而且有 project.manage（封存時這個權限還留著）；看不到是 NotFound。</summary>
    private async Task<Result<ProjectAccess>> ManageableAsync(
        WorkDbContext db, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        return access.Can(WorkPermissions.ProjectManage) ? access : access.Missing(WorkPermissions.ProjectManage);
    }

    private async Task<Result<ProjectSummary>> ReloadSummaryAsync(
        WorkDbContext db, Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        return access is null ? WorkFailures.ProjectNotFound(projectKey) : access.Summary;
    }
}
