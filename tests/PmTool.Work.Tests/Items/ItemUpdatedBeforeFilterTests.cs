using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>項目清單的「最後更新早於」條件（總覽的「停滯」連到清單用）：專案的清單與跨專案的清單同一套。</summary>
[TestFixture]
public sealed class ItemUpdatedBeforeFilterTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 4, 0, 0, TimeSpan.Zero);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_UpdatedBefore_IsStrictlyEarlierAndComposesWithTheStateCategory()
    {
        // Arrange
        var (scenario, finished) = await ScenarioWithOldAndNewItemsAsync();

        // Act
        var before = await ListAsync(scenario.Admin, new WorkItemQuery { UpdatedBefore = Start.AddDays(5) });
        var onTheLine = await ListAsync(scenario.Admin, new WorkItemQuery { UpdatedBefore = Start });
        var openOnly = await ListAsync(
            scenario.Admin,
            new WorkItemQuery { UpdatedBefore = Start.AddDays(5), StateCategories = [PmTool.Blueprint.Model.StateCategory.NotStarted, PmTool.Blueprint.Model.StateCategory.InProgress] });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.Select(i => i.Title), Is.EquivalentTo(new[] { "舊的", finished.Title }));
            Assert.That(onTheLine, Is.Empty, "剛好等於的不算更早");
            Assert.That(openOnly.Select(i => i.Title), Is.EqualTo(new[] { "舊的" }));
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_UpdatedBefore_FiltersTheSameWay()
    {
        // Arrange
        var (scenario, _) = await ScenarioWithOldAndNewItemsAsync();

        // Act
        var page = await _host.Items.ListAcrossProjectsAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { UpdatedBefore = Start.AddDays(5), StateCategories = [PmTool.Blueprint.Model.StateCategory.NotStarted] }, CancellationToken.None);

        // Assert
        Assert.That(page.Value.Items.Select(i => i.Title), Is.EqualTo(new[] { "舊的" }));
    }

    private async Task<IReadOnlyList<WorkItem>> ListAsync(Actor actor, WorkItemQuery query)
    {
        var page = await _host.Items.ListAsync(actor, "WEB", query, CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, () => page.Failure!.Message);
        return page.Value.Items;
    }

    private async Task<(Scenario Scenario, WorkItem Finished)> ScenarioWithOldAndNewItemsAsync()
    {
        _host.Clock.Advance(Start - _host.Clock.GetUtcNow());
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "舊的");
        var finished = await scenario.CreateTaskAsync("WEB", "舊的完成");
        var moved = await _host.Items.TransitionAsync(scenario.Admin, finished.Key, "done", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        _host.Clock.Advance(TimeSpan.FromDays(10));
        await scenario.CreateTaskAsync("WEB", "新的");
        return (scenario, finished);
    }
}
