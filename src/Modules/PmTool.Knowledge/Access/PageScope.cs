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
    Guid? DeletionId)
{
    public bool IsDeleted => DeletedAt is not null;

    public PageReference ToReference() => new(Id, Number, Title);

    public PageNode ToNode() => new(Id, Number, ParentId, Position, Title, IsRestricted, UpdatedAt, UpdatedBy);
}

/// <summary>
/// 呼叫者在一個空間裡看得到哪些頁面。規則只有這一份：看得到頁面 = 組織管理員，或者這一頁與它每一層上層裡
/// 自己設了限制的，每一份名單都有呼叫者（限制往下繼承，只能更嚴）。垃圾桶裡的頁面照原本的位置算。
/// 一次讀出空間裡頁面的骨架與呼叫者所在的名單（最多 <see cref="KnowledgeRules.MaxPagesPerSpace"/> 頁），
/// 之後都在記憶體裡算，不是一頁一個查詢。呼叫者要先確認看得到這個空間。
/// 平常的路只讀沒刪的頁面（<see cref="LoadLiveAsync"/>）：垃圾桶沒有上限，不能讓每個請求都跟著變慢；
/// 活的頁面的上層一定是活的（刪除連底下一起刪、復原時上層不在就放最上層），所以不讀垃圾桶也算得出可見性。
/// 只有垃圾桶的清單與復原才連刪除的一起讀（<see cref="LoadWithTrashAsync"/>）。
/// </summary>
internal sealed class PageScope
{
    private readonly Dictionary<Guid, PageInfo> _byId;
    private readonly Dictionary<int, PageInfo> _byNumber;
    private readonly ILookup<Guid?, PageInfo> _children;
    private readonly HashSet<Guid> _listedRestrictedPages;
    private readonly Dictionary<Guid, bool> _visible = [];
    private readonly bool _isAdmin;

    private PageScope(IReadOnlyList<PageInfo> pages, HashSet<Guid> listedRestrictedPages, bool isAdmin)
    {
        _byId = pages.ToDictionary(p => p.Id);
        _byNumber = pages.ToDictionary(p => p.Number);
        _children = pages.ToLookup(p => p.ParentId);
        _listedRestrictedPages = listedRestrictedPages;
        _isAdmin = isAdmin;
    }

    /// <summary>空間裡還沒刪的頁面數（含看不到的）：上限算的是它。</summary>
    public int LiveCount => _byId.Values.Count(p => !p.IsDeleted);

    /// <summary>只讀還沒刪的頁面：樹、單頁、修改、新增、搬移、刪除、限制、附件都用它。刪除的頁面當作不存在。</summary>
    public static Task<PageScope> LoadLiveAsync(KnowledgeDbContext db, Actor actor, Guid spaceId, CancellationToken cancellationToken) =>
        LoadAsync(db, actor, spaceId, includeTrash: false, cancellationToken);

    /// <summary>連垃圾桶裡的頁面一起讀：只有垃圾桶的清單與復原需要。</summary>
    public static Task<PageScope> LoadWithTrashAsync(KnowledgeDbContext db, Actor actor, Guid spaceId, CancellationToken cancellationToken) =>
        LoadAsync(db, actor, spaceId, includeTrash: true, cancellationToken);

