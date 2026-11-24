using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的專案服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeProjectService : IProjectService
{
    private readonly ConcurrentQueue<Actor> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, NewProject Project)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _getCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid PersonId, string Role)> _setMemberCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid PersonId)> _removeMemberCalls = new();

    public IReadOnlyList<TemplateSummary> Templates { get; set; } = [];

    public Func<Actor, Task<IReadOnlyList<ProjectSummary>>> OnList { get; set; } =
        _ => Task.FromResult<IReadOnlyList<ProjectSummary>>([]);

    public Func<Actor, NewProject, Result<ProjectSummary>> OnCreate { get; set; } =
        (_, project) => TestData.Project(project.Key);

    /// <summary>預設像專案的管理員：看得到專案，也能管理成員。</summary>
    public Func<Actor, string, Result<ProjectDetail>> OnGet { get; set; } =
        (_, key) => new ProjectDetail(
            TestData.Project(key), TestData.Blueprint(), [], [WorkPermissions.ItemRead, WorkPermissions.ProjectManage]);

    public Func<Actor, string, Guid, string, Result<ProjectMember>> OnSetMember { get; set; } =
        (_, _, personId, role) => new ProjectMember(personId, role);

    /// <summary>預設像移除一位編輯者。</summary>
    public Func<Actor, string, Guid, Result<ProjectMember>> OnRemoveMember { get; set; } =
        (_, _, personId) => new ProjectMember(personId, "editor");

    public IReadOnlyList<Actor> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, NewProject Project)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid PersonId, string Role)> SetMemberCalls =>
        _setMemberCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid PersonId)> RemoveMemberCalls => _removeMemberCalls.ToArray();

    public IReadOnlyList<TemplateSummary> ListTemplates() => Templates;

    public Task<IReadOnlyList<ProjectSummary>> ListAsync(Actor actor, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue(actor);
        return OnList(actor);
    }

    public Task<Result<ProjectSummary>> CreateAsync(Actor actor, NewProject project, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, project));
        return Task.FromResult(OnCreate(actor, project));
    }

    public Task<Result<ProjectDetail>> GetAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnGet(actor, projectKey));
    }

    public Task<Result<ProjectMember>> SetMemberAsync(
        Actor actor, string projectKey, Guid personId, string role, CancellationToken cancellationToken)
    {
        _setMemberCalls.Enqueue((actor, projectKey, personId, role));
        return Task.FromResult(OnSetMember(actor, projectKey, personId, role));
    }

    public Task<Result<ProjectMember>> RemoveMemberAsync(
        Actor actor, string projectKey, Guid personId, CancellationToken cancellationToken)
    {
        _removeMemberCalls.Enqueue((actor, projectKey, personId));
        return Task.FromResult(OnRemoveMember(actor, projectKey, personId));
    }

    public Func<Actor, string, string, Result<ProjectSummary>> OnRename { get; set; } =
        (_, key, name) => TestData.Project(key);

    public Func<Actor, string, Result<ProjectSummary>> OnArchive { get; set; } =
        (_, key) => TestData.Project(key, archivedAt: TestData.UpdatedAt);

    public Func<Actor, string, Result<ProjectSummary>> OnUnarchive { get; set; } = (_, key) => TestData.Project(key);

    public ConcurrentQueue<(Actor Actor, string ProjectKey, string Name)> RenameCalls { get; } = new();

    public ConcurrentQueue<(Actor Actor, string ProjectKey)> ArchiveCalls { get; } = new();

    public ConcurrentQueue<(Actor Actor, string ProjectKey)> UnarchiveCalls { get; } = new();

    public Task<Result<ProjectSummary>> RenameAsync(Actor actor, string projectKey, string name, CancellationToken cancellationToken)
    {
        RenameCalls.Enqueue((actor, projectKey, name));
        return Task.FromResult(OnRename(actor, projectKey, name));
    }

    public Task<Result<ProjectSummary>> ArchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        ArchiveCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnArchive(actor, projectKey));
    }

    public Task<Result<ProjectSummary>> UnarchiveAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        UnarchiveCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnUnarchive(actor, projectKey));
    }
}

