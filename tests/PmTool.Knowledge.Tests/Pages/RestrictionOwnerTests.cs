using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Identity;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 放寬一份限制（拿掉、加人、搬出有限制的範圍）只有設定它的人與空間的管理者能做；收緊名單上的人都能做，而且不轉移擁有者。
/// </summary>
[TestFixture]
public sealed class RestrictionOwnerTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private async Task<PageRestriction> RestrictAsync(Actor actor, int number, params Actor[] people)
    {
        var result = await _host.Pages.SetRestrictionAsync(actor, "HR", number, [.. people.Select(p => p.PersonId)], CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        return result.Value;
    }

    private async Task<(Org Org, Actor Setter, Actor Listed, Actor Third, Actor Outsider, PageDetail Page)> ArrangeAsync()
    {
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var setter = org.Member();
        var listed = org.Member();
        var third = org.Member();
        var outsider = org.Member();
        var page = await org.PageAsync("HR", "頁面");
        await RestrictAsync(setter, page.Number, setter, listed, third);
        return (org, setter, listed, third, outsider, page);
    }

    [Test]
    public async Task SetRestrictionAsync_TheSetter_CanAddPeopleAndRemoveTheRestriction()
    {
        // Arrange
        var (org, setter, listed, third, outsider, page) = await ArrangeAsync();

        // Act
        var added = await _host.Pages.SetRestrictionAsync(setter, "HR", page.Number, [setter.PersonId, listed.PersonId, third.PersonId, outsider.PersonId], CancellationToken.None);
        var removed = await _host.Pages.SetRestrictionAsync(setter, "HR", page.Number, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Succeeded, Is.True);
            Assert.That(removed.Value.People, Is.Null);
            Assert.That(removed.Value.RestrictedBy, Is.Null);
            Assert.That(org.Id, Is.Not.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task SetRestrictionAsync_AnotherPersonOnTheList_CanRemovePeopleButNotAddOrLiftTheRestriction()
    {
        // Arrange
        var (_, setter, listed, third, outsider, page) = await ArrangeAsync();

        // Act
        var shrunk = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, [setter.PersonId, listed.PersonId], CancellationToken.None);
        var addedBack = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, [setter.PersonId, listed.PersonId, third.PersonId], CancellationToken.None);
        var addedOther = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, [setter.PersonId, listed.PersonId, outsider.PersonId], CancellationToken.None);
        var lifted = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, null, CancellationToken.None);
        var after = await _host.Pages.GetRestrictionAsync(setter, "HR", page.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shrunk.Succeeded, Is.True);
            Assert.That(shrunk.Value.RestrictedBy, Is.EqualTo(setter.PersonId), "收緊不轉移擁有者");
            Assert.That(shrunk.Value.CanRelax, Is.False);
            Assert.That(addedBack.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(addedBack.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(addedOther.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(lifted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(after.Value.People, Is.EqualTo(new[] { setter.PersonId, listed.PersonId }));
            Assert.That(after.Value.CanRelax, Is.True);
        }
    }

    [Test]
    public async Task SetRestrictionAsync_OrganizationAdmin_CanRelaxAnyRestrictionWithoutBecomingTheOwner()
    {
        // Arrange
        var (org, setter, _, _, outsider, page) = await ArrangeAsync();

        // Act
        var added = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [setter.PersonId, outsider.PersonId], CancellationToken.None);
        var lifted = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(added.Value.RestrictedBy, Is.EqualTo(setter.PersonId));
            Assert.That(added.Value.CanRelax, Is.True);
            Assert.That(lifted.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task SetRestrictionAsync_AddingARestrictionToAnOpenPage_MakesTheCallerTheOwner()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var first = org.Member();
        var second = org.Member();
        var page = await org.PageAsync("HR", "頁");

        // Act
        var restricted = await RestrictAsync(second, page.Number, first, second);
        var seenByFirst = await _host.Pages.GetRestrictionAsync(first, "HR", page.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restricted.RestrictedBy, Is.EqualTo(second.PersonId));
            Assert.That(seenByFirst.Value.CanRelax, Is.False);
            Assert.That(seenByFirst.Value.RestrictedBy, Is.EqualTo(second.PersonId));
        }
    }

    [Test]
    public async Task SetRestrictionAsync_SameListFromSomeoneWhoCannotRelax_IsNotRefused()
    {
        // Arrange
        var (_, setter, listed, third, _, page) = await ArrangeAsync();

        // Act
        var same = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, [setter.PersonId, listed.PersonId, third.PersonId], CancellationToken.None);

        // Assert
        Assert.That(same.Succeeded, Is.True);
    }

    [Test]
    public async Task SetRestrictionAsync_PageRestrictedBeforeOwnersWereRecorded_OnlyAnAdminCanRelaxIt()
    {
        // Arrange
        var (org, setter, listed, _, outsider, page) = await ArrangeAsync();
        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Pages.Where(p => p.Id == page.Id).ExecuteUpdateAsync(set => set.SetProperty(p => p.RestrictedBy, (Guid?)null));
        }

        // Act
        var bySetter = await _host.Pages.SetRestrictionAsync(setter, "HR", page.Number, [setter.PersonId, listed.PersonId, outsider.PersonId], CancellationToken.None);
        var byAdmin = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, null, CancellationToken.None);

        // Assert
        Assert.That(bySetter.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
        Assert.That(byAdmin.Succeeded, Is.True);
    }

    [Test]
    public async Task SetRestrictionAsync_ProjectManager_CanRelaxARestrictionAnEditorSet()
    {
        // Arrange
        var org = new Org(_host);
        var manager = org.Person(OrganizationRole.Member);
        var (space, projectId) = await org.CreateProjectSpaceAsync(manager);
        var editor = org.Person(OrganizationRole.Member);
        var peer = org.Person(OrganizationRole.Member);
        _host.Work.Grant(org.Id, editor.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);
        _host.Work.Grant(org.Id, peer.PersonId, projectId, space.Key, FakeWorkAccessQueries.EditorPermissions);
        var page = await org.PageAsync(space.Key, "頁", actor: editor);
        await _host.Pages.SetRestrictionAsync(editor, space.Key, page.Number, [editor.PersonId, peer.PersonId], CancellationToken.None);

        // Act
        var byPeer = await _host.Pages.SetRestrictionAsync(peer, space.Key, page.Number, null, CancellationToken.None);
        var byManager = await _host.Pages.SetRestrictionAsync(manager, space.Key, page.Number, null, CancellationToken.None);

        // Assert
        Assert.That(byPeer.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
        Assert.That(byManager.Succeeded, Is.True);
    }

    [Test]
    public async Task SetRestrictionAsync_HiddenDescendantsConflictComesBeforeTheOwnerCheck()
    {
        // Arrange
        var (org, setter, listed, _, _, page) = await ArrangeAsync();
        var child = await org.PageAsync("HR", "子", parent: page.Number, actor: setter);
        await _host.Pages.SetRestrictionAsync(setter, "HR", child.Number, [setter.PersonId], CancellationToken.None);

        // Act
        var refused = await _host.Pages.SetRestrictionAsync(listed, "HR", page.Number, null, CancellationToken.None);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
    }

    [Test]
    public async Task MoveAsync_OutOfSomeoneElsesRestriction_IsRefusedForOthersAndAllowedForTheSetter()
    {
        // Arrange：甲設的限制底下三層，垃圾桶裡還有一頁。
        var (org, setter, listed, _, _, top) = await ArrangeAsync();
        var middle = await org.PageAsync("HR", "中", parent: top.Number, actor: setter);
        var leaf = await org.PageAsync("HR", "葉", parent: middle.Number, actor: setter);
        var inTrash = await org.PageAsync("HR", "垃圾桶裡", parent: middle.Number, actor: setter);
        await _host.Pages.DeleteAsync(setter, "HR", inTrash.Number, CancellationToken.None);

        // Act
        var byListed = await _host.Pages.MoveAsync(listed, "HR", middle.Number, new PageMove(null, null), CancellationToken.None);
        var bySetter = await _host.Pages.MoveAsync(setter, "HR", middle.Number, new PageMove(null, null), CancellationToken.None);
        var outline = await org.OutlineAsync("HR", org.Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byListed.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(bySetter.Succeeded, Is.True);
            Assert.That(outline, Does.Contain("中").And.Contain("葉<" + middle.Number));
            Assert.That(leaf.Number, Is.GreaterThan(0));
        }
    }

    [Test]
    public async Task MoveAsync_FromOneRestrictionToAnotherWithTheSameOrANarrowerList_NeedsTheOwnerOrAnAdmin()
    {
        // Arrange
        var (org, setter, listed, third, _, source) = await ArrangeAsync();
        var moved = await org.PageAsync("HR", "要搬的", parent: source.Number, actor: setter);
        var same = await org.PageAsync("HR", "名單相同", actor: setter);
        var narrower = await org.PageAsync("HR", "名單比較小", actor: setter);
        var ownedByListed = await org.PageAsync("HR", "他自己設的", actor: listed);
        await RestrictAsync(setter, same.Number, setter, listed, third);
        await RestrictAsync(setter, narrower.Number, setter, listed);
        await RestrictAsync(listed, ownedByListed.Number, setter, listed, third);

        // Act
        var toSame = await _host.Pages.MoveAsync(listed, "HR", moved.Number, new PageMove(same.Number, null), CancellationToken.None);
        var toNarrower = await _host.Pages.MoveAsync(listed, "HR", moved.Number, new PageMove(narrower.Number, null), CancellationToken.None);
        var toOwnRestriction = await _host.Pages.MoveAsync(listed, "HR", moved.Number, new PageMove(ownedByListed.Number, null), CancellationToken.None);
        var byAdmin = await _host.Pages.MoveAsync(org.Admin, "HR", moved.Number, new PageMove(same.Number, null), CancellationToken.None);
        await _host.Pages.MoveAsync(org.Admin, "HR", moved.Number, new PageMove(source.Number, null), CancellationToken.None);
        var bySetter = await _host.Pages.MoveAsync(setter, "HR", moved.Number, new PageMove(narrower.Number, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(toSame.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(toNarrower.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(toOwnRestriction.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(toOwnRestriction.Failure?.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(byAdmin.Succeeded, Is.True, () => byAdmin.Failure?.Message ?? string.Empty);
            Assert.That(bySetter.Succeeded, Is.True, () => bySetter.Failure?.Message ?? string.Empty);
        }
    }

    [Test]
    public async Task MoveAsync_IntoARestrictionTheMoverSetHimself_DoesNotLetHimLeaveTheOriginalOne()
    {
        // Arrange：名單上的人自己設一個名單相同的限制頁，想把別人限制底下的頁面搬進去。
        var (org, setter, listed, third, _, source) = await ArrangeAsync();
        var moved = await org.PageAsync("HR", "要搬的", parent: source.Number, actor: setter);
        var mine = await org.PageAsync("HR", "我設的", actor: listed);
        await RestrictAsync(listed, mine.Number, setter, listed, third);

        // Act
        var refused = await _host.Pages.MoveAsync(listed, "HR", moved.Number, new PageMove(mine.Number, null), CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(setter, "HR", moved.Number, CancellationToken.None);
        var outline = await org.OutlineAsync("HR", org.Admin);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(restriction.Value.RestrictedAncestors.Select(a => a.Number), Is.EqualTo(new[] { source.Number }));
            Assert.That(outline, Does.Contain("要搬的<" + source.Number));
        }
    }

    [Test]
    public async Task MoveAsync_IntoAWiderRestriction_IsRefusedForOthers()
    {
        // Arrange
        var (org, setter, listed, third, outsider, source) = await ArrangeAsync();
        var moved = await org.PageAsync("HR", "要搬的", parent: source.Number, actor: setter);
        var wider = await org.PageAsync("HR", "名單比較大", actor: setter);
        await RestrictAsync(setter, wider.Number, setter, listed, third, outsider);

        // Act
        var refused = await _host.Pages.MoveAsync(listed, "HR", moved.Number, new PageMove(wider.Number, null), CancellationToken.None);
        var allowed = await _host.Pages.MoveAsync(setter, "HR", moved.Number, new PageMove(wider.Number, null), CancellationToken.None);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
        Assert.That(allowed.Succeeded, Is.True);
    }

    [Test]
    public async Task MoveAsync_IntoARestrictionAndOutOfNone_IsNotRelaxing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var one = org.Member();
        var two = org.Member();
        var open = await org.PageAsync("HR", "開放的");
        var secret = await org.PageAsync("HR", "限制的");
        await RestrictAsync(one, secret.Number, one, two);

        // Act
        var moved = await _host.Pages.MoveAsync(two, "HR", open.Number, new PageMove(secret.Number, null), CancellationToken.None);

        // Assert
        Assert.That(moved.Succeeded, Is.True);
    }

    [Test]
    public async Task RestoreAsync_RestrictionWrittenForTheRestoredPage_KeepsTheOriginalOwner()
    {
        // Arrange：丙在甲設的限制底下，丙被刪掉之後上層也被刪掉；乙把丙拿回來。
        var (org, setter, listed, _, _, restricted) = await ArrangeAsync();
        var child = await org.PageAsync("HR", "子", parent: restricted.Number, actor: setter);
        await _host.Pages.DeleteAsync(setter, "HR", child.Number, CancellationToken.None);
        await _host.Pages.DeleteAsync(setter, "HR", restricted.Number, CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(listed, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(listed, "HR", child.Number, CancellationToken.None);
        var lifted = await _host.Pages.SetRestrictionAsync(listed, "HR", child.Number, null, CancellationToken.None);
        var bySetter = await _host.Pages.SetRestrictionAsync(setter, "HR", child.Number, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Succeeded, Is.True, () => restored.Failure?.Message ?? string.Empty);
            Assert.That(restriction.Value.RestrictedBy, Is.EqualTo(setter.PersonId));
            Assert.That(restriction.Value.CanRelax, Is.False);
            Assert.That(lifted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(bySetter.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task RestoreAsync_LayersSetByDifferentPeople_LeaveNoOwnerSoOnlyAnAdminCanRelax()
    {
        // Arrange：甲設了外層，乙在裡面又設了一層；子頁和兩層都進了垃圾桶，甲把子頁拿回來。
        var (org, setter, listed, _, _, outer) = await ArrangeAsync();
        var inner = await org.PageAsync("HR", "內層", parent: outer.Number, actor: listed);
        await RestrictAsync(listed, inner.Number, setter, listed);
        var child = await org.PageAsync("HR", "子", parent: inner.Number, actor: setter);
        await _host.Pages.DeleteAsync(setter, "HR", child.Number, CancellationToken.None);
        await _host.Pages.DeleteAsync(setter, "HR", outer.Number, CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(setter, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(setter, "HR", child.Number, CancellationToken.None);
        var bySetter = await _host.Pages.SetRestrictionAsync(setter, "HR", child.Number, null, CancellationToken.None);
        var byAdmin = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", child.Number, null, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.Succeeded, Is.True, () => restored.Failure?.Message ?? string.Empty);
            Assert.That(restriction.Value.RestrictedBy, Is.Null);
            Assert.That(restriction.Value.CanRelax, Is.False);
            Assert.That(bySetter.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionOwnerRequired));
            Assert.That(byAdmin.Succeeded, Is.True);
        }
    }
}
