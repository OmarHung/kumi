using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>封存與取消封存：冪等、權限、ArchivedAt、封存後的權限只剩讀取與 project.manage、「我的工作」不列。</summary>
[TestFixture]
public sealed class ProjectArchiveTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ArchiveAsync_LiveProject_SetsArchivedAtToNowAndListAndDetailShowIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var archived = await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var listed = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(archived.Value.ArchivedAt, Is.EqualTo(_host.Clock.GetUtcNow()));
            Assert.That(listed.Single().ArchivedAt, Is.EqualTo(archived.Value.ArchivedAt));
            Assert.That(detail.Value.Project.ArchivedAt, Is.EqualTo(archived.Value.ArchivedAt));
        }
    }

    [Test]
    public async Task ArchiveAsync_AlreadyArchived_ReturnsTheOriginalArchivedAt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var first = await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var second = await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(second.Value.ArchivedAt, Is.EqualTo(first.Value.ArchivedAt));
    }

    [Test]
    public async Task UnarchiveAsync_ArchivedProject_ClearsArchivedAtAndRestoresThePermissions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var unarchived = await _host.Projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var created = await _host.Items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "又能寫了" }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(unarchived.Value.ArchivedAt, Is.Null);
            Assert.That(detail.Value.MyPermissions, Has.Member(WorkPermissions.ItemCreate));
            Assert.That(created.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task UnarchiveAsync_NotArchived_DoesNothingAndReturnsTheProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Value.ArchivedAt, Is.Null);
    }

    [Test]
    public async Task ArchiveAsync_EditorWithoutProjectManage_IsForbiddenAndTheProjectStaysLive()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Projects.ArchiveAsync(editor, "WEB", CancellationToken.None);
        var listed = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(listed.Single().ArchivedAt, Is.Null);
        }
    }

    [Test]
    public async Task UnarchiveAsync_EditorWithoutProjectManage_IsForbiddenNotArchivedError()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Projects.UnarchiveAsync(editor, "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task ArchiveAsync_NotAMember_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.ArchiveAsync(scenario.Person(), "WEB", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task GetAsync_ArchivedProject_PermissionsAreOnlyTheReadOnesAndProjectManage_EvenForAnOrganizationAdmin()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var manager = await scenario.MemberAsync("WEB", "admin");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var asOrganizationAdmin = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var asProjectAdmin = await _host.Projects.GetAsync(manager, "WEB", CancellationToken.None);
        var asEditor = await _host.Projects.GetAsync(editor, "WEB", CancellationToken.None);

        // Assert
        string[] expected = [WorkPermissions.ItemRead, WorkPermissions.BlueprintRead, WorkPermissions.ProjectManage];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asOrganizationAdmin.Value.MyPermissions, Is.EquivalentTo(expected));
            Assert.That(asProjectAdmin.Value.MyPermissions, Is.EquivalentTo(expected));
            Assert.That(asEditor.Value.MyPermissions, Is.EquivalentTo(new[] { WorkPermissions.ItemRead, WorkPermissions.BlueprintRead }), "原本沒有的不會多出來");
        }
    }

    [Test]
    public async Task ListAssignedToMeAsync_ArchivedProject_LeavesItsItemsOutAndTheyReturnAfterUnarchive()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("APP");
        var me = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "APP", me.PersonId, "editor", CancellationToken.None);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "網站", AssigneeId = me.PersonId });
        await scenario.CreateItemAsync("APP", new NewWorkItem { Type = "task", Title = "應用", AssigneeId = me.PersonId });

        // Act
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var whileArchived = await _host.Items.ListAssignedToMeAsync(me, CancellationToken.None);
        await _host.Projects.UnarchiveAsync(scenario.Admin, "WEB", CancellationToken.None);
        var afterUnarchive = await _host.Items.ListAssignedToMeAsync(me, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(whileArchived.Select(i => i.Title), Is.EqualTo(new[] { "應用" }));
            Assert.That(afterUnarchive.Select(i => i.Title), Is.EquivalentTo(new[] { "網站", "應用" }));
        }
    }

    [Test]
    public async Task SearchAsync_ArchivedProject_IsStillSearchable()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "獨特的搜尋字樣" });
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var found = await _host.Items.SearchAsync(scenario.Admin, "獨特的搜尋字樣", 10, CancellationToken.None);

        // Assert
        Assert.That(found.Value.Select(i => i.Title), Is.EqualTo(new[] { "獨特的搜尋字樣" }));
    }
}
