using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Knowledge;

namespace PmTool.Api.Tests.Support;

/// <summary>知識庫的假資料：全是固定的值，形狀測試才能整份比對。</summary>
internal static class KnowledgeSamples
{
    public static readonly Guid SpaceId = Guid.Parse("0190a000-0000-7000-8000-0000000000c1");
    public static readonly Guid PageId = Guid.Parse("0190a000-0000-7000-8000-0000000000c2");
    public static readonly Guid ParentId = Guid.Parse("0190a000-0000-7000-8000-0000000000c3");
    public static readonly Guid AttachmentId = Guid.Parse("0190a000-0000-7000-8000-0000000000c4");
    public static readonly DateTimeOffset At = new(2026, 11, 20, 3, 0, 0, TimeSpan.Zero);

    public static SpaceSummary Space(string key = "HR", Guid? createdBy = null, bool archived = false) =>
        new(
            SpaceId,
            key,
            "人資",
            "人資的文件",
            null,
            At,
            createdBy ?? TestUsers.Owner.PersonId,
            archived ? At : null,
            new SpacePermissions(!archived, !archived, CanManage: true));

    public static SpaceSummary ProjectSpace() =>
        Space("WEB") with { Project = new SpaceProject(Guid.Parse("0190a000-0000-7000-8000-0000000000d1"), "WEB", "Example 網站", false) };

    public static PageReference Ref(int number = 1, string title = "首頁") =>
        new(number == 1 ? ParentId : PageId, number, title);

    public static PageDetail Page(int number = 2, string body = "內文", long revision = 3) =>
        new(
            PageId,
            Space(),
            number,
            "新進人員",
            body,
            revision,
            [Ref()],
            IsRestricted: true,
            RestrictedAncestors: [Ref()],
            At,
            TestUsers.Owner.PersonId,
            At.AddHours(1),
            TestUsers.Member.PersonId,
            VersionCount: 4,
            new PagePermissions(CanEdit: true, CanComment: true, CanDelete: false));

    public static PageTree Tree() =>
        new(
            Space(),
            [
                new PageNode(ParentId, 1, null, 0, "首頁", false, At, TestUsers.Owner.PersonId),
                new PageNode(PageId, 2, ParentId, 0, "新進人員", true, At.AddHours(1), TestUsers.Member.PersonId),
            ]);

    public static PageAttachment Attachment(Guid? id = null, string fileName = "規格.pdf", string contentType = "application/pdf", long size = 11) =>
        new(
            id ?? AttachmentId,
            PageId,
            fileName,
            contentType,
            size,
            "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            TestUsers.Member.PersonId,
            At);

    public static Failure NotFound(string code) => new(FailureKind.NotFound, code, "找不到。");
}

internal sealed class FakeKnowledgeSpaceService : IKnowledgeSpaceService
{
    private readonly ConcurrentQueue<(Actor Actor, NewSpace Space)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string Key, SpaceSettings Settings)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string Key, bool Archived)> _archiveCalls = new();

    public Func<Actor, IReadOnlyList<SpaceSummary>> OnList { get; set; } = _ => [KnowledgeSamples.Space()];

    public Func<Actor, string, Result<SpaceSummary>> OnGet { get; set; } = (_, key) => KnowledgeSamples.Space(key);

    public Func<Actor, NewSpace, Result<SpaceSummary>> OnCreate { get; set; } = (_, space) => KnowledgeSamples.Space(space.Key);

    public Func<Actor, string, SpaceSettings, Result<SpaceSummary>> OnUpdate { get; set; } =
        (_, key, settings) => KnowledgeSamples.Space(key) with { Name = settings.Name, Description = settings.Description };

    public Func<Actor, string, bool, Result<SpaceSummary>> OnArchive { get; set; } =
        (_, key, archived) => KnowledgeSamples.Space(key, archived: archived);

    public IReadOnlyList<(Actor Actor, NewSpace Space)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string Key, SpaceSettings Settings)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string Key, bool Archived)> ArchiveCalls => _archiveCalls.ToArray();

    public Task<IReadOnlyList<SpaceSummary>> ListAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult(OnList(actor));

    public Task<Result<SpaceSummary>> GetAsync(Actor actor, string spaceKey, CancellationToken cancellationToken) =>
        Task.FromResult(OnGet(actor, spaceKey));

    public Task<Result<SpaceSummary>> CreateAsync(Actor actor, NewSpace space, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, space));
        return Task.FromResult(OnCreate(actor, space));
    }

    public Task<Result<SpaceSummary>> UpdateAsync(Actor actor, string spaceKey, SpaceSettings settings, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, spaceKey, settings));
        return Task.FromResult(OnUpdate(actor, spaceKey, settings));
    }

    public Task<Result<SpaceSummary>> SetArchivedAsync(Actor actor, string spaceKey, bool archived, CancellationToken cancellationToken)
    {
        _archiveCalls.Enqueue((actor, spaceKey, archived));
        return Task.FromResult(OnArchive(actor, spaceKey, archived));
    }
}

