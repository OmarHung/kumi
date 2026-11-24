namespace PmTool.Work.Tests.Items;

/// <summary>項目清單的日期期間篩選（時間軸用）：期間和範圍有重疊的才符合。</summary>
[TestFixture]
public sealed class ItemDatesRangeFilterTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_DatesRange_MatchesEveryKindOfOverlap()
    {
        // Arrange：範圍是 Day+0 ～ Day+10。
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { DatesFrom = Day, DatesTo = Day.AddDays(10) });

        // Assert：2 只有開始日在內、3 只有到期日在內、4 跨過整個範圍、5 部分重疊、6 只有一天在邊上。
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 2, 3, 4, 5, 6 }));
    }

    [Test]
    public async Task ListAsync_DatesRange_LeavesOutItemsEntirelyOutsideAndWithoutDates()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { DatesFrom = Day, DatesTo = Day.AddDays(10) });

        // Assert：1 完全在範圍之前、7 完全在之後、8 沒有任何日期。
        Assert.That(Numbers(page), Does.Not.Contain(1L).And.Not.Contain(7L).And.Not.Contain(8L));
    }

    [Test]
    public async Task ListAsync_DatesFromOnly_HasNoUpperBoundAndExcludesItemsWithoutDates()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { DatesFrom = Day.AddDays(11) });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 4, 7 }));
    }

    [Test]
    public async Task ListAsync_DatesToOnly_HasNoLowerBound()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { DatesTo = Day.AddDays(-5) });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 1 }));
    }

    [Test]
    public async Task ListAsync_DatesRangeOfOneDay_IncludesItemsTouchingThatDay()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act：Day+10 剛好是 WEB-5 的到期日、WEB-6 的唯一日期，也在 WEB-4 的期間內。
        var page = await ListAsync(scenario, new WorkItemQuery { DatesFrom = Day.AddDays(10), DatesTo = Day.AddDays(10) });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 4, 5, 6 }));
    }

    [Test]
    public async Task ListAsync_DatesRangeNextToOtherFilters_BothApply()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery
        {
            DatesFrom = Day,
            DatesTo = Day.AddDays(10),
            Priorities = [Priority.High],
        });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 4 }));
    }

    [Test]
    public async Task ListAsync_DatesRangeTogetherWithOnlyWithoutDueDate_IsQueryInvalid([Values] bool useFrom)
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();
        var query = useFrom
            ? new WorkItemQuery { OnlyWithoutDueDate = true, DatesFrom = Day }
            : new WorkItemQuery { OnlyWithoutDueDate = true, DatesTo = Day };

        // Act
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "noDue");
    }

    /// <summary>
    /// 範圍是 Day ～ Day+10。WEB-1 前 20 到前 15 天；WEB-2 Day+2 開始（無到期）；WEB-3 只有到期 Day+5；
    /// WEB-4 前 3 到 Day+20（高）；WEB-5 Day+8 到 Day+10；WEB-6 只有開始 Day+10；WEB-7 Day+11 到 Day+12；
    /// WEB-8 沒有日期。
    /// </summary>
    private async Task<Scenario> ScenarioWithItemsAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario, Priority.Low, Day.AddDays(-20), Day.AddDays(-15));
        await Create(scenario, Priority.Low, Day.AddDays(2), null);
        await Create(scenario, Priority.Low, null, Day.AddDays(5));
        await Create(scenario, Priority.High, Day.AddDays(-3), Day.AddDays(20));
        await Create(scenario, Priority.Low, Day.AddDays(8), Day.AddDays(10));
        await Create(scenario, Priority.Low, Day.AddDays(10), null);
        await Create(scenario, Priority.Low, Day.AddDays(11), Day.AddDays(12));
        await Create(scenario, Priority.Low, null, null);
        return scenario;
    }

    private static Task<WorkItem> Create(Scenario scenario, Priority priority, DateOnly? start, DateOnly? due) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Priority = priority,
            StartDate = start,
            DueDate = due,
        });

    private async Task<WorkItemPage> ListAsync(Scenario scenario, WorkItemQuery query)
    {
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private static long[] Numbers(WorkItemPage page) => [.. page.Items.Select(i => i.Number)];
}
