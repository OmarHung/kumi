using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>依自訂欄位排序：各型別的順序、單選依選項順序、空值在兩個方向都排最後、同值依流水號，以及不能排的欄位。</summary>
[TestFixture]
public sealed class ItemFieldSortTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_SortByNumber_IsNumericAndEmptyIsLastInBothDirections()
    {
        // Arrange：10 排在 9 後面（文字排序會倒過來）；流水號 4 沒有值。
        var scenario = await ProjectAsync(("story_points", 10), ("story_points", 9), ("story_points", 9.5), null);

        // Act
        var ascending = await SortedAsync(scenario, "story_points", descending: false);
        var descending = await SortedAsync(scenario, "story_points", descending: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascending, Is.EqualTo(new long[] { 2, 3, 1, 4 }));
            Assert.That(descending, Is.EqualTo(new long[] { 1, 3, 2, 4 }));
        }
    }

    [Test]
    public async Task ListAsync_SortByCurrencyAndDateAndText_FollowTheValues()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await AddAsync(scenario, ("budget", 500), ("review_date", "2026-12-01"), ("summary", "banana"));
        await AddAsync(scenario, ("budget", 80), ("review_date", "2026-01-15"), ("summary", "apple"));
        await AddAsync(scenario);

        // Act
        var budget = await SortedAsync(scenario, "budget", descending: false);
        var date = await SortedAsync(scenario, "review_date", descending: true);
        var text = await SortedAsync(scenario, "summary", descending: false);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(budget, Is.EqualTo(new long[] { 2, 1, 3 }));
            Assert.That(date, Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(text, Is.EqualTo(new long[] { 2, 1, 3 }));
        }
    }

    [Test]
    public async Task ListAsync_SortBySingleSelect_FollowsTheOptionOrderNotTheAlphabet()
    {
        // Arrange：選項的順序是 feature、bug、chore；字母順序是 bug、chore、feature。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await AddAsync(scenario, ("category", "chore"));
        await AddAsync(scenario, ("category", "feature"));
        await AddAsync(scenario);
        await AddAsync(scenario, ("category", "bug"));
        await AddAsync(scenario, ("category", "feature"));

        // Act
        var ascending = await SortedAsync(scenario, "category", descending: false);
        var descending = await SortedAsync(scenario, "category", descending: true);

        // Assert：同值依流水號；沒有值的在兩個方向都排最後。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascending, Is.EqualTo(new long[] { 2, 5, 4, 1, 3 }));
            Assert.That(descending, Is.EqualTo(new long[] { 1, 4, 2, 5, 3 }));
        }
    }

    [Test]
    public async Task ListAsync_SortByCheckbox_UncheckedFirst()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await AddAsync(scenario, ("blocked", true));
        await AddAsync(scenario);
        await AddAsync(scenario, ("blocked", true));

        // Act
        var ascending = await SortedAsync(scenario, "blocked", descending: false);
        var descending = await SortedAsync(scenario, "blocked", descending: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ascending, Is.EqualTo(new long[] { 2, 1, 3 }));
            Assert.That(descending, Is.EqualTo(new long[] { 1, 3, 2 }));
        }
    }

    [Test]
    public async Task ListAsync_SortByField_PagesAreStableAcrossCursors()
    {
        // Arrange
        var scenario = await ProjectAsync(("story_points", 3), ("story_points", 3), ("story_points", 1), ("story_points", 3), ("story_points", 2));
        var query = new WorkItemQuery { SortBy = WorkItemSortField.Field, SortFieldKey = "story_points", Limit = 2 };

        // Act
        var first = (await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None)).Value;
        var second = (await _host.Items.ListAsync(scenario.Admin, "WEB", query with { Cursor = first.NextCursor }, CancellationToken.None)).Value;
        var third = (await _host.Items.ListAsync(scenario.Admin, "WEB", query with { Cursor = second.NextCursor }, CancellationToken.None)).Value;

        // Assert
        Assert.That(
            first.Items.Concat(second.Items).Concat(third.Items).Select(i => i.Number),
            Is.EqualTo(new long[] { 3, 5, 1, 2, 4 }));
    }

    [TestCase("reviewer", "不能排序")]
    [TestCase("tags", "不能排序")]
    [TestCase("notes", "不能排序")]
    [TestCase("reference_url", "不能排序")]
    [TestCase("nope", "沒有")]
    public async Task ListAsync_SortByAFieldThatCannotBeSortedOrDoesNotExist_IsQueryInvalidAtSort(string key, string reason)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { SortBy = WorkItemSortField.Field, SortFieldKey = key }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Single().Path, Is.EqualTo("sort"));
        Assert.That(result.Failure.Details.Single().Message, Does.Contain(reason));
    }

    [Test]
    public async Task ListAsync_FieldSortWithoutAKey_OrAKeyWithoutFieldSort_IsQueryInvalidAtSort()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var missing = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { SortBy = WorkItemSortField.Field }, CancellationToken.None);
        var stray = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { SortFieldKey = "category" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Failure!.Details.Single().Path, Is.EqualTo("sort"));
            Assert.That(stray.Failure!.Details.Single().Path, Is.EqualTo("sort"));
        }
    }

    private async Task<Scenario> ProjectAsync(params (string Key, object Value)?[] points)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var point in points)
        {
            await AddAsync(scenario, point is { } p ? [(p.Key, (object?)p.Value)] : []);
        }

        return scenario;
    }

    private static async Task<WorkItem> AddAsync(Scenario scenario, params (string Key, object? Value)[] fields) =>
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = Scenario.Fields(fields) });

    private async Task<long[]> SortedAsync(Scenario scenario, string key, bool descending)
    {
        var query = new WorkItemQuery { SortBy = WorkItemSortField.Field, SortFieldKey = key, Descending = descending };
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return [.. result.Value.Items.Select(i => i.Number)];
    }
}
