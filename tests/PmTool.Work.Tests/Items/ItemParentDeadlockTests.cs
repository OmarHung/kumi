using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 設上層時的互鎖。修改先鎖住自己那一列，再對上層取共用鎖；兩個請求互相設成對方的上層，會各自鎖住自己再等對方。
/// 取共用鎖之前先用沒上鎖的讀取檢查類型與繞圈，不合法的那一邊直接回 422、放開自己那一列；
/// 其他還是互鎖的情況，被 PostgreSQL 退回的那一邊整個重做一次，再互鎖才丟出例外。
/// 同時進來的另一個寫入用 <see cref="RowLockHolder"/> 在另一條連線上做出來。
/// </summary>
[TestFixture]
public sealed class ItemParentDeadlockTests
{
    private const int Rounds = 3;

    // 沒有排隊的請求幾毫秒就做完了；等這麼久還沒好，表示它在等另一個交易放開那一列。
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_TwoItemsSetAsEachOthersParentAtTheSameTime_OneSucceedsAndTheOtherIsInvalid()
    {
        // Arrange：每一輪開新的專案，需求 WEB-1 與任務 WEB-2。兩列先被另一個交易鎖住，兩個請求都排在後面，
        // 一放開就同時鎖住各自的那一列，再去等對方。
        for (var round = 0; round < Rounds; round++)
        {
            var (scenario, project) = await AgencyAsync();
            var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
            var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁" });
            await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
            await holder.LockItemsAsync(project.Id, RowLockHolder.ForUpdate, requirement.Number, task.Number);
            var (first, second) = (_host.NewRequest(), _host.NewRequest());

            // Act
            var taskUnderRequirement = Task.Run(() => first.Items.UpdateAsync(
                scenario.Admin, task.Key, new WorkItemPatch { ParentKey = requirement.Key }, CancellationToken.None));
            var requirementUnderTask = Task.Run(() => second.Items.UpdateAsync(
                scenario.Admin, requirement.Key, new WorkItemPatch { ParentKey = task.Key }, CancellationToken.None));
            await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 2);
            await holder.CommitAsync();
            var results = await Task.WhenAll(taskUnderRequirement, requirementUnderTask);
            await first.Scope.DisposeAsync();
            await second.Scope.DisposeAsync();

            // Assert：需求只能放在最上層；任務先掛上去的話，看到的是繞圈。
            Assert.That(results[0].Succeeded, Is.True, () => $"第 {round + 1} 輪：{results[0].Failure!.Message}");
            Assert.That(results[0].Value.ParentKey, Is.EqualTo(requirement.Key));
            Assert.That(results[1].Failure?.Kind, Is.EqualTo(FailureKind.Invalid), $"第 {round + 1} 輪");
            Assert.That(results[1].Failure!.Code, Is.AnyOf(WorkErrorCodes.ItemParentNotAllowed, WorkErrorCodes.ItemParentCycle));
        }
    }

    [TestCase(false, WorkErrorCodes.ItemParentNotAllowed, TestName = "UpdateAsync_ParentOfATypeThatIsNotAllowedWhileItIsLocked_IsRejectedWithoutWaiting")]
    [TestCase(true, WorkErrorCodes.ItemParentCycle, TestName = "UpdateAsync_ParentThatWouldFormACycleWhileItIsLocked_IsRejectedWithoutWaiting")]
    public async Task UpdateAsync_InvalidParentWhileItIsLocked_IsRejectedWithoutWaiting(bool taskUnderRequirement, string code)
    {
        // Arrange：任務那一列被另一個交易鎖著；要把需求掛到任務底下（需求只能放在最上層，或任務本來就掛在需求底下）。
        var (scenario, project) = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "做登入頁", ParentKey = taskUnderRequirement ? requirement.Key : null });
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForUpdate, task.Number);

        // Act
        var update = Task.Run(() => _host.Items.UpdateAsync(
            scenario.Admin, requirement.Key, new WorkItemPatch { ParentKey = task.Key }, CancellationToken.None));
        var finishedWhileLocked = await Task.WhenAny(update, Task.Delay(Patience)) == update;
        await holder.CommitAsync();
        var result = await update;

        // Assert
        Assert.That(finishedWhileLocked, Is.True, "不合法的上層不必等它的鎖");
        ItemAssert.Invalid(result, code, "parent");
    }

    [Test]
    public async Task UpdateAsync_PickedAsTheDeadlockVictim_RetriesOnceAndSucceeds()
    {
        // Arrange：另一個交易鎖住需求；任務要掛到需求底下，鎖住自己之後等需求。
        var (scenario, project) = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForNoKeyUpdate, requirement.Number);
        var update = Task.Run(() => _host.Items.UpdateAsync(
            scenario.Admin, task.Key, new WorkItemPatch { ParentKey = requirement.Key }, CancellationToken.None));
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);

        // Act：另一個交易反過來要任務那一列，互鎖；請求先開始等，先發現、被退回，重做時排在另一個交易後面。
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForShare, task.Number);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await holder.CommitAsync();
        var result = await update;
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, task.Key);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That(result.Value.ParentKey, Is.EqualTo(requirement.Key));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Updated }), "重做之前的那一次什麼都沒留下");
    }

    [Test]
    public async Task ChangeTypeAsync_PickedAsTheDeadlockVictim_RetriesOnceAndSucceeds()
    {
        // Arrange：任務掛在需求底下，要改成錯誤（只能掛在需求底下）；另一個交易鎖住需求，改類型鎖住任務之後等它。
        var scenario = await BugTypeProject.CreateAsync(_host);
        var project = (await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None)).Value.Project;
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "登入失敗", ParentKey = requirement.Key });
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForNoKeyUpdate, requirement.Number);
        var change = Task.Run(() => _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "bug", CancellationToken.None));
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);

        // Act
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForShare, task.Number);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await holder.CommitAsync();
        var result = await change;

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.Type, result.Value.ParentKey), Is.EqualTo(("bug", requirement.Key)));
    }

    [Test]
    public async Task UpdateAsync_PickedAsTheDeadlockVictimAgainWhenRetrying_Throws()
    {
        // Arrange：和上一個一樣先互鎖一次。
        var (scenario, project) = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁" });
        await using var holder = await RowLockHolder.BeginAsync(_host.ConnectionString);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForNoKeyUpdate, requirement.Number);
        var update = Task.Run(() => _host.Items.UpdateAsync(
            scenario.Admin, task.Key, new WorkItemPatch { ParentKey = requirement.Key }, CancellationToken.None));
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);
        await holder.LockItemsAsync(project.Id, RowLockHolder.ForShare, task.Number);
        await RowLockHolder.WaitForLockWaitersAsync(_host.ConnectionString, 1);

        // Act：重做的請求已經對專案取了共用鎖、正在等任務那一列；另一個交易再要專案的排他鎖，第二次互鎖。
        await holder.LockProjectAsync(project.Id);
        var thrown = await CaughtAsync(update);
        await holder.CommitAsync();
        var readBack = await _host.Items.GetAsync(scenario.Admin, task.Key, CancellationToken.None);

        // Assert
        Assert.That(DeadlockIn(thrown), Is.True, () => $"應該丟出互鎖的例外：{thrown}");
        Assert.That(readBack.Value.ParentKey, Is.Null, "什麼都沒改");
    }

    private static bool DeadlockIn(Exception? exception) =>
        exception switch
        {
            null => false,
            PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected } => true,
            _ => DeadlockIn(exception.InnerException),
        };

    private static async Task<Exception?> CaughtAsync(Task task)
    {
        try
        {
            await task;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private async Task<(Scenario Scenario, ProjectSummary Project)> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        var project = await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return (scenario, project);
    }
}
