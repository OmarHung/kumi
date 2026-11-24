using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的收件匣：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeNotificationInbox : INotificationInbox
{
    private readonly ConcurrentQueue<(Actor Actor, NotificationQuery Query)> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid[] Ids)> _markReadCalls = new();
    private readonly ConcurrentQueue<Actor> _markAllCalls = new();

    public Func<Actor, NotificationQuery, Result<NotificationPage>> OnList { get; set; } =
        (_, _) => new NotificationPage([], null);

    public Func<Actor, int> OnCountUnread { get; set; } = _ => 0;

    public Func<Actor, IReadOnlyCollection<Guid>, int> OnMarkRead { get; set; } = (_, ids) => ids.Count;

    public Func<Actor, int> OnMarkAllRead { get; set; } = _ => 0;

    public IReadOnlyList<(Actor Actor, NotificationQuery Query)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid[] Ids)> MarkReadCalls => _markReadCalls.ToArray();

    public IReadOnlyList<Actor> MarkAllReadCalls => _markAllCalls.ToArray();

    public Task<Result<NotificationPage>> ListAsync(Actor actor, NotificationQuery query, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, query));
        return Task.FromResult(OnList(actor, query));
    }

    public Task<int> CountUnreadAsync(Actor actor, CancellationToken cancellationToken) => Task.FromResult(OnCountUnread(actor));

    public Task<int> MarkReadAsync(Actor actor, IReadOnlyCollection<Guid> notificationIds, CancellationToken cancellationToken)
    {
        _markReadCalls.Enqueue((actor, [.. notificationIds]));
        return Task.FromResult(OnMarkRead(actor, notificationIds));
    }

    public Task<int> MarkAllReadAsync(Actor actor, CancellationToken cancellationToken)
    {
        _markAllCalls.Enqueue(actor);
        return Task.FromResult(OnMarkAllRead(actor));
    }
}

internal sealed class FakeItemSubscriptionService : IItemSubscriptionService
{
    private readonly ConcurrentQueue<(Actor Actor, Guid ProjectId, Guid ItemId)> _getCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid ProjectId, Guid ItemId, bool Following)> _setCalls = new();

    public Func<Actor, Guid, Guid, ItemSubscription> OnGet { get; set; } = (_, _, _) => new ItemSubscription(false, null, true);

    public Func<Actor, Guid, Guid, bool, Result<ItemSubscription>> OnSet { get; set; } =
        (_, _, _, following) => new ItemSubscription(following, following ? SubscriptionReason.Manual : null, true);

    public IReadOnlyList<(Actor Actor, Guid ProjectId, Guid ItemId)> GetCalls => _getCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid ProjectId, Guid ItemId, bool Following)> SetCalls => _setCalls.ToArray();

    public Task<ItemSubscription> GetAsync(Actor actor, Guid projectId, Guid itemId, CancellationToken cancellationToken)
    {
        _getCalls.Enqueue((actor, projectId, itemId));
        return Task.FromResult(OnGet(actor, projectId, itemId));
    }

    public Task<Result<ItemSubscription>> SetAsync(Actor actor, Guid projectId, Guid itemId, bool following, CancellationToken cancellationToken)
    {
        _setCalls.Enqueue((actor, projectId, itemId, following));
        return Task.FromResult(OnSet(actor, projectId, itemId, following));
    }
}

/// <summary>測試用的存取查詢。預設任何編號都找得到。</summary>
internal sealed class FakeWorkAccessQueries : IWorkAccessQueries
{
    public static readonly Guid ProjectId = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    public static readonly Guid ItemId = Guid.Parse("00000000-0000-7000-8000-0000000000b1");

    private readonly ConcurrentQueue<(Actor Actor, string ItemKey)> _resolveCalls = new();

    public Func<Actor, string, Result<ItemReference>> OnResolve { get; set; } =
        (_, key) => new ItemReference(ProjectId, key.Split('-')[0], ItemId, key, "範例項目");

    public IReadOnlyList<(Actor Actor, string ItemKey)> ResolveCalls => _resolveCalls.ToArray();

    public Task<Result<ItemReference>> ResolveItemAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        _resolveCalls.Enqueue((actor, itemKey));
        return Task.FromResult(OnResolve(actor, itemKey));
    }

    public Task<IReadOnlySet<Guid>> FilterReadersAsync(
        Guid organizationId, Guid projectId, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(personIds.ToHashSet());

    public Task<IReadOnlySet<Guid>> FilterItemReadersAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(personIds.ToHashSet());

    public Task<IReadOnlySet<Guid>> ReadableItemIdsAsync(Actor actor, IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(itemIds.ToHashSet());

    public Task<IReadOnlySet<Guid>> OwnOnlyProjectIdsAsync(Actor actor, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

    /// <summary>算「看得到哪些專案」之前會等它，測試用來讓查詢停在半路、或丟出例外。</summary>
    public Func<CancellationToken, Task>? BeforeReadable { get; set; }

    public async Task<IReadOnlySet<Guid>> ReadableProjectIdsAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (BeforeReadable is { } before)
        {
            await before(cancellationToken);
        }

        return new HashSet<Guid> { ProjectId };
    }
}
