using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Knowledge.Tests;

/// <summary>
/// 測試用的存取查詢：誰在哪個專案有哪些權限由測試設定。知識庫不自己判斷專案的權限，所以不需要真的工作引擎。
/// 只有知識庫用的三個查詢有實作。
/// </summary>
internal sealed class FakeWorkAccessQueries : IWorkAccessQueries
{
    public static readonly IReadOnlyList<string> ViewerPermissions = [WorkPermissions.ItemRead];

    public static readonly IReadOnlyList<string> EditorPermissions =
        [WorkPermissions.ItemRead, WorkPermissions.ItemCreate, WorkPermissions.CommentCreate];

    /// <summary>內建「執行者」：只看得到自己的項目（<c>item.read_own</c>），所以在專案空間是唯讀。</summary>
    public static readonly IReadOnlyList<string> ExecutorPermissions =
    [
        WorkPermissions.ItemReadOwn, WorkPermissions.ItemCreate, WorkPermissions.ItemUpdate,
        WorkPermissions.ItemTransition, WorkPermissions.CommentCreate, WorkPermissions.BlueprintRead,
    ];

    public static readonly IReadOnlyList<string> ManagerPermissions =
        [WorkPermissions.ItemRead, WorkPermissions.ItemCreate, WorkPermissions.CommentCreate, WorkPermissions.ProjectManage];

    private readonly Lock _gate = new();
    private readonly Dictionary<(Guid Organization, Guid Person), Dictionary<Guid, ProjectAccessSummary>> _visible = [];

    /// <summary>這個人看得到這個專案，權限是 <paramref name="permissions"/>（封存的專案由呼叫端自己把權限收斂成唯讀）。</summary>
    public ProjectAccessSummary Grant(
        Guid organizationId, Guid personId, Guid projectId, string key, IReadOnlyList<string> permissions, bool archived = false)
    {
        var summary = new ProjectAccessSummary(projectId, key, $"專案 {key}", archived, permissions);
        lock (_gate)
        {
            if (!_visible.TryGetValue((organizationId, personId), out var projects))
            {
                _visible[(organizationId, personId)] = projects = [];
            }

            projects[projectId] = summary;
        }

        return summary;
    }

    public void Revoke(Guid organizationId, Guid personId, Guid projectId)
    {
        lock (_gate)
        {
            _visible.GetValueOrDefault((organizationId, personId))?.Remove(projectId);
        }
    }

    /// <summary>VisibleProjectsAsync 被呼叫了幾次（替所有看得到的專案算權限，知識庫不該用它）。</summary>
    public int VisibleProjectCalls { get; private set; }

    /// <summary>FindVisibleProjectsAsync 被呼叫了幾次（清單與附件要一次問完、只問用得到的專案）。</summary>
    public int FindVisibleProjectsCalls { get; private set; }

    /// <summary>最近一次 FindVisibleProjectsAsync 問的專案編號。</summary>
    public IReadOnlyList<Guid> LastRequestedProjectIds { get; private set; } = [];

    public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> VisibleProjectsAsync(Actor actor, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            VisibleProjectCalls++;
        }

        return Task.FromResult(Snapshot(actor));
    }

    public Task<ProjectAccessSummary?> FindVisibleProjectAsync(Actor actor, string projectKey, CancellationToken cancellationToken) =>
        Task.FromResult(
            Snapshot(actor).Values.FirstOrDefault(p => string.Equals(p.Key, projectKey, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyDictionary<Guid, ProjectAccessSummary>> FindVisibleProjectsAsync(
        Actor actor, IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            FindVisibleProjectsCalls++;
            LastRequestedProjectIds = [.. projectIds];
        }

        return Task.FromResult<IReadOnlyDictionary<Guid, ProjectAccessSummary>>(
            Snapshot(actor).Where(pair => projectIds.Contains(pair.Key)).ToDictionary());
    }

    private IReadOnlyDictionary<Guid, ProjectAccessSummary> Snapshot(Actor actor)
    {
        lock (_gate)
        {
            return new Dictionary<Guid, ProjectAccessSummary>(_visible.GetValueOrDefault((actor.OrganizationId, actor.PersonId)) ?? []);
        }
    }

    public Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        throw new NotSupportedException("知識庫不解析項目編號。");

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
}
