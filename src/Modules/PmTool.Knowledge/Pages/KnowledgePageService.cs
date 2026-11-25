using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using Microsoft.Extensions.Logging;
using PmTool.Identity;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;
using PmTool.Storage;

namespace PmTool.Knowledge.Pages;

/// <summary>
/// 知識庫的頁面。這個檔案是讀的部分與共用的小工具；寫入分在 <c>.Content</c>（新增、修改）、<c>.Tree</c>（搬移、刪除、復原）、
/// <c>.Restriction</c>（限制）、<c>.Trash</c>（垃圾桶的清單與永久刪除）。看不到的頁面一律當作不存在，所以每一條路都先找頁面、再檢查權限。
/// </summary>
internal sealed partial class KnowledgePageService(
    IDbContextFactory<KnowledgeDbContext> contexts,
    SpaceAccess access,
    IPeopleDirectory directory,
    IOrganizationClock organizationClock,
    IFileStore files,
    KnowledgeLimits limits,
    ILogger<KnowledgePageService> logger,
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

    public async Task<Result<PageRestriction>> GetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var target = await FindLiveAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        return target.Failure is { } failure ? failure : await BuildRestrictionAsync(db, actor, target.Value, cancellationToken);
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

    private static async Task<PageRestriction> BuildRestrictionAsync(
        KnowledgeDbContext db, Actor actor, PageTarget target, CancellationToken cancellationToken)
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

        return ComposeRestriction(actor, target.Space, listed, target.Page.RestrictedBy, RestrictedAncestorsOf(target.Scope, target.Page));
    }

    private static PageRestriction ComposeRestriction(
        Actor actor, SpaceContext space, IReadOnlyList<Guid>? people, Guid? restrictedBy, IReadOnlyList<PageReference> restrictedAncestors) =>
        new(people, restrictedAncestors)
        {
            RestrictedBy = people is null ? null : restrictedBy,
            CanRelax = people is null || CanRelax(actor, space, restrictedBy),
        };

    /// <summary>放寬一份限制（拿掉、加人、搬出有限制的範圍）：設定那份限制的人，或空間的管理者。</summary>
    private static bool CanRelax(Actor actor, SpaceContext space, Guid? restrictedBy) =>
        space.Permissions.CanManage || restrictedBy == actor.PersonId;

    private static IReadOnlyList<PageReference> RestrictedAncestorsOf(PageScope scope, PageInfo page) =>
        [.. scope.AncestorsOf(page).Where(a => a.IsRestricted).Select(a => a.ToReference())];

    /// <summary>找到頁面之後才讀內容：這一刻頁面可能剛被永久刪除，讀不到就是找不到。</summary>
    private static async Task<Result<PageDetail>> BuildDetailAsync(KnowledgeDbContext db, PageTarget target, CancellationToken cancellationToken)
    {
        var organizationId = target.Space.Space.OrganizationId;
        var row = await db.Pages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.Id == target.Page.Id, cancellationToken);
        if (row is null)
        {
            return KnowledgeFailures.PageNotFound(target.Page.Number);
        }

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
