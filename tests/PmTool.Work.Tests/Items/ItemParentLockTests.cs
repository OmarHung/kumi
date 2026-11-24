using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 「檢查沒有下層再刪除」「檢查下層再改類型」不能和「新增一個下層」交錯：新增時先對上層那一列取共用鎖再檢查，
/// 刪除與改類型先鎖住自己那一列。兩邊一定是一邊先做完、另一邊照做完的結果檢查，不會留下掛在刪除的項目底下、
/// 或類型不允許的下層。「進行中」用停住的時鐘做出來：這些操作都是在鎖住、檢查完之後才讀時間。
/// </summary>
[TestFixture]
public sealed class ItemParentLockTests
{
    // 沒有排隊的請求幾毫秒就做完了；等這麼久還沒好，表示它在等另一個交易放開那一列。
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateChild_WhileTheParentIsBeingDeleted_WaitsAndThenFindsNoParent()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "要刪掉的需求" });

        // Act
        var (deleted, created) = await WhilePausedAsync(
            slow => slow.DeleteAsync(scenario.Admin, parent.Key, CancellationToken.None),
            () => _host.Items.CreateAsync(scenario.Admin, "WEB", Child(parent.Key), CancellationToken.None));

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        ItemAssert.Invalid(created, WorkErrorCodes.ItemUnknownParent, "parent");
    }

    [Test]
    public async Task DeleteParent_WhileAChildIsBeingCreated_WaitsAndThenSeesTheChild()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });

        // Act
        var (created, deleted) = await WhilePausedAsync(
            slow => slow.CreateAsync(scenario.Admin, "WEB", Child(parent.Key), CancellationToken.None),
            () => _host.Items.DeleteAsync(scenario.Admin, parent.Key, CancellationToken.None));

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        ItemAssert.Failed(deleted, FailureKind.Conflict, WorkErrorCodes.ItemHasChildren);
        Assert.That(deleted.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { $"children/{created.Value.Key}" }));
    }

    [Test]
    public async Task CreateChild_WhileTheParentIsChangingToATypeThatCannotHoldIt_WaitsAndThenIsNotAllowed()
    {
        // Arrange：需求變更單改成任務之後，任務就不能掛在它底下。
        var scenario = await AgencyAsync();
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });

        // Act
        var (changed, created) = await WhilePausedAsync(
            slow => slow.ChangeTypeAsync(scenario.Admin, parent.Key, "task", CancellationToken.None),
            () => _host.Items.CreateAsync(scenario.Admin, "WEB", Child(parent.Key), CancellationToken.None));

        // Assert
        Assert.That(changed.Succeeded, Is.True, () => changed.Failure!.Message);
        ItemAssert.Invalid(created, WorkErrorCodes.ItemParentNotAllowed, "parent");
    }

    [Test]
    public async Task ChangeParentType_WhileAChildIsBeingCreated_WaitsAndThenSeesTheChild()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });

        // Act
        var (created, changed) = await WhilePausedAsync(
            slow => slow.CreateAsync(scenario.Admin, "WEB", Child(parent.Key), CancellationToken.None),
            () => _host.Items.ChangeTypeAsync(scenario.Admin, parent.Key, "task", CancellationToken.None));

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        ItemAssert.Failed(changed, FailureKind.Invalid, WorkErrorCodes.ItemChildrenNotAllowed);
    }

    [Test]
    public async Task ChangeParentType_WhileTheChildIsChangingType_WaitsAndThenChecksTheChildsNewType()
    {
        // Arrange：任務掛在需求底下。任務改成錯誤（只能掛在需求底下）的同時，需求要改成需求變更單；
        // 各看各的都沒問題，合起來就是錯誤掛在需求變更單底下。改類型的下層對上層取共用鎖，上層要等它提交再看下層。
        var scenario = await BugTypeProject.CreateAsync(_host);
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var child = await scenario.CreateItemAsync("WEB", Child(parent.Key));

        // Act
        var (childChanged, parentChanged) = await WhilePausedAsync(
            slow => slow.ChangeTypeAsync(scenario.Admin, child.Key, "bug", CancellationToken.None),
            () => _host.Items.ChangeTypeAsync(scenario.Admin, parent.Key, "change_request", CancellationToken.None));

        // Assert
        Assert.That(childChanged.Succeeded, Is.True, () => childChanged.Failure!.Message);
        ItemAssert.Failed(parentChanged, FailureKind.Invalid, WorkErrorCodes.ItemChildrenNotAllowed);
    }

    [Test]
    public async Task MoveChildUnderTheParent_WhileTheParentIsBeingDeleted_WaitsAndThenFindsNoParent()
    {
        // Arrange：修改既有的任務、把上層設成正在刪除的需求，和新增一樣要等。
        var scenario = await AgencyAsync();
        var parent = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "要刪掉的需求" });
        var task = await scenario.CreateItemAsync("WEB", Child(null));

        // Act
        var (deleted, moved) = await WhilePausedAsync(
            slow => slow.DeleteAsync(scenario.Admin, parent.Key, CancellationToken.None),
            () => _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = parent.Key }, CancellationToken.None));

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        ItemAssert.Invalid(moved, WorkErrorCodes.ItemUnknownParent, "parent");
    }

    /// <summary>
    /// <paramref name="slow"/> 用停住的時鐘執行，停在鎖住、檢查完之後；這時發出 <paramref name="other"/>，
    /// 確認它一直等著，再讓前一個做完。
    /// </summary>
    private async Task<(Result<TSlow> Slow, Result<TOther> Other)> WhilePausedAsync<TSlow, TOther>(
        Func<IWorkItemService, Task<Result<TSlow>>> slow, Func<Task<Result<TOther>>> other)
    {
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowItems = scope.ServiceProvider.GetRequiredService<IWorkItemService>();
        var first = Task.Run(() => slow(slowItems));
        await clock.Paused;

        Task<Result<TOther>> second;
        bool finishedWhilePaused;
        try
        {
            second = Task.Run(other);
            finishedWhilePaused = await Task.WhenAny(second, Task.Delay(Grace)) == second;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(first, second);
        Assert.That(finishedWhilePaused, Is.False, "要等前一個提交");
        return (first.Result, second.Result);
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Child(string? parentKey) => new() { Type = "task", Title = "子任務", ParentKey = parentKey };
}
