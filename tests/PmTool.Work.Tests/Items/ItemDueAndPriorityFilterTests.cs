namespace PmTool.Work.Tests.Items;

/// <summary>項目清單的優先度與到期日篩選：每種條件、組合、邊界與不合法的組合。</summary>
[TestFixture]
public sealed class ItemDueAndPriorityFilterTests
{
    private static readonly DateOnly Day = new(2026, 11, 10);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_Priorities_AreOrWithinTheListAndNoneMeansUnset()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var high = await ListAsync(scenario, new WorkItemQuery { Priorities = [Priority.High] });
        var highOrUrgent = await ListAsync(scenario, new WorkItemQuery { Priorities = [Priority.High, Priority.Urgent] });
        var none = await ListAsync(scenario, new WorkItemQuery { Priorities = [Priority.None] });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(high), Is.EqualTo(new long[] { 2 }));
            Assert.That(Numbers(highOrUrgent), Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(Numbers(none), Is.EqualTo(new long[] { 5 }));
        }
    }

    [Test]
    public async Task ListAsync_DueRange_IncludesBothEndsAndLeavesOutItemsWithoutADueDate()
    {
        // Arrange：到期日是 -1、0、+1、+7 天，另有兩個沒有到期日。
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var from = await ListAsync(scenario, new WorkItemQuery { DueFrom = Day });
        var to = await ListAsync(scenario, new WorkItemQuery { DueTo = Day });
        var both = await ListAsync(scenario, new WorkItemQuery { DueFrom = Day, DueTo = Day });
        var week = await ListAsync(scenario, new WorkItemQuery { DueFrom = Day.AddDays(1), DueTo = Day.AddDays(7) });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(from), Is.EqualTo(new long[] { 2, 3, 4 }));
            Assert.That(Numbers(to), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(Numbers(both), Is.EqualTo(new long[] { 2 }));
            Assert.That(Numbers(week), Is.EqualTo(new long[] { 3, 4 }));
        }
    }

    [Test]
    public async Task ListAsync_DueFromAfterDueTo_IsEmptyNotAnError()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var result = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { DueFrom = Day.AddDays(3), DueTo = Day }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.Value.Items, Is.Empty);
    }

    [Test]
    public async Task ListAsync_OnlyWithoutDueDate_ListsTheItemsWithNoDueDate()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { OnlyWithoutDueDate = true });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 5, 6 }));
    }

    [Test]
    public async Task ListAsync_PriorityAndDueTogether_AreAnd()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act：到期日在今天之後而且是緊急。
        var page = await ListAsync(scenario, new WorkItemQuery { DueFrom = Day, Priorities = [Priority.Urgent] });
        var noDuePriority = await ListAsync(scenario, new WorkItemQuery { OnlyWithoutDueDate = true, Priorities = [Priority.None] });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 3 }));
        Assert.That(Numbers(noDuePriority), Is.EqualTo(new long[] { 5 }));
    }

    [Test]
    public async Task ListAsync_DueFilterNextToAssigneeFilter_BothApply()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { DueTo = Day, OnlyUnassigned = true });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public async Task ListAsync_OnlyWithoutDueDateTogetherWithARange_IsQueryInvalid([Values] bool useFrom)
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();
        var query = useFrom
            ? new WorkItemQuery { OnlyWithoutDueDate = true, DueFrom = Day }
            : new WorkItemQuery { OnlyWithoutDueDate = true, DueTo = Day };

        // Act
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "noDue");
    }

    [Test]
    public async Task ListAsync_UnknownOrMissingPriorities_IsQueryInvalid()
    {
        // Arrange
        var scenario = await ScenarioWithItemsAsync();

        // Act
        var unknown = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { Priorities = [(Priority)99] }, CancellationToken.None);
        var missing = await _host.Items.ListAsync(
            scenario.Admin, "WEB", new WorkItemQuery { Priorities = null! }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(unknown, WorkErrorCodes.QueryInvalid, "priority");
        ItemAssert.Invalid(missing, WorkErrorCodes.QueryInvalid, "priority");
    }

    /// <summary>
    /// WEB-1 昨天到期、低；WEB-2 今天到期、高；WEB-3 明天到期、緊急；WEB-4 七天後到期、中；
    /// WEB-5 沒有到期日、沒有優先度；WEB-6 沒有到期日、低。WEB-3 之外都沒有負責人（WEB-3 給別人）。
    /// </summary>
    private async Task<Scenario> ScenarioWithItemsAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var other = await scenario.MemberAsync("WEB", "editor");
        await Create(scenario, Priority.Low, Day.AddDays(-1), null);
        await Create(scenario, Priority.High, Day, null);
        await Create(scenario, Priority.Urgent, Day.AddDays(1), other.PersonId);
        await Create(scenario, Priority.Medium, Day.AddDays(7), other.PersonId);
        await Create(scenario, Priority.None, null, null);
        await Create(scenario, Priority.Low, null, other.PersonId);
        return scenario;
    }

    private static Task<WorkItem> Create(Scenario scenario, Priority priority, DateOnly? due, Guid? assignee) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Priority = priority,
            DueDate = due,
            AssigneeId = assignee,
        });

    private async Task<WorkItemPage> ListAsync(Scenario scenario, WorkItemQuery query)
    {
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private static long[] Numbers(WorkItemPage page) => [.. page.Items.Select(i => i.Number)];
}
