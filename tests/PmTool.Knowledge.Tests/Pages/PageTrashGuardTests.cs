using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 垃圾桶裡的頁面沿現在的上層算誰看得到，所以搬移與改限制要連垃圾桶裡的後代一起檢查：
/// 不然上層的名單一變，垃圾桶裡的頁面就多出（或少掉）能看的人。
/// </summary>
[TestFixture]
public sealed class PageTrashGuardTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task SetRestrictionAsync_RemovingTheParentListWhileAHiddenChildIsInTheTrash_IsRefusedAndNobodyGainsAccess()
    {
        // Arrange：人事 {甲,乙,丙}，考核 {甲,丙}；甲把人事縮成 {甲,乙}，丙就看不到考核；甲再把考核刪進垃圾桶。
        var scene = await SceneAsync();
        await _host.Pages.SetRestrictionAsync(scene.A, "HR", scene.Hr.Number, [scene.A.PersonId, scene.B.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(scene.A, "HR", scene.Review.Number, CancellationToken.None);

        // Act：乙看不到考核，拿掉人事的限制。
        var refused = await _host.Pages.SetRestrictionAsync(scene.B, "HR", scene.Hr.Number, null, CancellationToken.None);
        var trashOfC = await _host.Pages.ListTrashAsync(scene.C, "HR", new TrashQuery(), CancellationToken.None);
        var after = await _host.Pages.GetRestrictionAsync(scene.Org.Admin, "HR", scene.Hr.Number, CancellationToken.None);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(after.Value.People, Is.EqualTo(new[] { scene.A.PersonId, scene.B.PersonId }));
        Assert.That(trashOfC.Value.Items, Is.Empty);
    }

    [Test]
    public async Task MoveAsync_MovingTheMiddleLayerToTheTopWhileAHiddenPageIsInTheTrash_IsRefusedAndMovesNothing()
    {
        // Arrange：人事 → 中間層 → 考核；考核在垃圾桶裡，乙看不到它。
        var scene = await SceneAsync(withMiddle: true);
        await _host.Pages.SetRestrictionAsync(scene.A, "HR", scene.Hr.Number, [scene.A.PersonId, scene.B.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(scene.A, "HR", scene.Review.Number, CancellationToken.None);

        // Act
        var refused = await _host.Pages.MoveAsync(scene.B, "HR", scene.Middle!.Number, new PageMove(null, null), CancellationToken.None);
        var outline = await scene.Org.OutlineAsync("HR");

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(outline, Is.EqualTo(new[] { "人事", "中間層<" + scene.Hr.Number }));
    }

    [Test]
    public async Task SetRestrictionAsync_NarrowingTheParentToOneselfWhileATrashedPageBelongsToOthers_IsRefusedAndTheOwnerCanStillRestore()
    {
        // Arrange：人事 {甲,乙,丙}，考核 {甲,丙}；甲刪了考核。
        var scene = await SceneAsync();
        await _host.Pages.DeleteAsync(scene.A, "HR", scene.Review.Number, CancellationToken.None);

        // Act：乙看不到考核，把人事縮成只有自己，甲就看不到、也復原不了自己刪的頁面。
        var refused = await _host.Pages.SetRestrictionAsync(scene.B, "HR", scene.Hr.Number, [scene.B.PersonId], CancellationToken.None);
        var restored = await _host.Pages.RestoreAsync(scene.A, "HR", scene.Review.Number, CancellationToken.None);

        // Assert
        Assert.That(refused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        Assert.That(restored.Succeeded, Is.True);
    }

    [Test]
    public async Task MoveAndSetRestriction_OnlyTrashedPagesTheCallerCanSee_AreNotBlocked()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var top = await org.PageAsync("HR", "上層");
        var child = await org.PageAsync("HR", "子頁", parent: top.Number);
        var target = await org.PageAsync("HR", "目的地");
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Act
        var restricted = await _host.Pages.SetRestrictionAsync(member, "HR", top.Number, [member.PersonId], CancellationToken.None);
        var moved = await _host.Pages.MoveAsync(member, "HR", top.Number, new PageMove(target.Number, null), CancellationToken.None);

        // Assert
        Assert.That(restricted.Succeeded, Is.True);
        Assert.That(moved.Succeeded, Is.True);
    }

    [Test]
    public async Task MoveAndSetRestriction_OrganizationAdminIsNotBlockedByHiddenPagesInTheTrash()
    {
        // Arrange
        var scene = await SceneAsync();
        await _host.Pages.DeleteAsync(scene.A, "HR", scene.Review.Number, CancellationToken.None);
        var target = await scene.Org.PageAsync("HR", "目的地");

        // Act
        var restricted = await _host.Pages.SetRestrictionAsync(scene.Org.Admin, "HR", scene.Hr.Number, null, CancellationToken.None);
        var moved = await _host.Pages.MoveAsync(scene.Org.Admin, "HR", scene.Hr.Number, new PageMove(target.Number, null), CancellationToken.None);

        // Assert
        Assert.That(restricted.Succeeded, Is.True);
        Assert.That(moved.Succeeded, Is.True);
    }

    [Test]
    public async Task SetRestrictionAndMove_WhenAHiddenPageSitsDeeperInTheTrashThanTheTreeCanBe_AreRefused()
    {
        // Arrange：垃圾桶裡的一串（刪掉時最深 10 層）後來被搬到 9 層的深處，連起來比活的樹允許的還深；最底下那一頁只有乙看得到。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (caller, other) = (org.Member(), org.Member());
        var root = await org.PageAsync("HR", "Q");
        var chain = new List<PageDetail>();
        for (var level = 1; level <= KnowledgeRules.MaxDepth - 1; level++)
        {
            chain.Add(await org.PageAsync("HR", $"D{level}", parent: (chain.Count == 0 ? root : chain[^1]).Number));
        }

        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", chain[^1].Number, [other.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", chain[0].Number, CancellationToken.None);
        var top = await org.PageAsync("HR", "T1");
        var live = new List<PageDetail> { top };
        for (var level = 2; level <= KnowledgeRules.MaxDepth - 1; level++)
        {
            live.Add(await org.PageAsync("HR", $"T{level}", parent: live[^1].Number));
        }

        var moved = await _host.Pages.MoveAsync(org.Admin, "HR", root.Number, new PageMove(live[^1].Number, null), CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure?.Message ?? string.Empty);
        var elsewhere = await org.PageAsync("HR", "別處");

        // Act
        var restricted = await _host.Pages.SetRestrictionAsync(caller, "HR", top.Number, [caller.PersonId], CancellationToken.None);
        var relocated = await _host.Pages.MoveAsync(caller, "HR", top.Number, new PageMove(elsewhere.Number, null), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(restricted.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
            Assert.That(relocated.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageHasHiddenDescendants));
        }
    }

    private async Task<Scene> SceneAsync(bool withMiddle = false)
    {
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b, c) = (org.Member(), org.Member(), org.Member());
        var hr = await org.PageAsync("HR", "人事");
        var middle = withMiddle ? await org.PageAsync("HR", "中間層", parent: hr.Number) : null;
        var review = await org.PageAsync("HR", "考核", parent: (middle ?? hr).Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", hr.Number, [a.PersonId, b.PersonId, c.PersonId], CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", review.Number, [a.PersonId, c.PersonId], CancellationToken.None);
        return new Scene(org, a, b, c, hr, middle, review);
    }

    private sealed record Scene(Org Org, Actor A, Actor B, Actor C, PageDetail Hr, PageDetail? Middle, PageDetail Review);
}
