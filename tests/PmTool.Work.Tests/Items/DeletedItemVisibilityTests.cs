using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 刪除的項目在每個地方都不存在：讀取、修改、改狀態、改類型、活動與留言都是 NotFound，
/// 清單（含下層與搜尋）、「我的工作」不列，也不能被設成上層。
/// </summary>
[TestFixture]
public sealed class DeletedItemVisibilityTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task EveryOperationOnTheItem_AfterItIsDeleted_IsNotFound()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "刪掉的需求" });
        await DeleteAsync(scenario, item.Key);
        var items = _host.Items;

        // Act
        Failure?[] failures =
        [
            (await items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Failure,
            (await items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改標題" }, CancellationToken.None)).Failure,
            (await items.TransitionAsync(scenario.Admin, item.Key, "quoting", CancellationToken.None)).Failure,
            (await items.ChangeTypeAsync(scenario.Admin, item.Key, "change_request", CancellationToken.None)).Failure,
            (await _host.Activity.ListAsync(scenario.Admin, item.Key, new ActivityQuery(), CancellationToken.None)).Failure,
            (await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "還在嗎？", null, CancellationToken.None)).Failure,
        ];

        // Assert
        Assert.That(
            failures.Select(f => (f?.Kind, f?.Code)),
            Has.All.EqualTo(((FailureKind?)FailureKind.NotFound, WorkErrorCodes.ItemNotFound)));
    }

    [Test]
    public async Task ListAsync_DeletedItem_IsLeftOutOfTheListTheChildrenAndTheSearch()
    {
        // Arrange：WEB-1 是需求，WEB-2、WEB-3 掛在它底下；刪掉 WEB-2。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "首頁改版" });
        var deleted = await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await scenario.CreateItemAsync("WEB", Task("首頁的文案", requirement.Key));
        await DeleteAsync(scenario, deleted.Key);

        // Act
        var all = await ListAsync(scenario, new WorkItemQuery());
        var children = await ListAsync(scenario, new WorkItemQuery { ParentKey = requirement.Key });
        var byTitle = await ListAsync(scenario, new WorkItemQuery { Search = "版面" });
        var byKey = await ListAsync(scenario, new WorkItemQuery { Search = deleted.Key });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all, Is.EqualTo(new[] { "WEB-1", "WEB-3" }));
            Assert.That(children, Is.EqualTo(new[] { "WEB-3" }));
            Assert.That(byTitle, Is.Empty);
            Assert.That(byKey, Is.Empty);
        }
    }

    [Test]
    public async Task ListAsync_ChildrenOfADeletedParent_IsEmpty()
    {
        // Arrange：下層先刪、上層再刪。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "首頁改版" });
        var child = await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await DeleteAsync(scenario, child.Key);
        await DeleteAsync(scenario, requirement.Key);

        // Act
        var children = await ListAsync(scenario, new WorkItemQuery { ParentKey = requirement.Key });

        // Assert
        Assert.That(children, Is.Empty);
    }

    [Test]
    public async Task ListAssignedToMeAsync_DeletedItem_IsLeftOut()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var me = await scenario.MemberAsync("WEB", "editor");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "留著的", AssigneeId = me.PersonId });
        var deleted = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "刪掉的", AssigneeId = me.PersonId });
        await DeleteAsync(scenario, deleted.Key);

        // Act
        var mine = await _host.Items.ListAssignedToMeAsync(me, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
    }

    [Test]
    public async Task CreateAndUpdate_ParentIsADeletedItem_IsUnknownParent()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var deleted = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "刪掉的需求" });
        var task = await scenario.CreateItemAsync("WEB", Task("沒有上層的任務", null));
        await DeleteAsync(scenario, deleted.Key);

        // Act
        var created = await _host.Items.CreateAsync(scenario.Admin, "WEB", Task("想掛上去", deleted.Key), CancellationToken.None);
        var updated = await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = deleted.Key }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(created, WorkErrorCodes.ItemUnknownParent, "parent");
        ItemAssert.Invalid(updated, WorkErrorCodes.ItemUnknownParent, "parent");
    }

    private async Task<string[]> ListAsync(Scenario scenario, WorkItemQuery query)
    {
        var page = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(page.Succeeded, Is.True, () => page.Failure!.Message);
        return [.. page.Value.Items.Select(i => i.Key)];
    }

    private async Task DeleteAsync(Scenario scenario, string itemKey)
    {
        var result = await _host.Items.DeleteAsync(scenario.Admin, itemKey, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Task(string title, string? parentKey) => new() { Type = "task", Title = title, ParentKey = parentKey };
}