internal sealed class FakeKnowledgePageService : IKnowledgePageService
{
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, NewPage Page)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, int Number, PageChanges Changes)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, int Number, PageMove Move)> _moveCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, int Number, IReadOnlyList<Guid>? People)> _restrictionCalls = new();
    private readonly ConcurrentQueue<(string Operation, string SpaceKey, int Number)> _calls = new();
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, TrashQuery Query)> _trashQueries = new();
    private readonly ConcurrentQueue<(Actor Actor, string SpaceKey, TrashPurgeRequest Request)> _purgeCalls = new();

    public Func<Result<PageTree>> OnTree { get; set; } = () => KnowledgeSamples.Tree();

    public Func<int, Result<PageDetail>> OnGet { get; set; } = number => KnowledgeSamples.Page(number);

    public Func<NewPage, Result<PageDetail>> OnCreate { get; set; } = _ => KnowledgeSamples.Page(2, revision: 1);

    public Func<PageChanges, Result<PageDetail>> OnUpdate { get; set; } =
        changes => KnowledgeSamples.Page(2, changes.Body ?? "內文", changes.ExpectedRevision + 1);

    public Func<Result<PageTree>> OnMove { get; set; } = () => KnowledgeSamples.Tree();

    public Func<int, Result<PageDeletion>> OnDelete { get; set; } =
        number => new PageDeletion(Guid.Parse("0190a000-0000-7000-8000-0000000000e1"), KnowledgeSamples.Ref(number), 3);

    public Func<int, Result<PageDetail>> OnRestore { get; set; } = number => KnowledgeSamples.Page(number);

    public Func<IReadOnlyList<TrashEntry>> OnTrash { get; set; } = () =>
    [
        new TrashEntry(
            Guid.Parse("0190a000-0000-7000-8000-0000000000e1"),
            KnowledgeSamples.Ref(2, "舊頁"),
            3,
            KnowledgeSamples.Ref(),
            KnowledgeSamples.At,
            TestUsers.Member.PersonId),
    ];

    /// <summary>垃圾桶回應的游標與容量（清單內容由 <see cref="OnTrash"/> 給）。</summary>
    public string? TrashNextCursor { get; set; }

    public TrashCapacity? TrashCapacity { get; set; }

    public Func<TrashPurgeRequest, Result<TrashPurge>> OnPurge { get; set; } = request =>
        new TrashPurge(request.DeletionId is null ? 2 : 1, 5, 3, 0, HasMoreExpired: request.DeletionId is null);

    public Func<Result<PageRestriction>> OnGetRestriction { get; set; } = () =>
        new PageRestriction([TestUsers.Owner.PersonId, TestUsers.Member.PersonId], [KnowledgeSamples.Ref()]);

    public Func<IReadOnlyList<Guid>?, Result<PageRestriction>> OnSetRestriction { get; set; } =
        people => new PageRestriction(people, []);

    public IReadOnlyList<(Actor Actor, string SpaceKey, NewPage Page)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string SpaceKey, int Number, PageChanges Changes)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string SpaceKey, int Number, PageMove Move)> MoveCalls => _moveCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string SpaceKey, TrashQuery Query)> TrashQueries => _trashQueries.ToArray();

    public IReadOnlyList<(Actor Actor, string SpaceKey, TrashPurgeRequest Request)> PurgeCalls => _purgeCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string SpaceKey, int Number, IReadOnlyList<Guid>? People)> RestrictionCalls => _restrictionCalls.ToArray();

    /// <summary>讀、刪除、復原這類只有編號的呼叫：（操作、空間、編號）。</summary>
    public IReadOnlyList<(string Operation, string SpaceKey, int Number)> Calls => _calls.ToArray();

    public Task<Result<PageTree>> GetTreeAsync(Actor actor, string spaceKey, CancellationToken cancellationToken)
    {
        _calls.Enqueue(("tree", spaceKey, 0));
        return Task.FromResult(OnTree());
    }

    public Task<Result<PageDetail>> GetAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        _calls.Enqueue(("get", spaceKey, pageNumber));
        return Task.FromResult(OnGet(pageNumber));
    }

    public Task<Result<PageDetail>> CreateAsync(Actor actor, string spaceKey, NewPage page, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, spaceKey, page));
        return Task.FromResult(OnCreate(page));
    }

    public Task<Result<PageDetail>> UpdateAsync(
        Actor actor, string spaceKey, int pageNumber, PageChanges changes, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, spaceKey, pageNumber, changes));
        return Task.FromResult(OnUpdate(changes));
    }

    public Task<Result<PageTree>> MoveAsync(Actor actor, string spaceKey, int pageNumber, PageMove move, CancellationToken cancellationToken)
    {
        _moveCalls.Enqueue((actor, spaceKey, pageNumber, move));
        return Task.FromResult(OnMove());
    }

    public Task<Result<PageDeletion>> DeleteAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        _calls.Enqueue(("delete", spaceKey, pageNumber));
        return Task.FromResult(OnDelete(pageNumber));
    }

    public Task<Result<PageDetail>> RestoreAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        _calls.Enqueue(("restore", spaceKey, pageNumber));
        return Task.FromResult(OnRestore(pageNumber));
    }

    public Task<Result<TrashPage>> ListTrashAsync(Actor actor, string spaceKey, TrashQuery query, CancellationToken cancellationToken)
    {
        _trashQueries.Enqueue((actor, spaceKey, query));
        return Task.FromResult<Result<TrashPage>>(new TrashPage(OnTrash().ToList(), TrashNextCursor, TrashCapacity));
    }

    public Task<Result<TrashPurge>> PurgeTrashAsync(Actor actor, string spaceKey, TrashPurgeRequest request, CancellationToken cancellationToken)
    {
        _purgeCalls.Enqueue((actor, spaceKey, request));
        return Task.FromResult(OnPurge(request));
    }

    public Task<Result<PageRestriction>> GetRestrictionAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken)
    {
        _calls.Enqueue(("get-restriction", spaceKey, pageNumber));
        return Task.FromResult(OnGetRestriction());
    }

    public Task<Result<PageRestriction>> SetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, IReadOnlyList<Guid>? people, CancellationToken cancellationToken)
    {
        _restrictionCalls.Enqueue((actor, spaceKey, pageNumber, people));
        return Task.FromResult(OnSetRestriction(people));
    }
}

