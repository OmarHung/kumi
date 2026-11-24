namespace PmTool.Work.Tests.Items;

/// <summary>上層項目：同一個專案、類型符合藍圖的 parents、不會繞圈。用 agency_project：任務可以掛在需求或需求變更單底下。</summary>
[TestFixture]
public sealed class ItemHierarchyTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_ParentOfAnAllowedType_LinksTheItems()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var task = await scenario.CreateItemAsync("WEB", Task(requirement.Key));
        var readBack = await _host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        Assert.That(task.ParentKey, Is.EqualTo("WEB-1"));
        Assert.That(readBack.Value.ParentKey, Is.EqualTo("WEB-1"));
    }

    [TestCase("WEB-99")]
    [TestCase("not-a-key")]
    public async Task CreateAsync_ParentThatDoesNotExist_IsUnknownParent(string parentKey)
    {
        // Arrange
        var scenario = await AgencyAsync();

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", Task(parentKey), CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownParent, "parent");
    }

    [Test]
    public async Task CreateAsync_ParentInAnotherProject_IsUnknownParent()
    {
        // Arrange
        var scenario = await AgencyAsync();
        await scenario.CreateProjectAsync("APP", Scenario.AgencyProject);
        var elsewhere = await scenario.CreateItemAsync("APP", Requirement());

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", Task(elsewhere.Key), CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownParent, "parent");
    }

    [Test]
    public async Task CreateAsync_ParentOfATypeThatIsNotAllowed_IsParentNotAllowed()
    {
        // Arrange：需求只能放在最上層。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", Requirement() with { ParentKey = requirement.Key }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemParentNotAllowed, "parent");
        Assert.That(result.Failure!.Message, Does.Contain("「需求」"));
    }

    [Test]
    public async Task UpdateAsync_ParentIsTheItemItself_IsParentCycle()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var task = await scenario.CreateItemAsync("WEB", Task(null));

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = task.Key }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemParentCycle, "parent");
    }

    [Test]
    public async Task UpdateAsync_ParentIsAChildOfTheItem_IsParentCycle()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement());
        var task = await scenario.CreateItemAsync("WEB", Task(requirement.Key));

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, requirement.Key, new WorkItemPatch { ParentKey = task.Key }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemParentCycle, "parent");
    }

    [Test]
    public async Task UpdateAsync_MoveToAnotherParentAndThenRemoveIt_IsKept()
    {
        // Arrange
        var scenario = await AgencyAsync();
        await scenario.CreateItemAsync("WEB", Requirement());
        var change = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "改需求" });
        var task = await scenario.CreateItemAsync("WEB", Task("WEB-1"));

        // Act
        var moved = await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = change.Key }, CancellationToken.None);
        var removed = await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = null }, CancellationToken.None);

        // Assert
        Assert.That(moved.Value.ParentKey, Is.EqualTo(change.Key));
        Assert.That(removed.Value.ParentKey, Is.Null);
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Requirement() => new() { Type = "requirement", Title = "會員登入" };

    private static NewWorkItem Task(string? parentKey) => new() { Type = "task", Title = "做登入頁", ParentKey = parentKey };
}
