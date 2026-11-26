using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using static PmTool.Work.Tests.Overview.OverviewSupport;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Overview;

/// <summary>總覽的查詢次數不隨專案的個數增加（沒有 N+1），藍圖的快取是空的也一樣。</summary>
[TestFixture]
public sealed class ProjectOverviewQueryCountTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_OrganizationAdmin_UsesTheSameNumberOfQueriesForTwoAndTwentyProjects()
    {
        // Arrange
        SetNow(_host, Noon);
        var few = await ScenarioWithProjectsAsync(2);
        var many = await ScenarioWithProjectsAsync(20);

        // Act
        var forFew = await CountQueriesAsync(few.Admin);
        var forMany = await CountQueriesAsync(many.Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forMany, Is.EqualTo(forFew));
            Assert.That(forMany, Is.LessThanOrEqualTo(8));
        }
    }

    [Test]
    public async Task ListAsync_OwnItemsOnlyMember_UsesTheSameNumberOfQueriesForTwoAndTwentyProjects()
    {
        // Arrange
        SetNow(_host, Noon);
        var few = await ScenarioWithProjectsAsync(2);
        var many = await ScenarioWithProjectsAsync(20);
        var fewMember = few.Person();
        var manyMember = many.Person();
        foreach (var (scenario, member, count) in new[] { (few, fewMember, 2), (many, manyMember, 20) })
        {
            for (var n = 0; n < count; n++)
            {
                await AddAsync(scenario, $"Q{n:00}", member, "contributor");
            }
        }

        // Act
        var forFew = await CountQueriesAsync(fewMember);
        var forMany = await CountQueriesAsync(manyMember);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(forMany, Is.EqualTo(forFew));
            Assert.That(forMany, Is.LessThanOrEqualTo(8));
        }
    }

    private async Task<Scenario> ScenarioWithProjectsAsync(int projects)
    {
        var scenario = new Scenario(_host);
        for (var n = 0; n < projects; n++)
        {
            var key = $"Q{n:00}";
            await scenario.CreateProjectAsync(key);
            var cycle = await CreateCycleAsync(_host, scenario.Admin, key, new CycleDraft("這一期", Today.AddDays(-2), Today.AddDays(2)));
            var milestone = await CreateMilestoneAsync(_host, scenario.Admin, key, new MilestoneDraft("上線", Today.AddDays(5), null));
            await scenario.CreateItemAsync(
                key, new NewWorkItem { Type = "task", Title = "一項", DueDate = Today, CycleId = cycle.Id, MilestoneId = milestone.Id });
        }

        return scenario;
    }

    // 每次用新的服務容器：藍圖的快取是空的，連讀藍圖的查詢也算進去。
    private async Task<int> CountQueriesAsync(PmTool.Abstractions.Actor actor)
    {
        using var sql = new SqlLog();
        await using var provider = WorkTestHost.BuildProvider(
            _host.ConnectionString, _host.Clock, services => services.AddLogging(logging => logging.AddProvider(sql)));
        await using var scope = provider.CreateAsyncScope();
        var overview = scope.ServiceProvider.GetRequiredService<IProjectOverviewService>();

        var listed = await overview.ListAsync(actor, ProjectOverviewScope.Visible, CancellationToken.None);

        Assert.That(listed, Is.Not.Empty);
        return sql.Commands.Count(c => c.Contains("SELECT", StringComparison.Ordinal));
    }
}