/// <summary>測試用的工作項目服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeWorkItemService : IWorkItemService
{
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, NewWorkItem Item)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey)> _getCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey, WorkItemPatch Patch)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey, string ToState)> _transitionCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey)> _deleteCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey, string? Type)> _changeTypeCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, WorkItemQuery Query)> _listCalls = new();
    private readonly ConcurrentQueue<Actor> _myWorkCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, CrossProjectScope Scope, WorkItemQuery Query)> _listAcrossCalls = new();
    private readonly ConcurrentQueue<Actor> _itemTypeCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string Text, int Limit)> _searchCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey)> _restoreCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, DeletedItemQuery Query)> _listDeletedCalls = new();

    public Func<Actor, string, Result<WorkItem>> OnRestore { get; set; } = (_, key) => TestData.Item(key);

    public Func<Actor, string, DeletedItemQuery, Result<DeletedItemPage>> OnListDeleted { get; set; } =
        (_, _, _) => new DeletedItemPage([], null);

    public IReadOnlyList<(Actor Actor, string ItemKey)> RestoreCalls => _restoreCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, DeletedItemQuery Query)> ListDeletedCalls => _listDeletedCalls.ToArray();

    public Func<Actor, string, NewWorkItem, Result<WorkItem>> OnCreate { get; set; } =
        (_, _, item) => TestData.Item() with { Type = item.Type, Title = item.Title };

    public Func<Actor, string, Result<WorkItem>> OnGet { get; set; } = (_, key) => TestData.Item(key);

    public Func<Actor, string, WorkItemPatch, Result<WorkItem>> OnUpdate { get; set; } = (_, key, _) => TestData.Item(key);

    public Func<Actor, string, string, Result<WorkItem>> OnTransition { get; set; } = (_, key, _) => TestData.Item(key);

    public Func<Actor, string, Result<DeletedWorkItem>> OnDelete { get; set; } = (_, key) => new DeletedWorkItem(key, TestData.UpdatedAt);

    public Func<Actor, string, string?, Result<WorkItem>> OnChangeType { get; set; } =
        (_, key, type) => TestData.Item(key) with { Type = type ?? "task" };

    public Func<Actor, string, WorkItemQuery, Result<WorkItemPage>> OnList { get; set; } =
        (_, _, _) => new WorkItemPage([], null);

    public Func<Actor, IReadOnlyList<WorkItem>> OnMyWork { get; set; } = _ => [];

    public Func<Actor, CrossProjectScope, WorkItemQuery, Result<WorkItemPage>> OnListAcross { get; set; } =
        (_, _, _) => new WorkItemPage([], null);

    public Func<Actor, IReadOnlyList<ItemTypeSummary>> OnItemTypes { get; set; } = _ => [];

    public IReadOnlyList<(Actor Actor, CrossProjectScope Scope, WorkItemQuery Query)> ListAcrossCalls => _listAcrossCalls.ToArray();

    public IReadOnlyList<Actor> ItemTypeCalls => _itemTypeCalls.ToArray();

    public Func<Actor, string, int, Result<IReadOnlyList<WorkItem>>> OnSearch { get; set; } =
        (_, _, _) => new List<WorkItem>();

    public IReadOnlyList<(Actor Actor, string Text, int Limit)> SearchCalls => _searchCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, NewWorkItem Item)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey)> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey, WorkItemPatch Patch)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey, string ToState)> TransitionCalls => _transitionCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey, string? Type)> ChangeTypeCalls => _changeTypeCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, WorkItemQuery Query)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<Actor> MyWorkCalls => _myWorkCalls.ToArray();

    public Task<Result<WorkItem>> CreateAsync(
        Actor actor, string projectKey, NewWorkItem item, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, projectKey, item));
        return Task.FromResult(OnCreate(actor, projectKey, item));
    }

    public Task<Result<WorkItem>> GetAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue((actor, itemKey));
        return Task.FromResult(OnGet(actor, itemKey));
    }

    public Task<Result<WorkItem>> UpdateAsync(
        Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, itemKey, patch));
        return Task.FromResult(OnUpdate(actor, itemKey, patch));
    }

    public Task<Result<WorkItem>> TransitionAsync(
        Actor actor, string itemKey, string toState, CancellationToken cancellationToken)
    {
        _transitionCalls.Enqueue((actor, itemKey, toState));
        return Task.FromResult(OnTransition(actor, itemKey, toState));
    }

    public Task<Result<DeletedWorkItem>> DeleteAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, itemKey));
        return Task.FromResult(OnDelete(actor, itemKey));
    }

    public Task<Result<WorkItem>> RestoreAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        _restoreCalls.Enqueue((actor, itemKey));
        return Task.FromResult(OnRestore(actor, itemKey));
    }

    public Task<Result<DeletedItemPage>> ListDeletedAsync(
        Actor actor, string projectKey, DeletedItemQuery query, CancellationToken cancellationToken)
    {
        _listDeletedCalls.Enqueue((actor, projectKey, query));
        return Task.FromResult(OnListDeleted(actor, projectKey, query));
    }

    public Task<Result<WorkItem>> ChangeTypeAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken)
    {
        _changeTypeCalls.Enqueue((actor, itemKey, type));
        return Task.FromResult(OnChangeType(actor, itemKey, type));
    }

    public Task<Result<WorkItemPage>> ListAsync(
        Actor actor, string projectKey, WorkItemQuery query, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, projectKey, query));
        return Task.FromResult(OnList(actor, projectKey, query));
    }

    public Task<Result<WorkItemPage>> ListAcrossProjectsAsync(
        Actor actor, CrossProjectScope scope, WorkItemQuery query, CancellationToken cancellationToken)
    {
        _listAcrossCalls.Enqueue((actor, scope, query));
        return Task.FromResult(OnListAcross(actor, scope, query));
    }

    public Task<IReadOnlyList<ItemTypeSummary>> ListItemTypesAsync(Actor actor, CancellationToken cancellationToken)
    {
        _itemTypeCalls.Enqueue(actor);
        return Task.FromResult(OnItemTypes(actor));
    }

    public Task<IReadOnlyList<WorkItem>> ListAssignedToMeAsync(Actor actor, CancellationToken cancellationToken)
    {
        _myWorkCalls.Enqueue(actor);
        return Task.FromResult(OnMyWork(actor));
    }

    public Task<Result<IReadOnlyList<WorkItem>>> SearchAsync(Actor actor, string text, int limit, CancellationToken cancellationToken)
    {
        _searchCalls.Enqueue((actor, text, limit));
        return Task.FromResult(OnSearch(actor, text, limit));
    }
}

