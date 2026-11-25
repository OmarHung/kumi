using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

/// <summary>
/// 知識庫的頁面。這個檔案是讀的部分與共用的小工具；寫入分在 <c>.Content</c>（新增、修改）、<c>.Tree</c>（搬移、刪除、復原）、
/// <c>.Restriction</c>（限制）。看不到的頁面一律當作不存在，所以每一條路都先找頁面、再檢查權限。
/// </summary>
internal sealed partial class KnowledgePageService(
    IDbContextFactory<KnowledgeDbContext> contexts,
    SpaceAccess access,
    IPeopleDirectory directory,
    TimeProvider clock) : IKnowledgePageService
{
    public async Task<Result<PageTree>> GetTreeAsync(Actor actor, string spaceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        return BuildTree(space, await PageScope.LoadLiveAsync(db, actor, space, cancellationToken));
    }

    public async Task<Result<PageDetail>> GetAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        return target.Failure is { } failure ? failure : await BuildDetailAsync(db, target.Value, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<TrashEntry>>> ListTrashAsync(Actor actor, string spaceKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        var scope = await PageScope.LoadWithTrashAsync(db, actor, space, cancellationToken);
        return scope.VisibleTrash()
            .Select(entry => new TrashEntry(
                entry.Top.DeletionId!.Value,
                entry.Top.ToReference(),
                entry.PageCount,
                scope.ById(entry.Top.ParentId)?.ToReference(),
                entry.Top.DeletedAt!.Value,
                entry.Top.DeletedBy!.Value))
            .ToList();
    }

    public async Task<Result<PageRestriction>> GetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        return target.Failure is { } failure ? failure : await BuildRestrictionAsync(db, target.Value, cancellationToken);
    }

    /// <summary>找呼叫者看得到的、還沒刪的頁面。空間或頁面看不到都是 NotFound。</summary>
    private async Task<Result<PageTarget>> FindLiveAsync(
        KnowledgeDbContext db, Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        var scope = await PageScope.LoadLiveAsync(db, actor, space, cancellationToken);
        return FindLive(space, scope, pageNumber);
    }

    private static Result<PageTarget> FindLive(SpaceContext space, PageScope scope, int pageNumber) =>
        scope.FindVisible(pageNumber) is { IsDeleted: false } page
            ? new PageTarget(space, scope, page)
            : KnowledgeFailures.PageNotFound(pageNumber);

    private static PageTree BuildTree(SpaceContext space, PageScope scope) =>
        new(space.ToSummary(), [.. scope.VisibleTree().Select(p => p.ToNode())]);

    private static async Task<PageRestriction> BuildRestrictionAsync(KnowledgeDbContext db, PageTarget target, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid>? listed = null;
        if (target.Page.IsRestricted)
        {
            listed = await db.PageRestrictions.AsNoTracking()
                .Where(r => r.OrganizationId == target.Space.Space.OrganizationId && r.PageId == target.Page.Id)
                .OrderBy(r => r.Position)
                .Select(r => r.PersonId)
                .ToListAsync(cancellationToken);
        }

        return new PageRestriction(listed, RestrictedAncestorsOf(target.Scope, target.Page));
    }

    private static IReadOnlyList<PageReference> RestrictedAncestorsOf(PageScope scope, PageInfo page) =>
        [.. scope.AncestorsOf(page).Where(a => a.IsRestricted).Select(a => a.ToReference())];

    private static async Task<PageDetail> BuildDetailAsync(KnowledgeDbContext db, PageTarget target, CancellationToken cancellationToken)
    {
        var organizationId = target.Space.Space.OrganizationId;
        var row = await db.Pages.AsNoTracking()
            .SingleAsync(p => p.OrganizationId == organizationId && p.Id == target.Page.Id, cancellationToken);
        var versionCount = await db.PageVersions.CountAsync(v => v.OrganizationId == organizationId && v.PageId == row.Id, cancellationToken);
        return ComposeDetail(target.Space, target.Scope, target.Page, row.Body, row.Revision, row.CreatedAt, row.CreatedBy, versionCount);
    }

    private static PageDetail ComposeDetail(
        SpaceContext space,
        PageScope scope,
        PageInfo page,
        string body,
        long revision,
        DateTimeOffset createdAt,
        Guid createdBy,
        int versionCount) =>
        new(
            page.Id,
            space.ToSummary(),
            page.Number,
            page.Title,
            body,
            revision,
            [.. scope.AncestorsOf(page).Select(a => a.ToReference())],
            page.IsRestricted,
            RestrictedAncestorsOf(scope, page),
            createdAt,
            createdBy,
            page.UpdatedAt,
            page.UpdatedBy,
            versionCount,
            new PagePermissions(
                space.Permissions.CanEdit,
                space.Permissions.CanComment,
                space.Permissions.CanEdit && !scope.HasHiddenDescendants(page)));

    /// <summary>空間、這一份看得到的頁面範圍，與要處理的那一頁。</summary>
    private sealed record PageTarget(SpaceContext Space, PageScope Scope, PageInfo Page);
}
