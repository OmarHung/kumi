using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

[TestFixture]
public sealed class ItemUpdateTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_OnlyGivenFields_ChangeAndTheRestStay()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "原本的標題",
            AssigneeId = scenario.Admin.PersonId,
            DueDate = new DateOnly(2026, 12, 1),
            Fields = Scenario.Fields(("category", "bug"), ("summary", "說明")),
        });
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, original.Key, new WorkItemPatch { Priority = Priority.Urgent }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated.Value.Priority, Is.EqualTo(Priority.Urgent));
            Assert.That(readBack.Value.Title, Is.EqualTo("原本的標題"));
            Assert.That(readBack.Value.AssigneeId, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(readBack.Value.DueDate, Is.EqualTo(new DateOnly(2026, 12, 1)));
            Assert.That(readBack.Value.Fields.Keys, Is.EquivalentTo(new[] { "category", "summary" }));
            Assert.That(readBack.Value.CreatedAt, Is.EqualTo(original.CreatedAt));
            Assert.That(readBack.Value.UpdatedAt, Is.EqualTo(original.CreatedAt.AddMinutes(5)));
            Assert.That(readBack.Value, Is.EqualTo(updated.Value with { Fields = readBack.Value.Fields }));
        }
    }

    [Test]
    public async Task UpdateAsync_NullForNullableCoreFields_ClearsThem()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            AssigneeId = scenario.Admin.PersonId,
            StartDate = new DateOnly(2026, 11, 1),
            DueDate = new DateOnly(2026, 12, 1),
            Estimate = 8m,
        });
        var patch = new WorkItemPatch { AssigneeId = null, StartDate = null, DueDate = null, Estimate = null, Title = "新標題" };

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);

        // Assert
        Assert.That(
            (updated.Value.AssigneeId, updated.Value.StartDate, updated.Value.DueDate, updated.Value.Estimate, updated.Value.Title),
            Is.EqualTo(((Guid?)null, (DateOnly?)null, (DateOnly?)null, (decimal?)null, "新標題")));
    }

    [Test]
    public async Task UpdateAsync_CustomFields_SetsReplacesAndRemovesOnlyTheGivenKeys()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Fields = Scenario.Fields(("category", "bug"), ("tags", new[] { "docs" }), ("story_points", 3)),
        });
        var patch = new WorkItemPatch { Fields = Scenario.Fields(("category", "chore"), ("tags", null), ("summary", "補上說明")) };

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        var fields = readBack.Value.Fields;
        Assert.That(fields.Keys, Is.EquivalentTo(new[] { "category", "story_points", "summary" }));
        Assert.That((fields["category"].GetString(), fields["story_points"].GetInt32(), fields["summary"].GetString()), Is.EqualTo(("chore", 3, "補上說明")));
    }

    [Test]
    public async Task UpdateAsync_InvalidChange_IsRejectedAndNothingChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB", "原本");
        var patch = new WorkItemPatch { Title = "會被退回的修改", Fields = Scenario.Fields(("review_date", "明天")) };

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, patch, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "review_date");
        Assert.That(readBack.Value.Title, Is.EqualTo("原本"));
    }

    [Test]
    public async Task UpdateAsync_NullTitle_IsTitleRequired()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, new WorkItemPatch { Title = null! }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemTitleRequired, "title");
    }

    [Test]
    public async Task UpdateAsync_NullFields_IsInvalidAndNothingChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB", "原本");

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, original.Key, new WorkItemPatch { Title = "會被退回的修改", Fields = null! }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, original.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "fields");
        Assert.That(readBack.Value.Title, Is.EqualTo("原本"));
    }

    [Test]
    public async Task UpdateAsync_AssigneeWhoIsNotAMember_IsUnknownAssignee()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, original.Key, new WorkItemPatch { AssigneeId = scenario.Person().PersonId }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownAssignee, "assignee");
    }

    [Test]
    public async Task UpdateAsync_EmptyPatch_ReturnsTheItemUnchanged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, original.Key, new WorkItemPatch(), CancellationToken.None);

        // Assert
        Assert.That(result.Value.UpdatedAt, Is.EqualTo(original.UpdatedAt));
    }

    [TestCase("viewer")]
    [TestCase("commenter")]
    public async Task UpdateAsync_RoleWithoutItemUpdate_IsForbidden(string role)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");
        var member = await scenario.MemberAsync("WEB", role);

        // Act
        var result = await _host.Items.UpdateAsync(member, original.Key, new WorkItemPatch { Title = "改不了" }, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task UpdateAsync_Editor_CanUpdate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var original = await scenario.CreateTaskAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Items.UpdateAsync(editor, original.Key, new WorkItemPatch { Title = "編輯者改的" }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.Title, Is.EqualTo("編輯者改的"));
    }

    [TestCase("WEB-99")]
    [TestCase("WEB-0")]
    [TestCase("WEB")]
    [TestCase("WEB-1-2")]
    [TestCase("-1")]
    [TestCase("")]
    public async Task UpdateAsync_ItemThatDoesNotExist_IsItemNotFound(string itemKey)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, itemKey, new WorkItemPatch { Title = "x" }, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task GetAsync_ItemInAProjectTheCallerCannotSee_IsItemNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var outsider = await _host.Items.GetAsync(scenario.Person(), item.Key, CancellationToken.None);
        var otherOrganization = await _host.Items.GetAsync(new Scenario(_host).Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(outsider, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(otherOrganization, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task GetAsync_Viewer_CanReadWithALowerCaseKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Items.GetAsync(viewer, "web-1", CancellationToken.None);

        // Assert
        Assert.That(result.Value.Key, Is.EqualTo(item.Key));
    }
}
