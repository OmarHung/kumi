using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>專案空間裡有 project.manage 的人，和組織管理員一樣看得到受限的頁面，才處理得了被限制給別人的頁面。</summary>
[TestFixture]
public sealed class ProjectManagerVisibilityTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private (Org Org, Actor Manager, Actor Editor, Actor Viewer, SpaceSummary Space) Arrange()
    {
        var org = new Org(_host);
        var manager = org.Person(OrganizationRole.Member);
        var (space, projectId) = org.CreateProjectSpaceAsync(manager).GetAwaiter().GetResult();
        var editor = org.Person(OrganizationRole.Member);
        var viewer = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, space.Key, FakeWorkAccessQueries.ViewerPermissions);
        return (org, manager, editor, viewer, space);
    }

    [Test]
    public async Task GetAsync_RestrictedPageInProjectSpace_IsVisibleToTheProjectManagerButNotToOtherMembers()
    {
        // Arrange
        var (org, manager, editor, viewer, space) = Arrange();
        var page = await org.PageAsync(space.Key, "只給編輯者", actor: editor);
        var restricted = await _host.Pages.SetRestrictionAsync(editor, space.Key, page.Number, [editor.PersonId], CancellationToken.None);
        Assert.That(restricted.Succeeded, Is.True);

        // Act
        var byManager = await _host.Pages.GetAsync(manager, space.Key, page.Number, CancellationToken.None);
        var byViewer = await _host.Pages.GetAsync(viewer, space.Key, page.Number, CancellationToken.None);
        var managerTree = await org.OutlineAsync(space.Key, manager);
        var viewerTree = await org.OutlineAsync(space.Key, viewer);

        // Assert
        Assert.That(byManager.Succeeded, Is.True);
        Assert.That(byViewer.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(managerTree, Is.EqualTo(new[] { "只給編輯者" }));
        Assert.That(viewerTree, Is.Empty);
    }

    [Test]
    public async Task DeleteAsync_ProjectManagerWithRestrictedChildrenHeCannotSeeInTheList_StillSucceeds()
    {
        // Arrange
        var (org, manager, editor, _, space) = Arrange();
        var parent = await org.PageAsync(space.Key, "父", actor: manager);
        var child = await org.PageAsync(space.Key, "子", parent: parent.Number, actor: editor);
        await _host.Pages.SetRestrictionAsync(editor, space.Key, child.Number, [editor.PersonId], CancellationToken.None);

        // Act
        var deleted = await _host.Pages.DeleteAsync(manager, space.Key, parent.Number, CancellationToken.None);

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure?.Message ?? string.Empty);
    }
}
