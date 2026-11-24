using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Work.ChangeSets;

namespace PmTool.Work.Tests.ChangeSets;

/// <summary>
/// 套用與項目的寫入靠專案那一列互斥：套用進行中進來的寫入要等它提交，之後讀到的是新的藍圖；
/// 進行中的寫入也要先做完，套用才開始，看得到它寫的值。
/// 「進行中」用停住的時鐘做出來：套用與修改都是在鎖住之後才讀時間，停在那裡就是拿著鎖還沒提交。
/// </summary>
[TestFixture]
public sealed class ChangeSetLockTests
{
    // 沒有排隊的請求幾毫秒就做完了；等這麼久還沒好，表示它在等另一個交易放開專案那一列。
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    // 測等不到鎖的情況用的等鎖上限，不必真的等五秒；調成這樣的套用過了 Patience 還沒回來，就是沒有上限。
    private static readonly TimeSpan ShortLockTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_WhileAnApplyIsInFlight_WaitsAndThenCannotWriteTheDroppedField()
    {
        // Arrange：WEB-1 有摘要，會被套用動到；WEB-2 沒有，套用不會碰它。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "有摘要", Fields = Scenario.Fields(("summary", "原本的摘要")) });
        var untouched = await scenario.CreateTaskAsync("WEB", "沒有摘要");
        var proposed = await ProposeDroppingSummaryAsync(scenario);
        var patch = new WorkItemPatch { Fields = Scenario.Fields(("summary", "套用中寫的")) };

        // Act
        var (apply, update) = await WhileApplyingAsync(scenario, proposed, () => _host.Items.UpdateAsync(scenario.Admin, untouched.Key, patch, CancellationToken.None));
        var readBack = await _host.Items.GetAsync(scenario.Admin, untouched.Key, CancellationToken.None);

        // Assert
        Assert.That(apply.Succeeded, Is.True, () => ChangeSetDrafts.Describe(apply.Failure!));
        ItemAssert.Invalid(update, WorkErrorCodes.ItemUnknownField, "summary");
        Assert.That(readBack.Value.Fields, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_WhileAnApplyIsInFlight_WaitsAndThenUsesTheNewBlueprint()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ProposeDroppingSummaryAsync(scenario);
        var item = new NewWorkItem { Type = "task", Title = "套用中新增", Fields = Scenario.Fields(("summary", "套用中寫的")) };

        // Act
        var (apply, create) = await WhileApplyingAsync(scenario, proposed, () => _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None));

        // Assert
        Assert.That(apply.Succeeded, Is.True);
        ItemAssert.Invalid(create, WorkErrorCodes.ItemUnknownField, "summary");
    }

    [Test]
    public async Task TransitionAsync_WhileAnApplyIsInFlight_WaitsAndThenUsesTheNewWorkflow()
    {
        // Arrange：套用拿掉「進行中」，套用中要轉到進行中的項目，等套用做完就找不到這個狀態了。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var proposed = await ChangeSetDrafts.ProposeAsync(
            scenario, "WEB", b => b.WithoutState("task_flow", "doing"), BlueprintEdits.Moves(("task_flow", "doing", "todo")));

