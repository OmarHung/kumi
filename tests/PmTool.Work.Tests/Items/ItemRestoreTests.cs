using PmTool.Abstractions;
using PmTool.Work.Events;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 復原刪除的項目：回到清單、我的工作與讀取；重送不出錯；上層也刪除時擋下；只有能刪除的人能復原。
/// 用 agency_project：任務可以掛在需求底下。
/// </summary>
[TestFixture]
public sealed class ItemRestoreTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RestoreAsync_DeletedItem_IsReadableListedAndInMyWorkAgainWithItsOwnData()
    {
        // Arrange：指派給管理員、有說明的任務，刪除之後過了一陣子才復原。
        var scenario = await SimpleAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "寫報告",
            Description = "第三章",
            AssigneeId = scenario.Admin.PersonId,
            Priority = Priority.High,
        });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        _host.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var read = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        var mine = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        var stillDeleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.UpdatedAt, Is.EqualTo(_host.Clock.GetUtcNow()));
            ItemAssert.Same(read.Value, restored);
            Assert.That((restored.Key, restored.Title, restored.Description, restored.Priority, restored.CreatedAt),
                Is.EqualTo((item.Key, "寫報告", "第三章", Priority.High, item.CreatedAt)));
            Assert.That(listed.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(stillDeleted, Is.Empty);
        }
    }

    [Test]
    public async Task RestoreAsync_ItemThatIsNotDeleted_ReturnsItWithoutAnActivityOrAnEvent()
    {
        // Arrange：刪除之後復原兩次（重送）。
        var scenario = await SimpleAsync();
        var item = await scenario.CreateTaskAsync("WEB", "重送");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        var first = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var again = await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
        var events = await EventFeed.OfAsync(_host, scenario);

        // Assert
        ItemAssert.Same(again, first);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Deleted, ActivityKind.Restored }));
            Assert.That(events.Select(e => e.Kind), Is.EqualTo(new[] { WorkEventKind.ItemCreated, WorkEventKind.ItemDeleted, WorkEventKind.ItemRestored }));
        }
    }

    [Test]
    public async Task RestoreAsync_NeverDeletedItem_IsANoOp()
    {
        // Arrange
        var scenario = await SimpleAsync();
        var item = await scenario.CreateTaskAsync("WEB", "沒刪過");

        // Act
        var result = await _host.Items.RestoreAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Same(result.Value, item);
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task RestoreAsync_WhenTheBlueprintHasNotChanged_RecordsARestoredActivityWithoutChangesAndAnEvent()
    {
        // Arrange
        var scenario = await SimpleAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var assignee = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "照舊", AssigneeId = assignee.PersonId });
        await ItemRecovery.DeleteAsync(_host, editor, item.Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        await ItemRecovery.RestoreAsync(_host, editor, item.Key);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);
        var restoredEvent = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((activity.Kind, activity.ActorId, activity.At), Is.EqualTo((ActivityKind.Restored, editor.PersonId, _host.Clock.GetUtcNow())));
            Assert.That((activity.Changes, activity.FromState, activity.ToState), Is.EqualTo((Array.Empty<FieldChange>(), (string?)null, (string?)null)));
            Assert.That((restoredEvent.Kind, restoredEvent.ActorPersonId, restoredEvent.OccurredAt), Is.EqualTo((WorkEventKind.ItemRestored, editor.PersonId, _host.Clock.GetUtcNow())));
            Assert.That(restoredEvent.Item.ItemKey, Is.EqualTo(item.Key));
            Assert.That(restoredEvent.Item.AssigneeId, Is.EqualTo(assignee.PersonId));
            Assert.That(restoredEvent.Item.ToState, Is.EqualTo(new StateSnapshot("todo", "待辦")));
            Assert.That(restoredEvent.Item.ActivityId, Is.EqualTo(activity.Id));
        }
    }

    [Test]
    public async Task RestoreAsync_ParentIsAlsoDeleted_IsParentDeletedWithOneDetailAndRestoresNothing()
    {
        // Arrange：任務掛在需求底下，先刪任務、再刪需求。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement("首頁改版"));
        var task = await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, task.Key, requirement.Key);

        // Act
        var result = await _host.Items.RestoreAsync(scenario.Admin, task.Key, CancellationToken.None);
        var stillDeleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.ItemParentDeleted);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (WorkErrorCodes.ItemParentDeleted, "parent") }));
            Assert.That(result.Failure.Message, Is.EqualTo("上層 WEB-1 也已刪除，先復原它。"));
            Assert.That(stillDeleted.Select(i => i.Key), Is.EquivalentTo(new[] { "WEB-1", "WEB-2" }));
        }
    }

    [Test]
    public async Task RestoreAsync_ParentRestoredFirst_BringsTheChildBackUnderIt()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement("首頁改版"));
        var task = await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, task.Key, requirement.Key);

        // Act
        await ItemRecovery.RestoreAsync(_host, scenario.Admin, requirement.Key);
        var restored = await ItemRecovery.RestoreAsync(_host, scenario.Admin, task.Key);
        var children = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery { ParentKey = requirement.Key }, CancellationToken.None);

        // Assert
        Assert.That(restored.ParentKey, Is.EqualTo("WEB-1"));
        Assert.That(children.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2" }));
    }

    [TestCase("commenter")]
    [TestCase("viewer")]
    [TestCase("project_manager")]
    public async Task RestoreAsync_RoleWithoutItemDelete_IsForbiddenAndRestoresNothing(string role)
    {
        // Arrange
        var scenario = await AgencyAsync();
        var member = await scenario.MemberAsync("WEB", role);
        var item = await scenario.CreateItemAsync("WEB", Requirement("不能復原"));
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);

        // Act
        var result = await _host.Items.RestoreAsync(member, item.Key, CancellationToken.None);
        var stillDeleted = await ItemRecovery.ListDeletedAsync(_host, scenario.Admin, "WEB");

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(stillDeleted, Has.Count.EqualTo(1));
    }

    [TestCase("WEB-99")]
    [TestCase("not-a-key")]
    public async Task RestoreAsync_ItemThatDoesNotExist_IsNotFound(string itemKey)
    {
        // Arrange
        var scenario = await SimpleAsync();

        // Act
        var result = await _host.Items.RestoreAsync(scenario.Admin, itemKey, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task RestoreAsync_ProjectTheCallerCannotSee_IsNotFound()
    {
        // Arrange
        var scenario = await SimpleAsync();
        var item = await scenario.CreateTaskAsync("WEB", "看不到");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);

        // Act
        var result = await _host.Items.RestoreAsync(scenario.Person(), item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task RestoreAsync_RestoredItem_CanBeDeletedAgainAndRestoredAgain()
    {
        // Arrange
        var scenario = await SimpleAsync();
        var item = await scenario.CreateTaskAsync("WEB", "來回");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await ItemRecovery.RestoreAsync(_host, scenario.Admin, item.Key);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(
            activities.Select(a => a.Kind),
            Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Deleted, ActivityKind.Restored, ActivityKind.Deleted, ActivityKind.Restored }));
    }

    private async Task<Scenario> SimpleAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        return scenario;
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Requirement(string title) => new() { Type = "requirement", Title = title };

    private static NewWorkItem Task(string title, string? parentKey) => new() { Type = "task", Title = title, ParentKey = parentKey };
}
