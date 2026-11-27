using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions.SavedViews;

/// <summary>記憶體裡的檢視服務：專案的與「我的工作」的各一份清單，記下每次寫入的參數，失敗由測試指定。</summary>
internal sealed class RecordingSavedViews : ISavedViewService
{
    public List<SavedView> Project { get; } = [];

    public List<SavedView> MyWork { get; } = [];

    public List<string> Calls { get; } = [];

    public List<SavedViewDraft> Drafts { get; } = [];

    public Failure? FailWith { get; set; }

    public Task<Result<IReadOnlyList<SavedView>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<SavedView>>>(Project.ToList());

    public Task<Result<SavedView>> CreateAsync(Actor actor, string projectKey, SavedViewDraft draft, CancellationToken cancellationToken) =>
        Record($"create:{projectKey}", null, draft);

    public Task<Result<SavedView>> ReplaceAsync(Actor actor, string projectKey, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken) =>
        Record($"replace:{projectKey}", viewId, draft);

    public Task<Result<Unit>> DeleteAsync(Actor actor, string projectKey, Guid viewId, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{projectKey}:{viewId}");
        return Task.FromResult(FailWith is { } failure ? (Result<Unit>)failure : Unit.Value);
    }

    public Task<IReadOnlyList<SavedView>> ListMyWorkAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedView>>(MyWork.ToList());

    public Task<Result<SavedView>> CreateMyWorkAsync(Actor actor, SavedViewDraft draft, CancellationToken cancellationToken) =>
        Record("create:my_work", null, draft);

    public Task<Result<SavedView>> ReplaceMyWorkAsync(Actor actor, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken) =>
        Record("replace:my_work", viewId, draft);

    public Task<Result<Unit>> DeleteMyWorkAsync(Actor actor, Guid viewId, CancellationToken cancellationToken)
    {
        Calls.Add($"delete:my_work:{viewId}");
        return Task.FromResult(FailWith is { } failure ? (Result<Unit>)failure : Unit.Value);
    }

    public static SavedView View(string name, SavedViewLayout layout = SavedViewLayout.Table, SavedViewQuery? query = null) =>
        new(Guid.NewGuid(), name, layout, query ?? new SavedViewQuery(null, null, null, null, null, null, null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private Task<Result<SavedView>> Record(string call, Guid? id, SavedViewDraft draft)
    {
        Calls.Add(id is { } value ? $"{call}:{value}" : call);
        Drafts.Add(draft);
        return Task.FromResult(
            FailWith is { } failure
                ? (Result<SavedView>)failure
                : new SavedView(id ?? Guid.NewGuid(), draft.Name, draft.Layout, draft.Query, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
    }
}
