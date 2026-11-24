using PmTool.Work;

namespace PmTool.Knowledge.Tests.Spaces;

[TestFixture]
public sealed class SpaceSettingsTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_Admin_ReplacesNameAndDescription()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var updated = await _host.Spaces.UpdateAsync(org.Admin, "HR", new SpaceSettings("新名稱", "新簡介"), CancellationToken.None);
        var reread = await _host.Spaces.GetAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That((updated.Value.Name, updated.Value.Description), Is.EqualTo(("新名稱", "新簡介")));
        Assert.That((reread.Value.Name, reread.Value.Description), Is.EqualTo(("新名稱", "新簡介")));
    }

    [Test]
    public async Task UpdateAsync_ClearingTheDescription_StoresNoDescription()
    {
        // Arrange
        var org = new Org(_host);
        await _host.Spaces.CreateAsync(org.Admin, new NewSpace("HR", "人資", "舊簡介", null), CancellationToken.None);

        // Act
        var updated = await _host.Spaces.UpdateAsync(org.Admin, "HR", new SpaceSettings("人資", null), CancellationToken.None);

        // Assert
        Assert.That(updated.Value.Description, Is.Null);
    }

    [Test]
    public async Task UpdateAsync_NoChange_DoesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var before = (await _host.Spaces.GetAsync(org.Admin, "HR", CancellationToken.None)).Value;

        // Act
        var updated = await _host.Spaces.UpdateAsync(org.Admin, "HR", new SpaceSettings(before.Name, before.Description), CancellationToken.None);

        // Assert
        Assert.That(updated.Value, Is.EqualTo(before));
    }

    [Test]
    public async Task UpdateAsync_MemberOrInvalidName_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var byMember = await _host.Spaces.UpdateAsync(org.Member(), "HR", new SpaceSettings("改名", null), CancellationToken.None);
        var invalid = await _host.Spaces.UpdateAsync(org.Admin, "HR", new SpaceSettings(" ", null), CancellationToken.None);
        var unknown = await _host.Spaces.UpdateAsync(org.Admin, "NOPE", new SpaceSettings("改名", null), CancellationToken.None);

        // Assert
        Assert.That(byMember.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(invalid.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNameRequired));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
    }

    [Test]
    public async Task SetArchivedAsync_ArchiveThenUnarchive_TogglesReadOnly()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var archived = await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: true, CancellationToken.None);
        var again = await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: true, CancellationToken.None);
        var restored = await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: false, CancellationToken.None);

        // Assert
        Assert.That(archived.Value.ArchivedAt, Is.Not.Null);
        Assert.That(archived.Value.MyPermissions, Is.EqualTo(new SpacePermissions(false, false, true)));
        Assert.That(again.Value.ArchivedAt, Is.EqualTo(archived.Value.ArchivedAt));
        Assert.That(restored.Value.ArchivedAt, Is.Null);
        Assert.That(restored.Value.MyPermissions.CanEdit, Is.True);
    }

    [Test]
    public async Task SetArchivedAsync_Member_IsForbidden()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var archived = await _host.Spaces.SetArchivedAsync(org.Member(), "HR", archived: true, CancellationToken.None);

        // Assert
        Assert.That(archived.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
    }

    [Test]
    public async Task SetArchivedAsync_ProjectSpaceWhoseProjectIsArchived_StaysReadOnlyAfterUnarchiving()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        await _host.Spaces.SetArchivedAsync(org.Admin, space.Key, archived: true, CancellationToken.None);
        _host.Work.Grant(org.Id, org.Admin.PersonId, projectId, "WEB", [WorkPermissions.ItemRead, WorkPermissions.ProjectManage], archived: true);

        // Act
        var unarchived = await _host.Spaces.SetArchivedAsync(org.Admin, space.Key, archived: false, CancellationToken.None);

        // Assert
        Assert.That(unarchived.Value.ArchivedAt, Is.Null);
        Assert.That(unarchived.Value.MyPermissions.CanEdit, Is.False);
    }

    [Test]
    public async Task SetArchivedAsync_ProjectManagerCanArchiveTheirProjectSpace()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Member();
        var (space, _) = await org.CreateProjectSpaceAsync(manager, "WEB");

        // Act
        var archived = await _host.Spaces.SetArchivedAsync(manager, space.Key, archived: true, CancellationToken.None);

        // Assert
        Assert.That(archived.Value.ArchivedAt, Is.Not.Null);
    }
}
