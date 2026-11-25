using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>
/// 復原時頁面原本靠上層的名單保護：放不回原本的上層時，那幾層的限制要變成這一頁自己的，不能因為復原就變成大家都看得到。
/// 都用祖 → 父 → 子的樹。
/// </summary>
[TestFixture]
public sealed class PageRestoreRestrictionTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task RestoreAsync_ParentWithAListIsStillInTheTrash_PageKeepsThatListAsItsOwn()
    {
        // Arrange：父有名單（甲），子沒有；子先刪、父後刪，所以子單獨復原時上層還在垃圾桶裡。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b) = (org.Member(), org.Member());
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", parent.Number, [a.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", parent.Number, CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(a, "HR", child.Number, CancellationToken.None);
        var byA = await _host.Pages.GetAsync(a, "HR", child.Number, CancellationToken.None);
        var byB = await _host.Pages.GetAsync(b, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Succeeded, Is.True);
        Assert.That(restored.Value.IsRestricted, Is.True);
        Assert.That(byA.Succeeded, Is.True);
        Assert.That(byB.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(restriction.Value.People, Is.EquivalentTo(new[] { org.Admin.PersonId, a.PersonId }));
    }

    [Test]
    public async Task RestoreAsync_ParentWasMovedTooDeepToGoBack_PageStaysHiddenFromPeopleOutsideTheParentsList()
    {
        // Arrange：父有名單（甲）、底下一條兩層的鏈整批進垃圾桶；父再搬到第 9 層，放回去會超過深度，所以只能放最上層。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b) = (org.Member(), org.Member());
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        var grandchild = await org.PageAsync("HR", "孫", parent: child.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", parent.Number, [a.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var anchor = await org.PageAsync("HR", "L1");
        for (var level = 2; level <= 8; level++)
        {
            anchor = await org.PageAsync("HR", $"L{level}", parent: anchor.Number);
        }

        await _host.Pages.MoveAsync(org.Admin, "HR", parent.Number, new PageMove(anchor.Number, null), CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(a, "HR", child.Number, CancellationToken.None);
        var childByB = await _host.Pages.GetAsync(b, "HR", child.Number, CancellationToken.None);
        var grandchildByB = await _host.Pages.GetAsync(b, "HR", grandchild.Number, CancellationToken.None);
        var grandchildByA = await _host.Pages.GetAsync(a, "HR", grandchild.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.Ancestors, Is.Empty);
        Assert.That(childByB.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(grandchildByB.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(grandchildByA.Succeeded, Is.True);
    }

    [Test]
    public async Task RestoreAsync_TwoLostLayersWithLists_PageGetsTheIntersection()
    {
        // Arrange：祖 {甲, 乙}、父 {甲, 丙}，子沒有名單；三頁依序進垃圾桶（各自一批），子放不回父。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b, c) = (org.Member(), org.Member(), org.Member());
        var (grand, parent, child) = await ChainAsync(org);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", grand.Number, [a.PersonId, b.PersonId], CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", parent.Number, [a.PersonId, c.PersonId], CancellationToken.None);
        await TrashLeafToRootAsync(org, child, parent, grand);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var seen = new Dictionary<string, bool>();
        foreach (var (name, actor) in new[] { ("甲", a), ("乙", b), ("丙", c) })
        {
            seen[name] = (await _host.Pages.GetAsync(actor, "HR", child.Number, CancellationToken.None)).Succeeded;
        }

        // Assert：名單是兩份的交集（甲，加上設定名單時自動列入的管理員）。
        Assert.That(restored.Value.Ancestors, Is.Empty);
        Assert.That(restriction.Value.People, Is.EquivalentTo(new[] { org.Admin.PersonId, a.PersonId }));
        Assert.That(seen, Is.EqualTo(new Dictionary<string, bool> { ["甲"] = true, ["乙"] = false, ["丙"] = false }));
    }

    [Test]
    public async Task RestoreAsync_LostListsHaveNoOneInCommon_OnlyTheRestoringPersonRemains()
    {
        // Arrange：兩份名單由不同的管理員設定，交集是空的。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b) = (org.Member(), org.Member());
        var otherAdmin = org.Owner();
        var (grand, parent, child) = await ChainAsync(org);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", grand.Number, [a.PersonId], CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(otherAdmin, "HR", parent.Number, [b.PersonId], CancellationToken.None);
        await TrashLeafToRootAsync(org, child, parent, grand);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var byA = await _host.Pages.GetAsync(a, "HR", child.Number, CancellationToken.None);
        var byB = await _host.Pages.GetAsync(b, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Succeeded, Is.True);
        Assert.That(restriction.Value.People, Is.EqualTo(new[] { org.Admin.PersonId }));
        Assert.That(byA.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(byB.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task RestoreAsync_PageHadItsOwnListToo_IsIntersectedWithTheLostLayers()
    {
        // Arrange：父 {甲, 丙}；子自己 {甲, 乙}。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b, c) = (org.Member(), org.Member(), org.Member());
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", parent.Number, [a.PersonId, c.PersonId], CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", child.Number, [a.PersonId, b.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", parent.Number, CancellationToken.None);

        // Act
        await _host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(restriction.Value.People, Is.EquivalentTo(new[] { org.Admin.PersonId, a.PersonId }));
    }

    [Test]
    public async Task RestoreAsync_NoLayerOfTheFormerChainWasRestricted_AddsNoRestriction()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var viewer = org.Member();
        var (grand, parent, child) = await ChainAsync(org);
        await TrashLeafToRootAsync(org, child, parent, grand);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var byViewer = await _host.Pages.GetAsync(viewer, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.IsRestricted, Is.False);
        Assert.That(restriction.Value.People, Is.Null);
        Assert.That(byViewer.Succeeded, Is.True);
    }

    [Test]
    public async Task RestoreAsync_GoesBackUnderTheSameParent_AddsNoListOfItsOwn()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b) = (org.Member(), org.Member());
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", parent.Number, [a.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(a, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(a, "HR", child.Number, CancellationToken.None);
        var byB = await _host.Pages.GetAsync(b, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(restored.Value.IsRestricted, Is.False);
        Assert.That(restored.Value.RestrictedAncestors.Select(r => r.Title), Is.EqualTo(new[] { "父" }));
        Assert.That(restriction.Value.People, Is.Null);
        Assert.That(byB.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    private async Task<(PageDetail Grand, PageDetail Parent, PageDetail Child)> ChainAsync(Org org)
    {
        var grand = await org.PageAsync("HR", "祖");
        var parent = await org.PageAsync("HR", "父", parent: grand.Number);
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        return (grand, parent, child);
    }

    /// <summary>子、父、祖依序各自進垃圾桶（各一批），之後復原子時它的上層鏈都在垃圾桶裡。</summary>
    private async Task TrashLeafToRootAsync(Org org, PageDetail child, PageDetail parent, PageDetail grand)
    {
        foreach (var page in new[] { child, parent, grand })
        {
            var deleted = await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None);
            Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        }
    }
}
