using PmTool.Work;

namespace PmTool.Api.Live;

/// <summary>要送給一個群組的一則 <c>items</c> 訊息。</summary>
/// <param name="ActorId">這一則裡所有的事件都是同一個人做的就是他，否則 null（網頁用它略過自己剛做的修改）。</param>
public sealed record LiveItemsPush(string Group, string ProjectKey, IReadOnlyList<string>? ItemKeys, Guid? ActorId);

/// <summary>
/// 把一輪讀到的事件分給群組。純邏輯：哪些群組收得到，只看事件裡就有的資料（專案、建立者、負責人、前一個負責人），不查資料庫。
/// 同一輪同一個群組的事件併成一則、編號不重複；超過 <see cref="MaxItemKeys"/> 個編號就不列了（null，整個專案重抓）。
/// </summary>
public static class LiveEventRouter
{
    /// <summary>一則訊息最多列幾個項目編號。歷史匯入一次會有幾千個事件，不能把它們全部塞進訊息。</summary>
    public const int MaxItemKeys = 100;

    public static IReadOnlyList<LiveItemsPush> Route(IEnumerable<WorkEvent> events)
    {
        Dictionary<(string Group, string ProjectKey), Pending> pending = [];
        foreach (var workEvent in events.Where(e => e.Kind != WorkEventKind.Unknown))
        {
            var item = workEvent.Item;
            foreach (var group in GroupsOf(item))
            {
                if (!pending.TryGetValue((group, item.ProjectKey), out var entry))
                {
                    pending[(group, item.ProjectKey)] = entry = new Pending();
                }

                entry.Add(item.ItemKey, workEvent.ActorPersonId);
            }
        }

        return [.. pending.Select(pair => new LiveItemsPush(pair.Key.Group, pair.Key.ProjectKey, pair.Value.Keys, pair.Value.ActorId))];
    }

    private static IEnumerable<string> GroupsOf(ItemEventData item)
    {
        yield return LiveGroups.Project(item.ProjectId);
        foreach (var person in new[] { item.CreatedBy, item.AssigneeId, item.PreviousAssigneeId }.OfType<Guid>().Distinct())
        {
            yield return LiveGroups.Own(item.ProjectId, person);
        }
    }

    private sealed class Pending
    {
        private readonly List<string> _keys = [];
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private bool _overflowed;
        private bool _hasActor;
        private bool _mixedActors;
        private Guid _actor;

        public IReadOnlyList<string>? Keys => _overflowed ? null : _keys;

        public Guid? ActorId => _hasActor && !_mixedActors ? _actor : null;

        public void Add(string itemKey, Guid actorId)
        {
            if (!_hasActor)
            {
                (_hasActor, _actor) = (true, actorId);
            }
            else if (_actor != actorId)
            {
                _mixedActors = true;
            }

            if (_overflowed || !_seen.Add(itemKey))
            {
                return;
            }

            if (_keys.Count >= MaxItemKeys)
            {
                _overflowed = true;
                _keys.Clear();
                _seen.Clear();
                return;
            }

            _keys.Add(itemKey);
        }
    }
}
