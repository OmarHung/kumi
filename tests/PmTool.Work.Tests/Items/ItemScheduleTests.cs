namespace PmTool.Work.Tests.Items;

/// <summary>開始日與到期日：到期日不能早於開始日。修改時拿合併之後的值比，只有其中一個有值時不比。</summary>
[TestFixture]
public sealed class ItemScheduleTests
{
    private static readonly DateOnly Early = new(2026, 11, 10);
    private static readonly DateOnly Late = new(2026, 11, 20);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_StartAfterDue_IsInvalidOnTheDueDate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = new NewWorkItem { Type = "task", Title = "任務", StartDate = Late, DueDate = Early };

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "due_date");
        Assert.That(result.Failure!.Message, Does.Contain("到期日不能早於開始日"));
    }

    [Test]
    public async Task CreateAsync_StartOnTheDueDate_IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", StartDate = Late, DueDate = Late });

        // Assert
        Assert.That((created.StartDate, created.DueDate), Is.EqualTo(((DateOnly?)Late, (DateOnly?)Late)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task CreateAsync_OnlyOneOfTheDates_IsNotCompared(bool onlyStart)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = onlyStart
            ? new NewWorkItem { Type = "task", Title = "任務", StartDate = Late }
            : new NewWorkItem { Type = "task", Title = "任務", DueDate = Early };

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task UpdateAsync_DueBeforeTheStoredStart_IsInvalidAndNothingChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", StartDate = Late });

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, new WorkItemPatch { DueDate = Early }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "due_date");
        Assert.That(readBack.Value.DueDate, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_StartAfterTheStoredDue_IsInvalidOnTheDueDate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", DueDate = Early });

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, new WorkItemPatch { StartDate = Late }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "due_date");
    }

    [Test]
    public async Task UpdateAsync_MovingBothDatesLater_ComparesTheNewValues()
    {
        // Arrange：新的開始日晚於舊的到期日，但新的兩個日期本身是對的。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", StartDate = Early, DueDate = Early });
        var patch = new WorkItemPatch { StartDate = Late, DueDate = new DateOnly(2026, 11, 25) };

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.StartDate, result.Value.DueDate), Is.EqualTo(((DateOnly?)Late, (DateOnly?)new DateOnly(2026, 11, 25))));
    }

    [Test]
    public async Task UpdateAsync_ClearingTheStartWhileMovingTheDueEarlier_IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", StartDate = Late, DueDate = Late });

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, original.Key, new WorkItemPatch { StartDate = null, DueDate = Early }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.StartDate, result.Value.DueDate), Is.EqualTo(((DateOnly?)null, (DateOnly?)Early)));
    }
}
