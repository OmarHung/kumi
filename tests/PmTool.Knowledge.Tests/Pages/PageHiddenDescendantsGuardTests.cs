using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Knowledge.Access;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>搬移、改限制和刪除一樣：底下有自己看不到的頁面時不能做，因為這會改變那些頁面誰看得到。</summary>
[TestFixture]
public sealed class PageHiddenDescendantsGuardTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SetRestrictionAsync_BranchWithAPageTheCallerCannotSee_IsRefusedAndChangesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var top = await org.PageAsync("HR", "上層");
        var secret = await org.PageAsync("HR", "秘密", parent: top.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var refused = await _host.Pages.SetRestrictionAsync(member, "HR", top.Number, [member.PersonId], CancellationToken.None);
        var after = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", top.Number, CancellationToken.None);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(refused.Failure?.Message, Does.Contain("限制"));
        Assert.That(after.Value.People, Is.Null);
    }

    [Test]
    public async Task MoveAsync_BranchWithAPageTheCallerCannotSee_IsRefusedAndMovesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var top = await org.PageAsync("HR", "上層");
        var secret = await org.PageAsync("HR", "秘密", parent: top.Number);
        var target = await org.PageAsync("HR", "目的地");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var refused = await _host.Pages.MoveAsync(member, "HR", top.Number, new PageMove(target.Number, null), CancellationToken.None);
        var outline = await org.OutlineAsync("HR");

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(refused.Failure?.Message, Does.Contain("搬移"));
        Assert.That(outline, Is.EqualTo(new[] { "上層", "秘密<" + top.Number, "目的地" }));
    }

    [Test]
    public async Task MoveAndSetRestriction_BranchWhoseDescendantsAreAllVisible_WorkAsBefore()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var top = await org.PageAsync("HR", "上層");
        await org.PageAsync("HR", "子頁", parent: top.Number);
        var target = await org.PageAsync("HR", "目的地");

        // Act
        var restricted = await _host.Pages.SetRestrictionAsync(member, "HR", top.Number, [member.PersonId], CancellationToken.None);
        var moved = await _host.Pages.MoveAsync(member, "HR", top.Number, new PageMove(target.Number, null), CancellationToken.None);

        // Assert
        Assert.That(restricted.Succeeded, Is.True);
        Assert.That(moved.Succeeded, Is.True);
    }

    [Test]
    public async Task MoveAndSetRestriction_OrganizationAdminIsNotBlockedByHiddenDescendants()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var top = await org.PageAsync("HR", "上層");
        var secret = await org.PageAsync("HR", "秘密", parent: top.Number);
        var target = await org.PageAsync("HR", "目的地");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [member.PersonId], CancellationToken.None);
        var otherAdmin = org.Owner();

        // Act
        var restricted = await _host.Pages.SetRestrictionAsync(otherAdmin, "HR", top.Number, [otherAdmin.PersonId], CancellationToken.None);
        var moved = await _host.Pages.MoveAsync(otherAdmin, "HR", top.Number, new PageMove(target.Number, null), CancellationToken.None);

        // Assert
        Assert.That(restricted.Succeeded, Is.True);
        Assert.That(moved.Succeeded, Is.True);
    }

    [Test]
    public async Task PageScope_LivePageWhoseParentIsNotInTheData_IsNotVisibleToOrdinaryPeople()
    {
        // Arrange：資料不一致：活的子頁，它的上層卻已經在垃圾桶裡（讀活的頁面時上層不在範圍內）。
        var org = new Org(_host);
        var space = await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        await using var db = await _host.CreateDbContextAsync();
        await db.Pages.Where(p => p.Id == parent.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.DeletedAt, KnowledgeTestHost.StartTime));

        // Act
        var row = await db.Spaces.SingleAsync(s => s.Id == space.Id);
        var context = new SpaceContext(row, null, new SpacePermissions(true, true, false), false);
        var byMember = await PageScope.LoadLiveAsync(db, member, context, CancellationToken.None);
        var byAdmin = await PageScope.LoadLiveAsync(db, org.Admin, context, CancellationToken.None);

        // Assert
        Assert.That(byMember.FindVisible(child.Number), Is.Null);
        Assert.That(byAdmin.FindVisible(child.Number), Is.Not.Null);
    }
}
