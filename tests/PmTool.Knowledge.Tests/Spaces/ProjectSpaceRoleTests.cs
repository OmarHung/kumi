using PmTool.Abstractions;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Knowledge.Tests.Spaces;

/// <summary>專案空間的寫入看專案角色：要能新增項目、而且看得到全部的項目；只看自己項目的執行者看得到頁面、不能改。</summary>
[TestFixture]
public sealed class ProjectSpaceRoleTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private async Task<(Org Org, Actor Manager, Guid ProjectId, SpaceSummary Space)> ArrangeAsync()
    {
        var org = new Org(_host);
        var manager = org.Person(OrganizationRole.Member);
        var (space, projectId) = await org.CreateProjectSpaceAsync(manager);
        return (org, manager, projectId, space);
    }

    [Test]
    public async Task ExecutorWithOnlyTheirOwnItems_CanReadTheSpaceButNotWriteIt()
    {
        // Arrange
        var (org, manager, projectId, space) = await ArrangeAsync();
        var page = await org.PageAsync(space.Key, "文件", actor: manager);
        var executor = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, executor.PersonId, projectId, space.Key, FakeWorkAccessQueries.ExecutorPermissions);

        // Act
        var tree = await _host.Pages.GetTreeAsync(executor, space.Key, CancellationToken.None);
        var read = await _host.Pages.GetAsync(executor, space.Key, page.Number, CancellationToken.None);
        var created = await _host.Pages.CreateAsync(executor, space.Key, new NewPage(null, "新的", null), CancellationToken.None);
        var updated = await _host.Pages.UpdateAsync(executor, space.Key, page.Number, new PageChanges("改", null, page.Revision), CancellationToken.None);
        var deleted = await _host.Pages.DeleteAsync(executor, space.Key, page.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tree.Value.Pages, Has.Count.EqualTo(1));
            Assert.That(read.Value.MyPermissions.CanEdit, Is.False);
            Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(updated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(deleted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        }
    }

    [Test]
    public async Task ExecutorWithOnlyTheirOwnItems_CannotMoveRestoreRestrictOrAttachButCanStillRead()
    {
        // Arrange
        var (org, manager, projectId, space) = await ArrangeAsync();
        var page = await org.PageAsync(space.Key, "文件", actor: manager);
        var other = await org.PageAsync(space.Key, "另一頁", actor: manager);
        var trashed = await org.PageAsync(space.Key, "刪掉的", actor: manager);
        await _host.Pages.DeleteAsync(manager, space.Key, trashed.Number, CancellationToken.None);
        var executor = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, executor.PersonId, projectId, space.Key, FakeWorkAccessQueries.ExecutorPermissions);
        var upload = new NewPageAttachment { FileName = "a.txt", ContentType = "text/plain", Content = new MemoryStream("內容"u8.ToArray()) };

        // Act
        var moved = await _host.Pages.MoveAsync(executor, space.Key, page.Number, new PageMove(other.Number, null), CancellationToken.None);
        var restored = await _host.Pages.RestoreAsync(executor, space.Key, trashed.Number, CancellationToken.None);
        var restricted = await _host.Pages.SetRestrictionAsync(executor, space.Key, page.Number, [executor.PersonId], CancellationToken.None);
        var attached = await _host.Attachments.AddAsync(executor, space.Key, page.Number, upload, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(executor, space.Key, page.Number, CancellationToken.None);
        var attachments = await _host.Attachments.ListAsync(executor, space.Key, page.Number, CancellationToken.None);
        var outline = await org.OutlineAsync(space.Key, executor);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(moved.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(restored.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(restricted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(attached.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
            Assert.That(restriction.Succeeded, Is.True);
            Assert.That(attachments.Succeeded, Is.True);
            Assert.That(outline, Is.EquivalentTo(new[] { "文件", "另一頁" }));
        }
    }

    [Test]
    public async Task ExecutorCannotEditEvenAPageTheyWroteBeforeTheirRoleChanged()
    {
        // Arrange
        var (org, _, projectId, space) = await ArrangeAsync();
        var person = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, person.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);
        var page = await org.PageAsync(space.Key, "我寫的", actor: person);
        _host.Work.Grant(org.Id, person.PersonId, projectId, space.Key, FakeWorkAccessQueries.ExecutorPermissions);

        // Act
        var updated = await _host.Pages.UpdateAsync(person, space.Key, page.Number, new PageChanges("改", null, page.Revision), CancellationToken.None);

        // Assert
        Assert.That(updated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
    }

    [Test]
    public async Task RoleThatCanCreateItemsButNotReadAllOfThem_CannotWrite()
    {
        // Arrange
        var (org, manager, projectId, space) = await ArrangeAsync();
        var page = await org.PageAsync(space.Key, "文件", actor: manager);
        var person = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, person.PersonId, projectId, space.Key, [WorkPermissions.ItemCreate, WorkPermissions.CommentCreate]);

        // Act
        var created = await _host.Pages.CreateAsync(person, space.Key, new NewPage(null, "新的", null), CancellationToken.None);

        // Assert
        Assert.That(created.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(page.Number, Is.EqualTo(1));
    }

    [Test]
    public async Task EditorAndManager_CanStillWrite()
    {
        // Arrange
        var (org, manager, projectId, space) = await ArrangeAsync();
        var editor = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);

        // Act
        var byEditor = await _host.Pages.CreateAsync(editor, space.Key, new NewPage(null, "編輯者的", null), CancellationToken.None);
        var byManager = await _host.Pages.CreateAsync(manager, space.Key, new NewPage(null, "管理者的", null), CancellationToken.None);

        // Assert
        Assert.That(byEditor.Succeeded && byManager.Succeeded, Is.True);
    }

    [Test]
    public async Task ProjectManager_SeesAndDeletesARestrictedPageHeIsNotListedOn()
    {
        // Arrange
        var (org, manager, projectId, space) = await ArrangeAsync();
        var editor = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);
        var page = await org.PageAsync(space.Key, "祕密", actor: editor);
        await _host.Pages.SetRestrictionAsync(editor, space.Key, page.Number, [editor.PersonId], CancellationToken.None);

        // Act
        var read = await _host.Pages.GetAsync(manager, space.Key, page.Number, CancellationToken.None);
        var deleted = await _host.Pages.DeleteAsync(manager, space.Key, page.Number, CancellationToken.None);

        // Assert
        Assert.That(read.Succeeded && deleted.Succeeded, Is.True);
    }
}
