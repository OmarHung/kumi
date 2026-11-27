using PmTool.Abstractions;
using PmTool.Knowledge;

namespace PmTool.Ai.Tests.Actions.Knowledge;

/// <summary>知識庫的假資料：空間 HR（組織空間）裡有三頁，1 首頁 → 2 新進人員 → 3 手冊。</summary>
internal static class KnowledgeData
{
    public static readonly Guid Page1 = Guid.Parse("0190a000-0000-7000-8000-000000000001");
    public static readonly Guid Page2 = Guid.Parse("0190a000-0000-7000-8000-000000000002");
    public static readonly Guid Page3 = Guid.Parse("0190a000-0000-7000-8000-000000000003");
    public static readonly Guid Page4 = Guid.Parse("0190a000-0000-7000-8000-000000000004");
    public static readonly Guid Person = Guid.Parse("0190a000-0000-7000-8000-0000000000f1");
    public static readonly DateTimeOffset At = new(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);

    public static SpaceSummary Space(
        string key = "HR", bool canEdit = true, bool canManage = false, bool archived = false, string? description = "人資文件", string? projectKey = null) =>
        new(
            Guid.Parse("0190a000-0000-7000-8000-0000000000c1"),
            key,
            "人資",
            description,
            projectKey is null ? null : new SpaceProject(Guid.NewGuid(), projectKey, "專案", false),
            At,
            Guid.NewGuid(),
            archived ? At : null,
            new SpacePermissions(canEdit && !archived, canEdit && !archived, canManage));

    /// <summary>1 首頁（最上層）→ 2 新進人員 → 3 手冊；4 FAQ 是最上層的第二頁。</summary>
    public static PageTree Tree(SpaceSummary? space = null) =>
        new(
            space ?? Space(),
            [
                new PageNode(Page1, 1, null, 0, "首頁", false, At, Guid.NewGuid()),
                new PageNode(Page2, 2, Page1, 0, "新進人員", false, At, Guid.NewGuid()),
                new PageNode(Page3, 3, Page2, 0, "手冊", false, At, Guid.NewGuid()),
                new PageNode(Page4, 4, null, 1, "FAQ", false, At, Guid.NewGuid()),
            ]);

    public static PageDetail Page(int number = 2, string body = "舊內文", long revision = 3, bool canDelete = true, SpaceSummary? space = null) =>
        new(
            Page2,
            space ?? Space(),
            number,
            "新進人員",
            body,
            revision,
            [new PageReference(Page1, 1, "首頁")],
            IsRestricted: false,
            [],
            At,
            Guid.NewGuid(),
            At,
            Guid.NewGuid(),
            VersionCount: 1,
            new PagePermissions(CanEdit: true, CanComment: true, CanDelete: canDelete));

    public static Failure NotFound(string code = KnowledgeErrorCodes.PageNotFound) => new(FailureKind.NotFound, code, "找不到。");
}

internal sealed class FakeKnowledgeSpaces : IKnowledgeSpaceService
{
    public Dictionary<string, SpaceSummary> Spaces { get; } = new(StringComparer.OrdinalIgnoreCase) { ["HR"] = KnowledgeData.Space() };

    public Func<NewSpace, Result<SpaceSummary>>? OnCreate { get; set; }

    public List<NewSpace> Created { get; } = [];

    public List<(string Key, SpaceSettings Settings)> Updated { get; } = [];

    public List<(string Key, bool Archived)> Archived { get; } = [];

    public Task<IReadOnlyList<SpaceSummary>> ListAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SpaceSummary>>([.. Spaces.Values]);

