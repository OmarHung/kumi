using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Ai.Tests.Assistant.Fakes;

/// <summary>項目相關的工作引擎假物件：記下每次寫入的人與內容，讀的結果由測試設定。</summary>
internal static class ItemSamples
{
    public static readonly Guid MemberId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    public static WorkItem Item(string key = "WEB-12", string title = "改版首頁", string project = "WEB", string? description = "舊的說明") =>
        new(
            key, project, 12, "task", title, "todo", PmTool.Blueprint.Model.StateCategory.NotStarted, null, Priority.None,
            null, null, null, null, new Dictionary<string, JsonElement>(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
        {
            Description = description,
            CreatedBy = MemberId,
        };

    public static readonly IReadOnlyList<string> EditorPermissions =
        [.. WorkSamples.AllPermissions, WorkPermissions.ItemUpdate, WorkPermissions.ItemTransition, WorkPermissions.CommentCreate];

    public static ProjectDetail DetailWithMember(IReadOnlyList<string>? permissions = null) =>
        WorkSamples.Detail(permissions ?? EditorPermissions) with { Members = [new ProjectMember(MemberId, "member")] };
}

internal sealed class FakeWorkItemService : IWorkItemService
{
    private readonly List<(Actor Actor, string Key, WorkItemPatch Patch)> _updates = [];
    private readonly List<(Actor Actor, string Key, string State)> _transitions = [];
    private readonly List<(Actor Actor, string ProjectKey, NewWorkItem Item)> _creates = [];

    public Dictionary<string, WorkItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase) { ["WEB-12"] = ItemSamples.Item() };

    public Func<string, WorkItemPatch, Result<WorkItem>>? OnUpdate { get; set; }

    public Func<string, string, Result<WorkItem>>? OnTransition { get; set; }

    public IReadOnlyList<(Actor Actor, string Key, WorkItemPatch Patch)> Updates => _updates;

    /// <summary>設了就用它決定每一次建立的結果（參數是第幾次、從 0 起算，與要建立的內容）；沒設時依序回 WEB-101、WEB-102…。</summary>
    public Func<int, NewWorkItem, Result<WorkItem>>? OnCreate { get; set; }

    public IReadOnlyList<(Actor Actor, string ProjectKey, NewWorkItem Item)> Creates => _creates;

    public IReadOnlyList<(Actor Actor, string Key, string State)> Transitions => _transitions;

    public Task<Result<WorkItem>> GetAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<WorkItem>>(Items.TryGetValue(itemKey, out var item)
            ? item
            : WorkSamples.Failure(FailureKind.NotFound, WorkErrorCodes.ItemNotFound, "找不到項目。"));

    public Task<Result<WorkItem>> UpdateAsync(Actor actor, string itemKey, WorkItemPatch patch, CancellationToken cancellationToken)
    {
        _updates.Add((actor, itemKey, patch));
        Steps.Add("update");
        return Task.FromResult(OnUpdate?.Invoke(itemKey, patch) ?? Items[itemKey]);
    }

    public Task<Result<WorkItem>> TransitionAsync(Actor actor, string itemKey, string toState, CancellationToken cancellationToken)
    {
        _transitions.Add((actor, itemKey, toState));
        Steps.Add("transition");
        return Task.FromResult(OnTransition?.Invoke(itemKey, toState) ?? Items[itemKey]);
    }

    public Task<Result<WorkItemPage>> ListAsync(Actor actor, string projectKey, WorkItemQuery query, CancellationToken cancellationToken)
    {
        var found = Items.Values
            .Where(item => item.ProjectKey == projectKey)
            .Where(item => query.ParentKey is null ? query.Search is null || item.Title.Contains(query.Search, StringComparison.Ordinal) || item.Key == query.Search : item.ParentKey == query.ParentKey)
            .ToList();
        return Task.FromResult<Result<WorkItemPage>>(new WorkItemPage([.. found.Take(query.Limit)], found.Count > query.Limit ? "more" : null));
    }

    public Task<Result<WorkItem>> CreateAsync(Actor actor, string projectKey, NewWorkItem item, CancellationToken cancellationToken)
    {
        int index;
        lock (_creates)
        {
            index = _creates.Count;
            _creates.Add((actor, projectKey, item));
        }

        return Task.FromResult(OnCreate?.Invoke(index, item) ?? ItemSamples.Item($"{projectKey}-{101 + index}", item.Title, projectKey));
    }

    public Task<Result<DeletedWorkItem>> DeleteAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<WorkItem>> RestoreAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    /// <summary>設了就用它回已刪除項目的清單（參數是專案代號與條件）；沒設時不支援。</summary>
    public Func<string, DeletedItemQuery, Result<DeletedItemPage>>? OnListDeleted { get; set; }

    public Task<Result<DeletedItemPage>> ListDeletedAsync(
        Actor actor, string projectKey, DeletedItemQuery query, CancellationToken cancellationToken) =>
        OnListDeleted is { } handler ? Task.FromResult(handler(projectKey, query)) : throw new NotSupportedException();

    private readonly List<(Actor Actor, string Key, string Type)> _typeChanges = [];

    /// <summary>設了就用它決定改類型的結果；沒設時回項目現在的樣子。</summary>
    public Func<string, string, Result<WorkItem>>? OnChangeType { get; set; }

    public IReadOnlyList<(Actor Actor, string Key, string Type)> TypeChanges => _typeChanges;

    public Task<Result<WorkItem>> ChangeTypeAsync(Actor actor, string itemKey, string type, CancellationToken cancellationToken)
    {
        _typeChanges.Add((actor, itemKey, type));
        Steps.Add("type");
        return Task.FromResult(OnChangeType?.Invoke(itemKey, type) ?? Items[itemKey]);
    }

    /// <summary>每次寫入（改類型、改欄位、改狀態、留言）依發生的順序記下步驟名稱，測套用的順序用。</summary>
    public List<string> Steps { get; } = [];

    public Task<IReadOnlyList<WorkItem>> ListAssignedToMeAsync(Actor actor, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    private readonly List<(Actor Actor, CrossProjectScope Scope, WorkItemQuery Query)> _acrossCalls = [];

    /// <summary>跨專案清單的結果；沒設就和以前一樣不支援。參數是呼叫者與條件。</summary>
    public Func<Actor, WorkItemQuery, Result<WorkItemPage>>? OnListAcross { get; set; }

    public IReadOnlyList<(Actor Actor, CrossProjectScope Scope, WorkItemQuery Query)> AcrossCalls => _acrossCalls;

    public Task<Result<WorkItemPage>> ListAcrossProjectsAsync(
        Actor actor, CrossProjectScope scope, WorkItemQuery query, CancellationToken cancellationToken)
    {
        _acrossCalls.Add((actor, scope, query));
        return OnListAcross is { } handler ? Task.FromResult(handler(actor, query)) : throw new NotSupportedException();
    }

    public Task<IReadOnlyList<ItemTypeSummary>> ListItemTypesAsync(Actor actor, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<WorkItem>>> SearchAsync(Actor actor, string text, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class FakeItemActivityService : IItemActivityService
{
    private readonly List<(Actor Actor, string Key, string Body)> _comments = [];

    public IReadOnlyList<ItemActivity> Activities { get; set; } = [];

    public Func<string, Result<ItemActivity>>? OnComment { get; set; }

    public IReadOnlyList<(Actor Actor, string Key, string Body)> Comments => _comments;

    /// <summary>設成 <see cref="FakeWorkItemService.Steps"/>，留言就會記在同一份依序的紀錄裡。</summary>
    public List<string>? StepLog { get; set; }

    /// <summary>每次留言的 replyTo，和 <see cref="Comments"/> 同一個順序。</summary>
    public List<Guid?> ReplyTos { get; } = [];

    /// <summary>每次讀活動的條件；測 list_comments 有沒有只要留言與翻頁用。</summary>
    public List<ActivityQuery> Queries { get; } = [];

    // 照 Kind 篩、游標是下一筆的位置、一頁最多 Limit 筆：和真的服務同一個形狀，沒給條件時還是全部。
    public Task<Result<ItemActivityPage>> ListAsync(Actor actor, string itemKey, ActivityQuery query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        var all = Activities.Where(a => query.Kind is null || a.Kind == query.Kind).ToList();
        var start = int.TryParse(query.Cursor, out var parsed) ? parsed : 0;
        var page = all.Skip(start).Take(query.Limit).ToList();
        var next = start + page.Count < all.Count ? (start + page.Count).ToString() : null;
        return Task.FromResult<Result<ItemActivityPage>>(new ItemActivityPage(page, next));
    }

    public Task<Result<ItemActivity>> AddCommentAsync(
        Actor actor, string itemKey, string body, Guid? replyTo, CancellationToken cancellationToken)
    {
        _comments.Add((actor, itemKey, body));
        ReplyTos.Add(replyTo);
        StepLog?.Add("comment");
        return Task.FromResult(OnComment?.Invoke(body)
            ?? new ItemActivity(Guid.NewGuid(), itemKey, ActivityKind.Commented, actor.PersonId, DateTimeOffset.UnixEpoch) { Comment = body });
    }

    /// <summary>AI 助理沒有編輯、刪除、復原留言的工具；任何一次呼叫都表示它越界了，測試靠這個數字確認沒有。</summary>
    public int CommentEditCalls { get; private set; }

    public Task<Result<ItemActivity>> EditCommentAsync(
        Actor actor, string itemKey, Guid commentId, string body, CancellationToken cancellationToken)
    {
        CommentEditCalls++;
        return Task.FromResult<Result<ItemActivity>>(new Failure(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, "助理不能改留言。"));
    }

    public Task<Result<ItemActivity>> DeleteCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        CommentEditCalls++;
        return Task.FromResult<Result<ItemActivity>>(new Failure(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, "助理不能刪留言。"));
    }

    public Task<Result<ItemActivity>> RestoreCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        CommentEditCalls++;
        return Task.FromResult<Result<ItemActivity>>(new Failure(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, "助理不能復原留言。"));
    }

    public Task<Result<IReadOnlyList<CommentRevision>>> ListCommentRevisionsAsync(
        Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken)
    {
        CommentEditCalls++;
        return Task.FromResult<Result<IReadOnlyList<CommentRevision>>>(new Failure(FailureKind.Forbidden, WorkErrorCodes.CommentNotAuthor, "助理不讀版本。"));
    }
}

/// <summary>
/// 假的人員目錄。沒登記過的人一律是一般成員（<see cref="DefaultRole"/>）；主機代理的測試登記每個人的角色與停用狀態，
/// 也能在測試途中改（途中降權）。<see cref="DefaultRole"/> 設成 null 時，沒登記的人（或登記在別的組織的人）查不到。
/// </summary>
internal sealed class FakePeopleDirectory : IPeopleDirectory
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(Guid Organization, Guid Person), PersonSummary> _people = new();

    public OrganizationRole? DefaultRole { get; set; } = OrganizationRole.Member;

    public void Set(
        Guid organizationId, Guid personId, OrganizationRole role, bool disabled = false, string name = "王小明", string? jobTitle = null, string? responsibilities = null) =>
        _people[(organizationId, personId)] = new PersonSummary(personId, name, null, role, disabled) { JobTitle = jobTitle, Responsibilities = responsibilities };

    public void SetRole(Guid organizationId, Guid personId, OrganizationRole role)
    {
        var found = _people.TryGetValue((organizationId, personId), out var existing) ? existing : null;
        Set(organizationId, personId, role, found?.IsDisabled ?? false, found?.DisplayName ?? "王小明", found?.JobTitle, found?.Responsibilities);
    }

    public void SetDisabled(Guid organizationId, Guid personId, bool disabled)
    {
        var found = _people.TryGetValue((organizationId, personId), out var existing) ? existing : null;
        Set(organizationId, personId, found?.Role ?? OrganizationRole.Member, disabled, found?.DisplayName ?? "王小明", found?.JobTitle, found?.Responsibilities);
    }

    public Task<IReadOnlyDictionary<Guid, PersonSummary>> FindManyAsync(
        Guid organizationId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
    {
        var found = new Dictionary<Guid, PersonSummary>();
        foreach (var id in personIds)
        {
            if (_people.TryGetValue((organizationId, id), out var person))
            {
                found[id] = person;
            }
            else if (DefaultRole is { } role)
            {
                found[id] = new PersonSummary(id, "王小明", null, role, false);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, PersonSummary>>(found);
    }

    public Task<IReadOnlyList<PersonSummary>> ListAsync(Guid organizationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PersonSummary>>(
            [.. _people.Where(entry => entry.Key.Organization == organizationId).Select(entry => entry.Value).OrderBy(person => person.DisplayName, StringComparer.Ordinal)]);

    public Task<Result<PersonSummary>> CreateAsync(Actor actor, NewPerson person, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> SetDisabledAsync(Actor actor, Guid personId, bool disabled, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> ResetPasswordAsync(Actor actor, Guid personId, string newPassword, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PersonSummary>> SetRoleAsync(Actor actor, Guid personId, OrganizationRole role, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