/// <summary>一次上傳呼叫：伺服器交給附件服務的檔名、類型與讀到的內容。</summary>
internal sealed record KnowledgeUploadCall(Actor Actor, string SpaceKey, int Number, string FileName, string? ContentType, byte[] Content);

internal sealed class FakeKnowledgeAttachmentService : IKnowledgeAttachmentService
{
    private readonly ConcurrentQueue<KnowledgeUploadCall> _addCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _openCalls = new();

    public Func<KnowledgeUploadCall, Result<PageAttachment>> OnAdd { get; set; } =
        call => KnowledgeSamples.Attachment(fileName: call.FileName, contentType: call.ContentType ?? "application/octet-stream", size: call.Content.Length);

    public Func<Result<IReadOnlyList<PageAttachment>>> OnList { get; set; } =
        () => new List<PageAttachment> { KnowledgeSamples.Attachment() };

    public Func<Guid, Result<(PageAttachment Attachment, byte[] Content)>> OnOpen { get; set; } =
        id => (KnowledgeSamples.Attachment(id), "hello world"u8.ToArray());

    public IReadOnlyList<KnowledgeUploadCall> AddCalls => _addCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> OpenCalls => _openCalls.ToArray();

    public async Task<Result<PageAttachment>> AddAsync(
        Actor actor, string spaceKey, int pageNumber, NewPageAttachment upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        var call = new KnowledgeUploadCall(actor, spaceKey, pageNumber, upload.FileName, upload.ContentType, buffer.ToArray());
        _addCalls.Enqueue(call);
        return OnAdd(call);
    }

    public Task<Result<IReadOnlyList<PageAttachment>>> ListAsync(
        Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        Task.FromResult(OnList());

    public Task<Result<PageAttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        _openCalls.Enqueue((actor, attachmentId));
        var opened = OnOpen(attachmentId);
        return Task.FromResult<Result<PageAttachmentContent>>(
            opened.Failure is { } failure ? failure : new PageAttachmentContent(opened.Value.Attachment, new MemoryStream(opened.Value.Content)));
    }
}
