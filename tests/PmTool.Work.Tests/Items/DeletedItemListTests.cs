using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>已刪除的項目清單：最近刪除的在前、同一刻依編號遞減，游標分頁，只有這個專案的，需要 item.read。</summary>
[TestFixture]
public sealed class DeletedItemListTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListDeletedAsync_OrdersByDeletionTimeDescendingThenNumberDescending()
    {
        // Arrange：1 最早刪，2 與 3 同一刻刪，4 沒刪。
        var scenario = await ProjectAsync();
        var items = new List<WorkItem>();
        foreach (var title in new[] { "一", "二", "三", "四" })
        {
            items.Add(await scenario.CreateTaskAsync("WEB", title));
        }

        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await DeleteAtTheSameInstantAsync(scenario, items[0].Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await DeleteAtTheSameInstantAsync(scenario, items[1].Key, items[2].Key);

        // Act
        var deleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        Assert.That(deleted.Select(i => i.Key), Is.EqualTo(new[] { "WEB-3", "WEB-2", "WEB-1" }));
    }

    [Test]
    public async Task ListDeletedAsync_ItemsWithTheirDeletedByStateAndParent_CarryThem()
    {
        // Arrange：任務停在「進行中」，掛在需求底下；一個人刪了任務，另一個人刪了需求。
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var editor = await scenario.MemberAsync("WEB", "editor");
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", ParentKey = requirement.Key });
        var moved = await _host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True);
        await ItemRecovery.DeleteAsync(_host, editor, task.Key);
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, requirement.Key);
        var deletedAt = _host.Clock.GetUtcNow();

        // Act
        var deleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        var taskRow = deleted.Single(i => i.Key == task.Key);
        var requirementRow = deleted.Single(i => i.Key == requirement.Key);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((taskRow.ProjectKey, taskRow.Number, taskRow.Type, taskRow.Title, taskRow.State, taskRow.StateCategory),
                Is.EqualTo(("WEB", 2L, "task", "任務", "doing", StateCategory.InProgress)));
            Assert.That((taskRow.ParentKey, taskRow.DeletedBy), Is.EqualTo(("WEB-1", (Guid?)editor.PersonId)));
            Assert.That((requirementRow.ParentKey, requirementRow.DeletedBy, requirementRow.DeletedAt),
                Is.EqualTo(((string?)null, (Guid?)scenario.Admin.PersonId, deletedAt)));
        }
    }

    [Test]
    public async Task ListDeletedAsync_Paged_ContinuesFromTheCursorAndEndsWithoutOne()
    {
        // Arrange：五個依序刪除。
        var scenario = await ProjectAsync();
        for (var i = 1; i <= 5; i++)
        {
            var item = await scenario.CreateTaskAsync("WEB", $"任務 {i}");
            await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        }

        // Act
        var first = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Limit = 2 }, CancellationToken.None);
        var second = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Limit = 2, Cursor = first.Value.NextCursor }, CancellationToken.None);
        var third = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Limit = 2, Cursor = second.Value.NextCursor }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-5", "WEB-4" }));
            Assert.That(second.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-3", "WEB-2" }));
            Assert.That(third.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(new[] { first.Value.NextCursor, second.Value.NextCursor }, Has.None.Null);
            Assert.That(third.Value.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task ListDeletedAsync_ExactlyOnePageOfItems_HasNoNextCursor()
    {
        // Arrange
        var scenario = await ProjectAsync();
        for (var i = 0; i < 2; i++)
        {
            await ItemRecovery.DeleteAsync(_host, scenario.Admin, (await scenario.CreateTaskAsync("WEB")).Key);
        }

        // Act
        var page = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Limit = 2 }, CancellationToken.None);

        // Assert
        Assert.That((page.Value.Items.Count, page.Value.NextCursor), Is.EqualTo((2, (string?)null)));
    }

    [TestCase(0)]
    [TestCase(201)]
    [TestCase(-1)]
    public async Task ListDeletedAsync_LimitOutOfRange_IsQueryInvalidForLimit(int limit)
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Limit = limit }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "limit");
    }

    [TestCase("not-a-cursor!")]
    [TestCase("")]
    public async Task ListDeletedAsync_BadCursor_IsQueryInvalidForCursor(string cursor)
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Items.ListDeletedAsync(scenario.Admin, "WEB", new DeletedItemQuery { Cursor = cursor }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "cursor");
    }

    [Test]
    public async Task ListDeletedAsync_OnlyThisProjectsItems_AndRestoredOnesDropOut()
    {
        // Arrange：兩個專案各刪一個；這個專案另外刪了又復原一個。
        var scenario = await ProjectAsync();
        await scenario.CreateProjectAsync("OTH");
        var here = await scenario.CreateTaskAsync("WEB", "這裡");
        var other = await scenario.CreateTaskAsync("OTH", "別處");
        var back = await scenario.CreateTaskAsync("WEB", "復原了");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, here.Key, other.Key, back.Key);
        await ItemRecovery.RestoreAsync(_host, scenario.Admin, back.Key);

        // Act
        var deleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "web");

        // Assert
        Assert.That(deleted.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
    }

    [Test]
    public async Task ListDeletedAsync_ViewerWithItemRead_CanSeeButAProjectOutsiderCannot()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, (await scenario.CreateTaskAsync("WEB")).Key);

        // Act
        var asViewer = await _host.Items.ListDeletedAsync(viewer, "WEB", new DeletedItemQuery(), CancellationToken.None);
        var asOutsider = await _host.Items.ListDeletedAsync(scenario.Person(), "WEB", new DeletedItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(asViewer.Value.Items, Has.Count.EqualTo(1));
        ItemAssert.Failed(asOutsider, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task ListDeletedAsync_ProjectThatDoesNotExist_IsProjectNotFound()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Items.ListDeletedAsync(scenario.Admin, "NOPE", new DeletedItemQuery(), CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    // 同一個交易時刻刪的幾個：時鐘不動，連續刪除。
    private async Task DeleteAtTheSameInstantAsync(Scenario scenario, params string[] keys)
    {
        foreach (var key in keys)
        {
            var result = await _host.Items.DeleteAsync(scenario.Admin, key, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }
    }

    private async Task<Scenario> ProjectAsync(string template = Scenario.SimpleTasks)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        return scenario;
    }
}
