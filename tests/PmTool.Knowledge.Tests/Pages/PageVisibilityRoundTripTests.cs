using PmTool.Abstractions;

namespace PmTool.Knowledge.Tests.Pages;

/// <summary>刪除再復原不能改變誰讀得到哪一頁：不管照什麼順序刪、什麼順序拿回來。</summary>
[TestFixture]
public sealed class PageVisibilityRoundTripTests
{
    private KnowledgeTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await KnowledgeTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    // 層級的編號：0 是最上層，3 是最深。
    private static readonly int[][] DeleteOrders = [[3, 2, 1, 0], [0], [1, 3], [2, 0], [3, 1, 0]];
    private static readonly int[][] RestoreOrders = [[0, 1, 2, 3], [3, 2, 1, 0], [1, 0, 3, 2], [2, 3, 0, 1]];

    public static IEnumerable<TestCaseData> Orders() =>
        from del in DeleteOrders
        from restore in RestoreOrders
        select new TestCaseData(del, restore).SetName($"AfterRestoringEverything_EveryoneReadsTheSamePagesAsBefore(delete {string.Join(",", del)}; restore {string.Join(",", restore)})");

    [TestCaseSource(nameof(Orders))]
    public async Task AfterRestoringEverything_EveryoneReadsTheSamePagesAsBefore(int[] deleteOrder, int[] restoreOrder)
    {
        // Arrange：四層，名單各不相同；五種身分（組織管理員、四個一般成員，其中一個不在任何名單）。
        var org = new Org(_host);
        await org.CreateSpaceAsync("HR");
        var people = new[] { org.Admin, org.Member(), org.Member(), org.Member(), org.Member() };
        var (p1, p2, p3, p4) = (people[1], people[2], people[3], people[4]);
        var pages = new List<PageDetail> { await org.PageAsync("HR", "L1") };
        for (var level = 2; level <= 4; level++)
        {
            pages.Add(await org.PageAsync("HR", $"L{level}", parent: pages[^1].Number));
        }

        var lists = new[] { [p1, p2, p3], new[] { p1, p2, p3 }, [p1, p2], [p2, p3] };
        lists[0] = [p1, p2, p3, p4];
        for (var level = 0; level < 4; level++)
        {
            await _host.Pages.SetRestrictionAsync(org.Admin, "HR", pages[level].Number, [.. lists[level].Select(p => p.PersonId)], CancellationToken.None);
        }

        var before = await ReadMatrixAsync(people, pages);

        // Act
        foreach (var level in deleteOrder)
        {
            await _host.Pages.DeleteAsync(org.Admin, "HR", pages[level].Number, CancellationToken.None);
        }

        foreach (var level in restoreOrder)
        {
            await _host.Pages.RestoreAsync(org.Admin, "HR", pages[level].Number, CancellationToken.None);
        }

        var after = await ReadMatrixAsync(people, pages);

        // Assert
        Assert.That(after, Is.EqualTo(before));
        Assert.That(before.Count(row => row.Value), Is.GreaterThan(4), "對照表要有看得到與看不到兩種結果才有意義");
        Assert.That(before.Any(row => !row.Value), Is.True);
    }

    private async Task<IReadOnlyDictionary<(Guid Person, int Level), bool>> ReadMatrixAsync(IReadOnlyList<Actor> people, IReadOnlyList<PageDetail> pages)
    {
        var matrix = new Dictionary<(Guid, int), bool>();
        foreach (var person in people)
        {
            for (var level = 0; level < pages.Count; level++)
            {
                var read = await _host.Pages.GetAsync(person, "HR", pages[level].Number, CancellationToken.None);
                matrix[(person.PersonId, level)] = read.Succeeded;
            }
        }

        return matrix;
    }
}
