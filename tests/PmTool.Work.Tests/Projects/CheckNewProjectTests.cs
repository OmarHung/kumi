using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>CheckNewProjectAsync：和 CreateAsync 同一套檢查，什麼都不寫（AI 助理的新專案建議在提出時先擋）。</summary>
[TestFixture]
public sealed class CheckNewProjectTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CheckNewProject_ValidProjectForAnAdmin_SucceedsAndCreatesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var checkedProject = await _host.Projects.CheckNewProjectAsync(
            scenario.Admin, new NewProject("CHK", "檢查用", Scenario.SimpleTasks), CancellationToken.None);
        var projects = await _host.Projects.ListAsync(scenario.Admin, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(checkedProject.Succeeded, Is.True, () => checkedProject.Failure!.Message);
            Assert.That(projects.Select(p => p.Key), Does.Not.Contain("CHK"));
        }
    }

    [Test]
    public async Task CheckNewProject_NotAnAdmin_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var checkedProject = await _host.Projects.CheckNewProjectAsync(
            scenario.Person(), new NewProject("CHK", "檢查用", Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        Assert.That(checkedProject.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task CheckNewProject_KeyAlreadyUsed_IsKeyTaken()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USED");

        // Act
        var checkedProject = await _host.Projects.CheckNewProjectAsync(
            scenario.Admin, new NewProject("USED", "重複", Scenario.SimpleTasks), CancellationToken.None);

        // Assert
        Assert.That(checkedProject.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectKeyTaken));
    }

    [Test]
    public async Task CheckNewProject_BadKeyNameAndTemplate_ReportsEveryProblemAtOnce()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act
        var checkedProject = await _host.Projects.CheckNewProjectAsync(
            scenario.Admin, new NewProject("bad key", " ", "no_such_template"), CancellationToken.None);

        // Assert
        Assert.That(checkedProject.Failure?.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "key", "name", "template" }));
    }
}
