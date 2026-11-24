namespace PmTool.Work.Tests.Events;

/// <summary>每個寫入點在同一個交易裡留下的事件：種類、做的人、項目與這一次改了什麼。</summary>
[TestFixture]
public sealed class EventContentTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_WithAssignee_WritesAnItemCreatedEventWithTheAssigneeAndTheFirstState()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var assignee = await scenario.MemberAsync("WEB", "editor");

        // Act
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "新任務", AssigneeId = assignee.PersonId });
        var written = await EventFeed.OfAsync(_host, scenario);

        // Assert
        var created = written.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((created.Kind, created.ActorPersonId, created.OccurredAt), Is.EqualTo((WorkEventKind.ItemCreated, scenario.Admin.PersonId, item.CreatedAt)));
            Assert.That(
                (created.Item.ProjectKey, created.Item.ItemKey, created.Item.Title, created.Item.CreatedBy),
                Is.EqualTo(("WEB", item.Key, "新任務", scenario.Admin.PersonId)));
            Assert.That((created.Item.AssigneeId, created.Item.AssigneeChanged, created.Item.PreviousAssigneeId), Is.EqualTo((assignee.PersonId, true, (Guid?)null)));
            Assert.That(created.Item.ToState, Is.EqualTo(new StateSnapshot("todo", "待辦")));
            Assert.That(created.Item.FromState, Is.Null);
            Assert.That(created.Item.ChangedFields, Is.Empty);
        }
    }

    [Test]
    public async Task CreateAsync_WithoutAssignee_WritesAnEventWithNoAssignee()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        await scenario.CreateTaskAsync("WEB");
        var created = (await EventFeed.OfAsync(_host, scenario)).Single();

        // Assert
        Assert.That((created.Item.AssigneeId, created.Item.AssigneeChanged), Is.EqualTo(((Guid?)null, false)));
    }

    [Test]
    public async Task UpdateAsync_ChangingTheAssignee_RecordsTheNewAndThePreviousAssigneeAndTheChangedFields()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await scenario.MemberAsync("WEB", "editor");
        var second = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", AssigneeId = first.PersonId });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var updated = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { AssigneeId = second.PersonId, Title = "改過" }, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.ActorPersonId, written.OccurredAt), Is.EqualTo((WorkEventKind.ItemUpdated, scenario.Admin.PersonId, updated.Value.UpdatedAt)));
            Assert.That(
                (written.Item.AssigneeId, written.Item.AssigneeChanged, written.Item.PreviousAssigneeId, written.Item.Title),
                Is.EqualTo((second.PersonId, true, first.PersonId, "改過")));
            Assert.That(written.Item.ChangedFields, Is.EquivalentTo(new[] { "title", "assignee" }));
            Assert.That(written.Item.MentionedPersonIds, Is.Empty);
        }
    }

    [Test]
    public async Task UpdateAsync_ChangingOtherFields_DoesNotMarkTheAssigneeAsChanged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var assignee = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", AssigneeId = assignee.PersonId });

        // Act：負責人給一樣的值不算改。
        await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { AssigneeId = assignee.PersonId, Title = "改過" }, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That((written.Item.AssigneeChanged, written.Item.PreviousAssigneeId, written.Item.AssigneeId), Is.EqualTo((false, (Guid?)null, assignee.PersonId)));
        Assert.That(written.Item.ChangedFields, Is.EqualTo(new[] { "title" }));
    }

    [Test]
    public async Task UpdateAsync_NothingChanged_WritesNoEvent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "任務");

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "任務" }, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Select(e => e.Kind), Is.EqualTo(new[] { WorkEventKind.ItemCreated }));
    }

    [Test]
    public async Task UpdateAsync_DescriptionWithANewMention_ListsOnlyThePeopleAddedByThisChange()
    {
        // Arrange：說明原本就提及了 old，這次又加了 added。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var old = await scenario.MemberAsync("WEB", "editor");
        var added = await scenario.MemberAsync("WEB", "viewer");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = $"請 {EventFeed.Mention(old)} 看" });

        // Act
        await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Description = $"請 {EventFeed.Mention(old)} 和 {EventFeed.Mention(added)} 看" }, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That(written.Item.MentionedPersonIds, Is.EqualTo(new[] { added.PersonId }));
        Assert.That(written.Item.ChangedFields, Is.EqualTo(new[] { "description" }));
    }

    [Test]
    public async Task UpdateAsync_ChangingOnlyTheTitle_DoesNotMentionAgainWhoeverTheDescriptionAlreadyMentions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var mentioned = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = EventFeed.Mention(mentioned) });

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改過" }, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.MentionedPersonIds, Is.Empty);
    }

    [Test]
    public async Task TransitionAsync_RecordsTheStateNamesAtThatTime()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.OccurredAt), Is.EqualTo((WorkEventKind.ItemTransitioned, moved.Value.UpdatedAt)));
            Assert.That(written.Item.FromState, Is.EqualTo(new StateSnapshot("todo", "待辦")));
            Assert.That(written.Item.ToState, Is.EqualTo(new StateSnapshot("doing", "進行中")));
        }
    }

    [Test]
    public async Task TransitionAsync_BlockedByTheWorkflow_LeavesNoEvent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act：沒有這個狀態。
        var blocked = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "no_such_state", CancellationToken.None);

        // Assert
        Assert.That(blocked.Succeeded, Is.False);
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Select(e => e.Kind), Is.EqualTo(new[] { WorkEventKind.ItemCreated }));
    }

    [Test]
    public async Task AddCommentAsync_RecordsTheExcerptWithMentionsAsPlainNames()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var teammate = await scenario.MemberAsync("WEB", "commenter");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, $"麻煩 {EventFeed.Mention(teammate, "王小明")} 確認", null, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.Kind, Is.EqualTo(WorkEventKind.ItemCommented));
            Assert.That(written.Item.CommentExcerpt, Is.EqualTo("麻煩 @王小明 確認"));
            Assert.That(written.Item.MentionedPersonIds, Is.EqualTo(new[] { teammate.PersonId }));
        }
    }

    [Test]
    public async Task AddCommentAsync_LongComment_CutsTheExcerptAtTheLimit()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, new string('字', WorkEventRules.MaxExcerptLength + 50), null, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.LastAsync(_host, scenario)).Item.CommentExcerpt, Is.EqualTo(new string('字', WorkEventRules.MaxExcerptLength)));
    }

    [Test]
    public async Task AddCommentAsync_Rejected_LeavesNoEvent()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act：檢視者不能留言。
        var rejected = await _host.Activity.AddCommentAsync(viewer, item.Key, "留言", null, CancellationToken.None);

        // Assert
        Assert.That(rejected.Succeeded, Is.False);
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Select(e => e.Kind), Is.EqualTo(new[] { WorkEventKind.ItemCreated }));
    }

    [Test]
    public async Task DeleteAsync_WritesAnItemDeletedEventForTheActor()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "要刪掉的");

        // Act
        var deleted = await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.ActorPersonId, written.OccurredAt), Is.EqualTo((WorkEventKind.ItemDeleted, scenario.Admin.PersonId, deleted.Value.DeletedAt)));
            Assert.That((written.Item.ItemKey, written.Item.Title), Is.EqualTo((item.Key, "要刪掉的")));
        }
    }

    [Test]
    public async Task ChangeTypeAsync_WritesAnUpdatedEventForTheLiveUpdatesAndReaders()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "需求" });

        // Act
        var changed = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "task", CancellationToken.None);

        // Assert
        Assert.That(changed.Succeeded, Is.True);
        Assert.That(
            (await EventFeed.OfAsync(_host, scenario)).Select(e => e.Kind), Is.EqualTo(new[] { WorkEventKind.ItemCreated, WorkEventKind.ItemUpdated }));
    }

    [Test]
    public async Task EventsOfSeveralWrites_AreReadInTheOrderTheyHappened()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var item = await scenario.CreateTaskAsync("WEB");
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改" }, CancellationToken.None);
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "留言", null, CancellationToken.None);
        await _host.Items.TransitionAsync(scenario.Admin, item.Key, "done", CancellationToken.None);
        await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That((await EventFeed.OfAsync(_host, scenario)).Select(e => e.Kind), Is.EqualTo(new[]
        {
            WorkEventKind.ItemCreated, WorkEventKind.ItemUpdated, WorkEventKind.ItemCommented, WorkEventKind.ItemTransitioned, WorkEventKind.ItemDeleted,
        }));
    }
}
