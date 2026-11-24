using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

[TestFixture]
public sealed class PageUpdateTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_TitleAndBody_BumpsTheRevisionAndRecordsWhoAndWhen()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "舊標題", body: "舊內文");
        var editor = org.Member();
        _host.Clock.Advance(TimeSpan.FromHours(2));

        // Act
        var updated = await _host.Pages.UpdateAsync(editor, "HR", page.Number, new PageChanges("新標題", "新內文", page.Revision), CancellationToken.None);

        // Assert
        Assert.That((updated.Value.Title, updated.Value.Body, updated.Value.Revision), Is.EqualTo(("新標題", "新內文", 2L)));
        Assert.That(updated.Value.UpdatedBy, Is.EqualTo(editor.PersonId));
        Assert.That(updated.Value.UpdatedAt, Is.GreaterThan(page.UpdatedAt));
        Assert.That((updated.Value.CreatedBy, updated.Value.CreatedAt), Is.EqualTo((page.CreatedBy, page.CreatedAt)));
    }

    [Test]
    public async Task UpdateAsync_OnlyTheBody_KeepsTheTitle()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "舊");

        // Act
        var updated = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "新", page.Revision), CancellationToken.None);
        var cleared = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "", updated.Value.Revision), CancellationToken.None);

        // Assert
        Assert.That((updated.Value.Title, updated.Value.Body), Is.EqualTo(("標題", "新")));
        Assert.That(cleared.Value.Body, Is.Empty);
    }

    [Test]
    public async Task UpdateAsync_StaleRevision_IsAConflictAndChangesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "原本");
        await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "別人先存了", page.Revision), CancellationToken.None);

        // Act
        var stale = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "我的版本", page.Revision), CancellationToken.None);
        var current = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(stale.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageConflict));
        Assert.That(stale.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
        Assert.That((current.Value.Body, current.Value.Revision), Is.EqualTo(("別人先存了", 2L)));
    }

    [Test]
    public async Task UpdateAsync_TwoSimultaneousSaves_OnlyOneWinsTheRevision()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題");

        // Act
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(index =>
            Task.Run(() => _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, $"版本 {index}", page.Revision), CancellationToken.None))));

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(1));
        Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Failure!.Code), Is.All.EqualTo(KnowledgeErrorCodes.PageConflict));
    }

    [Test]
    public async Task UpdateAsync_SameContent_ChangesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "內文");
        _host.Clock.Advance(TimeSpan.FromHours(1));
        var other = org.Member();

        // Act
        var nothing = await _host.Pages.UpdateAsync(other, "HR", page.Number, new PageChanges(null, null, page.Revision), CancellationToken.None);
        var same = await _host.Pages.UpdateAsync(other, "HR", page.Number, new PageChanges("標題", "內文", page.Revision), CancellationToken.None);

        // Assert：版本、更新的時刻與人都不變。
        foreach (var result in new[] { nothing.Value, same.Value })
        {
            Assert.That(
                (result.Revision, result.UpdatedAt, result.UpdatedBy, result.VersionCount),
                Is.EqualTo((page.Revision, page.UpdatedAt, page.UpdatedBy, page.VersionCount)));
        }
    }

    [Test]
    public async Task UpdateAsync_BodyThatDiffersOnlyByATrailingLineBreak_IsAChange()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "內文");

        // Act：頭尾的空白與換行是內容，不去掉。
        var updated = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, "內文\r\n", page.Revision), CancellationToken.None);

        // Assert
        Assert.That((updated.Value.Body, updated.Value.Revision), Is.EqualTo(("內文\n", 2L)));
    }

    [Test]
    public async Task UpdateAsync_StaleRevisionWithNothingToChange_IsStillAConflict()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題");

        // Act
        var stale = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges(null, null, 99), CancellationToken.None);

        // Assert
        Assert.That(stale.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageConflict));
    }

    [Test]
    public async Task UpdateAsync_InvalidContent_ReportsEveryProblemAndKeepsThePage()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "標題", body: "內文");

        // Act
        var invalid = await _host.Pages.UpdateAsync(
            org.Admin, "HR", page.Number, new PageChanges("", "-----BEGIN PRIVATE KEY-----", page.Revision), CancellationToken.None);
        var reread = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(
            invalid.Failure!.Details.Select(d => d.Code),
            Is.EqualTo(new[] { KnowledgeErrorCodes.PageTitleRequired, KnowledgeErrorCodes.PageBodySecret }));
        Assert.That(reread.Value.Revision, Is.EqualTo(page.Revision));
    }

    [Test]
    public async Task UpdateAsync_ViewerOfAProjectSpace_IsForbiddenAndUnknownPageIsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var page = await org.PageAsync(space.Key, "頁");
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);

        // Act
        var forbidden = await _host.Pages.UpdateAsync(viewer, space.Key, page.Number, new PageChanges("改", null, page.Revision), CancellationToken.None);
        var unknown = await _host.Pages.UpdateAsync(org.Admin, space.Key, 99, new PageChanges("改", null, 1), CancellationToken.None);
        var viewed = await _host.Pages.GetAsync(viewer, space.Key, page.Number, CancellationToken.None);

        // Assert
        Assert.That(forbidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(viewed.Value.MyPermissions, Is.EqualTo(new PagePermissions(false, false, false)));
    }

    [Test]
    public async Task UpdateAsync_DeletedPage_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁");
        await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Act
        var updated = await _host.Pages.UpdateAsync(org.Admin, "HR", page.Number, new PageChanges("改", null, page.Revision), CancellationToken.None);
        var read = await _host.Pages.GetAsync(org.Admin, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That((updated.Failure?.Code, read.Failure?.Code), Is.EqualTo((KnowledgeErrorCodes.PageNotFound, KnowledgeErrorCodes.PageNotFound)));
    }

    [Test]
    public async Task UpdateAsync_RenamingAPage_ShowsInTheTreeAndInTheChildrenAncestors()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var parent = await org.PageAsync("HR", "舊名");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);

        // Act
        await _host.Pages.UpdateAsync(org.Admin, "HR", parent.Number, new PageChanges("新名", null, parent.Revision), CancellationToken.None);
        var reread = await _host.Pages.GetAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(reread.Value.Ancestors.Single().Title, Is.EqualTo("新名"));
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "新名", "子<1" }));
    }
}
