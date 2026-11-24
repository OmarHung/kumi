using PmTool.Abstractions;

namespace PmTool.Work.Tests.OwnItems;

/// <summary>給通知用的兩個查詢，與事件裡的提及：只給看得到項目的人。</summary>
[TestFixture]
public sealed class OwnItemsAccessQueriesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task FilterItemReadersAsync_OwnOnlyMember_IsAReaderOnlyOfTheirOwnItems()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var project = (await _host.Projects.ListAsync(world.Scenario.Admin, CancellationToken.None)).Single();
        var everyone = new[] { world.Contributor.PersonId, world.Editor.PersonId, Guid.NewGuid() };

        // Act
        var onAssigned = await Readers(world, project.Id, new ItemOwnership(world.Editor.PersonId, world.Contributor.PersonId), everyone);
        var onCreated = await Readers(world, project.Id, new ItemOwnership(world.Contributor.PersonId, null), everyone);
        var onOthers = await Readers(world, project.Id, new ItemOwnership(world.Editor.PersonId, world.Editor.PersonId), everyone);

        // Assert：編輯者看得到全部；執行者只有負責或建立的；不是成員的永遠不是讀者。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onAssigned, Is.EquivalentTo(new[] { world.Contributor.PersonId, world.Editor.PersonId }));
            Assert.That(onCreated, Is.EquivalentTo(new[] { world.Contributor.PersonId, world.Editor.PersonId }));
            Assert.That(onOthers, Is.EquivalentTo(new[] { world.Editor.PersonId }));
        }
    }

    [Test]
    public async Task ReadableItemIdsAsync_ReturnsOnlyTheItemsTheActorCanSee()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var ids = await IdsAsync(world);
        var deleted = await world.Scenario.CreateTaskAsync("WEB", "刪掉的");

        // Act
        var contributor = await _host.Access.ReadableItemIdsAsync(world.Contributor, [ids.Assigned, ids.Created, ids.Others], CancellationToken.None);
        var editor = await _host.Access.ReadableItemIdsAsync(world.Editor, [ids.Assigned, ids.Created, ids.Others, Guid.NewGuid()], CancellationToken.None);
        var admin = await _host.Access.ReadableItemIdsAsync(world.Scenario.Admin, [ids.Others], CancellationToken.None);
        var stranger = await _host.Access.ReadableItemIdsAsync(world.Scenario.Person(), [ids.Assigned], CancellationToken.None);
        await _host.Items.DeleteAsync(world.Scenario.Admin, deleted.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(contributor, Is.EquivalentTo(new[] { ids.Assigned, ids.Created }));
            Assert.That(editor, Is.EquivalentTo(new[] { ids.Assigned, ids.Created, ids.Others }));
            Assert.That(admin, Is.EquivalentTo(new[] { ids.Others }));
            Assert.That(stranger, Is.Empty);
        }
    }

    [Test]
    public async Task ReadableItemIdsAsync_AfterTheAssigneeChanges_DropsTheItem()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var ids = await IdsAsync(world);
        await _host.Items.UpdateAsync(world.Editor, world.Assigned.Key, new WorkItemPatch { AssigneeId = world.Editor.PersonId }, CancellationToken.None);

        // Act
        var readable = await _host.Access.ReadableItemIdsAsync(world.Contributor, [ids.Assigned, ids.Created], CancellationToken.None);

        // Assert
        Assert.That(readable, Is.EquivalentTo(new[] { ids.Created }));
    }

    [Test]
    public async Task Mentions_OnAnItemTheyCannotSee_DoNotReachAnOwnOnlyMember()
    {
        // Arrange
        var world = await OwnItemsWorld.CreateAsync(_host);
        var mention = EventFeed.Mention(world.Contributor);

        // Act
        await _host.Activity.AddCommentAsync(world.Editor, world.Others.Key, $"請看 {mention}", null, CancellationToken.None);
        var onOthers = await EventFeed.LastAsync(_host, world.Scenario);
        await _host.Activity.AddCommentAsync(world.Editor, world.Assigned.Key, $"請看 {mention}", null, CancellationToken.None);
        var onAssigned = await EventFeed.LastAsync(_host, world.Scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(onOthers.Item.MentionedPersonIds, Is.Empty);
            Assert.That(onAssigned.Item.MentionedPersonIds, Is.EqualTo(new[] { world.Contributor.PersonId }));
        }
    }

    private async Task<IReadOnlySet<Guid>> Readers(OwnItemsWorld world, Guid projectId, ItemOwnership item, Guid[] people) =>
        await _host.Access.FilterItemReadersAsync(world.Scenario.OrganizationId, projectId, item, people, CancellationToken.None);

    private async Task<(Guid Assigned, Guid Created, Guid Others)> IdsAsync(OwnItemsWorld world)
    {
        var events = await EventFeed.OfAsync(_host, world.Scenario);
        Guid IdOf(WorkItem item) => events.First(e => e.Item.ItemKey == item.Key).Item.ItemId;
        return (IdOf(world.Assigned), IdOf(world.Created), IdOf(world.Others));
    }
}
