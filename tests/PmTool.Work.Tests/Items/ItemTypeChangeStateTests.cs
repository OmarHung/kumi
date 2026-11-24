using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 改類型時狀態怎麼選：新流程有同 key、同類別的狀態就留著；否則新流程不限制轉換、呼叫者也有 item.transition 時，
/// 搬到第一個同類別的狀態（沒有就是第一個狀態）；其他情況一律回到新流程的第一個狀態，不能借改類型跳過轉換的限制。
/// 用 agency_project：交付流程有限制轉換，任務流程沒有；<see cref="BugTypeProject"/> 再加一個「錯誤」類型，
/// 流程是 triage（backlog）、doing（done 類），不限制轉換。
/// </summary>
[TestFixture]
public sealed class ItemTypeChangeStateTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ChangeTypeAsync_SameStateKeyOfAnotherCategory_IsNotKeptAndMovesToTheFirstState()
    {
        // Arrange：任務在 doing（進行中類），掛在需求底下；錯誤的流程也有 doing，但它是 done 類，也沒有進行中類的狀態。
        var scenario = await BugTypeProject.CreateAsync(_host);
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "登入失敗", ParentKey = requirement.Key });
        await _host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "bug", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.Type, result.Value.State, result.Value.StateCategory), Is.EqualTo(("bug", "triage", StateCategory.Backlog)));
            Assert.That(result.Value.ParentKey, Is.EqualTo(requirement.Key));
            Assert.That((activity.FromState, activity.ToState), Is.EqualTo(("doing", "triage")));
        }
    }

    [Test]
    public async Task ChangeTypeAsync_NewWorkflowHasNoStateOfTheSameKeyOrCategory_MovesToItsFirstState()
    {
        // Arrange：任務在 todo（未開始類）；錯誤的流程沒有 todo，也沒有未開始類的狀態。
        var scenario = await BugTypeProject.CreateAsync(_host);
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "登入失敗" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "bug", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);

        // Assert
        Assert.That((result.Value.State, result.Value.StateCategory), Is.EqualTo(("triage", StateCategory.Backlog)));
        Assert.That((activity.FromState, activity.ToState), Is.EqualTo(("todo", "triage")));
    }

    [Test]
    public async Task ChangeTypeAsync_DoneTaskToAChangeRequest_StartsAtTheFirstStateOfTheRestrictedDeliveryFlow()
    {
        // Arrange：沒有上層的任務一路做完；交付流程限制轉換，進「結案」要專案經理、要先填客戶驗收日。
        var scenario = await AgencyAsync();
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "整理共用元件" });
        await TransitionAsync(scenario.Admin, task.Key, "doing", "done");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "change_request", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);
        var skipAhead = await _host.Items.TransitionAsync(scenario.Admin, task.Key, "designing", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.State, result.Value.StateCategory), Is.EqualTo(("confirming", StateCategory.NotStarted)), "不是「結案」");
            Assert.That((activity.FromState, activity.ToState), Is.EqualTo(("done", "confirming")));
        }

        ItemAssert.Invalid(skipAhead, WorkErrorCodes.TransitionNotAllowed, "state");
    }

    [TestCase("done", TestName = "ChangeTypeAsync_ChangeRequestFinishedAsATaskAndChangedBack_StartsOverInsteadOfClosing")]
    [TestCase("doing", "quoting", "designing", "developing", TestName = "ChangeTypeAsync_ChangeRequestInDevelopmentToATaskAndBack_StartsOverInsteadOfJumpingBackToDesign")]
    public async Task ChangeTypeAsync_ThereAndBackAgain_CannotWalkATransitionTheDeliveryFlowLacks(string taskState, params string[] deliverySteps)
    {
        // Arrange：需求變更單先照交付流程走幾步，改成任務（任務流程不限制，管理員搬到同類別的狀態），在任務流程裡隨意走到目標狀態。
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        await TransitionAsync(scenario.Admin, item.Key, deliverySteps);
        var asTask = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "task", CancellationToken.None);
        if (asTask.Value.State != taskState)
        {
            await TransitionAsync(scenario.Admin, item.Key, taskState);
        }

        // Act
        var back = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "change_request", CancellationToken.None);

        // Assert
        Assert.That(back.Succeeded, Is.True, () => back.Failure!.Message);
        Assert.That((back.Value.Type, back.Value.State), Is.EqualTo(("change_request", "confirming")));
    }

    [Test]
    public async Task ChangeTypeAsync_RoleWithoutItemTransitionToAnUnrestrictedFlow_MovesToTheFirstState()
    {
        // Arrange：自訂角色「文件整理」可以改項目，但不能改狀態；需求變更單停在「開發」（進行中類）。
        var scenario = await AgencyAsync();
        var writer = new RoleDefinition { Key = "doc_writer", Name = "文件整理", Permissions = [WorkPermissions.ItemRead, WorkPermissions.ItemUpdate] };
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with { Roles = [.. b.Roles, writer] });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var member = await scenario.MemberAsync("WEB", writer.Key);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "加上社群登入" });
        await TransitionAsync(scenario.Admin, item.Key, "quoting", "designing", "developing");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var result = await _host.Items.ChangeTypeAsync(member, item.Key, "task", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Value.State, result.Value.StateCategory), Is.EqualTo(("todo", StateCategory.NotStarted)), "不是同類別的「進行中」");
            Assert.That((activity.FromState, activity.ToState), Is.EqualTo(("developing", "todo")));
        }
    }

    private async Task TransitionAsync(Actor actor, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            var moved = await _host.Items.TransitionAsync(actor, itemKey, state, CancellationToken.None);
            Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        }
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }
}
