using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Access;

/// <summary>一頁的骨架：沒有內容，只有樹要用的欄位。</summary>
internal sealed record PageInfo(
    Guid Id,
    int Number,
    Guid? ParentId,
    int Position,
    string Title,
    bool IsRestricted,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy,
    DateTimeOffset? DeletedAt,
    Guid? DeletedBy,
    Guid? DeletionId,
    Guid? RestrictedBy = null)
{
    public bool IsDeleted => DeletedAt is not null;

    public PageReference ToReference() => new(Id, Number, Title);

    public PageNode ToNode() => new(Id, Number, ParentId, Position, Title, IsRestricted, UpdatedAt, UpdatedBy);
}

/// <summary>
/// 呼叫者在一個空間裡看得到哪些頁面。規則只有這一份：看得到頁面 = 組織管理員，或者這一頁與它每一層上層裡
/// 自己設了限制的，每一份名單都有呼叫者（限制往下繼承，只能更嚴）。垃圾桶裡的頁面照原本的位置算。
/// 一次讀出空間裡還沒刪的頁面的骨架與呼叫者所在的名單（最多 <see cref="KnowledgeRules.MaxPagesPerSpace"/> 頁），
/// 之後都在記憶體裡算，不是一頁一個查詢。呼叫者要先確認看得到這個空間。
/// 平常的路只讀沒刪的頁面（<see cref="LoadLiveAsync"/>）：垃圾桶沒有上限，不能讓每個請求都跟著變慢；
/// 活的頁面的上層一定是活的（刪除連底下一起刪、復原時上層不在就放最上層），所以不讀垃圾桶也算得出可見性。
/// 垃圾桶的清單、復原、改上層鏈的操作只補讀它們要的那幾批（<see cref="WithTrashBatchesAsync"/>、<see cref="WithTrashBelowAsync"/>），不讀整個垃圾桶。
/// 資料庫的複合外鍵只保證上層在同一個空間，擋不住兩頁互為上層：每一個沿著 <c>parent_id</c> 走的迴圈都記走過的頁面
/// 並限制層數，成環的頁面對一般人當作不存在。
/// </summary>
internal sealed class PageScope
{
    /// <summary>沿 parent_id 最多走幾步：正常的樹不超過 <see cref="KnowledgeRules.MaxDepth"/> 層，多留一倍給資料不一致時也走得完。</summary>
    private const int WalkLimit = KnowledgeRules.MaxDepth * 2;

    /// <summary>往下補讀垃圾桶時最多走幾頁：活的加上垃圾桶各自的上限，正常的資料不會碰到。</summary>
    private const int WalkPageLimit = KnowledgeRules.MaxPagesPerSpace + KnowledgeRules.MaxTrashPagesPerSpace;

    private static readonly Expression<Func<PageRow, PageInfo>> ToInfo = p => new PageInfo(
        p.Id, p.Number, p.ParentId, p.Position, p.Title, p.IsRestricted, p.UpdatedAt, p.UpdatedBy, p.DeletedAt, p.DeletedBy, p.DeletionId, p.RestrictedBy);

    private readonly Dictionary<Guid, PageInfo> _byId;
    private readonly Dictionary<int, PageInfo> _byNumber;
    private readonly ILookup<Guid?, PageInfo> _children;
    private readonly HashSet<Guid> _listedRestrictedPages;
    private readonly Dictionary<Guid, bool> _visible = [];

    private readonly bool _walkTruncated;

    private PageScope(IReadOnlyList<PageInfo> pages, HashSet<Guid> listedRestrictedPages, bool seesAll, bool walkTruncated = false)
    {
        _walkTruncated = walkTruncated;
        _byId = pages.ToDictionary(p => p.Id);
        _byNumber = pages.ToDictionary(p => p.Number);
        _children = pages.ToLookup(p => p.ParentId);
        _listedRestrictedPages = listedRestrictedPages;
        SeesAll = seesAll;
    }

    /// <summary>
    /// 看得到空間裡所有頁面（不看名單）：組織管理員，以及專案空間裡有 project.manage 的人。
    /// 管理者在自己的專案空間要能處理受限的頁面，不然一頁被限制給別人就沒人管得了。
    /// </summary>
    public bool SeesAll { get; }

    /// <summary>空間裡還沒刪的頁面數（含看不到的）：上限算的是它。</summary>
    public int LiveCount => _byId.Values.Count(p => !p.IsDeleted);