/// <summary>測試用的活動紀錄服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
/// <summary>一次編輯、刪除、復原或讀版本的呼叫。</summary>
internal sealed record CommentCall(string Action, Actor Actor, string ItemKey, Guid CommentId, string? Body);

internal sealed class FakeItemActivityService : IItemActivityService
{
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey, ActivityQuery Query)> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey, string Body)> _commentCalls = new();
    private readonly ConcurrentQueue<Guid?> _replyToCalls = new();

    public Func<Actor, string, ActivityQuery, Result<ItemActivityPage>> OnList { get; set; } =
        (_, _, _) => new ItemActivityPage([], null);

    public Func<Actor, string, string, Result<ItemActivity>> OnAddComment { get; set; } =
        (actor, key, body) => TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Comment = body };

    public IReadOnlyList<(Actor Actor, string ItemKey, ActivityQuery Query)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey, string Body)> CommentCalls => _commentCalls.ToArray();

    /// <summary>每次留言帶的 replyTo，順序和 <see cref="CommentCalls"/> 一樣。</summary>
    public IReadOnlyList<Guid?> ReplyToCalls => _replyToCalls.ToArray();

    public Task<Result<ItemActivityPage>> ListAsync(
        Actor actor, string itemKey, ActivityQuery query, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, itemKey, query));
        return Task.FromResult(OnList(actor, itemKey, query));
    }

    public Task<Result<ItemActivity>> AddCommentAsync(
        Actor actor, string itemKey, string body, Guid? replyTo, CancellationToken cancellationToken)
    {
        _commentCalls.Enqueue((actor, itemKey, body));
        _replyToCalls.Enqueue(replyTo);
        return Task.FromResult(OnAddComment(actor, itemKey, body));
    }

    private readonly ConcurrentQueue<CommentCall> _commentEditCalls = new();

    /// <summary>編輯、刪除、復原、讀版本各自的回覆；預設回一則留言的活動（編輯帶新的內容）。</summary>
    public Func<Actor, string, Guid, string, Result<ItemActivity>> OnEditComment { get; set; } =
        (actor, key, id, body) => TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Id = id, Comment = body };

    public Func<Actor, string, Guid, Result<ItemActivity>> OnDeleteComment { get; set; } =
        (actor, key, id) => TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Id = id, DeletedAt = TestData.FirstActivityAt, DeletedBy = actor.PersonId };

    public Func<Actor, string, Guid, Result<ItemActivity>> OnRestoreComment { get; set; } =
        (actor, key, id) => TestData.Activity(ActivityKind.Commented, actor.PersonId, itemKey: key) with { Id = id, Comment = "復原的內容" };

    public Func<Actor, string, Guid, Result<IReadOnlyList<CommentRevision>>> OnListCommentRevisions { get; set; } =
        (_, _, _) => new CommentRevision[] { new(2, "新的", TestData.FirstActivityAt.AddMinutes(2), true), new(1, "舊的", TestData.FirstActivityAt, false) };

    /// <summary>編輯、刪除、復原、讀版本的每一次呼叫，依序記下是哪一個動作。</summary>
    public IReadOnlyList<CommentCall> CommentEditCalls => _commentEditCalls.ToArray();

    public Task<Result<ItemActivity>> EditCommentAsync(
        Actor actor, string itemKey, Guid commentId, string body, CancellationToken cancellationToken)
    {
        _commentEditCalls.Enqueue(new CommentCall("edit", actor, itemKey, commentId, body));
        return Task.FromResult(OnEditComment(actor, itemKey, commentId, body));
    }

    public Task<Result<ItemActivity>> DeleteCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        _commentEditCalls.Enqueue(new CommentCall("delete", actor, itemKey, commentId, null));
        return Task.FromResult(OnDeleteComment(actor, itemKey, commentId));
    }

    public Task<Result<ItemActivity>> RestoreCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        _commentEditCalls.Enqueue(new CommentCall("restore", actor, itemKey, commentId, null));
        return Task.FromResult(OnRestoreComment(actor, itemKey, commentId));
    }

    public Task<Result<IReadOnlyList<CommentRevision>>> ListCommentRevisionsAsync(
        Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        _commentEditCalls.Enqueue(new CommentCall("revisions", actor, itemKey, commentId, null));
        return Task.FromResult(OnListCommentRevisions(actor, itemKey, commentId));
    }
}

