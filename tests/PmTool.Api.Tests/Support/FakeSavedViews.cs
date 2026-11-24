using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的檢視服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeSavedViewService : ISavedViewService
{
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey)> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, SavedViewDraft Draft)> _createCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid ViewId, SavedViewDraft Draft)> _replaceCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ProjectKey, Guid ViewId)> _deleteCalls = new();
    private readonly ConcurrentQueue<Actor> _listMyWorkCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, SavedViewDraft Draft)> _createMyWorkCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid ViewId, SavedViewDraft Draft)> _replaceMyWorkCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid ViewId)> _deleteMyWorkCalls = new();

    public static SavedView Sample(Guid? id = null, SavedViewDraft? draft = null) => new(
        id ?? Guid.Parse("018f0000-0000-7000-8000-000000000001"),
        draft?.Name ?? "我本週要交的",
        draft?.Layout ?? SavedViewLayout.Board,
        draft?.Query ?? new SavedViewQuery("task", "in_progress", "me", "登入", "due_date:asc", "week", "high"),
        TestData.UpdatedAt,
        TestData.UpdatedAt);

    public Func<Actor, string, Result<IReadOnlyList<SavedView>>> OnList { get; set; } =
        (_, _) => new List<SavedView> { Sample() };

    public Func<Actor, string, SavedViewDraft, Result<SavedView>> OnCreate { get; set; } =
        (_, _, draft) => Sample(draft: draft);

    public Func<Actor, string, Guid, SavedViewDraft, Result<SavedView>> OnReplace { get; set; } =
        (_, _, id, draft) => Sample(id, draft);

    public Func<Actor, string, Guid, Result<Unit>> OnDelete { get; set; } = (_, _, _) => Unit.Value;

    public Func<Actor, IReadOnlyList<SavedView>> OnListMyWork { get; set; } = _ => new List<SavedView> { Sample() };

    public Func<Actor, SavedViewDraft, Result<SavedView>> OnCreateMyWork { get; set; } = (_, draft) => Sample(draft: draft);

    public Func<Actor, Guid, SavedViewDraft, Result<SavedView>> OnReplaceMyWork { get; set; } = (_, id, draft) => Sample(id, draft);

    public Func<Actor, Guid, Result<Unit>> OnDeleteMyWork { get; set; } = (_, _) => Unit.Value;

    public IReadOnlyList<Actor> ListMyWorkCalls => _listMyWorkCalls.ToArray();

    public IReadOnlyList<(Actor Actor, SavedViewDraft Draft)> CreateMyWorkCalls => _createMyWorkCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid ViewId, SavedViewDraft Draft)> ReplaceMyWorkCalls => _replaceMyWorkCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid ViewId)> DeleteMyWorkCalls => _deleteMyWorkCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, SavedViewDraft Draft)> CreateCalls => _createCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid ViewId, SavedViewDraft Draft)> ReplaceCalls => _replaceCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ProjectKey, Guid ViewId)> DeleteCalls => _deleteCalls.ToArray();

    public Task<Result<IReadOnlyList<SavedView>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, projectKey));
        return Task.FromResult(OnList(actor, projectKey));
    }

    public Task<Result<SavedView>> CreateAsync(Actor actor, string projectKey, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        _createCalls.Enqueue((actor, projectKey, draft));
        return Task.FromResult(OnCreate(actor, projectKey, draft));
    }

    public Task<Result<SavedView>> ReplaceAsync(
        Actor actor, string projectKey, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        _replaceCalls.Enqueue((actor, projectKey, viewId, draft));
        return Task.FromResult(OnReplace(actor, projectKey, viewId, draft));
    }

    public Task<Result<Unit>> DeleteAsync(Actor actor, string projectKey, Guid viewId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, projectKey, viewId));
        return Task.FromResult(OnDelete(actor, projectKey, viewId));
    }

    public Task<IReadOnlyList<SavedView>> ListMyWorkAsync(Actor actor, CancellationToken cancellationToken)
    {
        _listMyWorkCalls.Enqueue(actor);
        return Task.FromResult(OnListMyWork(actor));
    }

    public Task<Result<SavedView>> CreateMyWorkAsync(Actor actor, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        _createMyWorkCalls.Enqueue((actor, draft));
        return Task.FromResult(OnCreateMyWork(actor, draft));
    }

    public Task<Result<SavedView>> ReplaceMyWorkAsync(Actor actor, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken)
    {
        _replaceMyWorkCalls.Enqueue((actor, viewId, draft));
        return Task.FromResult(OnReplaceMyWork(actor, viewId, draft));
    }

    public Task<Result<Unit>> DeleteMyWorkAsync(Actor actor, Guid viewId, CancellationToken cancellationToken)
    {
        _deleteMyWorkCalls.Enqueue((actor, viewId));
        return Task.FromResult(OnDeleteMyWork(actor, viewId));
    }
}
