using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;
using PmTool.Work;

namespace PmTool.Ai.Tests.CommentProposals;

/// <summary>留言的假物件：記下每次編輯、刪除、復原，結果由測試設定（預設成功、回傳原本的留言）。</summary>
internal sealed class FakeComments : IItemActivityService
{
    private readonly List<(string Action, Actor Actor, string ItemKey, Guid CommentId, string? Body)> _writes = [];

    public List<ItemActivity> Comments { get; } = [];

    public Func<string, Guid, Result<ItemActivity>?>? OnWrite { get; set; }

    public IReadOnlyList<(string Action, Actor Actor, string ItemKey, Guid CommentId, string? Body)> Writes => _writes;

    public Task<Result<ItemActivityPage>> ListAsync(Actor actor, string itemKey, ActivityQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<ItemActivityPage>>(new ItemActivityPage(
            [.. Comments.Where(c => string.Equals(c.ItemKey, itemKey, StringComparison.OrdinalIgnoreCase) && (query.Kind is null || c.Kind == query.Kind))], null));

    public Task<Result<ItemActivity>> EditCommentAsync(Actor actor, string itemKey, Guid commentId, string body, CancellationToken cancellationToken) =>
        Write("edit", actor, itemKey, commentId, body);

    public Task<Result<ItemActivity>> DeleteCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken) =>
        Write("delete", actor, itemKey, commentId, null);

    public Task<Result<ItemActivity>> RestoreCommentAsync(Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken) =>
        Write("restore", actor, itemKey, commentId, null);

    public Task<Result<ItemActivity>> AddCommentAsync(Actor actor, string itemKey, string body, Guid? replyTo, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<IReadOnlyList<CommentRevision>>> ListCommentRevisionsAsync(
        Actor actor, string itemKey, Guid commentId, CancellationToken cancellationToken) => throw new NotSupportedException();

    private Task<Result<ItemActivity>> Write(string action, Actor actor, string itemKey, Guid commentId, string? body)
    {
        _writes.Add((action, actor, itemKey, commentId, body));
        var found = Comments.First(c => c.Id == commentId);
        return Task.FromResult(OnWrite?.Invoke(action, commentId) ?? found);
    }
}

/// <summary>留言建議測試的共同環境：助理的環境，加上有記錄的留言假物件、WEB-12 與 WEB-13 兩個項目，以及一組可以調整的專案權限。</summary>
public abstract class CommentTestBase
{
    protected static readonly Guid Other = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private AssistantHarness _h = null!;

    internal AssistantHarness H => _h;

    internal FakeComments Comments { get; private set; } = null!;

    protected Actor Me => _h.Actor;

    internal ICommentProposalCreator Creator => _h.Host.Get<ICommentProposalCreator>();

    internal ICommentProposalService Proposals => _h.Host.Get<ICommentProposalService>();

    [SetUp]
    public async Task StartAsync()
    {
        Comments = new FakeComments();
        _h = await AssistantHarness.StartAsync(configure: services => services.AddSingleton<IItemActivityService>(Comments));
        _h.WorkItems.Items["WEB-13"] = ItemSamples.Item("WEB-13", "第二個任務");
        Permissions(manage: false);
    }

    [TearDown]
    public async Task StopAsync() => await _h.DisposeAsync();

    protected void Permissions(bool manage, bool comment = true, bool archived = false)
    {
        var perms = new List<string>(WorkSamples.AllPermissions);
        if (comment)
        {
            perms.Add(WorkPermissions.CommentCreate);
        }

        if (manage)
        {
            perms.Add(WorkPermissions.ProjectManage);
        }

        _h.Projects.OnGet = (_, _) =>
        {
            var detail = WorkSamples.Detail(perms);
            return archived ? detail with { Project = detail.Project with { ArchivedAt = DateTimeOffset.UnixEpoch } } : detail;
        };
    }

    /// <summary>加一則留言。預設是使用者自己的。</summary>
    protected ItemActivity AddComment(
        string itemKey = "WEB-12", Guid? author = null, string text = "原本的內容", DateTimeOffset? deletedAt = null, Guid? deletedBy = null, string? importedFrom = null)
    {
        var activity = new ItemActivity(Guid.NewGuid(), itemKey, ActivityKind.Commented, author ?? Me.PersonId, DateTimeOffset.UnixEpoch.AddDays(1))
        {
            Comment = deletedAt is null ? text : null,
            DeletedAt = deletedAt,
            DeletedBy = deletedBy,
            ImportedFrom = importedFrom,
        };
        Comments.Comments.Add(activity);
        return activity;
    }

    protected static CommentChangeDraft Edit(string itemKey, Guid id, string? body = "新的內容") => new(itemKey, id, CommentAction.Edit, body);

    protected static CommentChangeDraft Delete(string itemKey, Guid id) => new(itemKey, id, CommentAction.Delete, null);

    protected static CommentChangeDraft Restore(string itemKey, Guid id) => new(itemKey, id, CommentAction.Restore, null);

    protected async Task<Result<CommentProposal>> CreateAsync(params CommentChangeDraft[] changes) =>
        await Creator.CreateAsync(Me, new CommentProposalDraft("WEB", changes), CancellationToken.None);

    protected async Task<CommentProposal> CreatedAsync(params CommentChangeDraft[] changes)
    {
        var created = await CreateAsync(changes);
        Assert.That(created.Succeeded, Is.True, () => created.Failure?.Message ?? string.Empty);
        return created.Value;
    }

    protected static string IssueAt(Failure? failure, string path) => failure!.Details.Single(d => d.Path == path).Message;
}