    private static async Task<PageScope> LoadAsync(
        KnowledgeDbContext db, Actor actor, Guid spaceId, bool includeTrash, CancellationToken cancellationToken)
    {
        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.SpaceId == spaceId && (includeTrash || p.DeletedAt == null))
            .Select(p => new PageInfo(
                p.Id, p.Number, p.ParentId, p.Position, p.Title, p.IsRestricted, p.UpdatedAt, p.UpdatedBy, p.DeletedAt, p.DeletedBy, p.DeletionId))
            .ToListAsync(cancellationToken);
        var listed = await db.PageRestrictions.AsNoTracking()
            .Where(r => r.OrganizationId == actor.OrganizationId
                && r.PersonId == actor.PersonId
                && db.Pages.Any(p => p.Id == r.PageId && p.SpaceId == spaceId && (includeTrash || p.DeletedAt == null)))
            .Select(r => r.PageId)
            .ToListAsync(cancellationToken);
        return new PageScope(pages, [.. listed], actor.IsOrganizationAdmin);
    }

    /// <summary>依編號找頁面（含垃圾桶裡的）；不存在或呼叫者看不到都是 null。</summary>
    public PageInfo? FindVisible(int number) => _byNumber.TryGetValue(number, out var page) && CanSee(page) ? page : null;

    public PageInfo? FindVisible(int? number) => number is { } value ? FindVisible(value) : null;

    public PageInfo? ById(Guid? id) => id is { } value && _byId.TryGetValue(value, out var page) ? page : null;

    public bool CanSee(PageInfo page)
    {
        if (_isAdmin)
        {
            return true;
        }

        // 往上走到有答案（或最上層）為止，再由上往下填：每一頁只算一次。
        var path = new Stack<PageInfo>();
        for (var current = page; current is not null && !_visible.ContainsKey(current.Id); current = ById(current.ParentId))
        {
            path.Push(current);
        }

        while (path.TryPop(out var next))
        {
            var parentVisible = next.ParentId is not { } parentId || _visible.GetValueOrDefault(parentId, true);
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
        for (var current = ById(pageId); current is not null; current = ById(current.ParentId))
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

    /// <summary>底下還沒刪的頁面（不含自己），先序。</summary>
    public IReadOnlyList<PageInfo> LiveDescendantsOf(PageInfo page)
    {
        var result = new List<PageInfo>();
        var pending = new Stack<PageInfo>();
        pending.Push(page);
        while (pending.TryPop(out var current))
        {
            foreach (var child in LiveChildren(current.Id).Reverse())
            {
                pending.Push(child);
            }

            if (current != page)
            {
                result.Add(current);
            }
        }

        return result;
    }

    /// <summary>這一頁底下（還沒刪的）最深的相對層數：沒有子頁面是 0。</summary>
    public int MaxRelativeDepth(PageInfo page)
    {
        var deepest = 0;
        var pending = new Stack<(PageInfo Page, int Depth)>();
        pending.Push((page, 0));
        while (pending.TryPop(out var current))
        {
            deepest = Math.Max(deepest, current.Depth);
            foreach (var child in LiveChildren(current.Page.Id))
            {
                pending.Push((child, current.Depth + 1));
            }
        }

        return deepest;
    }

    /// <summary>底下有沒有呼叫者看不到的、還沒刪的頁面。有的話不能刪（免得刪掉自己看不到的內容）。</summary>
    public bool HasHiddenDescendants(PageInfo page) => !_isAdmin && LiveDescendantsOf(page).Any(p => !CanSee(p));

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

    /// <summary>
    /// 垃圾桶裡的每一批：所在的批次是同一個 <see cref="PageInfo.DeletionId"/>。只列看得到最上面那一頁的；
    /// 頁數只算呼叫者看得到的（底下有別人才看得到的頁面時，不能從總數洩漏）。
    /// </summary>
    public IReadOnlyList<(PageInfo Top, int PageCount)> VisibleTrash()
    {
        var batches = _byId.Values.Where(p => p.DeletionId is not null).GroupBy(p => p.DeletionId!.Value);
        var entries = new List<(PageInfo Top, int PageCount)>();
        foreach (var batch in batches)
        {
            var top = TopOfBatch(batch);
            if (top is not null && CanSee(top))
            {
                entries.Add((top, batch.Count(CanSee)));
            }
        }

        return [.. entries.OrderByDescending(e => e.Top.DeletedAt).ThenByDescending(e => e.Top.Number)];
    }

    /// <summary>這一批的所有頁面（同一個 <see cref="PageInfo.DeletionId"/>）。</summary>
    public IReadOnlyList<PageInfo> BatchOf(Guid deletionId) => [.. _byId.Values.Where(p => p.DeletionId == deletionId)];

    /// <summary>一批裡最上面的一頁：它的上層不在這一批裡。</summary>
    public static PageInfo? TopOfBatch(IEnumerable<PageInfo> batch)
    {
        var pages = batch.ToList();
        var ids = pages.Select(p => p.Id).ToHashSet();
        return pages.Where(p => p.ParentId is not { } parent || !ids.Contains(parent)).OrderBy(p => p.Number).FirstOrDefault();
    }
}
