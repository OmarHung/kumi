using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Projects;

/// <summary>
/// 派工用的查詢。看得到專案的規則與 <c>IProjectService.GetAsync</c> 同一份（<see cref="ProjectAccess.LoadAsync"/>），
/// 項目數的範圍照 <see cref="ItemScope"/>，所以只看自己項目的人拿不到別人的數字。
/// </summary>
internal sealed class ProjectStaffingQueries(IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache) : IProjectStaffingQueries
{
    public async Task<Result<IReadOnlyList<ProjectRoleInfo>>> ListRolesAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        var schema = access.Schema;
        IReadOnlyList<ProjectRoleInfo> roles =
        [
            .. ProjectRoles.BuiltInKeys.Select(key =>
                new ProjectRoleInfo(key, ProjectRoles.DisplayName(key, schema), true, ProjectRoles.PermissionsOf(key, schema))),
            .. schema.Blueprint.Roles.Select(role =>
                new ProjectRoleInfo(role.Key, role.Name, false, ProjectRoles.PermissionsOf(role.Key, schema))),
        ];
        return Ok(roles);
    }

    public async Task<Result<IReadOnlyDictionary<Guid, int>>> CountOpenByAssigneeAsync(
        Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var access = await ProjectAccess.LoadAsync(db, cache, actor, projectKey, cancellationToken);
        if (access is null)
        {
            return WorkFailures.ProjectNotFound(projectKey);
        }

        var done = StateCategoryText.Done;
        var canceled = StateCategoryText.Canceled;
        var counts = await access.Items.Apply(ItemStore.Live(db))
            .Where(i => i.OrganizationId == actor.OrganizationId && i.ProjectId == access.Project.Id
                && i.AssigneeId != null && i.StateCategory != done && i.StateCategory != canceled)
            .GroupBy(i => i.AssigneeId!.Value)
            .Select(g => new { PersonId = g.Key, Open = g.Count() })
            .ToListAsync(cancellationToken);
        return Ok<IReadOnlyDictionary<Guid, int>>(counts.ToDictionary(c => c.PersonId, c => c.Open));
    }

    // Result 只提供從值的隱含轉換，泛型推不出介面型別，所以包一層。
    private static Result<T> Ok<T>(T value) => value;
}
