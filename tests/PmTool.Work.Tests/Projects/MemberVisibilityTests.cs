namespace PmTool.Work.Tests.Projects;

/// <summary>沒有 blueprint.read 的角色（例如客戶）只看得到自己與看得到的項目的負責人，不是整份成員名單。</summary>
[TestFixture]
public sealed class MemberVisibilityTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetAsync_RoleWithoutBlueprintRead_ListsOnlyThemselvesAndTheAssigneesOfTheirItems()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        await scenario.MemberAsync("WEB", "editor");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "有負責人", AssigneeId = manager.PersonId });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "沒有負責人" });

        // Act
        var detail = await _host.Projects.GetAsync(client, "WEB", CancellationToken.None);

        // Assert
        Assert.That(detail.Value.Members.Select(m => m.PersonId), Is.EquivalentTo(new[] { client.PersonId, manager.PersonId }));
    }

    [Test]
    public async Task GetAsync_RoleWithBlueprintRead_ListsEveryMember()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await scenario.MemberAsync("WEB", "client");

        // Act
        var detail = await _host.Projects.GetAsync(manager, "WEB", CancellationToken.None);

        // Assert
        Assert.That(detail.Value.Members, Has.Count.EqualTo(4));
        Assert.That(detail.Value.Members.Select(m => m.PersonId), Does.Contain(editor.PersonId));
    }

    [Test]
    public async Task GetAsync_ClientWhoseVisibleItemsAreAssignedToNobody_ListsOnlyThemselves()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "沒有負責人" });

        // Act
        var detail = await _host.Projects.GetAsync(client, "WEB", CancellationToken.None);

        // Assert
        Assert.That(detail.Value.Members.Select(m => m.PersonId), Is.EqualTo(new[] { client.PersonId }));
    }
}
