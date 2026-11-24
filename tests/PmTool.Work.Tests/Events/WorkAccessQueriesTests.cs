using PmTool.Abstractions;

namespace PmTool.Work.Tests.Events;

/// <summary>給通知模組用的三個存取查詢：誰看得到哪個項目、哪些人看得到專案、一個人看得到哪些專案。</summary>
[TestFixture]
public sealed class WorkAccessQueriesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ResolveItemAsync_ReadableItem_ReturnsItsReferenceWithTheCanonicalKey()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var item = await scenario.CreateTaskAsync("WEB", "要找的項目");

        // Act：用小寫的編號找。
        var found = await _host.Access.ResolveItemAsync(viewer, item.Key.ToLowerInvariant(), CancellationToken.None);

        // Assert
        Assert.That(
            (found.Value.ProjectId, found.Value.ProjectKey, found.Value.ItemKey, found.Value.Title),
            Is.EqualTo((project.Id, "WEB", item.Key, "要找的項目")));
        Assert.That(found.Value.ItemId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task ResolveItemAsync_NotAMember_IsItemNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var found = await _host.Access.ResolveItemAsync(scenario.Person(), item.Key, CancellationToken.None);

        // Assert
        Assert.That(found.Failure?.Code, Is.EqualTo(WorkErrorCodes.ItemNotFound));
    }

    [Test]
    public async Task ResolveItemAsync_OrganizationAdminWhoIsNotAMember_CanResolve()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var otherAdmin = new Actor(scenario.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);

        // Act
        var found = await _host.Access.ResolveItemAsync(otherAdmin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(found.Value.ItemKey, Is.EqualTo(item.Key));
    }

    [Test]
    public async Task ResolveItemAsync_DeletedOrUnknownItem_IsItemNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Act
        var deleted = await _host.Access.ResolveItemAsync(scenario.Admin, item.Key, CancellationToken.None);
        var unknown = await _host.Access.ResolveItemAsync(scenario.Admin, "WEB-999", CancellationToken.None);

        // Assert
        Assert.That((deleted.Failure?.Code, unknown.Failure?.Code), Is.EqualTo((WorkErrorCodes.ItemNotFound, WorkErrorCodes.ItemNotFound)));
    }

    [Test]
    public async Task FilterReadersAsync_KeepsTheMembersWhoCanReadAndDropsEveryoneElse()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var stranger = scenario.Person();

        // Act
        var readers = await _host.Access.FilterReadersAsync(
            scenario.OrganizationId, project.Id, [viewer.PersonId, editor.PersonId, stranger.PersonId, Guid.NewGuid()], CancellationToken.None);

        // Assert
        Assert.That(readers, Is.EquivalentTo(new[] { viewer.PersonId, editor.PersonId }));
    }

    [Test]
    public async Task FilterReadersAsync_RemovedMember_IsNoLongerAReader()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.RemoveMemberAsync(scenario.Admin, "WEB", member.PersonId, CancellationToken.None);

        // Act
        var readers = await _host.Access.FilterReadersAsync(scenario.OrganizationId, project.Id, [member.PersonId], CancellationToken.None);

        // Assert
        Assert.That(readers, Is.Empty);
    }

    [Test]
    public async Task FilterReadersAsync_ProjectOfAnotherOrganizationOrUnknown_ReturnsNobody()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");

        // Act
        var otherOrganization = await _host.Access.FilterReadersAsync(Guid.NewGuid(), project.Id, [member.PersonId], CancellationToken.None);
        var unknownProject = await _host.Access.FilterReadersAsync(scenario.OrganizationId, Guid.NewGuid(), [member.PersonId], CancellationToken.None);
        var nobodyAsked = await _host.Access.FilterReadersAsync(scenario.OrganizationId, project.Id, [], CancellationToken.None);

        // Assert
        Assert.That((otherOrganization, unknownProject, nobodyAsked), Is.EqualTo((new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())));
    }

    [Test]
    public async Task ReadableProjectIdsAsync_Member_SeesOnlyTheProjectsTheyBelongTo()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var member = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var readable = await _host.Access.ReadableProjectIdsAsync(member, CancellationToken.None);

        // Assert
        Assert.That(readable, Is.EquivalentTo(new[] { web.Id }));
    }

    [Test]
    public async Task ReadableProjectIdsAsync_OrganizationAdmin_SeesEveryProjectOfTheOrganizationOnly()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        var ops = await scenario.CreateProjectAsync("OPS");
        var otherScenario = new Scenario(_host);
        await otherScenario.CreateProjectAsync("WEB");

        // Act
        var readable = await _host.Access.ReadableProjectIdsAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(readable, Is.EquivalentTo(new[] { web.Id, ops.Id }));
    }

    [Test]
    public async Task ReadableProjectIdsAsync_PersonWithoutMemberships_SeesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var readable = await _host.Access.ReadableProjectIdsAsync(scenario.Person(), CancellationToken.None);

        // Assert
        Assert.That(readable, Is.Empty);
    }
}