/// <summary>測試用的批次服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeBulkItemService : IBulkItemService
{
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, BulkItemUpdate Update)> _updateCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> _deleteCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> _restoreCalls = new();

    public Func<Actor, string, BulkItemUpdate, Result<BulkUpdateResult>> OnUpdate { get; set; } =
        (_, _, update) => new BulkUpdateResult([.. update.Keys.Select(key => TestData.Item(key))], []);

    public Func<Actor, string, IReadOnlyList<string>, Result<BulkDeleteResult>> OnDelete { get; set; } =
        (_, _, keys) => new BulkDeleteResult([.. keys.Select(key => new DeletedWorkItem(key, TestData.UpdatedAt))], []);

    public Func<Actor, string, IReadOnlyList<string>, Result<BulkRestoreResult>> OnRestore { get; set; } =
        (_, _, keys) => new BulkRestoreResult([.. keys.Select(key => TestData.Item(key))], []);

    public IReadOnlyList<(Actor Actor, string ProjectKey, BulkItemUpdate Update)> UpdateCalls => _updateCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, IReadOnlyList<string> Keys)> RestoreCalls => _restoreCalls.ToArray();

    public Task<Result<BulkUpdateResult>> UpdateAsync(Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken)
    {
        _updateCalls.Enqueue((actor, projectKey, update));
        return Task.FromResult(OnUpdate(actor, projectKey, update));
    }

    public Task<Result<BulkDeleteResult>> DeleteAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, projectKey, itemKeys));
        return Task.FromResult(OnDelete(actor, projectKey, itemKeys));
    }

    public Task<Result<BulkRestoreResult>> RestoreAsync(
        Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken)
    {
        _restoreCalls.Enqueue((actor, projectKey, itemKeys));
        return Task.FromResult(OnRestore(actor, projectKey, itemKeys));
    }
}