    /// <summary>只讀還沒刪的頁面：樹、單頁、修改、新增、搬移、刪除、限制、附件都用它。刪除的頁面當作不存在。</summary>
    public static async Task<PageScope> LoadLiveAsync(KnowledgeDbContext db, Actor actor, SpaceContext space, CancellationToken cancellationToken)
    {
        var spaceId = space.Space.Id;
        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && p.DeletedAt == null)
            .Select(ToInfo)
            .ToListAsync(cancellationToken);
        var listed = await db.PageRestrictions.AsNoTracking()
            .Where(r => r.OrganizationId == actor.OrganizationId
                && r.PersonId == actor.PersonId
                && db.Pages.Any(p => p.OrganizationId == actor.OrganizationId && p.Id == r.PageId && p.SpaceId == spaceId && p.DeletedAt == null))
            .Select(r => r.PageId)
            .ToListAsync(cancellationToken);
        return new PageScope(pages, [.. listed], SeesEverything(actor, space));
    }

    private static bool SeesEverything(Actor actor, SpaceContext space) =>
        actor.IsOrganizationAdmin || (space.Project is not null && space.Permissions.CanManage);

    /// <summary>
    /// 在這一份（只有活的頁面）之上，補讀 <paramref name="root"/> 底下垃圾桶裡的頁面：一個遞迴查詢沿 <c>parent_id</c> 往下走。
    /// 只讀這一棵子樹，不把整個垃圾桶讀進來。垃圾桶裡的樹可以比活的樹深（搬移只算活的頁面的深度），所以不用層數限制，
    /// 改限制走過的頁面數（<see cref="WalkPageLimit"/>）；成環時走過的頁面不會再走。超過上限就停，並且當作底下有看不到的頁面：寧可擋下，不放行。
    /// 垃圾桶裡的頁面誰看得到、復原後繼承什麼，都是沿現在的 <c>parent_id</c> 算的，所以改上層的操作要先算它們。
    /// </summary>
    public async Task<PageScope> WithTrashBelowAsync(
        KnowledgeDbContext db, Actor actor, Guid spaceId, PageInfo root, CancellationToken cancellationToken)
    {
        var organizationId = actor.OrganizationId;
        var reached = await db.Database
            .SqlQuery<Guid>(
                $"""
                WITH RECURSIVE below(id) AS (
                    SELECT p.id FROM knowledge.pages p
                    WHERE p.organization_id = {organizationId} AND p.space_id = {spaceId} AND p.parent_id = {root.Id}
                    UNION
                    SELECT c.id FROM knowledge.pages c JOIN below ON c.parent_id = below.id
                    WHERE c.organization_id = {organizationId} AND c.space_id = {spaceId}
                )
                SELECT id AS "Value" FROM below LIMIT {WalkPageLimit + 1}
                """)
            .ToListAsync(cancellationToken);
        var truncated = reached.Count > WalkPageLimit;
        var missing = reached.Take(WalkPageLimit).Where(id => !_byId.ContainsKey(id)).ToList();
        var fetched = missing.Count == 0
            ? []
            : await db.Pages.AsNoTracking()
                .Where(p => p.OrganizationId == organizationId && p.SpaceId == spaceId && missing.Contains(p.Id))
                .Select(ToInfo)
                .ToListAsync(cancellationToken);
        var pages = _byId.Values.ToDictionary(p => p.Id);
        var added = new List<Guid>();
        foreach (var page in fetched.Where(p => pages.TryAdd(p.Id, p)))
        {
            added.Add(page.Id);
        }

        return new PageScope([.. pages.Values], await ListedAsync(db, actor, added, cancellationToken), SeesAll, _walkTruncated || truncated);
    }

    /// <summary>
    /// 在這一份（只有活的頁面）之上，補讀 <paramref name="deletionIds"/> 這幾批的頁面，以及它們往上的上層鏈裡在垃圾桶裡的頁面
    /// （沿 <c>parent_id</c> 往上；垃圾桶裡的鏈可以比活的樹深，所以只受 <see cref="WalkPageLimit"/> 限制，每一輪都有新的頁面才會繼續），再補讀呼叫者所在的名單。
    /// 垃圾桶的清單與復原只讀它們要的那幾批，不讀整個垃圾桶。
    /// </summary>
    public async Task<PageScope> WithTrashBatchesAsync(
        KnowledgeDbContext db, Actor actor, Guid spaceId, IReadOnlyCollection<Guid> deletionIds, CancellationToken cancellationToken)
    {
        var pages = _byId.Values.ToDictionary(p => p.Id);
        var added = new List<Guid>();
        var batchPages = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && p.DeletionId != null && deletionIds.Contains(p.DeletionId.Value))
            .Select(ToInfo)
            .ToListAsync(cancellationToken);
        foreach (var page in batchPages.Where(p => pages.TryAdd(p.Id, p)))
        {
            added.Add(page.Id);
        }

        var pending = MissingParents(pages, batchPages);
        for (var round = 0; round < WalkPageLimit && pending.Count > 0; round++)
        {
            var ids = pending;
            var parents = await db.Pages.AsNoTracking()
                .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && ids.Contains(p.Id))
                .Select(ToInfo)
                .ToListAsync(cancellationToken);
            var fresh = parents.Where(p => pages.TryAdd(p.Id, p)).ToList();
            added.AddRange(fresh.Select(p => p.Id));
            pending = MissingParents(pages, fresh);
        }

        return new PageScope([.. pages.Values], await ListedAsync(db, actor, added, cancellationToken), SeesAll, _walkTruncated);
    }

    private static List<Guid> MissingParents(Dictionary<Guid, PageInfo> known, IEnumerable<PageInfo> from) =>
        [.. from.Where(p => p.ParentId is { } parent && !known.ContainsKey(parent)).Select(p => p.ParentId!.Value).Distinct()];

    /// <summary>目前已經讀到的名單，再加上新讀進來的頁面裡呼叫者所在的。</summary>
    private async Task<HashSet<Guid>> ListedAsync(KnowledgeDbContext db, Actor actor, List<Guid> added, CancellationToken cancellationToken)
    {
        var listed = new HashSet<Guid>(_listedRestrictedPages);
        if (added.Count > 0)
        {
            listed.UnionWith(await db.PageRestrictions.AsNoTracking()
                .Where(r => r.OrganizationId == actor.OrganizationId && r.PersonId == actor.PersonId && added.Contains(r.PageId))
                .Select(r => r.PageId)
                .ToListAsync(cancellationToken));
        }

        return listed;
    }

    /// <summary>依編號找頁面（含垃圾桶裡的）；不存在或呼叫者看不到都是 null。</summary>
    public PageInfo? FindVisible(int number) => _byNumber.TryGetValue(number, out var page) && CanSee(page) ? page : null;

    public PageInfo? FindVisible(int? number) => number is { } value ? FindVisible(value) : null;

    public PageInfo? ById(Guid? id) => id is { } value && _byId.TryGetValue(value, out var page) ? page : null;

    public bool CanSee(PageInfo page)
    {
        if (SeesAll)
        {
            return true;
        }

        // 往上走到有答案（或最上層）為止，再由上往下填：每一頁只算一次。
        // 走到走過的頁面是成環：整條路（含通向環的）都當作看不到，不是無限地走下去。
        var path = new Stack<PageInfo>();
        var seen = new HashSet<Guid>();
        for (var current = page; current is not null && !_visible.ContainsKey(current.Id); current = ById(current.ParentId))
        {
            if (!seen.Add(current.Id) || seen.Count > WalkLimit)
            {
                foreach (var onPath in path)
                {
                    _visible[onPath.Id] = false;
                }

                return false;
            }

            path.Push(current);
        }

        while (path.TryPop(out var next))
        {
            // 找不到上層的資料不一致：寧可看不到，也不要因為缺一層就放寬。上層一定先算過，所以正常的樹不會走到預設值。
            var parentVisible = next.ParentId is not { } parentId || _visible.GetValueOrDefault(parentId, false);
            _visible[next.Id] = parentVisible && (!next.IsRestricted || _listedRestrictedPages.Contains(next.Id));
        }

        return _visible[page.Id];
    }

    /// <summary>從最上層到上一層，不含自己。</summary>
    public IReadOnlyList<PageInfo> AncestorsOf(PageInfo page) => ChainTo(page.ParentId);

    /// <summary>從最上層到這一頁（含）。<paramref name="pageId"/> 是 null 是空的。</summary>
    public IReadOnlyList<PageInfo> ChainTo(Guid? pageId)
    {
        var chain = new List<PageInfo>();
        var seen = new HashSet<Guid>();
        for (var current = ById(pageId); current is not null && chain.Count < WalkLimit && seen.Add(current.Id); current = ById(current.ParentId))
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>最上層是第 1 層。</summary>
    public int DepthOf(PageInfo page) => ChainTo(page.Id).Count;

    /// <summary>同一層還沒刪的頁面，依位置（相同時依編號）。看不到的也在裡面：重新編號要把它們一起算。</summary>
    public IReadOnlyList<PageInfo> LiveChildren(Guid? parentId) =>
        [.. _children[parentId].Where(p => !p.IsDeleted).OrderBy(p => p.Position).ThenBy(p => p.Number)];

    /// <summary>底下還沒刪的頁面（不含自己），先序。成環時走過的頁面不再走。</summary>
    public IReadOnlyList<PageInfo> LiveDescendantsOf(PageInfo page)
    {
        var result = new List<PageInfo>();
        var seen = new HashSet<Guid> { page.Id };
        var pending = new Stack<PageInfo>();
        foreach (var child in LiveChildren(page.Id).Reverse().Where(c => seen.Add(c.Id)))
        {
            pending.Push(child);
        }

        while (pending.TryPop(out var current))
        {
            result.Add(current);
            foreach (var child in LiveChildren(current.Id).Reverse().Where(c => seen.Add(c.Id)))
            {
                pending.Push(child);
            }
        }

        return result;
    }

    /// <summary>這一頁底下（還沒刪的）最深的相對層數：沒有子頁面是 0。成環時走過的頁面不再走，層數也有上限。</summary>
    public int MaxRelativeDepth(PageInfo page)
    {
        var deepest = 0;
        var seen = new HashSet<Guid> { page.Id };
        var pending = new Stack<(PageInfo Page, int Depth)>();
        pending.Push((page, 0));
        while (pending.TryPop(out var current))
        {
            deepest = Math.Max(deepest, current.Depth);
            if (current.Depth >= WalkLimit)
            {
                continue;
            }

            foreach (var child in LiveChildren(current.Page.Id).Where(c => seen.Add(c.Id)))
            {
                pending.Push((child, current.Depth + 1));
            }
        }

        return deepest;
    }

    /// <summary>底下有沒有呼叫者看不到的、還沒刪的頁面。有的話不能刪（免得刪掉自己看不到的內容）。</summary>
    public bool HasHiddenDescendants(PageInfo page) => !SeesAll && LiveDescendantsOf(page).Any(p => !CanSee(p));

    /// <summary>
    /// 底下有沒有呼叫者看不到的頁面，垃圾桶裡的也算（要用 <see cref="WithTrashBelowAsync"/> 補讀過的範圍）。
    /// 搬移與改限制用它：這兩個操作會改變整棵子樹的上層鏈，垃圾桶裡的頁面也跟著變。
    /// </summary>
    public bool HasHiddenDescendantsIncludingTrash(PageInfo page)
    {
        if (SeesAll)
        {
            return false;
        }

        // 往下補讀到上限還沒走完：看不到有沒有看不到的頁面，當作有。
        if (_walkTruncated)
        {
            return true;
        }

        var seen = new HashSet<Guid> { page.Id };
        var pending = new Stack<PageInfo>();
        pending.Push(page);
        while (pending.TryPop(out var current))
        {
            foreach (var child in _children[current.Id])
            {
                if (!CanSee(child))
                {
                    return true;
                }

                if (seen.Add(child.Id))
                {
                    pending.Push(child);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 看得到的、還沒刪的頁面，先序：上層在它的子頁面前面，同一層依位置。看得到的頁面它的上層一定看得到，所以一定接得成一棵樹。
    /// 回傳的 <see cref="PageInfo.Position"/> 是呼叫者看得到的同層頁面裡的順序（0 起連續）：資料庫的位置把看不到的頁面也編進去，
    /// 照原樣給出去會從缺口洩漏「這裡有隱藏的頁面」。
    /// </summary>
    public IReadOnlyList<PageInfo> VisibleTree()
    {
        var result = new List<PageInfo>();
        var pending = new Stack<PageInfo>();
        PushVisibleChildren(pending, null);
        while (pending.TryPop(out var current))
        {
            result.Add(current);
            PushVisibleChildren(pending, current.Id);
        }

        return result;
    }

    /// <summary>把同一層看得到的頁面（重新編成 0 起連續的位置）倒著推進堆疊，先序才會由前往後取。</summary>
    private void PushVisibleChildren(Stack<PageInfo> pending, Guid? parentId)
    {
        var visible = LiveChildren(parentId).Where(CanSee).Select((child, index) => child with { Position = index }).ToList();
        for (var index = visible.Count - 1; index >= 0; index--)
        {
            pending.Push(visible[index]);
        }
    }

    /// <summary>這一批的所有頁面（同一個 <see cref="PageInfo.DeletionId"/>）。</summary>
    public IReadOnlyList<PageInfo> BatchOf(Guid deletionId) => [.. _byId.Values.Where(p => p.DeletionId == deletionId)];

    /// <summary>一批裡最上面的一頁：它的上層不在這一批裡。資料成環、整批都有上層時退而取編號最小的。</summary>
    public static PageInfo? TopOfBatch(IEnumerable<PageInfo> batch)
    {
        var pages = batch.ToList();
        var ids = pages.Select(p => p.Id).ToHashSet();
        return pages.Where(p => p.ParentId is not { } parent || !ids.Contains(parent)).OrderBy(p => p.Number).FirstOrDefault()
            ?? pages.OrderBy(p => p.Number).FirstOrDefault();
    }
}
