namespace PmTool.Work.Tests.Events;

/// <summary>事件帶著同一次寫入留下的那一筆活動的編號，通知才能指到時間軸上的那一則留言。</summary>
[TestFixture]
public sealed class EventActivityIdTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_EventCarriesTheIdOfTheCreatedActivity()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var item = await scenario.CreateTaskAsync("WEB");
        var written = await EventFeed.LastAsync(_host, scenario);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That((written.Kind, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemCreated, (Guid?)activity.Id)));
    }

    [Test]
    public async Task UpdateAsync_EventCarriesTheIdOfTheUpdatedActivity()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        _host.Clock.Advance(TimeSpan.FromMinutes(1)); // 活動依編號（含時間）排序，同一刻寫的兩筆順序不固定。

        // Act
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改過" }, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That((written.Kind, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemUpdated, (Guid?)activity.Id)));
    }

    [Test]
    public async Task TransitionAsync_EventCarriesTheIdOfTheTransitionedActivity()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That((written.Kind, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemTransitioned, (Guid?)activity.Id)));
    }

    [Test]
    public async Task AddCommentAsync_EventCarriesTheIdTheCommentIsReturnedWith()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var comment = await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "留言", null, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((written.Kind, written.Item.ActivityId), Is.EqualTo((WorkEventKind.ItemCommented, (Guid?)comment.Value.Id)));
            Assert.That(activity.Id, Is.EqualTo(comment.Value.Id));
        }
    }

    [Test]
    public async Task DeleteAsync_EventCarriesAnActivityIdOfItsOwn()
    {
        // Arrange：刪掉之後項目讀不到，時間軸也就讀不到刪除那一筆，只能確認它不是先前任何一筆的編號。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var earlier = (await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).Select(a => (Guid?)a.Id).ToList();

        // Act
        await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);
        var written = await EventFeed.LastAsync(_host, scenario);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(written.Kind, Is.EqualTo(WorkEventKind.ItemDeleted));
            Assert.That(written.Item.ActivityId, Is.Not.Null.And.Not.EqualTo(Guid.Empty));
            Assert.That(earlier, Does.Not.Contain(written.Item.ActivityId));
        }
    }
}
