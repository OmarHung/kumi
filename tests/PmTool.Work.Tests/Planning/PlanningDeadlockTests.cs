using PmTool.Abstractions;
using static PmTool.Work.Tests.Planning.PlanningSupport;

namespace PmTool.Work.Tests.Planning;

/// <summary>
/// 刪除週期、里程碑與修改項目的互鎖。修改項目先鎖項目、再對週期取共用鎖；刪除先鎖週期，再由外鍵清空項目的欄位，順序相反。
/// 被 PostgreSQL 退回的那一邊整個重做一次，刪除這邊也一樣，不能變成 500。
/// 另一個還沒提交的修改用 <see cref="RowLockHolder"/> 在另一條連線上做出來。
/// </summary>
[TestFixture]
public sealed class PlanningDeadlockTests
{
    private const int Rounds = 15;
    private const int ItemsPerRound = 4;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteCycleAsync_PickedAsTheDeadlockVictim_RetriesOnceAndSucceeds()
    {
        // Arrange：另一個交易已經鎖住項目（修改項目的第一步），刪除週期會鎖住週期再等項目。
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
        var item = await ItemInAsync(_host, scenario, "WEB", cycle.Id);
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForNoKeyUpdate, item.Number);
        var delete = Task.Run(() => _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Id, CancellationToken.None));
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);

        // Act：另一個交易反過來要週期，互鎖；刪除先開始等，先發現、被退回，重做時排在另一個交易後面。
        await holder.LockCycleAsync(cycle.Id);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await holder.CommitAsync();
        var result = await delete;
        var cycles = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);
        var reread = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That(cycles.Value, Is.Empty);
        Assert.That(reread.Value.CycleId, Is.Null);
    }

    [Test]
    public async Task DeleteMilestoneAsync_PickedAsTheDeadlockVictim_RetriesOnceAndSucceeds()
    {
        // Arrange
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB");
        var milestone = await CreateMilestoneAsync(_host, scenario.Admin, "WEB", MilestoneOf("v1", 20));
        var item = await ItemInAsync(_host, scenario, "WEB", milestoneId: milestone.Id);
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForNoKeyUpdate, item.Number);
        var delete = Task.Run(() => _host.Planning.DeleteMilestoneAsync(scenario.Admin, "WEB", milestone.Id, CancellationToken.None));
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);

        // Act
        await holder.LockMilestoneAsync(milestone.Id);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await holder.CommitAsync();
        var result = await delete;
        var milestones = await _host.Planning.ListMilestonesAsync(scenario.Admin, "WEB", CancellationToken.None);
        var reread = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That(milestones.Value, Is.Empty);
        Assert.That(reread.Value.MilestoneId, Is.Null);
    }

    [Test]
    public async Task DeleteCycleAsync_WhileItemsOfTheCycleAreUpdated_NeverEndsInAnUnhandledException()
    {
        for (var round = 0; round < Rounds; round++)
        {
            // Arrange：項目已經在週期裡，修改帶著同一個週期（還是要確認它存在），和刪除同時進來。
            var scenario = new Scenario(_host);
            await scenario.CreateProjectAsync("WEB");
            var cycle = await CreateCycleAsync(_host, scenario.Admin, "WEB", CycleOf("衝刺 1", 0, 13));
            var items = new List<WorkItem>();
            for (var i = 0; i < ItemsPerRound; i++)
            {
                items.Add(await ItemInAsync(_host, scenario, "WEB", cycle.Id, title: $"任務 {i}"));
            }

            // Act
            var requests = items.Select(item => (Item: item, Request: _host.NewRequest())).ToArray();
            var deleteRequest = _host.NewRequest();
            var updates = requests.Select(r => Task.Run(() => r.Request.Items.UpdateAsync(
                scenario.Admin, r.Item.Key, new WorkItemPatch { Title = r.Item.Title + "（改）", CycleId = cycle.Id }, CancellationToken.None))).ToArray();
            var delete = Task.Run(() => _host.Planning.DeleteCycleAsync(scenario.Admin, "WEB", cycle.Id, CancellationToken.None));
            var all = Task.WhenAll(updates.Cast<Task>().Append(delete));
            try
            {
                await all;
            }
            catch
            {
                Assert.Fail($"第 {round + 1} 輪：{string.Join("；", all.Exception?.InnerExceptions.Select(e => e.Message) ?? [])}");
            }

            foreach (var (_, request) in requests)
            {
                await request.Scope.DisposeAsync();
            }

            await deleteRequest.Scope.DisposeAsync();

            // Assert：刪除一定成功；修改成功就是搶在刪除之前，失敗就是週期已經不在。最後週期不在、項目都沒有週期。
            var cycles = await _host.Planning.ListCyclesAsync(scenario.Admin, "WEB", CancellationToken.None);
            Assert.That(delete.Result.Succeeded, Is.True, $"第 {round + 1} 輪");
            Assert.That(updates.Select(u => u.Result).Where(r => !r.Succeeded).Select(r => r.Failure!.Kind), Has.All.EqualTo(FailureKind.Invalid), $"第 {round + 1} 輪");
            Assert.That(cycles.Value, Is.Empty, $"第 {round + 1} 輪");
            foreach (var item in items)
            {
                var reread = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
                Assert.That(reread.Value.CycleId, Is.Null, $"第 {round + 1} 輪 {item.Key}");
            }
        }
    }
}
