using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 批次刪除與復原：上層和下層一起選時順序由伺服器排好（刪除先下層、復原先上層），
/// 還有不在這一批的下層、或上層沒復原時那一筆失敗；已經是目標狀態的算成功。
/// </summary>
[TestFixture]
public sealed class BulkDeleteRestoreTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteAsync_ParentAndItsChildrenInTheSameBatch_DeletesTheChildrenFirstAndSucceedsForAll()
    {
        // Arrange：需求在前、任務在後的請求順序，單獨刪需求會被擋下。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var first = await scenario.CreateItemAsync("WEB", Task("任務一", requirement.Key));
        var second = await scenario.CreateItemAsync("WEB", Task("任務二", requirement.Key));

        // Act
        var result = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [requirement.Key, first.Key, second.Key], CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Deleted.Select(d => d.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2", "WEB-3" }), "照請求的順序");
            Assert.That(result.Value.Failed, Is.Empty);
            Assert.That(listed.Value.Items, Is.Empty);
        }
    }

    [Test]
    public async Task DeleteAsync_ParentWithAChildOutsideTheBatch_FailsThatParentWithHasChildren()
    {
        // Arrange：兩個任務掛在需求底下，只選需求與其中一個。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var chosen = await scenario.CreateItemAsync("WEB", Task("選了", requirement.Key));
        var left = await scenario.CreateItemAsync("WEB", Task("沒選", requirement.Key));

        // Act
        var result = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [requirement.Key, chosen.Key], CancellationToken.None);
        var stillThere = await _host.Items.GetAsync(scenario.Admin, requirement.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Deleted.Select(d => d.Key), Is.EqualTo(new[] { chosen.Key }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { (requirement.Key, WorkErrorCodes.ItemHasChildren) }));
            Assert.That(result.Value.Failed[0].Failure.Details.Select(d => d.Path), Is.EqualTo(new[] { "children/" + left.Key }));
            Assert.That(stillThere.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_AlreadyDeleted_SucceedsWithTheOriginalDeletionTime()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var item = await scenario.CreateTaskAsync("WEB");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, item.Key);
        var deletedAt = _host.Clock.GetUtcNow();
        _host.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var result = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [item.Key], CancellationToken.None);
        var stored = await DeletedItems.ReadAsync(_host, scenario, item.Key);

        // Assert
        Assert.That(result.Value.Deleted, Is.EqualTo(new[] { new DeletedWorkItem(item.Key, deletedAt) }));
        Assert.That(stored.ActivityKinds, Is.EqualTo(new[] { "created", "deleted" }));
    }

    [Test]
    public async Task DeleteAsync_KeysFromAnotherProjectDuplicatesAndMissingOnes_AreFailuresOrDoneOnce()
    {
        // Arrange
        var scenario = await ProjectAsync();
        await scenario.CreateProjectAsync("OTH");
        var mine = await scenario.CreateTaskAsync("WEB");
        var other = await scenario.CreateTaskAsync("OTH");

        // Act
        var result = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [other.Key, mine.Key, "web-1", "WEB-9"], CancellationToken.None);
        var otherStillThere = await _host.Items.GetAsync(scenario.Admin, other.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Deleted.Select(d => d.Key), Is.EqualTo(new[] { "WEB-1" }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[]
            {
                ("OTH-1", WorkErrorCodes.ItemNotFound),
                ("WEB-9", WorkErrorCodes.ItemNotFound),
            }));
            Assert.That(otherStillThere.Succeeded, Is.True, "別的專案的項目沒有被刪");
        }
    }

    [Test]
    public async Task RestoreAsync_ParentAndChildrenDeletedTogether_RestoresTheParentFirstAndSucceedsForAll()
    {
        // Arrange：批次刪除整串，再用同一批編號復原（請求順序是下層在前）。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var task = await scenario.CreateItemAsync("WEB", Task("任務", requirement.Key));
        var deleted = await _host.Bulk.DeleteAsync(scenario.Admin, "WEB", [task.Key, requirement.Key], CancellationToken.None);
        Assert.That(deleted.Value.Failed, Is.Empty);

        // Act
        var result = await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [task.Key, requirement.Key], CancellationToken.None);
        var child = await _host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Restored.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2", "WEB-1" }), "照請求的順序");
            Assert.That(result.Value.Failed, Is.Empty);
            Assert.That(child.Value.ParentKey, Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task RestoreAsync_ChildWhoseParentIsDeletedAndNotInTheBatch_FailsWithParentDeleted()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        var task = await scenario.CreateItemAsync("WEB", Task("任務", requirement.Key));
        var other = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "另一個" });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, task.Key, requirement.Key, other.Key);

        // Act
        var result = await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [task.Key, other.Key], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Restored.Select(i => i.Key), Is.EqualTo(new[] { other.Key }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { (task.Key, WorkErrorCodes.ItemParentDeleted) }));
            Assert.That(result.Value.Failed[0].Failure.Details.Select(d => d.Path), Is.EqualTo(new[] { "parent" }));
        }
    }

    [Test]
    public async Task RestoreAsync_NotDeletedAndMissingAndDuplicateKeys_SucceedOnceFailAndAreDoneOnce()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var live = await scenario.CreateTaskAsync("WEB", "活著");
        var deleted = await scenario.CreateTaskAsync("WEB", "刪了");
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, deleted.Key);

        // Act
        var result = await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [deleted.Key, live.Key, "WEB-77", deleted.Key], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Restored.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2", "WEB-1" }));
            Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { ("WEB-77", WorkErrorCodes.ItemNotFound) }));
        }
    }

    [Test]
    public async Task RestoreAsync_TypeRemovedForOneItem_FailsOnlyThatOne()
    {
        // Arrange：一個需求變更單（之後類型被拿掉）、一個需求，都刪除。
        var scenario = await AgencyAsync();
        var changeRequest = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "變更" });
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        await ItemRecovery.DeleteAsync(_host, scenario.Admin, changeRequest.Key, requirement.Key);
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with
        {
            ItemTypes = [.. b.ItemTypes.Where(t => t.Key != "change_request").Select(t => t with { Parents = [.. t.Parents.Where(p => p != "change_request")] })],
            Views = [.. b.Views.Select(v => v.Filter is { } f ? v with { Filter = f with { ItemTypes = [.. f.ItemTypes.Where(t => t != "change_request")] } } : v)],
        });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);

        // Act
        var result = await _host.Bulk.RestoreAsync(scenario.Admin, "WEB", [changeRequest.Key, requirement.Key], CancellationToken.None);

        // Assert
        Assert.That(result.Value.Restored.Select(i => i.Key), Is.EqualTo(new[] { requirement.Key }));
        Assert.That(result.Value.Failed.Select(f => (f.Key, f.Failure.Code)), Is.EqualTo(new[] { (changeRequest.Key, WorkErrorCodes.ItemTypeRemoved) }));
    }

    private async Task<Scenario> ProjectAsync()
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

    private static NewWorkItem Task(string title, string? parentKey) => new() { Type = "task", Title = title, ParentKey = parentKey };
}
