using Microsoft.EntityFrameworkCore;
using PmTool.Knowledge.Persistence;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>同時進來的寫入：同一個空間的樹狀修改在鎖裡排隊，編號不重複、位置永遠 0 起連續、不超過上限。</summary>
[TestFixture]
public sealed class PageConcurrencyTests
{
    private const int Parallel = 12;

    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_ManyAtOnceInOneSpace_GetDistinctNumbersAndContiguousPositions()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, Parallel).Select(index =>
                _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, $"頁 {index}", null), CancellationToken.None)));
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(results.All(r => r.Succeeded), Is.True, () => string.Join(";", results.Select(r => r.Failure?.Message)));
        Assert.That(results.Select(r => r.Value.Number).Distinct().Count(), Is.EqualTo(Parallel));
        Assert.That(tree.Value.Pages.Select(p => p.Position), Is.EqualTo(Enumerable.Range(0, Parallel)));
        Assert.That(tree.Value.Pages.Select(p => p.Title).Order(), Is.EqualTo(Enumerable.Range(0, Parallel).Select(i => $"頁 {i}").Order()));
    }

    [Test]
    public async Task CreateAsync_ManyAtOnceUnderTheSameParent_KeepsThePositionsOfThatLevelContiguous()
    {
        // Arrange
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var parent = await org.PageAsync("HR", "上層");

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, Parallel).Select(index =>
                _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(parent.Number, $"子 {index}", null), CancellationToken.None)));
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(results.All(r => r.Succeeded), Is.True);
        Assert.That(tree.Value.Pages.Where(p => p.ParentId == parent.Id).Select(p => p.Position), Is.EqualTo(Enumerable.Range(0, Parallel)));
    }

    [Test]
    public async Task MoveAndDeleteAtOnceOnTheSameLevel_LeaveContiguousPositionsAndLoseNoPage()
    {
        // Arrange：8 頁同一層；其中 4 頁同時刪除、另外 4 頁同時搬到最後面。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var pages = new List<PageDetail>();
        for (var index = 0; index < 8; index++)
        {
            pages.Add(await org.PageAsync("HR", $"頁 {index}"));
        }

        var toDelete = pages.Where((_, index) => index % 2 == 0).ToList();
        var toMove = pages.Where((_, index) => index % 2 == 1).ToList();

        // Act
        var operations = toDelete.Select(p => (Task<bool>)DeleteAsync(org, p.Number))
            .Concat(toMove.Select(p => MoveToEndAsync(org, p.Number)));
        var outcomes = await Task.WhenAll(operations);
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);
        var trash = await _host.Pages.ListTrashAsync(org.Admin, "HR", new TrashQuery(), CancellationToken.None);

        // Assert
        Assert.That(outcomes, Is.All.True);
        Assert.That(tree.Value.Pages.Select(p => p.Position), Is.EqualTo(Enumerable.Range(0, toMove.Count)));
        Assert.That(tree.Value.Pages.Select(p => p.Number).Order(), Is.EqualTo(toMove.Select(p => p.Number).Order()));
        Assert.That(trash.Value.Items.Select(t => t.Page.Number).Order(), Is.EqualTo(toDelete.Select(p => p.Number).Order()));
    }

    [Test]
    public async Task CreateAsync_ManyAtOnceNearThePageLimit_NeverGoesOverIt()
    {
        // Arrange：只差 3 頁就滿了，同時送 10 個新增。
        const int room = 3;
        var org = new Org(_host);
        var space = await org.CreateSpaceAsync("HR");
        await SeedPagesAsync(org, space.Id, KnowledgeRules.MaxPagesPerSpace - room);

        // Act
        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(index =>
                _host.Pages.CreateAsync(org.Admin, "HR", new NewPage(null, $"搶 {index}", null), CancellationToken.None)));
        var tree = await _host.Pages.GetTreeAsync(org.Admin, "HR", CancellationToken.None);

        // Assert
        Assert.That(results.Count(r => r.Succeeded), Is.EqualTo(room));
        Assert.That(
            results.Where(r => !r.Succeeded).Select(r => r.Failure?.Code),
            Is.All.EqualTo(KnowledgeErrorCodes.PageLimitReached));
        Assert.That(tree.Value.Pages, Has.Count.EqualTo(KnowledgeRules.MaxPagesPerSpace));
    }

    private async Task<bool> DeleteAsync(Org org, int number) =>
        (await _host.Pages.DeleteAsync(org.Admin, "HR", number, CancellationToken.None)).Succeeded;

    private async Task<bool> MoveToEndAsync(Org org, int number) =>
        (await _host.Pages.MoveAsync(org.Admin, "HR", number, new PageMove(null, null), CancellationToken.None)).Succeeded;

    private async Task SeedPagesAsync(Org org, Guid spaceId, int count)
    {
        await using var db = await _host.CreateDbContextAsync();
        for (var number = 1; number <= count; number++)
        {
            db.Pages.Add(new PageRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = org.Id,
                SpaceId = spaceId,
                Number = number,
                Position = number - 1,
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
        await db.Spaces.Where(s => s.Id == spaceId).ExecuteUpdateAsync(set => set.SetProperty(s => s.NextPageNumber, count + 1));
    }
}
