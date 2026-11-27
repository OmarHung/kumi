using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Persistence;
using PmTool.Work.Templates;

namespace PmTool.Work.Projects;

internal sealed partial class ProjectDescriptionService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, TimeProvider time) : IProjectDescriptionService
{
    public async Task<Result<ProjectDescription>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 看得到專案就能讀：包含只看自己項目的角色，要知道部署到哪裡。
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        return access is null
            ? WorkFailures.ProjectNotFound(projectKey)
            : await ReadAsync(db, actor, access.Project.Id, cancellationToken);
    }

    public Task<Result<ProjectDescription>> SetAsync(Actor actor, string projectKey, string? text, CancellationToken cancellationToken) =>
        SetCoreAsync(actor, projectKey, text, Expectation.None, cancellationToken);

    public Task<Result<ProjectDescription>> SetIfUnchangedAsync(
        Actor actor, string projectKey, string? text, DateTimeOffset? expectedUpdatedAt, CancellationToken cancellationToken) =>
        SetCoreAsync(actor, projectKey, text, new Expectation(true, expectedUpdatedAt), cancellationToken);

    /// <summary>要不要比對現在的更新時刻；<see cref="ExpectedUpdatedAt"/> 是 null 表示「還沒寫過」。</summary>
    private sealed record Expectation(bool Check, DateTimeOffset? ExpectedUpdatedAt)
    {
        public static Expectation None { get; } = new(false, null);
    }

    private async Task<Result<ProjectDescription>> SetCoreAsync(
        Actor actor, string projectKey, string? text, Expectation expectation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        // 權限先於內容的檢查：沒有權限的人不該從驗證訊息得知任何事。封存時 project.manage 還在，所以封存的專案也能改。
        if (!access.Can(WorkPermissions.ProjectManage))
        {
            return access.Missing(WorkPermissions.ProjectManage);
        }

        var checkedText = ProjectDescriptionChecks.Check(text);
        if (checkedText.Failure is { } invalid)
        {
            return invalid;
        }

        var projectId = access.Project.Id;

        // 比對與寫入在同一個交易、同一把專案的鎖裡：不然比對通過之後、寫入之前別人先寫了，這次就蓋掉別人的修改。
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockDescriptionAsync(db, projectId, cancellationToken);
        var current = await ReadRowAsync(db, actor, projectId, cancellationToken);

        if (expectation.Check && current?.UpdatedAt.UtcTicks != expectation.ExpectedUpdatedAt?.UtcTicks)
        {
            return new Failure(
                FailureKind.Conflict,
                WorkErrorCodes.ProjectDescriptionConflict,
                "專案說明在你讀到它之後被別人改過了，沒有改任何東西；請重新讀一次再決定。");
        }

        // 從沒寫過還要清掉：沒有東西可清，不建一列。
        if (current is null && checkedText.Value is null)
        {
            return ProjectDescription.Empty;
        }

        if (current is not null && current.Text == checkedText.Value)
        {
            return ToDescription(current);
        }

        var written = new ProjectDescriptionEntity
        {
            ProjectId = projectId,
            OrganizationId = actor.OrganizationId,
            Text = checkedText.Value,
            UpdatedAt = WorkClock.Now(time),
            UpdatedBy = actor.PersonId,
        };
        await UpsertAsync(db, written, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDescription(written);
    }

    private static Task LockDescriptionAsync(WorkDbContext db, Guid projectId, CancellationToken cancellationToken)
    {
        var lockKey = $"project_description:{projectId}";
        return db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    private static async Task<ProjectDescription> ReadAsync(
        WorkDbContext db, Actor actor, Guid projectId, CancellationToken cancellationToken)
    {
        var row = await ReadRowAsync(db, actor, projectId, cancellationToken);
        return row is null ? ProjectDescription.Empty : ToDescription(row);
    }

    private static Task<ProjectDescriptionEntity?> ReadRowAsync(
        WorkDbContext db, Actor actor, Guid projectId, CancellationToken cancellationToken) =>
        db.ProjectDescriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.OrganizationId == actor.OrganizationId && d.ProjectId == projectId, cancellationToken);

    private static ProjectDescription ToDescription(ProjectDescriptionEntity row) => new(row.Text, row.UpdatedAt, row.UpdatedBy);

    // 「有就更新」：同一個專案同時寫兩次，後到的覆蓋先到的，不會撞主鍵。
    private static Task UpsertAsync(WorkDbContext db, ProjectDescriptionEntity row, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO work.project_descriptions (project_id, organization_id, text, updated_at, updated_by)
            VALUES ({row.ProjectId}, {row.OrganizationId}, {row.Text}, {row.UpdatedAt}, {row.UpdatedBy})
            ON CONFLICT (project_id) DO UPDATE
            SET text = EXCLUDED.text, updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by
            WHERE project_descriptions.organization_id = EXCLUDED.organization_id
            """,
            cancellationToken);
}
