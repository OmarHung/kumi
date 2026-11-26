using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>測試用的專案總覽服務：記住每次呼叫的參數，回傳值由測試設定。</summary>
internal sealed class FakeProjectOverviewService : IProjectOverviewService
{
    private readonly ConcurrentQueue<(Actor Actor, ProjectOverviewScope Scope)> _calls = new();

    public Func<IReadOnlyList<ProjectOverview>> OnList { get; set; } = () => [];

    public IReadOnlyList<(Actor Actor, ProjectOverviewScope Scope)> Calls => _calls.ToArray();

    public Task<IReadOnlyList<ProjectOverview>> ListAsync(Actor actor, ProjectOverviewScope scope, CancellationToken cancellationToken)
    {
        _calls.Enqueue((actor, scope));
        return Task.FromResult(OnList());
    }
}
