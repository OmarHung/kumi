using Microsoft.EntityFrameworkCore;
using Npgsql;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;
using PmTool.Work;

namespace PmTool.Knowledge.Spaces;

internal sealed class KnowledgeSpaceService(
    IDbContextFactory<KnowledgeDbContext> contexts,
    SpaceAccess access,
    IWorkAccessQueries work,
    TimeProvider clock) : IKnowledgeSpaceService
{
    public async Task<IReadOnlyList<SpaceSummary>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return [.. (await access.ListAsync(db, actor, cancellationToken)).Select(c => c.ToSummary())];
    }

    public async Task<Result<SpaceSummary>> GetAsync(Actor actor, string spaceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        return space is null ? KnowledgeFailures.SpaceNotFound(spaceKey) : space.ToSummary();
    }

    public async Task<Result<SpaceSummary>> CreateAsync(Actor actor, NewSpace space, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(space);

        // 權限先於內容的檢查：沒有權限的人不該靠 422 探到什麼。
        var target = await CheckCreatePermissionAsync(actor, space.ProjectKey, cancellationToken);
        if (target.Failure is { } denied)
        {
            return denied;
        }

        var checkedSpace = CheckNewSpace(space);
        if (checkedSpace.Failure is { } invalid)
        {
            return invalid;
        }

        var (key, name, description) = checkedSpace.Value;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await KnowledgeLocks.OrganizationAsync(db, actor.OrganizationId, cancellationToken);
        var taken = await CheckAvailabilityAsync(db, actor.OrganizationId, key, target.Value?.ProjectId, cancellationToken);
        if (taken is not null)
        {
            return taken;
        }

        var inserted = await InsertSpaceAsync(db, actor, (key, name, description), target.Value?.ProjectId, cancellationToken);
        if (inserted.Failure is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        var row = inserted.Value;

        // 建立的人一定是能管理它的人（組織管理員，或專案的 project.manage），所以組織空間用他自己的身分算：不必再問一次身分模組。
        var context = target.Value is { } project
            ? SpaceAccess.ForProjectSpace(row, project)
            : SpaceAccess.ForOrganizationSpace(actor, row, OrganizationRole.Admin)!;
        return context.ToSummary();
    }

    /// <summary>寫入新的空間。呼叫端在組織的鎖裡；唯一索引擋下的衝突回 <c>space_key_taken</c>。</summary>
    private async Task<Result<SpaceRow>> InsertSpaceAsync(
        KnowledgeDbContext db, Actor actor, (string Key, string Name, string? Description) space, Guid? projectId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var row = new SpaceRow
        {
            Id = Guid.CreateVersion7(now),
            OrganizationId = actor.OrganizationId,
            Key = space.Key,
            Name = space.Name,
            Description = space.Description,
            ProjectId = projectId,
            NextPageNumber = 1,
            CreatedBy = actor.PersonId,
            CreatedAt = now,
            UpdatedBy = actor.PersonId,
            UpdatedAt = now,
        };
        db.Spaces.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // 鎖已經排好隊；唯一索引是最後一道防線（例如別的程序沒走這條路寫入）。
            return KnowledgeFailures.Conflict(KnowledgeErrorCodes.SpaceKeyTaken, $"代號 {space.Key} 已經有空間在用了。");
        }

        return row;
    }

    public async Task<Result<SpaceSummary>> UpdateAsync(Actor actor, string spaceKey, SpaceSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(settings);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (context is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        if (context.CheckManage() is { } denied)
        {
            return denied;
        }

        var checkedSettings = CheckSettings(settings);
        if (checkedSettings.Failure is { } invalid)
        {
            return invalid;
        }

        var (name, description) = checkedSettings.Value;
        if (name == context.Space.Name && description == context.Space.Description)
        {
            return context.ToSummary();
        }

        var now = clock.GetUtcNow();
        await db.Spaces.Where(s => s.Id == context.Space.Id)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.Name, name)
                    .SetProperty(s => s.Description, description)
                    .SetProperty(s => s.UpdatedBy, actor.PersonId)
                    .SetProperty(s => s.UpdatedAt, now),
                cancellationToken);
        return (context with { Space = context.Space with { Name = name, Description = description, UpdatedBy = actor.PersonId, UpdatedAt = now } }).ToSummary();
    }

    public async Task<Result<SpaceSummary>> SetArchivedAsync(Actor actor, string spaceKey, bool archived, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var context = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (context is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        if (context.CheckManage() is { } denied)
        {
            return denied;
        }

        if (archived == (context.Space.ArchivedAt is not null))
        {
            return context.ToSummary();
        }

        var now = clock.GetUtcNow();
        DateTimeOffset? archivedAt = archived ? now : null;
        Guid? archivedBy = archived ? actor.PersonId : null;
        await db.Spaces.Where(s => s.Id == context.Space.Id)
            .ExecuteUpdateAsync(
                set => set.SetProperty(s => s.ArchivedAt, archivedAt)
                    .SetProperty(s => s.ArchivedBy, archivedBy)
                    .SetProperty(s => s.UpdatedBy, actor.PersonId)
                    .SetProperty(s => s.UpdatedAt, now),
                cancellationToken);
        var updated = context.Space with { ArchivedAt = archivedAt, ArchivedBy = archivedBy, UpdatedBy = actor.PersonId, UpdatedAt = now };

        // 權限與唯讀要依新的狀態重算（綁的專案封存時，取消封存空間也還是唯讀）。
        var refreshed = context.Project is { } project
            ? SpaceAccess.ForProjectSpace(updated, project)
            : SpaceAccess.ForOrganizationSpace(actor, updated, OrganizationRole.Admin)!;
        return refreshed.ToSummary();
    }

    /// <summary>
    /// 建立前的權限：組織空間要組織管理員；專案空間要看得到專案（不然 422）、有 project.manage、專案沒封存。
    /// 成功時回傳專案（組織空間是 null）。
    /// </summary>
    private async Task<Result<ProjectAccessSummary?>> CheckCreatePermissionAsync(Actor actor, string? projectKey, CancellationToken cancellationToken)
    {
        if (projectKey is null)
        {
            return actor.IsOrganizationAdmin
                ? (ProjectAccessSummary?)null
                : KnowledgeFailures.Forbidden("只有組織管理員可以建立組織空間。");
        }

        var project = await work.FindVisibleProjectAsync(actor, projectKey, cancellationToken);
        if (project is null)
        {
            return KnowledgeFailures.Invalid(KnowledgeErrorCodes.ProjectNotFound, "projectKey", "找不到這個專案。");
        }

        if (!project.Permissions.Contains(WorkPermissions.ProjectManage))
        {
            return KnowledgeFailures.Forbidden("你在這個專案沒有管理專案的權限，不能替它建立空間。");
        }

        return project.IsArchived ? KnowledgeFailures.SpaceArchived(project.Name) : project;
    }

    private static Result<(string Key, string Name, string? Description)> CheckNewSpace(NewSpace space)
    {
        var problems = new List<FailureDetail>();
        if (!KnowledgeText.TryNormalizeKey(space.Key, out var key))
        {
            problems.Add(new FailureDetail(
                KnowledgeErrorCodes.SpaceKeyInvalid, "key", "代號要以英文字母開頭，只能有英文字母與數字，2 到 10 個字元。"));
        }

        var settings = CheckSettings(new SpaceSettings(space.Name, space.Description));
        if (settings.Failure is { } failure)
        {
            problems.AddRange(failure.Details);
        }

        return problems.Count > 0 ? KnowledgeFailures.Invalid(problems) : (key, settings.Value.Name, settings.Value.Description);
    }

    private static Result<(string Name, string? Description)> CheckSettings(SpaceSettings settings)
    {
        var problems = new List<FailureDetail>();
        var name = KnowledgeText.CheckName(
            settings.Name,
            KnowledgeRules.MaxSpaceNameLength,
            "name",
            KnowledgeErrorCodes.SpaceNameRequired,
            KnowledgeErrorCodes.SpaceNameTooLong,
            "名稱");
        if (name.Failure is { } nameFailure)
        {
            problems.AddRange(nameFailure.Details);
        }

        var description = KnowledgeText.CheckDescription(settings.Description);
        if (description.Failure is { } descriptionFailure)
        {
            problems.AddRange(descriptionFailure.Details);
        }

        return problems.Count > 0 ? KnowledgeFailures.Invalid(problems) : (name.Value, description.Value);
    }

    private static async Task<Failure?> CheckAvailabilityAsync(
        KnowledgeDbContext db, Guid organizationId, string key, Guid? projectId, CancellationToken cancellationToken)
    {
        if (projectId is { } project
            && await db.Spaces.AnyAsync(s => s.OrganizationId == organizationId && s.ProjectId == project, cancellationToken))
        {
            return KnowledgeFailures.Conflict(KnowledgeErrorCodes.SpaceProjectTaken, "這個專案已經有空間了，一個專案最多一個空間。");
        }

        if (await db.Spaces.AnyAsync(s => s.OrganizationId == organizationId && s.Key == key, cancellationToken))
        {
            return KnowledgeFailures.Conflict(KnowledgeErrorCodes.SpaceKeyTaken, $"代號 {key} 已經有空間在用了。");
        }

        return await db.Spaces.CountAsync(s => s.OrganizationId == organizationId, cancellationToken) >= KnowledgeRules.MaxSpacesPerOrganization
            ? KnowledgeFailures.Conflict(KnowledgeErrorCodes.SpaceLimitReached, $"組織最多 {KnowledgeRules.MaxSpacesPerOrganization} 個空間（含封存的）。")
            : null;
    }
}
