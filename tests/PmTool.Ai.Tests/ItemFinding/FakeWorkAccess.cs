using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Ai.Tests.ItemFinding;

/// <summary>讀過的專案的檢查只問「看得到哪些專案、權限是什麼」；其他查詢不該被用到。</summary>
internal sealed class FakeWorkAccess : IWorkAccessQueries
{
    public static readonly IReadOnlyList<string> Reader = [WorkPermissions.ItemRead, WorkPermissions.BlueprintRead];

    public static readonly IReadOnlyList<string> OwnOnly = [WorkPermissions.ItemReadOwn, WorkPermissions.BlueprintRead];

    private readonly Dictionary<Guid, ProjectAccessSummary> _visible = [];

    /// <summary>可見專案被問了幾次（VisibleProjectsAsync 或 FindVisibleProjectsAsync）：一次查完，不是一個專案問一次。</summary>
    public int VisibleCalls { get; private set; }

    public void Set(Guid projectId, string key, IReadOnlyList<string> permissions) =>
        _visible[projectId] = new ProjectAccessSummary(projectId, key, key, false, permissions);

    public void Remove(Guid projectId) => _visible.Remove(projectId);

    public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> VisibleProjectsAsync(Actor actor, CancellationToken cancellationToken)
    {
        VisibleCalls++;
        return Task.FromResult<IReadOnlyDictionary<Guid, ProjectAccessSummary>>(new Dictionary<Guid, ProjectAccessSummary>(_visible));
    }

    public Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<Guid>> FilterReadersAsync(
        Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ProjectAccessSummary?> FindVisibleProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> FindVisibleProjectsAsync(
        Actor actor, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        VisibleCalls++;
        return Task.FromResult<IReadOnlyDictionary<Guid, ProjectAccessSummary>>(
            _visible.Where(entry => projectIds.Contains(entry.Key)).ToDictionary(entry => entry.Key, entry => entry.Value));
    }
}
