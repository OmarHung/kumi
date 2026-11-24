using PmTool.Work;

namespace PmTool.Notifications.Events;

/// <summary>
/// 一批事件裡「誰現在還看得到這個項目」的答案。看得到項目要看專案的角色，只看自己的角色還要看項目是誰負責、誰建立的
/// （<see cref="ItemOwnership"/>），所以答案依專案加上這兩個人分開存。每一組先用整批會碰到的人問工作引擎一次（<see cref="SeedAsync"/>），
/// 之後各個事件從這裡拿；碰到沒問過的人才補問，所以逐筆事件各問一次的情形不會發生。
/// 只活在一批之內：下一批重新問，被移出專案的人不會留在快取裡。
/// </summary>
internal sealed class ReaderCache(IWorkAccessQueries access)
{
    private readonly Dictionary<(Guid Organization, Guid Project, ItemOwnership Item), Known> _projects = [];

    public async Task SeedAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> people, CancellationToken cancellationToken) =>
        await EnsureAsync(organizationId, projectId, item, people, cancellationToken);

    /// <summary>候選人裡現在看得到這個項目的有誰。</summary>
    public async Task<IReadOnlySet<Guid>> ReadersAmongAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> candidates, CancellationToken cancellationToken)
    {
        var known = await EnsureAsync(organizationId, projectId, item, candidates, cancellationToken);
        return candidates.Where(known.Readers.Contains).ToHashSet();
    }

    private async Task<Known> EnsureAsync(
        Guid organizationId, Guid projectId, ItemOwnership item, IReadOnlyCollection<Guid> people, CancellationToken cancellationToken)
    {
        if (!_projects.TryGetValue((organizationId, projectId, item), out var known))
        {
            known = new Known();
            _projects[(organizationId, projectId, item)] = known;
        }

        Guid[] unasked = [.. people.Where(person => !known.Asked.Contains(person)).Distinct()];
        if (unasked.Length == 0)
        {
            return known;
        }

        // 問成功了才記下來：失敗（例外）時不留半套答案，下一個事件會再問。
        var readers = await access.FilterItemReadersAsync(organizationId, projectId, item, unasked, cancellationToken);
        known.Asked.UnionWith(unasked);
        known.Readers.UnionWith(readers);
        return known;
    }

    private sealed class Known
    {
        public HashSet<Guid> Asked { get; } = [];

        public HashSet<Guid> Readers { get; } = [];
    }
}