        // Act
        var (apply, transition) = await WhileApplyingAsync(scenario, proposed, () => _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None));

        // Assert
        Assert.That(apply.Succeeded, Is.True);
        ItemAssert.Failed(transition, FailureKind.Invalid, WorkErrorCodes.TransitionNotAllowed);
    }

    [Test]
    public async Task AddCommentAsync_WhileAnApplyTakesAwayTheRight_WaitsAndThenIsForbidden()
    {
        // Arrange：套用把客戶的留言權限拿掉；套用中客戶留言。留言看的是藍圖裡角色的權限。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with
        {
            Roles = [.. b.Roles.Select(r => r.Key == "client" ? r with { Permissions = ["item.read"] } : r)],
        });

        // Act
        var (apply, comment) = await WhileApplyingAsync(scenario, proposed, () =>
            _host.Activity.AddCommentAsync(client, item.Key, "套用中的留言", null, CancellationToken.None));

        // Assert
        Assert.That(apply.Succeeded, Is.True);
        ItemAssert.Failed(comment, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task SetMemberAsync_WhileAnApplyRemovesTheRole_WaitsAndThenRejectsTheRole()
    {
        // Arrange：套用拿掉「客戶」這個角色；套用中有人把新成員設成客戶。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with
        {
            Roles = [.. b.Roles.Where(r => r.Key != "client")],
            Views = [.. b.Views.Select(v => v with { VisibleTo = v.VisibleTo is null ? null : [.. v.VisibleTo.Where(r => r != "client")] })],
        });
        var person = scenario.Person();

        // Act
        var (apply, member) = await WhileApplyingAsync(scenario, proposed, () =>
            _host.Projects.SetMemberAsync(scenario.Admin, "WEB", person.PersonId, "client", CancellationToken.None));

        // Assert
        Assert.That(apply.Succeeded, Is.True);
        ItemAssert.Invalid(member, WorkErrorCodes.MemberUnknownRole, "role");
    }

    [Test]
    public async Task ApplyAsync_WhileAnUpdateIsInFlight_WaitsForItAndMigratesWhatItWrote()
    {
        // Arrange：送出時 WEB-1 還沒有摘要；另一個請求的修改正要寫進摘要，拿著鎖還沒提交。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var proposed = await ProposeDroppingSummaryAsync(scenario);
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowItems = scope.ServiceProvider.GetRequiredService<IWorkItemService>();
        var patch = new WorkItemPatch { Fields = Scenario.Fields(("summary", "剛寫的摘要")) };
        var update = Task.Run(() => slowItems.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None));
        await clock.Paused;
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        Task<Result<ChangeSetDetail>> apply;
        bool appliedWhileUpdating;
        try
        {
            apply = Task.Run(() => _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
            appliedWhileUpdating = await Task.WhenAny(apply, Task.Delay(Grace)) == apply;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(update, apply);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var migrated = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(appliedWhileUpdating, Is.False, "套用要等修改提交");
        Assert.That((update.Result.Succeeded, apply.Result.Succeeded), Is.EqualTo((true, true)));
        Assert.That(readBack.Value.Fields, Is.Empty);
        Assert.That(ActivityLog.Shapes(migrated), Is.EqualTo(new (string, string?, string?)[] { ("summary", "\"剛寫的摘要\"", null) }));
        Assert.That((await ArchivedValues.ListAsync(_host, scenario)).Select(a => a.Value), Is.EqualTo(new[] { "\"剛寫的摘要\"" }));
    }

    [Test]
    public async Task ApplyAsync_WhileACreateIsInFlight_WaitsForItWithoutDeadlocking()
    {
        // Arrange：新增在拿到共用鎖、讀了時間之後停住，接著還要改專案那一列（發流水號）。
        // 套用這時已經在等專案那一列；新增把共用鎖升級時不能卡在套用後面，否則兩邊互等。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var proposed = await ProposeDroppingSummaryAsync(scenario);
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowItems = scope.ServiceProvider.GetRequiredService<IWorkItemService>();
        var item = new NewWorkItem { Type = "task", Title = "新增中", Fields = Scenario.Fields(("summary", "新增時寫的")) };
        var create = Task.Run(() => slowItems.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None));
        await clock.Paused;

        // Act
        Task<Result<ChangeSetDetail>> apply;
        bool appliedWhileCreating;
        try
        {
            apply = Task.Run(() => _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
            appliedWhileCreating = await Task.WhenAny(apply, Task.Delay(Grace)) == apply;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(create, apply);
        var readBack = await _host.Items.GetAsync(scenario.Admin, create.Result.Value.Key, CancellationToken.None);

        // Assert
        Assert.That(appliedWhileCreating, Is.False, "套用要等新增提交");
        Assert.That((create.Result.Succeeded, apply.Result.Succeeded), Is.EqualTo((true, true)));
        Assert.That(readBack.Value.Fields, Is.Empty, "新增的值也被封存了");
    }

    [Test]
    public async Task ApplyAsync_WhenAnUpdateHoldsTheProjectLongerThanTheLockTimeout_IsBusyAndChangesNothing()
    {
        // Arrange：修改拿著專案的共用鎖停住；套用的等鎖上限調成很短，等不到就放棄。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "有摘要", Fields = Scenario.Fields(("summary", "原本的摘要")) });
        var proposed = await ProposeDroppingSummaryAsync(scenario);
        var clock = new PausingTimeProvider(_host.Clock);
        await using var slowProvider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var slowScope = slowProvider.CreateAsyncScope();
        var patch = new WorkItemPatch { Fields = Scenario.Fields(("summary", "修改中寫的")) };
        var update = Task.Run(() => slowScope.ServiceProvider.GetRequiredService<IWorkItemService>().UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None));
        await clock.Paused;
        await using var impatient = ImpatientProvider();
        await using var impatientScope = impatient.CreateAsyncScope();
        var changes = impatientScope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();

        // Act：等不到鎖的套用要自己放棄；放不下的話，修改要等這裡放行，兩邊會一直互等，所以最多等一段時間就放行。
        var apply = Task.Run(() => changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
        bool gaveUp;
        try
        {
            gaveUp = await Task.WhenAny(apply, Task.Delay(Patience)) == apply;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(apply, update);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var project = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var changeSet = await _host.Changes.GetAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);
        var archived = await ArchivedValues.ListAsync(_host, scenario);
        var retried = await _host.Changes.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None);

        // Assert
        Assert.That(gaveUp, Is.True, "套用要在等鎖的上限到了就放棄");
        ItemAssert.Failed(apply.Result, FailureKind.Conflict, WorkErrorCodes.ChangeSetBusy);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(apply.Result.Failure!.Message, Is.EqualTo("專案的項目正有人在修改，變更集 #1 等不到它們做完；這次什麼都沒有改，請稍後再試。"));
            Assert.That(update.Result.Succeeded, Is.True, "修改照常完成");
            Assert.That(readBack.Value.Fields["summary"].GetString(), Is.EqualTo("修改中寫的"));
            Assert.That(project.Value.Project.BlueprintVersion, Is.EqualTo(1));
            Assert.That((changeSet.Value.ChangeSet.Status, changeSet.Value.CanApply), Is.EqualTo((ChangeSetStatus.Pending, true)));
            Assert.That(archived, Is.Empty);
            Assert.That(retried.Succeeded, Is.True, "放棄的那一次什麼都沒留下，之後照樣套用得了");
        }
    }

    /// <summary>等鎖上限調成 <see cref="ShortLockTimeout"/> 的服務。</summary>
    private ServiceProvider ImpatientProvider() => WorkTestHost.BuildProvider(
        _host.ConnectionString, _host.Clock, services => services.AddSingleton(ChangeSetLimits.Default with { ApplyLockTimeout = ShortLockTimeout }));

    private static Task<ChangeSetDetail> ProposeDroppingSummaryAsync(Scenario scenario) =>
        ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b.WithoutField("summary"), BlueprintEdits.Drops("summary"));

    /// <summary>
    /// 套用在鎖住專案與項目、讀了時間之後停住；這時發出 <paramref name="write"/>，確認它一直等著，再讓套用做完。
    /// </summary>
    private async Task<(Result<ChangeSetDetail> Apply, Result<T> Write)> WhileApplyingAsync<T>(
        Scenario scenario, ChangeSetDetail proposed, Func<Task<Result<T>>> write)
    {
        var clock = new PausingTimeProvider(_host.Clock);
        await using var provider = WorkTestHost.BuildProvider(_host.ConnectionString, clock);
        await using var scope = provider.CreateAsyncScope();
        var slowChanges = scope.ServiceProvider.GetRequiredService<IBlueprintChangeService>();
        var apply = Task.Run(() => slowChanges.ApplyAsync(scenario.Admin, "WEB", proposed.ChangeSet.Number, CancellationToken.None));
        await clock.Paused;

        Task<Result<T>> pending;
        bool finishedWhileApplying;
        try
        {
            pending = Task.Run(write);
            finishedWhileApplying = await Task.WhenAny(pending, Task.Delay(Grace)) == pending;
        }
        finally
        {
            clock.Resume();
        }

        await Task.WhenAll(apply, pending);
        Assert.That(finishedWhileApplying, Is.False, "寫入要等套用提交");
        return (apply.Result, pending.Result);
    }
}
