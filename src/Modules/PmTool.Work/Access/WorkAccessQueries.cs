using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Access;

/// <summary>給通知模組用的存取查詢。判斷「看得到」的規則和讀專案、讀項目的一樣，只有這個模組決定。</summary>
internal sealed class WorkAccessQueries(IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache) : IWorkAccessQueries
{
    public async Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await ItemStore.FindAsync(db, cache, actor, itemKey, WorkPermissions.ItemRead, forUpdate: false, cancellationToken);
        if (target.Failure is { } failure)
        {
            return failure;
        }

        var (access, item, _) = target.Value;
        return new ItemReference(
            access.Project.Id, access.Project.Key, item.Id, ItemKeys.Format(access.Project.Key, item.Number), item.Title);
    }

    public async Task<IReadOnlySet<Guid>> FilterReadersAsync(
        Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(personIds);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return new HashSet<Guid>();
        }

        var schema = await cache.GetAsync(db, organizationId, project.Id, project.CurrentBlueprintVersion, cancellationToken);
        return await ProjectReaders.FilterAsync(db, organizationId, project.Id, schema, personIds, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(personIds);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var project = await db.Projects
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == projectId, cancellationToken);
        if (project is null)
        {
            return new HashSet<Guid>();
        }

        var schema = await cache.GetAsync(db, organizationId, project.Id, project.CurrentBlueprintVersion, cancellationToken);
        return await ProjectReaders.FilterItemAsync(db, organizationId, project.Id, schema, item, personIds, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(itemIds);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadableProjects.ItemIdsAsync(db, cache, actor, itemIds, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadableProjects.IdsAsync(db, cache, actor, cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadableProjects.OwnOnlyIdsAsync(db, cache, actor, cancellationToken);
    }
}