    public Task<Result<SpaceSummary>> GetAsync(Actor actor, string spaceKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<SpaceSummary>>(
            Spaces.TryGetValue(spaceKey, out var space) ? space : KnowledgeData.NotFound(KnowledgeErrorCodes.SpaceNotFound));

    public Task<Result<SpaceSummary>> CreateAsync(Actor actor, NewSpace space, CancellationToken cancellationToken)
    {
        Created.Add(space);
        return Task.FromResult(OnCreate?.Invoke(space) ?? KnowledgeData.Space(space.Key));
    }

    public Task<Result<SpaceSummary>> UpdateAsync(Actor actor, string spaceKey, SpaceSettings settings, CancellationToken cancellationToken)
    {
        Updated.Add((spaceKey, settings));
        return Task.FromResult<Result<SpaceSummary>>(KnowledgeData.Space(spaceKey));
    }

    public Task<Result<SpaceSummary>> SetArchivedAsync(Actor actor, string spaceKey, bool archived, CancellationToken cancellationToken)
    {
        Archived.Add((spaceKey, archived));
        return Task.FromResult<Result<SpaceSummary>>(KnowledgeData.Space(spaceKey, archived: archived));
    }
}

internal sealed class FakeKnowledgePages : IKnowledgePageService
{
    private int _nextNumber = 40;

    public PageTree Tree { get; set; } = KnowledgeData.Tree();

    public Func<int, Result<PageDetail>> OnGet { get; set; } = number => KnowledgeData.Page(number);

    public Func<NewPage, Result<PageDetail>>? OnCreate { get; set; }

    public Func<int, Result<PageDeletion>>? OnDelete { get; set; }

    public Func<IReadOnlyList<TrashEntry>> OnTrash { get; set; } = () => [];

    public Func<PageRestriction> OnRestriction { get; set; } = () => new PageRestriction(null, []) { CanRelax = true };

    public List<NewPage> Created { get; } = [];

    public List<(int Number, PageChanges Changes)> Updated { get; } = [];

    public List<(int Number, PageMove Move)> Moved { get; } = [];

    public List<int> Deleted { get; } = [];

    public List<int> Restored { get; } = [];

    public List<(int Number, IReadOnlyList<Guid>? People)> Restrictions { get; } = [];

    public List<TrashQuery> TrashQueries { get; } = [];

    public Task<Result<PageTree>> GetTreeAsync(Actor actor, string spaceKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PageTree>>(Tree);

    public Task<Result<PageDetail>> GetAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        Task.FromResult(OnGet(pageNumber));

    public Task<Result<PageDetail>> CreateAsync(Actor actor, string spaceKey, NewPage page, CancellationToken cancellationToken)
    {
        Created.Add(page);
        return Task.FromResult(OnCreate?.Invoke(page) ?? KnowledgeData.Page(_nextNumber++));
    }

    public Task<Result<PageDetail>> UpdateAsync(Actor actor, string spaceKey, int pageNumber, PageChanges changes, CancellationToken cancellationToken)
    {
        Updated.Add((pageNumber, changes));
        return Task.FromResult(OnGet(pageNumber));
    }

    public Task<Result<PageTree>> MoveAsync(Actor actor, string spaceKey, int pageNumber, PageMove move, CancellationToken cancellationToken)
    {
        Moved.Add((pageNumber, move));
        return Task.FromResult<Result<PageTree>>(Tree);
    }

    public Task<Result<PageDeletion>> DeleteAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        Deleted.Add(pageNumber);
        return Task.FromResult<Result<PageDeletion>>(OnDelete?.Invoke(pageNumber) ?? new PageDeletion(Guid.NewGuid(), new PageReference(Guid.NewGuid(), pageNumber, "頁"), 1));
    }

    public Task<Result<PageDetail>> RestoreAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        Restored.Add(pageNumber);
        return Task.FromResult(OnGet(pageNumber));
    }

    public Task<Result<TrashPage>> ListTrashAsync(Actor actor, string spaceKey, TrashQuery query, CancellationToken cancellationToken)
    {
        TrashQueries.Add(query);
        return Task.FromResult<Result<TrashPage>>(new TrashPage([.. OnTrash()], null, null));
    }

    public Task<Result<TrashPurge>> PurgeTrashAsync(Actor actor, string spaceKey, TrashPurgeRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageRestriction>> GetRestrictionAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PageRestriction>>(OnRestriction());

    public Task<Result<PageRestriction>> SetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, IReadOnlyList<Guid>? people, CancellationToken cancellationToken)
    {
        Restrictions.Add((pageNumber, people));
        return Task.FromResult<Result<PageRestriction>>(new PageRestriction(people, []));
    }
}
