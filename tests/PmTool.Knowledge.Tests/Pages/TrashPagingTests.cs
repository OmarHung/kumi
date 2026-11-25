using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Knowledge.Common;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>垃圾桶的清單分頁、只讀每一批最上面那一頁；批數與頁數有上限，滿了就不能再刪。</summary>
[TestFixture]
public sealed class TrashPagingTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    private async Task<List<PageDetail>> DeleteManyAsync(Org org, int count, string prefix = "頁")
    {
        var deleted = new List<PageDetail>();
        for (var index = 0; index < count; index++)
        {
            var page = await org.PageAsync("HR", $"{prefix}{index}");
            Assert.That((await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None)).Succeeded, Is.True);
            deleted.Add(page);
        }

        return deleted;
    }

    [Test]
    public async Task ListTrashAsync_FollowingTheCursor_ReturnsEveryBatchOnceNewestFirst()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var deleted = await DeleteManyAsync(org, 5);

        // Act
        var first = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(null, 2), CancellationToken.None);
        var second = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(first.Value.NextCursor, 2), CancellationToken.None);
        var third = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(second.Value.NextCursor, 2), CancellationToken.None);

        // Assert
        var numbers = first.Value.Items.Concat(second.Value.Items).Concat(third.Value.Items).Select(e => e.Page.Number).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(numbers, Is.EqualTo(deleted.Select(p => p.Number).Reverse().ToList()));
            Assert.That(first.Value.NextCursor, Is.Not.Null);
            Assert.That(second.Value.NextCursor, Is.Not.Null);
            Assert.That(third.Value.NextCursor, Is.Null);
            Assert.That(third.Value.Items, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ListTrashAsync_MoreBatchesThanOneReadChunk_StillPagesCompletely()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        await DeleteManyAsync(org, 120);

        // Act
        var first = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(null, KnowledgeRules.MaxTrashPageSize), CancellationToken.None);
        var second = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(first.Value.NextCursor, KnowledgeRules.MaxTrashPageSize), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value.Items, Has.Count.EqualTo(100));
            Assert.That(second.Value.Items, Has.Count.EqualTo(20));
            Assert.That(second.Value.NextCursor, Is.Null);
            Assert.That(first.Value.Items.Concat(second.Value.Items).Select(e => e.DeletionId).Distinct().Count(), Is.EqualTo(120));
        }
    }

    [Test]
    public async Task ListTrashAsync_ABatchIsOneEntryWithItsVisiblePageCount()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var top = await org.PageAsync("HR", "上");
        await org.PageAsync("HR", "下", parent: top.Number);
        await _host.Pages.DeleteAsync(org.Admin, "HR", top.Number, CancellationToken.None);

        // Act
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        Assert.That(trash.Value.Items.Select(e => (e.Page.Number, e.PageCount)), Is.EqualTo(new[] { (top.Number, 2) }));
    }

    [Test]
    public async Task ListTrashAsync_DefaultLimitIsFifty_AndBadQueriesAreRejected()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var query = new TrashQuery();

        // Act
        var zero = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(null, 0), CancellationToken.None);
        var tooMany = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(null, 101), CancellationToken.None);
        var garbage = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery("不是 cursor", 10), CancellationToken.None);
        var wrongShape = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery("YWJj", 10), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(query.Limit, Is.EqualTo(KnowledgeRules.DefaultTrashPageSize));
            Assert.That(zero.Failure?.Code, Is.EqualTo("query.invalid"));
            Assert.That(tooMany.Failure?.Code, Is.EqualTo("query.invalid"));
            Assert.That(garbage.Failure?.Code, Is.EqualTo("query.invalid"));
            Assert.That(wrongShape.Failure?.Code, Is.EqualTo("query.invalid"));
            Assert.That(garbage.Failure?.Kind, Is.EqualTo(FailureKind.Invalid));
        }
    }

    [Test]
    public async Task ListTrashAsync_CapacityIsOnlyShownToTheSpaceManager()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        await DeleteManyAsync(org, 3);

        // Act
        var byAdmin = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);
        var byMember = await _host.Pages.ListTrashAsync(org.Member(), "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byAdmin.Value.Capacity, Is.EqualTo(new TrashCapacity(3, KnowledgeRules.MaxTrashBatchesPerSpace)));
            Assert.That(byMember.Value.Capacity, Is.Null);
            Assert.That(byMember.Value.Items, Has.Count.EqualTo(3));
        }
    }

    [Test]
    public async Task ListTrashAsync_BatchesTheCallerCannotSee_AreSkippedWithoutLeavingAShortPage()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var member = org.Member();
        var visibleOne = await org.PageAsync("HR", "看得到一");
        var hidden = await org.PageAsync("HR", "看不到");
        var visibleTwo = await org.PageAsync("HR", "看得到二");
        await _host.Pages.SetRestrictionAsync(org.Admin, "HR", hidden.Number, [org.Admin.PersonId], CancellationToken.None);
        foreach (var page in new[] { visibleOne, hidden, visibleTwo })
        {
            await _host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None);
        }

        // Act
        var first = await _host.Pages.ListTrashAsync(member, "HR", new TrashQuery(null, 1), CancellationToken.None);
        var second = await _host.Pages.ListTrashAsync(member, "HR", new TrashQuery(first.Value.NextCursor, 1), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Value.Items.Select(e => e.Page.Number), Is.EqualTo(new[] { visibleTwo.Number }));
            Assert.That(second.Value.Items.Select(e => e.Page.Number), Is.EqualTo(new[] { visibleOne.Number }));
            Assert.That(second.Value.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task DeleteAsync_WhenTheTrashHasTheMaximumBatches_IsRefusedUntilOneIsPurged()
    {
        // Arrange
        await using var host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new KnowledgeLimits(MaxTrashBatchesPerSpace: 3)));
        var org = new Org(host);
        await org.CreateSpaceAsync("HR");
        var pages = new List<PageDetail>();
        for (var index = 0; index < 4; index++)
        {
            pages.Add(await org.PageAsync("HR", $"頁{index}"));
        }

        foreach (var page in pages.Take(3))
        {
            Assert.That((await host.Pages.DeleteAsync(org.Admin, "HR", page.Number, CancellationToken.None)).Succeeded, Is.True);
        }

        // Act
        var full = await host.Pages.DeleteAsync(org.Admin, "HR", pages[3].Number, CancellationToken.None);
        var trash = await host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);
        await host.Pages.PurgeTrashAsync(org.Admin, "HR", TrashPurgeRequest.Batch(trash.Value.Items[0].DeletionId), CancellationToken.None);
        var afterPurge = await host.Pages.DeleteAsync(org.Admin, "HR", pages[3].Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(full.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.TrashFull));
            Assert.That(full.Failure?.Kind, Is.EqualTo(FailureKind.Conflict));
            Assert.That(trash.Value.Capacity, Is.EqualTo(new TrashCapacity(3, 3)));
            Assert.That(afterPurge.Succeeded, Is.True);
        }
    }

    [Test]
    public async Task DeleteAsync_WhenTheTrashWouldHoldTooManyPages_IsRefused()
    {
        // Arrange
        await using var host = await KnowledgeTestHost.StartAsync(services => services.AddSingleton(new KnowledgeLimits(MaxTrashPagesPerSpace: 3)));
        var org = new Org(host);
        await org.CreateSpaceAsync("HR");
        var big = await org.PageAsync("HR", "大");
        await org.PageAsync("HR", "子一", parent: big.Number);
        await org.PageAsync("HR", "子二", parent: big.Number);
        var small = await org.PageAsync("HR", "小");
        var another = await org.PageAsync("HR", "另一個");

        // Act
        var bigDeleted = await host.Pages.DeleteAsync(org.Admin, "HR", big.Number, CancellationToken.None);
        var smallRefused = await host.Pages.DeleteAsync(org.Admin, "HR", small.Number, CancellationToken.None);
        var anotherRefused = await host.Pages.DeleteAsync(org.Admin, "HR", another.Number, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bigDeleted.Succeeded, Is.True);
            Assert.That(smallRefused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.TrashFull));
            Assert.That(anotherRefused.Failure?.Code, Is.EqualTo(KnowledgeErrorCodes.TrashFull));
        }
    }

    [Test]
    public async Task RestoreAsync_PageWhoseFormerParentWasMovedAfterTheDeletion_FollowsTheParentToItsNewPlace()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var parent = await org.PageAsync("HR", "父");
        var child = await org.PageAsync("HR", "子", parent: parent.Number);
        var newHome = await org.PageAsync("HR", "新家");
        await _host.Pages.DeleteAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        await _host.Pages.MoveAsync(org.Admin, "HR", parent.Number, new PageMove(newHome.Number, null), CancellationToken.None);

        // Act
        var restored = await _host.Pages.RestoreAsync(org.Admin, "HR", child.Number, CancellationToken.None);
        var outline = await org.OutlineAsync("HR");

        // Assert
        Assert.That(restored.Succeeded, Is.True);
        Assert.That(outline, Is.EqualTo(new[] { "新家", "父<" + newHome.Number, "子<" + parent.Number }));
    }
}
