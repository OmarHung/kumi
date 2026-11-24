using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>專案改名：只改名稱、代號不變；規則和建立時一樣；要 project.manage；封存的專案也可以改。</summary>
[TestFixture]
public sealed class ProjectRenameTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RenameAsync_ValidName_ChangesTheNameKeepsTheKeyAndShowsInTheListAndDetail()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var renamed = await _host.Projects.RenameAsync(scenario.Admin, "WEB", "  官網改版（第二期） ", CancellationToken.None);
        var listed = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);
        var detail = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(renamed.Succeeded, Is.True);
            Assert.That((renamed.Value.Key, renamed.Value.Name), Is.EqualTo(("WEB", "官網改版（第二期）")), "名稱去掉頭尾空白");
            Assert.That(listed.Single().Name, Is.EqualTo("官網改版（第二期）"));
            Assert.That(detail.Value.Project.Name, Is.EqualTo("官網改版（第二期）"));
        }
    }

    [Test]
    public async Task RenameAsync_SameName_SucceedsWithoutChange()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RenameAsync(scenario.Admin, "WEB", project.Name, CancellationToken.None);

        // Assert
        Assert.That(result.Value, Is.EqualTo(project));
    }

    [TestCase("", WorkErrorCodes.ProjectNameRequired)]
    [TestCase("   ", WorkErrorCodes.ProjectNameRequired)]
    [TestCase("名稱\n換行", WorkErrorCodes.ProjectNameRequired)]
    [TestCase("名稱\0", WorkErrorCodes.ProjectNameRequired)]
    public async Task RenameAsync_InvalidName_FailsWithTheNamePath(string name, string code)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RenameAsync(scenario.Admin, "WEB", name, CancellationToken.None);
        var listed = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
            Assert.That(result.Failure?.Code, Is.EqualTo(code));
            Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "name" }));
            Assert.That(listed.Single().Name, Is.EqualTo("範例專案 WEB"), "沒有被改掉");
        }
    }

    [Test]
    public async Task RenameAsync_TooLongName_FailsWithNameTooLong()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RenameAsync(scenario.Admin, "WEB", new string('名', WorkRules.MaxProjectNameLength + 1), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNameTooLong));
    }

    [Test]
    public async Task RenameAsync_ProjectAdminWhoIsNotAnOrganizationAdmin_Succeeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var manager = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Projects.RenameAsync(manager, "WEB", "新名稱", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }

    [Test]
    public async Task RenameAsync_EditorWithoutProjectManage_IsForbiddenAndNothingChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Projects.RenameAsync(editor, "WEB", "新名稱", CancellationToken.None);
        var listed = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
            Assert.That(listed.Single().Name, Is.EqualTo("範例專案 WEB"));
        }
    }

    [Test]
    public async Task RenameAsync_NotAMember_IsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Projects.RenameAsync(scenario.Person(), "WEB", "新名稱", CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task RenameAsync_ArchivedProject_StillWorks()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var result = await _host.Projects.RenameAsync(scenario.Admin, "WEB", "收起來的新名稱", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Name, Is.EqualTo("收起來的新名稱"));
            Assert.That(result.Value.ArchivedAt, Is.Not.Null, "改名不影響封存");
        }
    }
}
