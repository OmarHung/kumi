using PmTool.Abstractions;

namespace PmTool.Work.Tests.Events;

/// <summary>知識庫用的三個存取查詢：看得到的專案與在裡面的權限。</summary>
[TestFixture]
public sealed class VisibleProjectsQueriesTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task VisibleProjectsAsync_Member_SeesOnlyTheirProjectsWithTheirRolePermissions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("OPS");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var visible = await _host.Access.VisibleProjectsAsync(viewer, CancellationToken.None);

        // Assert
        Assert.That(visible.Keys, Is.EquivalentTo(new[] { web.Id }));
        Assert.That(visible[web.Id].Key, Is.EqualTo("WEB"));
        Assert.That(visible[web.Id].Permissions, Does.Contain(WorkPermissions.ItemRead));
        Assert.That(visible[web.Id].Permissions, Does.Not.Contain(WorkPermissions.ItemCreate));
    }

    [Test]
    public async Task VisibleProjectsAsync_OrganizationAdminWhoIsNotAMember_SeesEveryProjectWithAllPermissions()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        var ops = await scenario.CreateProjectAsync("OPS");
        var otherAdmin = new Actor(scenario.OrganizationId, Guid.NewGuid(), IsOrganizationAdmin: true);
        await new Scenario(_host).CreateProjectAsync("WEB");

        // Act
        var visible = await _host.Access.VisibleProjectsAsync(otherAdmin, CancellationToken.None);

        // Assert
        Assert.That(visible.Keys, Is.EquivalentTo(new[] { web.Id, ops.Id }));
        Assert.That(visible[web.Id].Permissions, Does.Contain(WorkPermissions.ProjectManage).And.Contain(WorkPermissions.ItemCreate));
    }

    [Test]
    public async Task VisibleProjectsAsync_ArchivedProject_IsStillVisibleButOnlyReadableAndManageable()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.ArchiveAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Act
        var visible = await _host.Access.VisibleProjectsAsync(editor, CancellationToken.None);

        // Assert
        Assert.That(visible[web.Id].IsArchived, Is.True);
        Assert.That(visible[web.Id].Permissions, Does.Not.Contain(WorkPermissions.ItemCreate));
        Assert.That(visible[web.Id].Permissions, Does.Contain(WorkPermissions.ItemRead));
    }

    [Test]
    public async Task VisibleProjectsAsync_PersonWithoutMemberships_SeesNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var visible = await _host.Access.VisibleProjectsAsync(scenario.Person(), CancellationToken.None);

        // Assert
        Assert.That(visible, Is.Empty);
    }

    [Test]
    public async Task FindVisibleProjectAsync_LowercaseKeyOfAMemberProject_ReturnsIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", "editor");

        // Act
        var found = await _host.Access.FindVisibleProjectAsync(member, "web", CancellationToken.None);

        // Assert
        Assert.That((found?.ProjectId, found?.Key), Is.EqualTo((web.Id, "WEB")));
        Assert.That(found!.Permissions, Does.Contain(WorkPermissions.ItemCreate));
    }

    [Test]
    public async Task FindVisibleProjectAsync_NotAMemberMalformedOrUnknownKey_ReturnsNull()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var stranger = scenario.Person();

        // Act
        var notMember = await _host.Access.FindVisibleProjectAsync(stranger, "WEB", CancellationToken.None);
        var malformed = await _host.Access.FindVisibleProjectAsync(scenario.Admin, "web\n", CancellationToken.None);
        var unknown = await _host.Access.FindVisibleProjectAsync(scenario.Admin, "NOPE", CancellationToken.None);

        // Assert
        Assert.That((notMember, malformed, unknown), Is.EqualTo(((ProjectAccessSummary?)null, (ProjectAccessSummary?)null, (ProjectAccessSummary?)null)));
    }

    [Test]
    public async Task FindVisibleProjectsAsync_AskedIds_ReturnsOnlyTheVisibleOnes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var web = await scenario.CreateProjectAsync("WEB");
        var ops = await scenario.CreateProjectAsync("OPS");
        var member = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var found = await _host.Access.FindVisibleProjectsAsync(member, [web.Id, ops.Id, Guid.NewGuid()], CancellationToken.None);
        var none = await _host.Access.FindVisibleProjectsAsync(member, [], CancellationToken.None);

        // Assert
        Assert.That(found.Keys, Is.EquivalentTo(new[] { web.Id }));
        Assert.That(none, Is.Empty);
    }
}
