using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Knowledge;

namespace PmTool.Ai.Actions.Knowledge;

/// <summary>一個空間的頁面樹索引：用編號找、找子頁面、算深度與子樹的高度（檢查搬移與新增會不會太深、搬進自己底下）。</summary>
internal sealed class TreeIndex
{
    private readonly Dictionary<int, PageNode> _byNumber;
    private readonly Dictionary<Guid, PageNode> _byId;
    private readonly ILookup<Guid, PageNode> _children;

    public TreeIndex(PageTree tree)
    {
        _byNumber = tree.Pages.ToDictionary(page => page.Number);
        _byId = tree.Pages.ToDictionary(page => page.Id);
        _children = tree.Pages.Where(page => page.ParentId is not null).ToLookup(page => page.ParentId!.Value);
    }

    public PageNode? Find(int number) => _byNumber.GetValueOrDefault(number);

    public PageNode? ParentOf(PageNode page) => page.ParentId is { } id ? _byId.GetValueOrDefault(id) : null;

    /// <summary>某個上層（null 是最上層）底下看得到的子頁面，依順序。</summary>
    public IReadOnlyList<PageNode> ChildrenOf(PageNode? parent) =>
        parent is null
            ? [.. _byNumber.Values.Where(page => page.ParentId is null).OrderBy(page => page.Position)]
            : [.. _children[parent.Id].OrderBy(page => page.Position)];

    /// <summary>從最上層算的層數（最上層是 1）；資料成環時算到上限就停。</summary>
    public int DepthOf(PageNode page)
    {
        var depth = 1;
        var visited = new HashSet<Guid> { page.Id };
        for (var current = ParentOf(page); current is not null && visited.Add(current.Id); current = ParentOf(current))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>這一頁底下（不含自己）有幾頁、最深往下幾層。</summary>
    public (int Count, int Height) SubtreeOf(PageNode page)
    {
        var count = 0;
        var height = 0;
        var queue = new Queue<(PageNode Node, int Level)>();
        queue.Enqueue((page, 0));
        var visited = new HashSet<Guid> { page.Id };
        while (queue.Count > 0)
        {
            var (node, level) = queue.Dequeue();
            height = Math.Max(height, level);
            foreach (var child in _children[node.Id].Where(child => visited.Add(child.Id)))
            {
                count++;
                queue.Enqueue((child, level + 1));
            }
        }

        return (count, height);
    }

    /// <summary><paramref name="page"/> 是不是 <paramref name="ancestor"/> 自己或它底下的頁面。</summary>
    public bool IsSameOrDescendant(PageNode page, PageNode ancestor)
    {
        var visited = new HashSet<Guid>();
        for (PageNode? current = page; current is not null && visited.Add(current.Id); current = ParentOf(current))
        {
            if (current.Id == ancestor.Id)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>建立建議時查現況用的快取：同一份建議裡同一個空間、頁面只查一次。找不到一律是 null，由呼叫的人寫成給模型看的問題。</summary>
internal sealed class KnowledgeLookup(IKnowledgeSpaceService spaces, IKnowledgePageService pages, IPeopleDirectory people, Actor actor)
{
    private const int MaxTrashPagesRead = 10;

    private readonly Dictionary<string, SpaceSummary?> _spaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TreeIndex?> _trees = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, int), PageDetail?> _pageDetails = [];
    private IReadOnlyList<SpaceSummary>? _allSpaces;

    public async Task<SpaceSummary?> SpaceAsync(string key, CancellationToken cancellationToken)
    {
        if (!_spaces.TryGetValue(key, out var space))
        {
            var found = await spaces.GetAsync(actor, key, cancellationToken);
            space = found.Failure is null ? found.Value : null;
            _spaces[key] = space;
        }

        return space;
    }

    public async Task<IReadOnlyList<SpaceSummary>> AllSpacesAsync(CancellationToken cancellationToken) =>
        _allSpaces ??= await spaces.ListAsync(actor, cancellationToken);

    public async Task<TreeIndex?> TreeAsync(string spaceKey, CancellationToken cancellationToken)
    {
        if (!_trees.TryGetValue(spaceKey, out var tree))
        {
            var found = await pages.GetTreeAsync(actor, spaceKey, cancellationToken);
            tree = found.Failure is null ? new TreeIndex(found.Value) : null;
            _trees[spaceKey] = tree;
        }

        return tree;
    }

    public async Task<PageDetail?> PageAsync(string spaceKey, int number, CancellationToken cancellationToken)
    {
        var key = (spaceKey.ToUpperInvariant(), number);
        if (!_pageDetails.TryGetValue(key, out var page))
        {
            var found = await pages.GetAsync(actor, spaceKey, number, cancellationToken);
            page = found.Failure is null ? found.Value : null;
            _pageDetails[key] = page;
        }

        return page;
    }

    /// <summary>垃圾桶裡最上面一頁是這個編號的那一批；只讀前幾頁，找不到就當作不在。</summary>
    public async Task<TrashEntry?> TrashEntryAsync(string spaceKey, int number, CancellationToken cancellationToken)
    {
        string? cursor = null;
        for (var read = 0; read < MaxTrashPagesRead; read++)
        {
            var result = await pages.ListTrashAsync(actor, spaceKey, new TrashQuery(cursor, KnowledgeRules.MaxTrashPageSize), cancellationToken);
            if (result.Failure is not null)
            {
                return null;
            }

            if (result.Value.Items.FirstOrDefault(entry => entry.Page.Number == number) is { } found)
            {
                return found;
            }

            cursor = result.Value.NextCursor;
            if (cursor is null)
            {
                return null;
            }
        }

        return null;
    }

    public async Task<PageRestriction?> RestrictionAsync(string spaceKey, int number, CancellationToken cancellationToken)
    {
        var result = await pages.GetRestrictionAsync(actor, spaceKey, number, cancellationToken);
        return result.Failure is null ? result.Value : null;
    }

    /// <summary>編號對應到組織裡的人；不在組織裡的不會出現在結果。</summary>
    public async Task<IReadOnlyDictionary<Guid, PersonSummary>> PeopleAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0 ? new Dictionary<Guid, PersonSummary>() : await people.FindManyAsync(actor.OrganizationId, ids, cancellationToken);
}
