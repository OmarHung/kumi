using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Knowledge.Tests.Spaces;

/// <summary>誰看得到哪些空間、能做什麼（交接 4.2）。</summary>
[TestFixture]
public sealed class SpaceAccessTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task GetAsync_OrganizationSpace_MemberCanEditAndCommentButNotManage()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var seen = await _host.Spaces.GetAsync(org.Member(), "hr", CancellationToken.None);

        // Assert
        Assert.That(seen.Value.MyPermissions, Is.EqualTo(new SpacePermissions(CanEdit: true, CanComment: true, CanManage: false)));
    }

    [Test]
    public async Task GetAsync_OrganizationSpace_OwnerAndAdminCanManage()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var byOwner = await _host.Spaces.GetAsync(org.Owner(), "HR", CancellationToken.None);
        var byAdmin = await _host.Spaces.GetAsync(org.Person(OrganizationRole.Admin), "HR", CancellationToken.None);

        // Assert
        Assert.That((byOwner.Value.MyPermissions.CanManage, byAdmin.Value.MyPermissions.CanManage), Is.EqualTo((true, true)));
    }

    [Test]
    public async Task GetAsync_OrganizationSpace_GuestAndPeopleNoLongerInTheOrganizationSeeNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var guest = await _host.Spaces.GetAsync(org.Guest(), "HR", CancellationToken.None);
        var stranger = await _host.Spaces.GetAsync(org.Stranger(), "HR", CancellationToken.None);

        // Assert
        Assert.That((guest.Failure?.Code, stranger.Failure?.Code), Is.EqualTo((KnowledgeErrorCodes.SpaceNotFound, KnowledgeErrorCodes.SpaceNotFound)));
        Assert.That(guest.Failure?.Kind, Is.EqualTo(FailureKind.NotFound));
    }

    [Test]
    public async Task GetAsync_SpaceOfAnotherOrganizationOrMalformedKey_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var other = new Org(_host);

        // Act
        var foreign = await _host.Spaces.GetAsync(other.Admin, "HR", CancellationToken.None);
        var malformed = await _host.Spaces.GetAsync(org.Admin, "H R", CancellationToken.None);
        var nothing = await _host.Spaces.GetAsync(org.Admin, null!, CancellationToken.None);

        // Assert
        Assert.That(
            (foreign.Failure?.Code, malformed.Failure?.Code, nothing.Failure?.Code),
            Is.EqualTo((KnowledgeErrorCodes.SpaceNotFound, KnowledgeErrorCodes.SpaceNotFound, KnowledgeErrorCodes.SpaceNotFound)));
    }

    [Test]
    public async Task GetAsync_ProjectSpace_PermissionsFollowTheProjectRole()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var viewer = org.Member();
        var editor = org.Member();
        var manager = org.Member();
        var outsider = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, "WEB", FakeWorkAccessQueries.EditorPermissions);
        _host.Work.Grant(org.Id, manager.PersonId, projectId, "WEB", FakeWorkAccessQueries.ManagerPermissions);

        // Act
        var asViewer = await _host.Spaces.GetAsync(viewer, space.Key, CancellationToken.None);
        var asEditor = await _host.Spaces.GetAsync(editor, space.Key, CancellationToken.None);
        var asManager = await _host.Spaces.GetAsync(manager, space.Key, CancellationToken.None);
        var asOutsider = await _host.Spaces.GetAsync(outsider, space.Key, CancellationToken.None);

        // Assert
        Assert.That(asViewer.Value.MyPermissions, Is.EqualTo(new SpacePermissions(false, false, false)));
        Assert.That(asEditor.Value.MyPermissions, Is.EqualTo(new SpacePermissions(true, true, false)));
        Assert.That(asManager.Value.MyPermissions, Is.EqualTo(new SpacePermissions(true, true, true)));
        Assert.That(asOutsider.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
    }

    [Test]
    public async Task GetAsync_ProjectSpace_GuestWhoIsAProjectMemberCanSeeIt()
    {
        // Arrange：訪客看不到組織空間，但專案空間沿用專案的規則。
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var guest = org.Guest();
        _host.Work.Grant(org.Id, guest.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);

        // Act
        var seen = await _host.Spaces.GetAsync(guest, space.Key, CancellationToken.None);

        // Assert
        Assert.That(seen.Succeeded, Is.True);
    }

    [Test]
    public async Task GetAsync_ProjectSpaceWhoseProjectIsArchived_IsReadOnlyEvenForTheManager()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        _host.Work.Grant(org.Id, org.Admin.PersonId, projectId, "WEB", [WorkPermissions.ItemRead, WorkPermissions.ProjectManage], archived: true);

        // Act
        var seen = await _host.Spaces.GetAsync(org.Admin, space.Key, CancellationToken.None);

        // Assert
        Assert.That(seen.Value.MyPermissions, Is.EqualTo(new SpacePermissions(CanEdit: false, CanComment: false, CanManage: true)));
        Assert.That(seen.Value.Project?.IsArchived, Is.True);
    }

    [Test]
    public async Task ListAsync_ShowsOnlyVisibleSpacesActiveOnesFirstThenByName()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("ZED");
        await org.CreateSpaceAsync("BETA");
        await org.CreateSpaceAsync("OLD");
        await _host.Spaces.SetArchivedAsync(org.Admin, "OLD", archived: true, CancellationToken.None);
        var viewer = org.Member();
        var (projectSpace, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var hiddenProjectId = Guid.NewGuid();
        _host.Work.Grant(org.Id, org.Admin.PersonId, hiddenProjectId, "SECRET", FakeWorkAccessQueries.ManagerPermissions);
        await _host.Spaces.CreateAsync(org.Admin, new NewSpace("SECRET", "別人的專案", null, "SECRET"), CancellationToken.None);
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        _host.Work.Revoke(org.Id, viewer.PersonId, Guid.NewGuid());

        // Act
        var listed = await _host.Spaces.ListAsync(viewer, CancellationToken.None);

        // Assert：沒封存的先、再依名稱（專案空間的名稱「專案空間 WEB」排在「空間 …」前面）。
        Assert.That(listed.Select(s => s.Key), Is.EqualTo(new[] { projectSpace.Key, "BETA", "ZED", "OLD" }));
    }

    [Test]
    public async Task ListAsync_AsksThePeopleDirectoryAndTheWorkEngineOnlyOnce()
    {
        // Arrange
        var org = new Org(_host);
        var (_, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "AAA");
        for (var index = 0; index < 3; index++)
        {
            var key = $"PR{index}";
            _host.Work.Grant(org.Id, org.Admin.PersonId, Guid.NewGuid(), key, FakeWorkAccessQueries.ManagerPermissions);
            await _host.Spaces.CreateAsync(org.Admin, new NewSpace(key, key, null, key), CancellationToken.None);
        }

        await org.CreateSpaceAsync("ORG1");
        await org.CreateSpaceAsync("ORG2");
        var before = _host.Work.FindVisibleProjectsCalls;

        // Act
        var listed = await _host.Spaces.ListAsync(org.Admin, CancellationToken.None);

        // Assert
        Assert.That(listed, Has.Count.EqualTo(6));
        Assert.That(_host.Work.FindVisibleProjectsCalls - before, Is.EqualTo(1));
        Assert.That(projectId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task ListAsync_OnlyAsksTheWorkEngineAboutTheProjectsSpacesAreBoundTo()
    {
        // Arrange
        var org = new Org(_host);
        var (_, boundProjectId) = await org.CreateProjectSpaceAsync(org.Admin, "BND");
        _host.Work.Grant(org.Id, org.Admin.PersonId, Guid.NewGuid(), "NOSPACE", FakeWorkAccessQueries.ManagerPermissions);
        var visibleBefore = _host.Work.VisibleProjectCalls;

        // Act
        await _host.Spaces.ListAsync(org.Admin, CancellationToken.None);

        // Assert
        Assert.That(_host.Work.LastRequestedProjectIds, Is.EqualTo(new[] { boundProjectId }));
        Assert.That(_host.Work.VisibleProjectCalls, Is.EqualTo(visibleBefore), "不替所有看得到的專案算權限");
    }

    [Test]
    public async Task ListAsync_PersonWithNoVisibleSpaces_GetsAnEmptyList()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var listed = await _host.Spaces.ListAsync(org.Guest(), CancellationToken.None);

        // Assert
        Assert.That(listed, Is.Empty);
    }
}
