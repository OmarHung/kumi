using PmTool.Identity;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>頁面的限制：往下繼承、只能更嚴、組織管理員一律看得到（交接 2.2、4.2）。</summary>
[TestFixture]
public sealed class PageRestrictionTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ThreeLevels_OnlyPeopleOnEveryListInTheChainCanSeeThePage()
    {
        // Arrange：上上層限制 A、B；上層沒限制；這一頁限制 B、C。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (a, b, c, outsider) = (org.Member(), org.Member(), org.Member(), org.Member());
        var top = await org.PageAsync("HR", "上上層");
        var middle = await org.PageAsync("HR", "上層", parent: top.Number);
        var leaf = await org.PageAsync("HR", "這一頁", parent: middle.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", top.Number, [a.PersonId, b.PersonId], CancellationToken.None);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", leaf.Number, [b.PersonId, c.PersonId], CancellationToken.None);

        // Act
        var seenBy = new Dictionary<string, bool>();
        foreach (var (name, actor) in new[] { ("A", a), ("B", b), ("C", c), ("外人", outsider), ("管理員", org.Admin) })
        {
            var read = await _host.Pages.GetAsync(actor, "HR", leaf.Number, CancellationToken.None);
            seenBy[name] = read.Succeeded;
        }

        // Assert：只有 B（兩份名單都有）與管理員。C 在這一頁的名單裡，但上上層的名單沒有他。
        Assert.That(seenBy, Is.EqualTo(new Dictionary<string, bool> { ["A"] = false, ["B"] = true, ["C"] = false, ["外人"] = false, ["管理員"] = true }));
    }

    [Test]
    public async Task GetAsync_RestrictedPage_ShowsTheInheritedRestrictionsAndHidesTheTreeFromOthers()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var insider = org.Member();
        var outsider = org.Member();
        var top = await org.PageAsync("HR", "機密");
        var child = await org.PageAsync("HR", "子頁", parent: top.Number);
        await org.PageAsync("HR", "公開");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", top.Number, [insider.PersonId], CancellationToken.None);

        // Act
        var insiderRead = await _host.Pages.GetAsync(insider, "HR", child.Number, CancellationToken.None);
        var outsiderRead = await _host.Pages.GetAsync(outsider, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(insiderRead.Value.RestrictedAncestors.Select(r => r.Title), Is.EqualTo(new[] { "機密" }));
        Assert.That(insiderRead.Value.IsRestricted, Is.False);
        Assert.That(outsiderRead.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(await org.OutlineAsync("HR", insider), Is.EqualTo(new[] { "機密", "子頁<1", "公開" }));
        Assert.That(await org.OutlineAsync("HR", outsider), Is.EqualTo(new[] { "公開" }));
        Assert.That(await org.OutlineAsync("HR", org.Admin), Is.EqualTo(new[] { "機密", "子頁<1", "公開" }));
    }

    [Test]
    public async Task Tree_ShowsWhichPagesCarryTheirOwnRestriction()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var secret = await org.PageAsync("HR", "機密");
        await org.PageAsync("HR", "公開");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(tree.Value.Pages.Select(p => (p.Title, p.IsRestricted)), Is.EqualTo(new[] { ("機密", true), ("公開", false) }));
    }

    [Test]
    public async Task EveryReadPath_TreatsAPageTheCallerCannotSeeAsNonexistent()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var outsider = org.Member();
        var secret = await org.PageAsync("HR", "機密");
        var child = await org.PageAsync("HR", "子頁", parent: secret.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);

        // Act
        var read = await _host.Pages.GetAsync(outsider, "HR", secret.Number, CancellationToken.None);
        var update = await _host.Pages.UpdateAsync(outsider, "HR", secret.Number, new PageChanges("改", null, 1), CancellationToken.None);
        var delete = await _host.Pages.DeleteAsync(outsider, "HR", secret.Number, CancellationToken.None);
        var restore = await _host.Pages.RestoreAsync(outsider, "HR", child.Number, CancellationToken.None);
        var restriction = await _host.Pages.GetRestrictionAsync(outsider, "HR", secret.Number, CancellationToken.None);
        var setRestriction = await _host.Pages.SetRestrictionAsync(outsider, "HR", secret.Number, null, CancellationToken.None);
        var createUnder = await _host.Pages.CreateAsync(outsider, "HR", new NewPage(secret.Number, "偷偷放", null), CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(outsider, "HR", new TrashQuery(), CancellationToken.None);

        // Assert：垃圾桶裡的子頁面照原本的位置算，也看不到。
        Assert.That(
            new[] { read.Failure?.Code, update.Failure?.Code, delete.Failure?.Code, restore.Failure?.Code, restriction.Failure?.Code, setRestriction.Failure?.Code },
            Is.All.EqualTo(KnowledgeErrorCodes.PageNotFound));
        Assert.That(createUnder.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PagePositionInvalid));
        Assert.That(trash.Value.Items, Is.Empty);
    }

    [Test]
    public async Task Trash_ShowsABatchToTheAdminAndTheListedPeopleOnly()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var insider = org.Member();
        var outsider = org.Member();
        var secret = await org.PageAsync("HR", "機密");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [insider.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(org.Admin, "HR", secret.Number, CancellationToken.None);

        // Act
        var forInsider = await _host.Pages.ListTrashAsync(insider, "HR", new TrashQuery(), CancellationToken.None);
        var forOutsider = await _host.Pages.ListTrashAsync(outsider, "HR", new TrashQuery(), CancellationToken.None);
        var forAdmin = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        Assert.That((forInsider.Value.Items.Count, forOutsider.Value.Items.Count, forAdmin.Value.Items.Count), Is.EqualTo((1, 0, 1)));
    }

    [Test]
    public async Task SetRestrictionAsync_AddsTheCallerToTheListAndDropsDuplicates()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var editor = org.Member();
        var friend = org.Member();
        var page = await org.PageAsync("HR", "頁");

        // Act
        var set = await _host.Pages.SetRestrictionAsync(editor, "HR", page.Number, [friend.PersonId, friend.PersonId], CancellationToken.None);
        var read = await _host.Pages.GetRestrictionAsync(editor, "HR", page.Number, CancellationToken.None);
        var detail = await _host.Pages.GetAsync(editor, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That(set.Value.People, Is.EqualTo(new[] { editor.PersonId, friend.PersonId }));
        Assert.That(read.Value.People, Is.EqualTo(set.Value.People));
        Assert.That(detail.Value.IsRestricted, Is.True);
        Assert.That(detail.Value.Revision, Is.EqualTo(page.Revision), "改限制不算內容的修改");
    }

    [Test]
    public async Task SetRestrictionAsync_ResendingTheSameList_ChangesNothing()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var friend = org.Member();
        var page = await org.PageAsync("HR", "頁");
        var first = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [friend.PersonId], CancellationToken.None);

        // Act
        var again = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, first.Value.People, CancellationToken.None);

        // Assert
        Assert.That(again.Value.People, Is.EqualTo(new[] { org.Admin.PersonId, friend.PersonId }));
    }

    [Test]
    public async Task SetRestrictionAsync_NullOrEmpty_RemovesTheRestriction()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var friend = org.Member();
        var other = org.Member();
        var page = await org.PageAsync("HR", "頁");
        await _host.Pages.SetRestrictionAsync(friend, "HR", page.Number, [friend.PersonId], CancellationToken.None);

        // Act
        var removed = await _host.Pages.SetRestrictionAsync(friend, "HR", page.Number, [], CancellationToken.None);
        var removedAgain = await _host.Pages.SetRestrictionAsync(friend, "HR", page.Number, null, CancellationToken.None);
        var seenByOther = await _host.Pages.GetAsync(other, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That((removed.Value.People, removedAgain.Value.People), Is.EqualTo(((IReadOnlyList<Guid>?)null, (IReadOnlyList<Guid>?)null)));
        Assert.That(seenByOther.Value.IsRestricted, Is.False);
    }

    [Test]
    public async Task SetRestrictionAsync_ReplacesTheWholeList()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var (first, second) = (org.Member(), org.Member());
        var page = await org.PageAsync("HR", "頁");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [first.PersonId], CancellationToken.None);

        // Act
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [second.PersonId], CancellationToken.None);
        var firstRead = await _host.Pages.GetAsync(first, "HR", page.Number, CancellationToken.None);
        var secondRead = await _host.Pages.GetAsync(second, "HR", page.Number, CancellationToken.None);

        // Assert
        Assert.That((firstRead.Succeeded, secondRead.Succeeded), Is.EqualTo((false, true)));
    }

    [Test]
    public async Task SetRestrictionAsync_PersonOutsideTheOrganization_IsRestrictionInvalidWithTheIndex()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var friend = org.Member();
        var page = await org.PageAsync("HR", "頁");
        var stranger = Guid.NewGuid();

        // Act
        var set = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [friend.PersonId, stranger], CancellationToken.None);

        // Assert
        Assert.That(set.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionInvalid));
        Assert.That(set.Failure?.Details.Single().Path, Is.EqualTo("people[1]"));
    }

    [Test]
    public async Task SetRestrictionAsync_MoreThanTheMaximumPeople_IsRestrictionInvalid()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁");
        var crowd = Enumerable.Range(0, KnowledgeRules.MaxRestrictedPeople).Select(_ => org.Member().PersonId).ToList();

        // Act：100 個人，再加上設定的人自己就是 101。
        var tooMany = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, crowd, CancellationToken.None);
        var fits = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, [.. crowd.Take(KnowledgeRules.MaxRestrictedPeople - 1)], CancellationToken.None);

        // Assert
        Assert.That(tooMany.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionInvalid));
        Assert.That(tooMany.Failure?.Details.Single().Path, Is.EqualTo("people"));
        Assert.That(fits.Value.People, Has.Count.EqualTo(KnowledgeRules.MaxRestrictedPeople));
    }

    [Test]
    public async Task SetRestrictionAsync_ViewerOfAProjectSpace_IsForbiddenAndArchivedSpaceIsReadOnly()
    {
        // Arrange
        var org = new Org(_host);
        var (space, projectId) = await org.CreateProjectSpaceAsync(org.Admin, "WEB");
        var page = await org.PageAsync(space.Key, "頁");
        var viewer = org.Member();
        _host.Work.Grant(org.Id, viewer.PersonId, projectId, "WEB", FakeWorkAccessQueries.ViewerPermissions);

        // Act
        var forbidden = await _host.Pages.SetRestrictionAsync(viewer, space.Key, page.Number, null, CancellationToken.None);
        await _host.Spaces.SetArchivedAsync(org.Admin, space.Key, archived: true, CancellationToken.None);
        var archived = await _host.Pages.SetRestrictionAsync(org.Admin, space.Key, page.Number, null, CancellationToken.None);
        var stillReadable = await _host.Pages.GetRestrictionAsync(viewer, space.Key, page.Number, CancellationToken.None);

        // Assert
        Assert.That(forbidden.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.Forbidden));
        Assert.That(archived.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.SpaceArchived));
        Assert.That(stillReadable.Succeeded, Is.True);
    }

    [Test]
    public async Task GetRestrictionAsync_ReportsOwnListAndTheRestrictedAncestors()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var top = await org.PageAsync("HR", "上");
        var leaf = await org.PageAsync("HR", "下", parent: top.Number);
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", top.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var onLeaf = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", leaf.Number, CancellationToken.None);
        var onTop = await _host.Pages.GetRestrictionAsync(org.Admin, "HR", top.Number, CancellationToken.None);

        // Assert
        Assert.That(onLeaf.Value.People, Is.Null);
        Assert.That(onLeaf.Value.RestrictedAncestors.Select(r => r.Title), Is.EqualTo(new[] { "上" }));
        Assert.That(onTop.Value.People, Is.EqualTo(new[] { org.Admin.PersonId }));
        Assert.That(onTop.Value.RestrictedAncestors, Is.Empty);
    }

    [Test]
    public async Task OrganizationAdmin_AlwaysSeesRestrictedPagesEvenWhenNotOnTheList()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var other = org.Person(OrganizationRole.Admin);
        var member = org.Member();
        var page = await org.PageAsync("HR", "頁", actor: member);
        await _host.Pages.SetRestrictionAsync(member, "HR", page.Number, [member.PersonId], CancellationToken.None);

        // Act
        var read = await _host.Pages.GetAsync(other, "HR", page.Number, CancellationToken.None);
        var tree = await _host.Pages.GetTreeAsync(other, "HR", CancellationToken.None);

        // Assert
        Assert.That(read.Succeeded, Is.True);
        Assert.That(tree.Value.Pages, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NewPage_InsideARestrictedBranch_InheritsItAndIsHiddenFromOthers()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var insider = org.Member();
        var outsider = org.Member();
        var top = await org.PageAsync("HR", "機密");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", top.Number, [insider.PersonId], CancellationToken.None);

        // Act
        var child = await org.PageAsync("HR", "新子頁", parent: top.Number, actor: insider);
        var byOutsider = await _host.Pages.GetAsync(outsider, "HR", child.Number, CancellationToken.None);

        // Assert
        Assert.That(child.IsRestricted, Is.False);
        Assert.That(child.RestrictedAncestors.Single().Title, Is.EqualTo("機密"));
        Assert.That(byOutsider.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.PageNotFound));
    }

    [Test]
    public async Task Trash_PageCountOnlyCountsThePagesTheCallerCanSee()
    {
        // Arrange：X 刪了 P，P 底下有只有 X 看得到的 C；Y 看得到 P、看不到 C。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var deleter = org.Member();
        var other = org.Member();
        var parent = await org.PageAsync("HR", "P");
        var hidden = await org.PageAsync("HR", "C", parent: parent.Number);
        await _host.Pages.SetRestrictionAsync(deleter, "HR", hidden.Number, [deleter.PersonId], CancellationToken.None);
        await _host.Pages.DeleteAsync(deleter, "HR", parent.Number, CancellationToken.None);

        // Act
        var forDeleter = await _host.Pages.ListTrashAsync(deleter, "HR", new TrashQuery(), CancellationToken.None);
        var forOther = await _host.Pages.ListTrashAsync(other, "HR", new TrashQuery(), CancellationToken.None);
        var forAdmin = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        Assert.That(forDeleter.Value.Items.Single().PageCount, Is.EqualTo(2));
        Assert.That(forOther.Value.Items.Single().PageCount, Is.EqualTo(1), "看不到的頁數不能從垃圾桶洩漏出來");
        Assert.That(forAdmin.Value.Items.Single().PageCount, Is.EqualTo(2));
    }

    [Test]
    public async Task Tree_PositionsAreContiguousForTheCallerEvenWhenASiblingIsHidden()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var outsider = org.Member();
        await org.PageAsync("HR", "A");
        var secret = await org.PageAsync("HR", "B");
        await org.PageAsync("HR", "C");
        await org.PageAsync("HR", "C1");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", secret.Number, [org.Admin.PersonId], CancellationToken.None);

        // Act
        var forOutsider = await _host.Pages.GetTreeAsync(outsider, "HR", CancellationToken.None);
        var forAdmin = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert：看不到 B 的人，C 是第 1 個（不是第 2 個），位置不洩漏有隱藏的頁面。
        Assert.That(
            forOutsider.Value.Pages.Select(p => (p.Title, p.Position)),
            Is.EqualTo(new[] { ("A", 0), ("C", 1), ("C1", 2) }));
        Assert.That(
            forAdmin.Value.Pages.Select(p => (p.Title, p.Position)),
            Is.EqualTo(new[] { ("A", 0), ("B", 1), ("C", 2), ("C1", 3) }));
    }

    [Test]
    public async Task SetRestrictionAsync_AbsurdlyLongList_IsRestrictionInvalidWithoutLookingAnyoneUp()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁");
        var friend = org.Member();
        var flood = Enumerable.Repeat(friend.PersonId, KnowledgeRules.MaxRestrictedPeople * 20).ToList();

        // Act
        var set = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, flood, CancellationToken.None);

        // Assert
        Assert.That(set.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.RestrictionInvalid));
        Assert.That(set.Failure?.Details.Single().Path, Is.EqualTo("people"));
    }

    [Test]
    public async Task SetRestrictionAsync_ManyDuplicatesOfFewPeople_StillWorksAndKeepsTheFirstSeenOrder()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var page = await org.PageAsync("HR", "頁");
        var first = org.Member();
        var second = org.Member();
        var repeated = Enumerable.Range(0, 60).SelectMany(_ => new[] { first.PersonId, second.PersonId }).ToList();

        // Act
        var set = await _host.Pages.SetRestrictionAsync(org.Admin, "HR", page.Number, repeated, CancellationToken.None);

        // Assert
        Assert.That(set.Value.People, Is.EqualTo(new[] { org.Admin.PersonId, first.PersonId, second.PersonId }));
    }
}
