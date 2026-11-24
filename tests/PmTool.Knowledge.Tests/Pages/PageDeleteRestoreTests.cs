using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Tests.Pages;

[TestFixture]
public sealed class PageDeleteRestoreTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteAsync_PageWithChildren_PutsTheWholeBranchInOneBatch()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A11", parent: a1.Number);
        await org.PageAsync("HR", "B");

        // Act
        var deleted = await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        var read = await _host.Pages.GetAsync(org.Admin, "HR", a1.Number, CancellationToken.None);

        // Assert
        Assert.That((deleted.Value.PageCount, deleted.Value.Page.Number), Is.EqualTo((3, a.Number)));
        Assert.That(deleted.Value.DeletionId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(read.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public async Task DeleteAsync_RenumbersTheFormerLevel()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        await org.PageAsync("HR", "A");
        var b = await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "C");

        // Act
        await _host.Pages.DeleteAsync(org.Admin, "HR", b.Number, CancellationToken.None);
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(tree.Value.Pages.Select(p => (p.Title, p.Position)), Is.EqualTo(new[] { ("A", 0), ("C", 1) }));
    }

    [Test]
    public async Task DeleteAsync_ChildThatWasAlreadyInTheTrash_KeepsItsOwnBatch()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A2", parent: a.Number);
        var first = await _host.Pages.DeleteAsync(org.Admin, "HR", a1.Number, CancellationToken.None);

        // Act
        var second = await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", CancellationToken.None);

        // Assert：第二批只有 A 與 A2，A1 還在第一批。
        Assert.That(second.Value.PageCount, Is.EqualTo(2));
        Assert.That(trash.Value.Select(t => (t.Page.Title, t.PageCount, t.DeletionId)), Is.EqualTo(new[] { ("A", 2, second.Value.DeletionId), ("A1", 1, first.Value.DeletionId) }));
    }

    [Test]
    public async Task DeleteAsync_BranchWithAPageTheCallerCannotSee_IsRefusedAndDeletesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var a = await org.PageAsync("HR", "A");
        var secret = await org.PageAsync("HR", "秘密", parent: a.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var byMember = await _host.Pages.DeleteAsync(member, "HR", a.Number, CancellationToken.None);
        var detail = await _host.Pages.GetAsync(member, "HR", a.Number, CancellationToken.None);
        var byAdmin = await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);

        // Assert
        Assert.That(byMember.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(detail.Value.MyPermissions, Is.EqualTo(new PagePermissions(CanEdit: true, CanComment: true, CanDelete: false)));
        Assert.That(byAdmin.Value.PageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task DeleteAsync_HiddenPageThatIsAlreadyInTheTrash_DoesNotBlockTheDelete()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var a = await org.PageAsync("HR", "A");
        var secret = await org.PageAsync("HR", "秘密", parent: a.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", secret.Number, CancellationToken.None);

        // Act
        var byMember = await _host.Pages.DeleteAsync(member, "HR", a.Number, CancellationToken.None);

        // Assert
        Assert.That(byMember.Value.PageCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteAsync_UnknownDeletedOrNotPermittedPage_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var a = await org.PageAsync(space.Key, "A");
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);
        var gone = await org.PageAsync(space.Key, "已刪");
        await _host.Pages.DeleteAsync(org.Admin, space.Key, gone.Number, CancellationToken.None);

        // Act
        var forbidden = await _host.Pages.DeleteAsync(viewer, space.Key, a.Number, CancellationToken.None);
        var unknown = await _host.Pages.DeleteAsync(org.Admin, space.Key, 99, CancellationToken.None);
        var again = await _host.Pages.DeleteAsync(org.Admin, space.Key, gone.Number, CancellationToken.None);

        // Assert
        Assert.That(forbidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(again.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task RestoreAsync_PageWhoseParentIsStillThere_GoesBackToTheEndOfTheParent()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A2", parent: a.Number);
        await _host.Pages.DeleteAsync(org.Admin, "HR", a1.Number, CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", a1.Number, CancellationToken.None);
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(restored.Value.Ancestors.Single().Number, Is.EqualTo(a.Number));
        Assert.That(tree.Value.Pages.Where(p => p.ParentId == a.Id).Select(p => (p.Title, p.Position)), Is.EqualTo(new[] { ("A2", 0), ("A1", 1) }));
    }

    [Test]
    public async Task RestoreAsync_Branch_ComesBackWithItsStructureAndOrder()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "A1", parent: a.Number);
        var a2 = await org.PageAsync("HR", "A2", parent: a.Number);
        await org.PageAsync("HR", "A21", parent: a2.Number);
        await org.PageAsync("HR", "B");
        await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);

        // Act：從批次裡的子頁面按復原，整批都回來。
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", a2.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.Title, Is.EqualTo("A2"));
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "B", "A", "A1<1", "A2<1", "A21<3" }));
    }

    [Test]
    public async Task RestoreAsync_ParentIsInTheTrash_GoesToTheEndOfTheTopLevel()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        await _host.Pages.DeleteAsync(org.Admin, "HR", a1.Number, CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        await org.PageAsync("HR", "B");

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", a1.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.Ancestors, Is.Empty);
        Assert.That(await org.OutlineAsync("HR"), Is.EqualTo(new[] { "B", "A1" }));
    }

    [Test]
    public async Task RestoreAsync_PageThatIsNotInTheTrash_JustReturnsIt()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var viewer = org.Guest();

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        var again = await _host.Pages.RestoreAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        var byGuest = await _host.Pages.RestoreAsync(viewer, "HR", a.Number, CancellationToken.None);

        // Assert
        Assert.That((restored.Value.Number, again.Value.Number), Is.EqualTo((a.Number, a.Number)));
        Assert.That(byGuest.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
    }

    [Test]
    public async Task RestoreAsync_UnknownPageOrNotAllowedToEdit_IsRejected()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var a = await org.PageAsync(space.Key, "A");
        await _host.Pages.DeleteAsync(org.Admin, space.Key, a.Number, CancellationToken.None);
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);

        // Act
        var unknown = await _host.Pages.RestoreAsync(org.Admin, space.Key, 99, CancellationToken.None);
        var forbidden = await _host.Pages.RestoreAsync(viewer, space.Key, a.Number, CancellationToken.None);

        // Assert
        Assert.That(unknown.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(forbidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
    }

    [Test]
    public async Task RestoreAsync_WouldPassThePageLimit_IsPageLimitReachedAndKeepsTheTrash()
    {
        // Arrange
        var org = new Org(_host);
        var space = await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        await org.PageAsync("HR", "A1", parent: a.Number);
        await _host.Pages.DeleteAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        await using (var db = await _host.CreateDbContextAsync())
        {
            for (var number = 100; number < 100 + KnowledgeRules.MaxPagesPerSpace - 1; number++)
            {
                db.Pages.Add(new PageRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = org.Id,
                    SpaceId = space.Id,
                    Number = number,
                    Position = number - 100,
                    Title = $"頁 {number}",
                    Body = string.Empty,
                    Revision = 1,
                    IsRestricted = false,
                    CreatedBy = org.Admin.PersonId,
                    CreatedAt = KnowledgeTestHost.StartTime,
                    UpdatedBy = org.Admin.PersonId,
                    UpdatedAt = KnowledgeTestHost.StartTime,
                });
            }

            await db.SaveChangesAsync();
        }

        // Act：現有 4,999 頁，整批是 2 頁。
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", a.Number, CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(restored.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageLimitReached));
        Assert.That(trash.Value.Single().PageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task RestoreAsync_ParentMovedDeeperWhileTheChildWasInTheTrash_GoesToTheTopLevelInsteadOfTooDeep()
    {
        // Arrange：P 底下有一條 3 層的鏈 C1→C2→C3，整批進垃圾桶；P 再搬到第 9 層。放回 P 底下 C3 會在第 12 層。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var p = await org.PageAsync("HR", "P");
        var c1 = await org.PageAsync("HR", "C1", parent: p.Number);
        var c2 = await org.PageAsync("HR", "C2", parent: c1.Number);
        await org.PageAsync("HR", "C3", parent: c2.Number);
        await _host.Pages.DeleteAsync(org.Admin, "HR", c1.Number, CancellationToken.None);
        var anchor = await org.PageAsync("HR", "L1");
        for (var level = 2; level <= 8; level++)
        {
            anchor = await org.PageAsync("HR", $"L{level}", parent: anchor.Number);
        }

        await _host.Pages.MoveAsync(org.Admin, "HR", p.Number, new PageMove(anchor.Number, null), CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", c1.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.Ancestors, Is.Empty);
    }

    [Test]
    public async Task ListTrashAsync_ShowsEachBatchOnceNewestFirstWithItsFormerParent()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var a = await org.PageAsync("HR", "A");
        var a1 = await org.PageAsync("HR", "A1", parent: a.Number);
        await org.PageAsync("HR", "A11", parent: a1.Number);
        var b = await org.PageAsync("HR", "B");
        await _host.Pages.DeleteAsync(org.Admin, "HR", a1.Number, CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        await _host.Pages.DeleteAsync(org.Admin, "HR", b.Number, CancellationToken.None);

        // Act
        var trash = await _host.Pages.ListTrashAsync(org.Member(), "HR", CancellationToken.None);

        // Assert
        Assert.That(trash.Value.Select(t => t.Page.Title), Is.EqualTo(new[] { "B", "A1" }));
        Assert.That(trash.Value[1].PageCount, Is.EqualTo(2));
        Assert.That(trash.Value[1].FormerParent?.Title, Is.EqualTo("A"));
        Assert.That(trash.Value[0].FormerParent, Is.Null);
        Assert.That(trash.Value[0].DeletedBy, Is.EqualTo(org.Admin.PersonId));
    }

    [Test]
    public async Task ListTrashAsync_GuestOrUnknownSpace_IsNotFound()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var guest = await _host.Pages.ListTrashAsync(org.Guest(), "HR", CancellationToken.None);

        // Assert
        Assert.That(guest.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceNotFound));
    }

    [Test]
    public async Task ManyBatchesInTheTrash_DoNotChangeWhatTheEverydayPathsSee()
    {
        // Arrange：一個空間裡有 30 批垃圾桶裡的頁面（每批 2 頁），另外還有活的頁面。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var live = await org.PageAsync("HR", "活的");
        var liveChild = await org.PageAsync("HR", "活的子頁", parent: live.Number);
        var deletedNumbers = new List<int>();
        for (var index = 0; index < 30; index++)
        {
            var gone = await org.PageAsync("HR", $"舊{index}", parent: live.Number);
            await org.PageAsync("HR", $"舊{index}-子", parent: gone.Number);
            deletedNumbers.Add(gone.Number);
            await _host.Pages.DeleteAsync(org.Admin, "HR", gone.Number, CancellationToken.None);
        }

        // Act
        var outline = await org.OutlineAsync("HR");
        var detail = await _host.Pages.GetAsync(org.Admin, "HR", liveChild.Number, CancellationToken.None);
        var deletedRead = await _host.Pages.GetAsync(org.Admin, "HR", deletedNumbers[0], CancellationToken.None);
        var underDeleted = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(deletedNumbers[1], "放在垃圾桶裡的頁面底下", null), CancellationToken.None);
        var moveDeleted = await _host.Pages.MoveAsync(org.Admin, "HR", deletedNumbers[2], new PageMove(null, null), CancellationToken.None);
        var newPage = await _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(live.Number, "新的", null), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", CancellationToken.None);
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", deletedNumbers[5], CancellationToken.None);

        // Assert
        Assert.That(outline, Is.EqualTo(new[] { "活的", "活的子頁<" + live.Number }));
        Assert.That(detail.Value.Ancestors.Select(b => b.Title), Is.EqualTo(new[] { "活的" }));
        Assert.That(deletedRead.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(underDeleted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
        Assert.That(moveDeleted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(newPage.Value.Number, Is.GreaterThan(deletedNumbers[^1] + 1), "編號不重複使用");
        Assert.That(trash.Value, Has.Count.EqualTo(30));
        Assert.That(trash.Value.Select(t => t.PageCount), Is.All.EqualTo(2));
        Assert.That(restored.Succeeded, Is.True);
        Assert.That(restored.Value.Ancestors.Select(b => b.Title), Is.EqualTo(new[] { "活的" }));
    }
}
