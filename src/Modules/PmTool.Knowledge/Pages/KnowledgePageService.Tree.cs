using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;
using PmTool.Knowledge.Common;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Pages;

internal sealed partial class KnowledgePageService
{
    public async Task<Result<PageTree>> MoveAsync(Actor actor, string spaceKey, int pageNumber, PageMove move, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(move);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var opened = await OpenTreeWriteAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (opened.Failure is { } failure)
        {
            return failure;
        }

        await using var transaction = opened.Value.Transaction;
        var target = opened.Value.Target;
        if (await HasHiddenPagesBelowAsync(db, actor, target, cancellationToken))
        {
            return HiddenDescendantsConflict("搬移");
        }

        var plan = PlanMove(target, move);
        if (plan.Failure is { } misplaced)
        {
            return misplaced;
        }

        var (newParent, newOrder, oldOrder) = plan.Value;
        if (newOrder is null)
        {
            return BuildTree(target.Space, target.Scope);
        }

        var spaceId = target.Space.Space.Id;
        if (oldOrder is not null)
        {
            // 換了上層：搬走的那一層補上空缺，搬進去的那一層連同它重新編號。
            Guid? newParentId = newParent?.Id;
            await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && p.Id == target.Page.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, newParentId), cancellationToken);
            await PageOrder.RenumberAsync(db, actor.OrganizationId, spaceId, oldOrder, cancellationToken);
        }

        await PageOrder.RenumberAsync(db, actor.OrganizationId, spaceId, newOrder, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return BuildTree(target.Space, await PageScope.LoadLiveAsync(db, actor, target.Space, cancellationToken));
    }

    public async Task<Result<PageDeletion>> DeleteAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var opened = await OpenTreeWriteAsync(db, actor, spaceKey, pageNumber, cancellationToken);
        if (opened.Failure is { } failure)
        {
            return failure;
        }

        await using var transaction = opened.Value.Transaction;
        var (space, scope, page) = opened.Value.Target;
        if (scope.HasHiddenDescendants(page))
        {
            return HiddenDescendantsConflict("刪除");
        }

        // 之前就刪了的子頁面保留自己那一批（它們不在 LiveDescendantsOf 裡）。
        var batch = new[] { page.Id }.Concat(scope.LiveDescendantsOf(page).Select(p => p.Id)).ToArray();
        var deletionId = Guid.CreateVersion7(clock.GetUtcNow());
        var now = clock.GetUtcNow();
        await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && batch.Contains(p.Id))
            .ExecuteUpdateAsync(
                set => set.SetProperty(p => p.DeletedAt, now)
                    .SetProperty(p => p.DeletedBy, actor.PersonId)
                    .SetProperty(p => p.DeletionId, deletionId),
                cancellationToken);
        var remaining = scope.LiveChildren(page.ParentId).Where(p => p.Id != page.Id);
        await PageOrder.RenumberAsync(db, actor.OrganizationId, space.Space.Id, PageOrder.Of(remaining), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PageDeletion(deletionId, page.ToReference(), batch.Length);
    }

    public async Task<Result<PageDetail>> RestoreAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await KnowledgeLocks.SpaceAsync(db, space.Space.Id, cancellationToken);

        // 只有復原要連垃圾桶一起讀：要找到那一批、算它放回去的位置。
        var scope = await PageScope.LoadWithTrashAsync(db, actor, space, cancellationToken);
        var page = scope.FindVisible(pageNumber);
        if (page is null)
        {
            return KnowledgeFailures.PageNotFound(pageNumber);
        }

        if (!page.IsDeleted)
        {
            return await BuildDetailAsync(db, new PageTarget(space, scope, page), cancellationToken);
        }

        if (space.CheckEdit() is { } denied)
        {
            return denied;
        }

        var batch = scope.BatchOf(page.DeletionId!.Value);
        if (scope.LiveCount + batch.Count > KnowledgeRules.MaxPagesPerSpace)
        {
            return KnowledgeFailures.Conflict(
                KnowledgeErrorCodes.PageLimitReached, $"拿回來之後會超過一個空間最多 {KnowledgeRules.MaxPagesPerSpace} 頁。");
        }

        await RestoreBatchAsync(db, actor, space.Space.Id, scope, batch, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var reloaded = await PageScope.LoadLiveAsync(db, actor, space, cancellationToken);
        return await BuildDetailAsync(db, new PageTarget(space, reloaded, reloaded.FindVisible(pageNumber)!), cancellationToken);
    }

    /// <summary>
    /// 底下有呼叫者看不到的頁面時，刪除、搬移、改限制都不能做：這些操作會改變那些頁面誰看得到、或把它們一起帶走。
    /// 搬移與改限制會改變子樹的上層鏈，垃圾桶裡的頁面照樣沿現在的上層算誰看得到、復原後繼承什麼，所以也要算進去
    /// （只讀這一棵子樹的垃圾桶）；刪除不改任何上層鏈，只看還沒刪的。已經在鎖裡，檢查與寫入之間沒有空窗。
    /// </summary>
    private static async Task<bool> HasHiddenPagesBelowAsync(
        KnowledgeDbContext db, Actor actor, PageTarget target, CancellationToken cancellationToken)
    {
        if (target.Scope.SeesAll)
        {
            return false;
        }

        var deep = await target.Scope.WithTrashBelowAsync(db, actor, target.Space.Space.Id, target.Page, cancellationToken);
        return deep.HasHiddenDescendantsIncludingTrash(target.Page);
    }

    /// <summary>
    /// 底下有呼叫者看不到的頁面時，刪除、搬移、改限制都不能做（回同一個 409）。
    /// </summary>
    private static Failure HiddenDescendantsConflict(string action) =>
        KnowledgeFailures.Conflict(
            KnowledgeErrorCodes.PageHasHiddenDescendants, $"底下有你看不到的頁面，不能{action}。請找看得到它們的人處理。");

    /// <summary>
    /// 拿回整批：清掉刪除的標記，最上面那一頁放到新的上層的最後面，整層重新編號。
    /// 原本的上層鏈有限制、而放回去之後它們不再是祖先時，把那些限制變成最上面那一頁自己的。
    /// </summary>
    private static async Task RestoreBatchAsync(
        KnowledgeDbContext db, Actor actor, Guid spaceId, PageScope scope, IReadOnlyList<PageInfo> batch, CancellationToken cancellationToken)
    {
        var top = PageScope.TopOfBatch(batch)!;
        var newParent = RestoreParent(scope, top, batch);
        var siblings = scope.LiveChildren(newParent?.Id);
        var inherited = await RestoredRestriction.ComputeAsync(db, actor, scope, top, newParent, cancellationToken);
        var ids = batch.Select(p => p.Id).ToArray();
        await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && ids.Contains(p.Id))
            .ExecuteUpdateAsync(
                set => set.SetProperty(p => p.DeletedAt, (DateTimeOffset?)null)
                    .SetProperty(p => p.DeletedBy, (Guid?)null)
                    .SetProperty(p => p.DeletionId, (Guid?)null),
                cancellationToken);
        Guid? newParentId = newParent?.Id;
        if (newParentId != top.ParentId)
        {
            await db.Pages.Where(p => p.OrganizationId == actor.OrganizationId && p.Id == top.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(p => p.ParentId, newParentId), cancellationToken);
        }

        if (inherited is not null)
        {
            await RestoredRestriction.WriteAsync(db, actor.OrganizationId, top.Id, inherited, cancellationToken);
        }

        await PageOrder.RenumberAsync(
            db, actor.OrganizationId, spaceId, PageOrder.Insert(PageOrder.Of(siblings), siblings.Count, top.Id, PageOrder.Unplaced), cancellationToken);
    }

    /// <summary>
    /// 拿回來放在哪一層：原本的上層還在就放回去（最後面），不在（在垃圾桶裡）或放回去會超過 <see cref="KnowledgeRules.MaxDepth"/> 層就放最上層。
    /// 後者是搬移之後才會有的情形：上層被搬到更深的地方，它垃圾桶裡的子頁面不算在搬移的深度檢查裡。
    /// </summary>
    private static PageInfo? RestoreParent(PageScope scope, PageInfo top, IReadOnlyList<PageInfo> batch)
    {
        var parent = scope.ById(top.ParentId);
        if (parent is null || parent.IsDeleted)
        {
            return null;
        }

        var batchDepth = BatchDepth(batch, top);
        return scope.DepthOf(parent) + batchDepth > KnowledgeRules.MaxDepth ? null : parent;
    }

    private static int BatchDepth(IReadOnlyList<PageInfo> batch, PageInfo top)
    {
        var children = batch.ToLookup(p => p.ParentId);
        var deepest = 0;
        var pending = new Stack<(Guid Id, int Depth)>();
        pending.Push((top.Id, 1));
        while (pending.TryPop(out var current))
        {
            deepest = Math.Max(deepest, current.Depth);
            foreach (var child in children[current.Id])
            {
                pending.Push((child.Id, current.Depth + 1));
            }
        }

        return deepest;
    }

    /// <summary>
    /// 搬移的計畫：新的上層、新那一層的順序、舊那一層的順序（沒換上層是 null）。兩個順序都是 null 表示位置沒變。
    /// 新的上層不能是自己或自己底下的；深度是「新的位置的深度 + 這一頁底下最深的相對層數」。
    /// </summary>
    private static Result<(PageInfo? Parent, IReadOnlyList<(Guid Id, int Current)>? NewOrder, IReadOnlyList<(Guid Id, int Current)>? OldOrder)> PlanMove(
        PageTarget target, PageMove move)
    {
        var (_, scope, page) = target;
        var parent = scope.FindVisible(move.ParentNumber);
        if (move.ParentNumber is not null && parent is not { IsDeleted: false })
        {
            return KnowledgeFailures.Invalid(KnowledgeErrorCodes.PagePositionInvalid, "parentNumber", "找不到要放在它底下的頁面。");
        }

        if (parent is not null && scope.ChainTo(parent.Id).Any(p => p.Id == page.Id))
        {
            return KnowledgeFailures.Invalid(KnowledgeErrorCodes.PagePositionInvalid, "parentNumber", "不能放在自己或自己的子頁面底下。");
        }

        var newDepth = (parent is null ? 0 : scope.DepthOf(parent)) + 1;
        if (newDepth + scope.MaxRelativeDepth(page) > KnowledgeRules.MaxDepth)
        {
            return KnowledgeFailures.Invalid(
                KnowledgeErrorCodes.PageTooDeep, "parentNumber", $"頁面最多 {KnowledgeRules.MaxDepth} 層，搬到這裡連同它底下的頁面會超過。");
        }

        var siblings = scope.LiveChildren(parent?.Id).Where(s => s.Id != page.Id).ToList();
        var index = IndexBefore(scope, siblings, move.BeforeNumber, exclude: page);
        if (index is null)
        {
            return KnowledgeFailures.Invalid(KnowledgeErrorCodes.PagePositionInvalid, "beforeNumber", "要放在它前面的頁面，必須是同一層看得到的頁面。");
        }

        var sameParent = parent?.Id == page.ParentId;
        var newOrder = PageOrder.Insert(PageOrder.Of(siblings), index.Value, page.Id, sameParent ? page.Position : PageOrder.Unplaced);
        if (sameParent && newOrder.Select(e => e.Id).SequenceEqual(scope.LiveChildren(page.ParentId).Select(s => s.Id)))
        {
            return (parent, null, null);
        }

        var oldOrder = sameParent ? null : PageOrder.Of(scope.LiveChildren(page.ParentId).Where(s => s.Id != page.Id));
        return (parent, newOrder, oldOrder);
    }

    /// <summary>
    /// 樹狀的寫入從這裡開始：開交易、拿空間的鎖、在鎖裡讀範圍，再找頁面（看不到或已刪是 NotFound）與檢查可不可以改。
    /// 先拿鎖再讀：讀到的樹要是鎖之後的樣子，位置才算得對。
    /// </summary>
    private async Task<Result<TreeWrite>> OpenTreeWriteAsync(
        KnowledgeDbContext db, Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        var space = await access.FindAsync(db, actor, spaceKey, cancellationToken);
        if (space is null)
        {
            return KnowledgeFailures.SpaceNotFound(spaceKey);
        }

        var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await KnowledgeLocks.SpaceAsync(db, space.Space.Id, cancellationToken);
        var scope = await PageScope.LoadLiveAsync(db, actor, space, cancellationToken);
        var found = FindLive(space, scope, pageNumber);
        var denied = found.Failure ?? space.CheckEdit();
        if (denied is not null)
        {
            await transaction.DisposeAsync();
            return denied;
        }

        return new TreeWrite(transaction, found.Value);
    }

    private sealed record TreeWrite(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction Transaction, PageTarget Target);
}
