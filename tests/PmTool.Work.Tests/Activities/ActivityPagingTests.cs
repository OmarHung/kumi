using System.Buffers.Text;
using System.Text;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>活動的分頁：依時間由舊到新、同一時間依編號，游標以鍵為準，不是位移量。</summary>
[TestFixture]
public sealed class ActivityPagingTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_MoreThanOnePage_ReadsEveryActivityOnceOldestFirst()
    {
        // Arrange：建立加上七則留言，每則之間時間都有走。
        var (scenario, item) = await ItemWithCommentsAsync(7, TimeSpan.FromMinutes(1));

        // Act
        var pages = await PagesAsync(scenario, item.Key, limit: 3);

        // Assert
        Assert.That(pages.Select(p => p.Items.Count), Is.EqualTo(new[] { 3, 3, 2 }));
        Assert.That(pages[^1].NextCursor, Is.Null);
        Assert.That(
            pages.SelectMany(p => p.Items).Select(a => a.Comment),
            Is.EqualTo(new[] { null, "留言 1", "留言 2", "留言 3", "留言 4", "留言 5", "留言 6", "留言 7" }));
    }

    [Test]
    public async Task ListAsync_ExactlyAMultipleOfTheLimit_EndsWithoutAnEmptyPage()
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(3, TimeSpan.FromMinutes(1));

        // Act
        var pages = await PagesAsync(scenario, item.Key, limit: 2);

        // Assert
        Assert.That(pages.Select(p => p.Items.Count), Is.EqualTo(new[] { 2, 2 }));
        Assert.That(pages[^1].NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_ManyActivitiesAtTheSameTime_AreOrderedByIdWithNoGapsOrRepeatsAcrossPages()
    {
        // Arrange：時鐘不動，十筆活動的時間都一樣，只能靠編號分先後。
        var (scenario, item) = await ItemWithCommentsAsync(9, TimeSpan.Zero);

        // Act
        var pages = await PagesAsync(scenario, item.Key, limit: 3);
        var activities = pages.SelectMany(p => p.Items).ToArray();

        // Assert：資料庫的 uuid 依位元組排序，和十六進位文字的字典順序相同。
        Assert.That(pages.Select(p => p.Items.Count), Is.EqualTo(new[] { 3, 3, 3, 1 }));
        Assert.That(activities.Select(a => a.At).Distinct(), Is.EqualTo(new[] { item.CreatedAt }));
        Assert.That(activities.Select(a => a.Id).Distinct().Count(), Is.EqualTo(10));
        Assert.That(activities.Select(a => a.Id.ToString("N")), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
        Assert.That(activities.Count(a => a.Kind == ActivityKind.Commented), Is.EqualTo(9));
    }

    [Test]
    public async Task ListAsync_ActivityAddedBetweenPages_ShowsUpOnTheNextPageWithoutRepeats()
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(2, TimeSpan.FromMinutes(1));
        var first = await PageAsync(scenario, item.Key, new ActivityQuery { Limit = 2 });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "翻頁時新增的", null, CancellationToken.None);

        // Act
        var second = await PageAsync(scenario, item.Key, new ActivityQuery { Limit = 2, Cursor = first.NextCursor });

        // Assert
        Assert.That(first.Items.Select(a => a.Comment), Is.EqualTo(new[] { null, "留言 1" }));
        Assert.That(second.Items.Select(a => a.Comment), Is.EqualTo(new[] { "留言 2", "翻頁時新增的" }));
        Assert.That(second.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_DefaultQuery_ReturnsUpToFiftyAndACursor()
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(50, TimeSpan.Zero);

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery());

        // Assert
        Assert.That(page.Items, Has.Count.EqualTo(50));
        Assert.That(page.NextCursor, Is.Not.Null);
    }

    [TestCase(1)]
    [TestCase(200)]
    public async Task ListAsync_LimitAtTheBoundaries_IsAccepted(int limit)
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(1, TimeSpan.Zero);

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Limit = limit });

        // Assert
        Assert.That(page.Items, Has.Count.EqualTo(Math.Min(limit, 2)));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(201)]
    [TestCase(int.MaxValue)]
    public async Task ListAsync_LimitOutOfRange_IsQueryInvalid(int limit)
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(1, TimeSpan.Zero);

        // Act
        var result = await _host.Activity.ListAsync(scenario.Admin, item.Key, new ActivityQuery { Limit = limit }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "limit");
    }

    public static IEnumerable<TestCaseData> TamperedCursors()
    {
        const string id = "0192f0d2a5c47b3e8f1a2b3c4d5e6f70";
        yield return new TestCaseData(string.Empty).SetArgDisplayNames("空字串");
        yield return new TestCaseData("不是游標").SetArgDisplayNames("不是 base64url");
        yield return new TestCaseData("a" + (char)0x0000).SetArgDisplayNames("帶 NUL");
        yield return new TestCaseData(new string('A', 97)).SetArgDisplayNames("太長");
        yield return new TestCaseData(Encode("o:5")).SetArgDisplayNames("項目清單的游標");
        yield return new TestCaseData(Encode("a:638000000000000000")).SetArgDisplayNames("少了編號");
        yield return new TestCaseData(Encode("a:-1:" + id)).SetArgDisplayNames("負的時間");
        yield return new TestCaseData(Encode("a:+1:" + id)).SetArgDisplayNames("帶正號的時間");
        yield return new TestCaseData(Encode("a::" + id)).SetArgDisplayNames("沒有時間");
        yield return new TestCaseData(Encode("a:99999999999999999999:" + id)).SetArgDisplayNames("超出範圍的時間");
        yield return new TestCaseData(Encode("a:3155378976000000000:" + id)).SetArgDisplayNames("超過最大日期");
        yield return new TestCaseData(Encode("a:638000000000000000" + (char)0x0000 + ":" + id)).SetArgDisplayNames("時間後面帶 NUL");
        yield return new TestCaseData(Encode("a:638000000000000000:not-a-guid")).SetArgDisplayNames("編號不是 uuid");
        yield return new TestCaseData(Encode("a:638000000000000000:" + id.ToUpperInvariant())).SetArgDisplayNames("編號大寫");
        yield return new TestCaseData(Encode("a:638000000000000000:0192f0d2-a5c4-7b3e-8f1a-2b3c4d5e6f70")).SetArgDisplayNames("編號帶連字號");
        yield return new TestCaseData(Encode("a:638000000000000000:" + id + ":x")).SetArgDisplayNames("後面多了東西");
    }

    [TestCaseSource(nameof(TamperedCursors))]
    public async Task ListAsync_TamperedCursor_IsQueryInvalid(string cursor)
    {
        // Arrange
        var (scenario, item) = await ItemWithCommentsAsync(1, TimeSpan.Zero);

        // Act
        var result = await _host.Activity.ListAsync(scenario.Admin, item.Key, new ActivityQuery { Cursor = cursor }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "cursor");
    }

    [Test]
    public async Task ListAsync_WellFormedCursorFromLongAgo_StartsFromTheBeginning()
    {
        // Arrange：格式對的游標就是一個位置；比所有活動都早的位置，等於從頭讀。
        var (scenario, item) = await ItemWithCommentsAsync(1, TimeSpan.Zero);
        var cursor = Encode("a:0:00000000000000000000000000000000");

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Cursor = cursor });

        // Assert
        Assert.That(page.Items, Has.Count.EqualTo(2));
    }

    private static string Encode(string text) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));

    private async Task<(Scenario Scenario, WorkItem Item)> ItemWithCommentsAsync(int comments, TimeSpan between)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        foreach (var n in Enumerable.Range(1, comments))
        {
            _host.Clock.Advance(between);
            var result = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, $"留言 {n}", null, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }

        return (scenario, item);
    }

    private async Task<ItemActivityPage> PageAsync(Scenario scenario, string itemKey, ActivityQuery query)
    {
        var result = await _host.Activity.ListAsync(scenario.Admin, itemKey, query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private async Task<IReadOnlyList<ItemActivityPage>> PagesAsync(Scenario scenario, string itemKey, int limit)
    {
        var pages = new List<ItemActivityPage>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(scenario, itemKey, new ActivityQuery { Limit = limit, Cursor = cursor });
            pages.Add(page);
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        return pages;
    }
}
