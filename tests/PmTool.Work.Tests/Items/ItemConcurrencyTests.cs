using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>同時進來的請求。每個請求用自己的範圍，和 API 同時處理多個請求一樣。</summary>
[TestFixture]
public sealed class ItemConcurrencyTests
{
    private const int Concurrency = 12;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_ManyAtTheSameTime_GetDistinctConsecutiveNumbers()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var results = await InParallelAsync(Concurrency, (items, i) =>
            items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = $"同時新增 {i}" }, CancellationToken.None));

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
        Assert.That(results.Select(r => r.Value.Number).Order(), Is.EqualTo(Enumerable.Range(1, Concurrency).Select(n => (long)n)));
    }

    [Test]
    public async Task UpdateAsync_DifferentFieldsAtTheSameTime_KeepsEveryChange()
    {
        // Arrange：每個請求改不同的欄位，核心欄位與自訂欄位都有。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "原本",
            Fields = Scenario.Fields(("summary", "原本的摘要"), ("notes", "會被拿掉")),
        });
        WorkItemPatch[] patches =
        [
            new() { Title = "新標題" },
            new() { Priority = Priority.High },
            new() { DueDate = new DateOnly(2026, 12, 24) },
            new() { Estimate = 5m },
            new() { AssigneeId = scenario.Admin.PersonId },
            new() { Fields = Scenario.Fields(("category", "feature")) },
            new() { Fields = Scenario.Fields(("tags", new[] { "docs" })) },
            new() { Fields = Scenario.Fields(("story_points", 8)) },
            new() { Fields = Scenario.Fields(("blocked", true)) },
            new() { Fields = Scenario.Fields(("reference_url", "https://example.com/a")) },
            new() { Fields = Scenario.Fields(("notes", null)) },
            new() { Fields = Scenario.Fields(("review_date", "2026-12-01")) },
        ];

        // Act
        var results = await InParallelAsync(patches.Length, (items, i) =>
            items.UpdateAsync(scenario.Admin, item.Key, patches[i], CancellationToken.None));
        var readBack = (await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None)).Value;

        // Assert
        Assert.That(results.Select(r => r.Succeeded), Has.All.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(readBack.Title, Is.EqualTo("新標題"));
            Assert.That(readBack.Priority, Is.EqualTo(Priority.High));
            Assert.That(readBack.DueDate, Is.EqualTo(new DateOnly(2026, 12, 24)));
            Assert.That(readBack.Estimate, Is.EqualTo(5m));
            Assert.That(readBack.AssigneeId, Is.EqualTo(scenario.Admin.PersonId));
            Assert.That(
                readBack.Fields.Keys,
                Is.EquivalentTo(new[] { "summary", "category", "tags", "story_points", "blocked", "reference_url", "review_date" }));
        }
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
