using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>活動清單的種類篩選：在資料庫裡就篩掉再分頁，翻頁的每一頁都只有那一種。</summary>
[TestFixture]
public sealed class ActivityKindFilterTests
{
    // 翻頁的保險：游標一直不結束時，測試失敗而不是卡住。
    private const int MaxPages = 10;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_KindCommented_ReturnsOnlyCommentsAndRepliesOldestFirst()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Commented });

        // Assert
        Assert.That(page.Items.Select(a => a.Kind).Distinct(), Is.EqualTo(new[] { ActivityKind.Commented }));
        Assert.That(page.Items.Select(a => a.Comment), Is.EqualTo(new[] { "留言 1", "回覆 1", "留言 2" }));
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_KindCommentedWithSmallLimit_PagesThroughEveryCommentOnce()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();
        var seen = new List<ItemActivityPage>();

        // Act
        var query = new ActivityQuery { Kind = ActivityKind.Commented, Limit = 2 };
        do
        {
            var page = await PageAsync(scenario, item.Key, query);
            seen.Add(page);
            query = query with { Cursor = page.NextCursor };
            if (seen.Count > MaxPages)
            {
                Assert.Fail($"讀了 {MaxPages} 頁還沒結束");
            }
        }
        while (query.Cursor is not null);

        // Assert
        var all = seen.SelectMany(p => p.Items).ToArray();
        Assert.That(seen.Select(p => p.Items.Count), Is.EqualTo(new[] { 2, 1 }));
        Assert.That(all.Select(a => a.Kind).Distinct(), Is.EqualTo(new[] { ActivityKind.Commented }));
        Assert.That(all.Select(a => a.Comment), Is.EqualTo(new[] { "留言 1", "回覆 1", "留言 2" }));
        Assert.That(all.Select(a => a.Id).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public async Task ListAsync_KindCommentedWithSameTimestamps_PagesByIdWithoutGapsOrRepeats()
    {
        // Arrange：三則留言不推進時鐘，時間完全一樣，只能靠 id 分先後。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var ids = new List<Guid>();
        foreach (var body in new[] { "同時 1", "同時 2", "同時 3" })
        {
            var added = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, body, null, CancellationToken.None);
            Assert.That(added.Succeeded, Is.True, () => added.Failure!.Message);
            ids.Add(added.Value.Id);
        }

        // Act
        var all = await ReadAllAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Commented, Limit = 1 });

        // Assert
        Assert.That(all.Select(a => a.At).Distinct().Count(), Is.EqualTo(1), "三筆的時間一樣");
        Assert.That(all.Select(a => a.Id), Is.EquivalentTo(ids));
        Assert.That(all.Select(a => a.Id).Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public async Task ListAsync_KindCommentedWithLimitEqualToMatches_HasNoNextCursor()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Commented, Limit = 3 });

        // Assert
        Assert.That(page.Items, Has.Count.EqualTo(3));
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_KindCommented_DoesNotShowAnotherItemsComments()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();
        var other = await scenario.CreateTaskAsync("WEB", "另一個項目");
        await CommentAsync(scenario, other.Key, "別的項目的留言", null);

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Commented });

        // Assert
        Assert.That(page.Items.Select(a => a.Comment), Is.EqualTo(new[] { "留言 1", "回覆 1", "留言 2" }));
    }

    [Test]
    public async Task ListAsync_KindTransitioned_ReturnsOnlyStateChanges()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Transitioned });

        // Assert
        Assert.That(page.Items.Select(a => (a.Kind, a.ToState)), Is.EqualTo(new[] { (ActivityKind.Transitioned, "doing") }));
    }

    [Test]
    public async Task ListAsync_NoKind_ReturnsEveryActivity()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery());

        // Assert：建立、留言 1、回覆 1、修改、狀態、留言 2。
        Assert.That(page.Items.Select(a => a.Kind), Is.EqualTo(new[]
        {
            ActivityKind.Created, ActivityKind.Commented, ActivityKind.Commented,
            ActivityKind.Updated, ActivityKind.Transitioned, ActivityKind.Commented,
        }));
    }

    [Test]
    public async Task ListAsync_KindWithNoMatches_ReturnsAnEmptyPage()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var page = await PageAsync(scenario, item.Key, new ActivityQuery { Kind = ActivityKind.Deleted });

        // Assert
        Assert.That(page.Items, Is.Empty);
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_UndefinedKind_IsQueryInvalid()
    {
        // Arrange
        var (scenario, item) = await MixedActivityAsync();

        // Act
        var result = await _host.Activity.ListAsync(
            scenario.Admin, item.Key, new ActivityQuery { Kind = (ActivityKind)99 }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "kind");
    }

    // 建立、留言 1、回覆 1、修改、改狀態、留言 2：種類穿插，時間各差一分鐘。
    private async Task<(Scenario Scenario, WorkItem Item)> MixedActivityAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var first = await CommentAsync(scenario, item.Key, "留言 1", null);
        await CommentAsync(scenario, item.Key, "回覆 1", first.Id);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改過的標題" }, CancellationToken.None);
        Assert.That(updated.Succeeded, Is.True, () => updated.Failure!.Message);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        await CommentAsync(scenario, item.Key, "留言 2", null);
        return (scenario, item);
    }

    private async Task<ItemActivity> CommentAsync(Scenario scenario, string itemKey, string body, Guid? replyTo)
    {
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await _host.Activity.AddCommentAsync(scenario.Admin, itemKey, body, replyTo, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private async Task<IReadOnlyList<ItemActivity>> ReadAllAsync(Scenario scenario, string itemKey, ActivityQuery first)
    {
        var all = new List<ItemActivity>();
        var query = first;
        var pages = 0;
        do
        {
            var page = await PageAsync(scenario, itemKey, query);
            all.AddRange(page.Items);
            query = query with { Cursor = page.NextCursor };
            if (++pages > MaxPages)
            {
                Assert.Fail($"讀了 {MaxPages} 頁還沒結束");
            }
        }
        while (query.Cursor is not null);
        return all;
    }

    private async Task<ItemActivityPage> PageAsync(Scenario scenario, string itemKey, ActivityQuery query)
    {
        var result = await _host.Activity.ListAsync(scenario.Admin, itemKey, query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }
}
