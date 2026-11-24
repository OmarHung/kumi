namespace PmTool.Knowledge.Tests.Pages;

[TestFixture]
public sealed class PageMoveTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task MoveAsync_BeforeASibling_ReordersTheLevel()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "B");
        var c = await org.PageAsync("HR", "C");

        // Act：C 移到 A 前面。
        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", c.Number, new PageMove(null, a.Number), CancellationToken.None);

        // Assert
        Assert.That(moved.Value.Pages.Select(p => (p.Title, p.Position)), Is.EqualTo(new[] { ("C", 0), ("A", 1), ("B", 2) }));
    }

    [Test]
    public async Task MoveAsync_ToTheEndOfTheSameLevel_ReordersTheLevel()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "C");

        // Act
        await _host.Pages.MoveAsync(org.Admin, "HR", a.Number, new PageMove(null, null), CancellationToken.None);

        // Assert
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "B", "C", "A" }));
    }

    [Test]
    public async Task MoveAsync_UnderAnotherPage_CarriesTheSubtreeAndRenumbersBothLevels()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        var b1 = await org.PageAsync("HR", "B1", parent: b.Number);
        await org.PageAsync("HR", "B1x", parent: b1.Number);
        await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A2", parent: a.Number);

        // Act：B（連同 B1、B1x）搬到 A 底下，放在 A2 前面。
        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(a.Number, 6), CancellationToken.None);

        // Assert
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "A", "A1<1", "B<1", "B1<2", "B1x<3", "A2<1" }));
        Assert.That(moved.Value.Pages.Where(p => p.ParentId == moved.Value.Pages[0].Id).Select(p => p.Position), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(moved.Value.Pages.Single(p => p.Title == "A").Position, Is.EqualTo(0));
    }

    [Test]
    public async Task MoveAsync_OutOfALevel_ClosesTheGapOfTheOldLevel()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "A1", parent: a.Number);
        var a2 = await org.PageAsync("HR", "A2", parent: a.Number);
        await org.PageAsync("HR", "A3", parent: a.Number);

        // Act
        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", a2.Number, new PageMove(null, null), CancellationToken.None);

        // Assert
        Assert.That(moved.Value.Pages.Where(p => p.ParentId is not null).Select(p => (p.Title, p.Position)), Is.EqualTo(new[] { ("A1", 0), ("A3", 1) }));
        Assert.That(moved.Value.Pages.Single(p => p.Title == "A2").Position, Is.EqualTo(1));
    }

    [Test]
    public async Task MoveAsync_ToTheCurrentPlace_ChangesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        var c = await org.PageAsync("HR", "C");
        var before = (await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None)).Value.Pages;

        // Act：B 放在 C 前面 = 它現在的位置。
        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(null, c.Number), CancellationToken.None);
        var atEnd = await _host.Pages.MoveAsync(org.Admin, "HR", c.Number, new PageMove(null, null), CancellationToken.None);

        // Assert
        Assert.That(moved.Value.Pages.Select(p => (p.Number, p.Position, p.UpdatedAt)), Is.EqualTo(before.Select(p => (p.Number, p.Position, p.UpdatedAt))));
        Assert.That(atEnd.Value.Pages.Select(p => p.Number), Is.EqualTo(before.Select(p => p.Number)));
    }

    [Test]
    public async Task MoveAsync_DoesNotTouchTheRevisionOrUpdatedAt()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        _host.Clock.Advance(TimeSpan.FromHours(3));

        // Act
        await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(a.Number, null), CancellationToken.None);
        var reread = await _host.Pages.GetAsync(org.Admin, "HR", b.Number, CancellationToken.None);

        // Assert
        Assert.That((reread.Value.Revision, reread.Value.UpdatedAt, reread.Value.VersionCount), Is.EqualTo((b.Revision, b.UpdatedAt, 1)));
        Assert.That(reread.Value.Ancestors.Single().Number, Is.EqualTo(a.Number));
    }

    [Test]
    public async Task MoveAsync_UnderItselfOrItsOwnDescendant_IsPositionInvalid()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        var a11 = await org.PageAsync("HR", "A11", parent: a1.Number);

        // Act
        var underItself = await _host.Pages.MoveAsync(org.Admin, "HR", a.Number, new PageMove(a.Number, null), CancellationToken.None);
        var underChild = await _host.Pages.MoveAsync(org.Admin, "HR", a.Number, new PageMove(a1.Number, null), CancellationToken.None);
        var underGrandchild = await _host.Pages.MoveAsync(org.Admin, "HR", a.Number, new PageMove(a11.Number, null), CancellationToken.None);

        // Assert
        Assert.That(
            new[] { underItself, underChild, underGrandchild }.Select(r => r.Failure?.Code),
            Is.All.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "A", "A1<1", "A11<2" }));
    }

    [Test]
    public async Task MoveAsync_UnknownParentOrBadBeforeNumber_IsPositionInvalid()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        var b = await org.PageAsync("HR", "B");

        // Act
        var unknownParent = await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(99, null), CancellationToken.None);
        var notASibling = await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(null, a1.Number), CancellationToken.None);
        var beforeItself = await _host.Pages.MoveAsync(org.Admin, "HR", b.Number, new PageMove(null, b.Number), CancellationToken.None);

        // Assert
        Assert.That(unknownParent.Failure?.Details.Single().Path, Is.EqualTo("parentNumber"));
        Assert.That(notASibling.Failure?.Details.Single().Path, Is.EqualTo("beforeNumber"));
        Assert.That(beforeItself.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
    }

    [Test]
    public async Task MoveAsync_SubtreeThatWouldGoBeyondTenLevels_IsTooDeep()
    {
        // Arrange：一條 6 層的鏈（P1..P6），另一頁 Q 底下有 5 層（Q1..Q5 含 Q 自己）。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var chain = await org.PageAsync("HR", "P1");
        for (var level = 2; level <= 6; level++)
        {
            chain = await org.PageAsync("HR", $"P{level}", parent: chain.Number);
        }

        var q = await org.PageAsync("HR", "Q1");
        var qLeaf = q;
        for (var level = 2; level <= 5; level++)
        {
            qLeaf = await org.PageAsync("HR", $"Q{level}", parent: qLeaf.Number);
        }

        // Act：Q 放在 P6 底下 → Q1 在第 7 層、Q5 在第 11 層。放在 P5 底下 → Q5 剛好第 10 層。
        var tooDeep = await _host.Pages.MoveAsync(org.Admin, "HR", q.Number, new PageMove(chain.Number, null), CancellationToken.None);
        var fits = await _host.Pages.MoveAsync(org.Admin, "HR", q.Number, new PageMove(chain.Ancestors[^1].Number, null), CancellationToken.None);

        // Assert
        Assert.That(tooDeep.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageTooDeep));
        Assert.That(fits.Succeeded, Is.True);
    }

    [Test]
    public async Task MoveAsync_RenumbersHiddenSiblingsToo_SoPositionsStayContiguous()
    {
        // Arrange：最上層 A、B（限制給管理員一個人）、C、D；成員看不到 B。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "C");
        var d = await org.PageAsync("HR", "D");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", b.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act：成員把 D 放到 A 前面。
        await _host.Pages.MoveAsync(member, "HR", d.Number, new PageMove(null, 1), CancellationToken.None);

        // Assert：管理員看到全部，位置是 D、A、B、C 連續的 0、1、2、3，沒有撞號。
        var tree = (await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None)).Value.Pages;
        Assert.That(tree.Select(p => (p.Title, p.Position)), Is.EqualTo(new[] { ("D", 0), ("A", 1), ("B", 2), ("C", 3) }));
    }

    [Test]
    public async Task MoveAsync_BesideAHiddenSibling_ReportsPositionInvalidForTheHiddenBeforeNumber()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        await org.PageAsync("HR", "A");
        var secret = await org.PageAsync("HR", "秘密");
        var c = await org.PageAsync("HR", "C");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var moved = await _host.Pages.MoveAsync(member, "HR", c.Number, new PageMove(null, secret.Number), CancellationToken.None);

        // Assert
        Assert.That(moved.Failure?.Details.Single().Path, Is.EqualTo("beforeNumber"));
    }

    [Test]
    public async Task MoveAsync_PageTheCallerCannotSeeOrCannotEdit_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var a = await org.PageAsync(space.Key, "A");
        var b = await org.PageAsync(space.Key, "B");
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        await _host.Pages.SetRestrictionAsync(org.Admin, space.Key, b.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var forbidden = await _host.Pages.MoveAsync(viewer, space.Key, a.Number, new PageMove(null, null), CancellationToken.None);
        var hidden = await _host.Pages.MoveAsync(viewer, space.Key, b.Number, new PageMove(null, null), CancellationToken.None);
        var unknown = await _host.Pages.MoveAsync(org.Admin, space.Key, 99, new PageMove(null, null), CancellationToken.None);

        // Assert：看不到的頁面是 NotFound，不是 Forbidden（不透露它存在）。
        Assert.That(forbidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(hidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task MoveAsync_ArchivedSpace_IsSpaceArchived()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await _host.Spaces.SetArchivedAsync(org.Admin, "HR", archived: true, CancellationToken.None);

        // Act
        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", a.Number, new PageMove(null, null), CancellationToken.None);

        // Assert
        Assert.That(moved.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
    }

    [Test]
    public async Task GetTreeAsync_ListsPagesInPreorderWithTheirPositions()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "A1", parent: a.Number);

        // Act
        var tree = await _host.Pages.GetTreeAsync(org.Member(), "HR", CancellationToken.None);

        // Assert
        Assert.That(tree.Value.Pages.Select(p => p.Title), Is.EqualTo(new[] { "A", "A1", "B" }));
        Assert.That(tree.Value.Space.Key, Is.EqualTo("HR"));
    }

    [Test]
    public async Task GetTreeAsync_GuestOrUnknownSpace_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var guest = await _host.Pages.GetTreeAsync(org.Guest(), "HR", CancellationToken.None);
        var unknown = await _host.Pages.GetTreeAsync(org.Admin, "NOPE", CancellationToken.None);

        // Assert
        Assert.That((guest.Failure?.Code, unknown.Failure?.Code), Is.EqualTo((KnowledgeErrorCodes.SpaceNotFound, KnowledgeErrorCodes.SpaceNotFound)));
    }
}
