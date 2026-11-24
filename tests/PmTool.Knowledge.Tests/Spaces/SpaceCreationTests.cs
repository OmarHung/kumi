using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Knowledge.Tests.Spaces;

[TestFixture]
public sealed class SpaceCreationTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_OrganizationAdmin_CreatesAnOrganizationSpaceWithUppercaseKey()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(org.Admin, new NewSpace(" hr ", "  人資  ", "  新人手冊  ", null), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
        var space = created.Value;
        Assert.That((space.Key, space.Name, space.Description), Is.EqualTo(("HR", "人資", "新人手冊")));
        Assert.That((space.Project, space.ArchivedAt), Is.EqualTo(((SpaceProject?)null, (DateTimeOffset?)null)));
        Assert.That(space.MyPermissions, Is.EqualTo(new SpacePermissions(CanEdit: true, CanComment: true, CanManage: true)));
    }

    [Test]
    public async Task CreateAsync_Member_IsForbiddenFromCreatingAnOrganizationSpace()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(org.Member(), new NewSpace("HR", "人資", null, null), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(created.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
    }

    [Test]
    public async Task CreateAsync_InvalidFields_ReportsEveryProblemAtOnce()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(
            org.Admin, new NewSpace("1x", "   ", new string('a', KnowledgeRules.MaxSpaceDescriptionLength + 1), null), CancellationToken.None);

        // Assert
        Assert.That(
            created.Failure!.Details.Select(d => (d.Code, d.Path)),
            Is.EqualTo(new[]
            {
                (KnowledgeErrorCodes.SpaceKeyInvalid, "key"),
                (KnowledgeErrorCodes.SpaceNameRequired, "name"),
                (KnowledgeErrorCodes.SpaceDescriptionInvalid, "description"),
            }));
        Assert.That(created.Failure.Kind, Is.EqualTo(FailureKind.Invalid));
    }

    [Test]
    public async Task CreateAsync_NameTooLongOrWithControlCharacters_IsRejected()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var tooLong = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("AA", new string('字', 101), null, null), CancellationToken.None);
        var withNul = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("AB", "名稱\0", null, null), CancellationToken.None);
        var withZeroWidth = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("AC", "名​稱", null, null), CancellationToken.None);
        var descriptionWithNul = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("AD", "名稱", "簡\0介", null), CancellationToken.None);

        // Assert
        Assert.That(tooLong.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNameTooLong));
        Assert.That(withNul.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNameRequired));
        Assert.That(withZeroWidth.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNameRequired));
        Assert.That(descriptionWithNul.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceDescriptionInvalid));
    }

    [Test]
    public async Task CreateAsync_BlankDescription_IsStoredAsNoDescription()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("HR", "人資", "  \r\n ", null), CancellationToken.None);

        // Assert
        Assert.That(created.Value.Description, Is.Null);
    }

    [Test]
    public async Task CreateAsync_KeyAlreadyUsedEvenByAnArchivedSpace_IsKeyTaken()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: true, CancellationToken.None);

        // Act
        var duplicate = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("hr", "另一個", null, null), CancellationToken.None);

        // Assert
        Assert.That(duplicate.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceKeyTaken));
        Assert.That(duplicate.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
    }

    [Test]
    public async Task CreateAsync_SameKeyInAnotherOrganization_IsAllowed()
    {
        // Arrange
        await new Org(_host).CreateSpaceAsync("HR");
        var other = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(other.Admin, new NewSpace("HR", "別的組織的人資", null, null), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
    }

    [Test]
    public async Task CreateAsync_OrganizationAlreadyAtTheLimit_IsLimitReached()
    {
        // Arrange
        var org = new Org(_host);
        await using (var db = await _host.CreateDbContextAsync())
        {
            for (var index = 0; index < KnowledgeRules.MaxSpacesPerOrganization; index++)
            {
                db.Spaces.Add(new Persistence.SpaceRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org.Id,
                    Key = $"S{index}",
                    Name = $"空間 {index}",
                    NextPageNumber = 1,
                    CreatedBy = org.Admin.PersonId,
                    CreatedAt = KnowledgeTestHost.StartTime,
                    UpdatedBy = org.Admin.PersonId,
                    UpdatedAt = KnowledgeTestHost.StartTime,
                });
            }

            await db.SaveChangesAsync();
        }

        // Act
        var created = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("OVER", "多一個", null, null), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceLimitReached));
    }

    [Test]
    public async Task CreateAsync_TwoSimultaneousRequestsForTheSameKey_OnlyOneWins()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            Task.Run(() => _host.Spaces.CreateAsync(org.Admin, new NewSpace("RACE", $"競爭 {index}", null, null), CancellationToken.None))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Is.All.EqualTo(KnowledgeErrorCodes.SpaceKeyTaken));
    }

    [Test]
    public async Task CreateAsync_ProjectSpaceByAProjectManager_BindsTheProject()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Member();
        var projectId = Guid.NewGuid();
        _host.Work.Grant(org.Id, manager.PersonId, projectId, "WEB", FakeWorkAccessQueries.ManagerPermissions);

        // Act
        var created = await _host.Spaces.CreateAsync(manager, new NewSpace("WEBDOC", "網站文件", null, "web"), CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True);
        Assert.That(created.Value.Project, Is.EqualTo(new SpaceProject(projectId, "WEB", "專案 WEB", false)));
        Assert.That(created.Value.MyPermissions, Is.EqualTo(new SpacePermissions(true, true, true)));
    }

    [Test]
    public async Task CreateAsync_ProjectTheCallerCannotSee_IsProjectNotFound()
    {
        // Arrange
        var org = new Org(_host);

        // Act
        var created = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("WEBDOC", "網站文件", null, "NOPE"), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.ProjectNotFound));
        Assert.That(created.Failure?.Details.Single().Path, Is.EqualTo("projectKey"));
    }

    [Test]
    public async Task CreateAsync_ProjectWithoutManagePermission_IsForbiddenBeforeContentIsChecked()
    {
        // Arrange
        var org = new Org(_host);
        var editor = org.Member();
        _host.Work.Grant(org.Id, editor.PersonId, Guid.NewGuid(), "WEB", FakeWorkAccessQueries.EditorPermissions);

        // Act：代號與名稱都不合格，但沒有權限的人先拿到 Forbidden，不會靠 422 探到什麼。
        var created = await _host.Spaces.CreateAsync(editor, new NewSpace("1", " ", null, "WEB"), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
    }

    [Test]
    public async Task CreateAsync_ArchivedProject_IsSpaceArchived()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Member();
        _host.Work.Grant(org.Id, manager.PersonId, Guid.NewGuid(), "OLD", [WorkPermissions.ItemRead, WorkPermissions.ProjectManage], archived: true);

        // Act
        var created = await _host.Spaces.CreateAsync(manager, new NewSpace("OLDDOC", "舊文件", null, "OLD"), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
    }

    [Test]
    public async Task CreateAsync_ProjectThatAlreadyHasASpace_IsProjectTaken()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateProjectSpaceAsync(org.Admin, "WEB");

        // Act
        var second = await _host.Spaces.CreateAsync(org.Admin, new NewSpace("OTHER", "第二個", null, "WEB"), CancellationToken.None);

        // Assert
        Assert.That(second.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceProjectTaken));
    }
}