/// <summary>測試用的匯入服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeItemImportService : IItemImportService
{
    public Func<Actor, string, ItemImport, Result<ItemImportResult>> OnImport { get; set; } =
        (_, _, import) => new ItemImportResult(
            import.DryRun, import.Rows.Count, import.DryRun ? [] : [.. import.Rows.Select((_, i) => $"WEB-{i + 1}")]);

    public ConcurrentQueue<(Actor Actor, string ProjectKey, ItemImport Import)> Calls { get; } = new();

    public Task<Result<ItemImportResult>> ImportAsync(Actor actor, string projectKey, ItemImport import, CancellationToken cancellationToken)
    {
        Calls.Enqueue((actor, projectKey, import));
        return Task.FromResult(OnImport(actor, projectKey, import));
    }
}

/// <summary>歷史匯入的假服務：記住每次呼叫的參數（附件把內容讀完），回傳值由測試設定。</summary>
internal sealed class FakeHistoryImportService : IHistoryImportService
{
    public Func<Actor, string, HistoryItems, Result<HistoryItemsResult>> OnItems { get; set; } =
        (_, _, import) => new HistoryItemsResult(
            import.DryRun,
            import.DryRun ? [] : [.. import.Rows.Select((row, i) => new HistoryItemRef(row.ExternalId, $"WEB-{i + 1}"))],
            []);

    public Func<Actor, string, HistoryComments, Result<HistoryCommentsResult>> OnComments { get; set; } =
        (_, _, import) => new HistoryCommentsResult(
            [.. import.Comments.Select(c => new HistoryCommentRef(c.ExternalId, Guid.Parse("0190a000-0000-7000-8000-0000000000c1")))], []);

    public Func<Actor, string, HistoryAttachment, byte[], Result<HistoryAttachmentResult>> OnAttachment { get; set; } =
        (_, itemKey, upload, content) => new HistoryAttachmentResult(
            FakeAttachmentService.Sample(itemKey: itemKey, fileName: upload.FileName, size: content.Length, uploadedBy: upload.UploadedBy), Existing: false);

    public Func<Actor, HistoryLookup, Result<IReadOnlyList<HistoryRef>>> OnLookup { get; set; } =
        (_, lookup) => lookup.ExternalIds.Select(id => new HistoryRef(id, lookup.Kind, "WEB", "WEB-1", Guid.Parse("0190a000-0000-7000-8000-0000000000d1"))).ToList();

    public ConcurrentQueue<(Actor Actor, string ProjectKey, HistoryItems Import)> ItemCalls { get; } = new();

    public ConcurrentQueue<(Actor Actor, string ProjectKey, HistoryComments Import)> CommentCalls { get; } = new();

    public ConcurrentQueue<(Actor Actor, string ItemKey, HistoryAttachment Upload, byte[] Content)> AttachmentCalls { get; } = new();

    public ConcurrentQueue<(Actor Actor, HistoryLookup Lookup)> LookupCalls { get; } = new();

    public Task<Result<HistoryItemsResult>> ImportItemsAsync(Actor actor, string projectKey, HistoryItems import, CancellationToken cancellationToken)
    {
        ItemCalls.Enqueue((actor, projectKey, import));
        return Task.FromResult(OnItems(actor, projectKey, import));
    }

    public Task<Result<HistoryCommentsResult>> ImportCommentsAsync(Actor actor, string projectKey, HistoryComments import, CancellationToken cancellationToken)
    {
        CommentCalls.Enqueue((actor, projectKey, import));
        return Task.FromResult(OnComments(actor, projectKey, import));
    }

    public async Task<Result<HistoryAttachmentResult>> ImportAttachmentAsync(
        Actor actor, string itemKey, HistoryAttachment upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        AttachmentCalls.Enqueue((actor, itemKey, upload, buffer.ToArray()));
        return OnAttachment(actor, itemKey, upload, buffer.ToArray());
    }

    public Task<Result<IReadOnlyList<HistoryRef>>> LookupAsync(Actor actor, HistoryLookup lookup, CancellationToken cancellationToken)
    {
        LookupCalls.Enqueue((actor, lookup));
        return Task.FromResult(OnLookup(actor, lookup));
    }
}
