using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>
/// 同時改同一個欄位：每個請求排隊拿到那一列的鎖，舊值是在鎖裡讀的，所以前一筆的新值就是後一筆的舊值。
/// 時鐘每讀一次就走一點，和真的時鐘一樣：在鎖裡讀到的時間，先後就是拿到鎖的先後。
/// </summary>
[TestFixture]
public sealed class ActivityConcurrencyTests
{
    private const int Concurrency = 10;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _host = await WorkTestHost.StartAsync();
        _host.Clock.AdvanceOnEveryRead(TimeSpan.FromMilliseconds(1));
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_SameFieldAtTheSameTime_RecordsAnUnbrokenChainOfChanges()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "原本" });

        // Act
        var results = await InParallelAsync(Concurrency, (items, i) =>
            items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = $"標題 {i}" }, CancellationToken.None));
        var updates = (await ActivityLog.AllAsync(_host, scenario.Admin, item.Key)).Where(a => a.Kind == ActivityKind.Updated).ToArray();
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
        Assert.That(updates, Has.Length.EqualTo(Concurrency));
        var titles = updates.Select(a => ActivityLog.Shapes(a).Single()).ToArray();
        Assert.That(titles.Select(t => t.Field), Has.All.EqualTo("title"));
        Assert.That(titles[0].From, Is.EqualTo("\"原本\""));
        Assert.That(titles.Skip(1).Select(t => t.From), Is.EqualTo(titles.SkipLast(1).Select(t => t.To)), "前一筆的新值應該是後一筆的舊值");
        Assert.That(titles[^1].To, Is.EqualTo(ActivityLog.Text(Scenario.Json(readBack.Value.Title))));
        Assert.That(titles.Select(t => t.To).Distinct().Count(), Is.EqualTo(Concurrency));
    }

    /// <summary>所有請求先準備好再一起放行，盡量讓它們真的同時打到資料庫。</summary>
    private async Task<Result<WorkItem>[]> InParallelAsync(int count, Func<IWorkItemService, int, Task<Result<WorkItem>>> call)
    {
        var requests = Enumerable.Range(0, count).Select(_ => _host.NewRequest()).ToArray();
        using var gate = new SemaphoreSlim(0, count);
        var tasks = requests.Select((request, i) => Task.Run(async () =>
        {
            await gate.WaitAsync();
            return await call(request.Items, i);
        })).ToArray();
        gate.Release(count);
        var results = await Task.WhenAll(tasks);
        foreach (var request in requests)
        {
            await request.Scope.DisposeAsync();
        }

        return results;
    }
}
