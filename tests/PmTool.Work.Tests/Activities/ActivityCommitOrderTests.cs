using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>
/// 活動依時間分頁，所以同一個項目的活動，時間的先後要和提交的先後一致：
/// 時間比較早、卻比較晚提交的活動，會落在讀者已經拿到的游標前面，往下翻就再也看不到。
/// </summary>
[TestFixture]
public sealed class ActivityCommitOrderTests
{
    // 沒有排隊的留言幾毫秒就提交了；等這麼久還沒好，表示它在等修改放開那一列。
    private static readonly TimeSpan CommentGrace = TimeSpan.FromMilliseconds(500);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task AddCommentAsync_WhileAnUpdateOfTheItemIsInFlight_WaitsAndIsOrderedAfterTheUpdate()
    {
        // Arrange：另一個請求的修改在鎖裡讀了時間（9:01），提交之前停住；之後時鐘走到 9:02。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB", "原本");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowItems = scope.ServiceProvider.GetRequiredService<IWorkItemService>();
        var update = Task.Run(() => slowItems.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改了" }, CancellationToken.None));
        await clock.Paused;
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act：修改還沒提交時留言，並在這時讀一次活動。
        IReadOnlyList<ItemActivity> seenWhileUpdating;
        Task<Result<ItemActivity>> comment;
        try
        {
            comment = Task.Run(() => _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "看過了", null, CancellationToken.None));
            await Task.WhenAny(comment, Task.Delay(CommentGrace));
            seenWhileUpdating = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(update, comment);
        var all = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert：已經讀到的活動要一直排在最前面，晚提交的只能接在後面；留言的時間是排到之後才讀的。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(seenWhileUpdating.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }), "留言要等修改提交");
            Assert.That(all.Take(seenWhileUpdating.Count).Select(a => a.Id), Is.EqualTo(seenWhileUpdating.Select(a => a.Id)));
            Assert.That(
                all.Select(a => (a.Kind, a.At)),
                Is.EqualTo(new[]
                {
                    (ActivityKind.Created, item.CreatedAt),
                    (ActivityKind.Updated, item.CreatedAt.AddMinutes(1)),
                    (ActivityKind.Commented, item.CreatedAt.AddMinutes(2)),
                }));
            Assert.That(comment.Result.Value.At, Is.EqualTo(item.CreatedAt.AddMinutes(2)));
        }
    }
}
