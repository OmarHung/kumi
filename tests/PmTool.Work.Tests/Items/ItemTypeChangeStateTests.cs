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

    [Test]
    public async Task ChangeTypeAsync_ScrumTaskInDoneToABug_KeepsTheStateBecauseTheFlowIsTheSame()
    {
        // Arrange：scrum 的故事、任務、缺陷共用有轉換限制的 sprint_flow。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("SCR", "scrum");
        var story = await scenario.CreateItemAsync("SCR", new NewWorkItem { Type = "story", Title = "登入" });
        var task = await scenario.CreateItemAsync("SCR", new NewWorkItem { Type = "task", Title = "做登入", ParentKey = story.Key });
        await TransitionAsync(scenario.Admin, task.Key, "in_progress", "in_review", "done");

        // Act
        var result = await _host.Items.ChangeTypeAsync(scenario.Admin, task.Key, "bug", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("bug", "done")));
    }

    [Test]
    public async Task ChangeTypeAsync_RequirementClosedByThePmToAChangeRequestAndBack_KeepsClosedForAnyoneBecauseTheFlowIsTheSame()
    {
        // Arrange：需求與需求變更單共用 delivery；進「結案」只有專案經理、要先填客戶驗收日。
        var scenario = await AgencyAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "requirement", Title = "會員登入", Fields = Scenario.Fields(("client_signoff_date", "2026-01-01")) });
        await TransitionAsync(manager, item.Key, "quoting", "designing", "developing", "internal_qa", "client_qa", "closed");

        // Act
        var toChange = await _host.Items.ChangeTypeAsync(editor, item.Key, "change_request", CancellationToken.None);
        var back = await _host.Items.ChangeTypeAsync(editor, item.Key, "requirement", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(toChange.Succeeded, Is.True, () => toChange.Failure!.Message);
            Assert.That((toChange.Value.Type, toChange.Value.State), Is.EqualTo(("change_request", "closed")));
            Assert.That((back.Value.Type, back.Value.State), Is.EqualTo(("requirement", "closed")));
        }
    }

    [Test]
    public async Task ChangeTypeAsync_ContentCalendarCampaignAndContentCanceled_KeepsTheStateBothWays()
    {
        // Arrange：活動與內容的流程不同，但共用的狀態只有圖形上的限制，沒有角色與欄位的要求。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("CAL", "content_calendar");
        var item = await scenario.CreateItemAsync("CAL", new NewWorkItem { Type = "campaign", Title = "年中慶" });
        await TransitionAsync(scenario.Admin, item.Key, "canceled");

        // Act
        var asContent = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "content", CancellationToken.None);
        var backToCampaign = await _host.Items.ChangeTypeAsync(scenario.Admin, item.Key, "campaign", CancellationToken.None);

        // Assert
        Assert.That(asContent.Succeeded, Is.True, () => asContent.Failure!.Message);
        Assert.That(backToCampaign.Succeeded, Is.True, () => backToCampaign.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((asContent.Value.Type, asContent.Value.State), Is.EqualTo(("content", "canceled")));
            Assert.That((backToCampaign.Value.Type, backToCampaign.Value.State), Is.EqualTo(("campaign", "canceled")));
        }
    }

    [Test]
    public async Task ChangeTypeAsync_ExecutorMovesADoneItemIntoAFlowWhoseDoneNeedsAManager_StartsOver()
    {
        // Arrange
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_b", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_b", "intake")));
    }

    [Test]
    public async Task ChangeTypeAsync_ManagerWithTheRequiredFieldFilled_KeepsTheDoneState()
    {
        // Arrange
        var scenario = await SharedDoneAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(manager, item.Key, "item_b", CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_b", "done")));
    }

    [Test]
    public async Task ChangeTypeAsync_ManagerWithoutTheRequiredField_StartsOver()
    {
        // Arrange
        var scenario = await SharedDoneAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: false);

        // Act
        var result = await _host.Items.ChangeTypeAsync(manager, item.Key, "item_b", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_b", "intake")));
    }

    [Test]
    public async Task ChangeTypeAsync_ExecutorThereAndBackAgain_CannotReachADoneStateTheFlowDoesNotAllow()
    {
        // Arrange：執行者把 A 的完成項目改成 B（回起點），再改回 A，不能留在 B 才走得到的位置。
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var toB = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_b", CancellationToken.None);
        var skip = await _host.Items.TransitionAsync(editor, item.Key, "done", CancellationToken.None);

        // Assert
        Assert.That(toB.Value.State, Is.EqualTo("intake"));
        ItemAssert.Invalid(skip, WorkErrorCodes.TransitionNotAllowed, "state");
    }

    [Test]
    public async Task ChangeTypeAsync_KeptStateHasAnotherEntryOnlyForManagers_ButTheCallerCanWalkThereAnyway_KeepsIt()
    {
        // Arrange：C 流程進 done 有兩條轉換，一條限定專案經理，另一條（wip → done）誰都能走；執行者從起點照轉換走得到 done。
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_c", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_c", "done")));
    }

    [Test]
    public async Task ChangeTypeAsync_EarlierStepOfTheNewFlowIsRestrictedToManagers_ExecutorStartsOver()
    {
        // Arrange：G 流程的最後一步不限，但前一步只有專案經理能走；執行者走不到 done。
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_g", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_g", "start")));
    }

    [Test]
    public async Task ChangeTypeAsync_EarlierStepOfTheNewFlowIsRestrictedToManagers_ManagerKeepsTheState()
    {
        // Arrange
        var scenario = await SharedDoneAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(manager, item.Key, "item_g", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_g", "done")));
    }

    [TestCase(false, "start", TestName = "ChangeTypeAsync_EarlierStepNeedsAFieldTheItemLacks_StartsOver")]
    [TestCase(true, "done", TestName = "ChangeTypeAsync_EarlierStepNeedsAFieldTheItemHas_KeepsTheState")]
    public async Task ChangeTypeAsync_EarlierStepRequiresAField_DecidesByTheItemsFields(bool withSignoff, string expected)
    {
        // Arrange：H 流程的第一步要先填客戶驗收日，最後一步不限。
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff);

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_h", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_h", expected)));
    }

    [Test]
    public async Task ChangeTypeAsync_NewFlowHasACycle_StillFindsTheKeptState()
    {
        // Arrange：L 流程有 wip → start 的回頭路。
        var scenario = await SharedDoneAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var result = await _host.Items.ChangeTypeAsync(editor, item.Key, "item_l", CancellationToken.None);

        // Assert
        Assert.That((result.Value.Type, result.Value.State), Is.EqualTo(("item_l", "done")));
    }

    [Test]
    public async Task ChangeTypeAsync_RoleWithoutItemTransitionNotMeetingTheEntryRule_StartsOverAndNeverLandsElsewhere()
    {
        // Arrange：沒有 item.transition 的自訂角色，符合條件時保留、不符合時回起點。
        var scenario = await SharedDoneAsync();
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with
        {
            Roles = [.. b.Roles, new RoleDefinition { Key = "editor_only", Name = "只能改", Permissions = [WorkPermissions.ItemRead, WorkPermissions.ItemUpdate] }],
        });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        var actor = await scenario.MemberAsync("WEB", "editor_only");
        var toB = await DoneItemOfFlowAAsync(scenario, withSignoff: true);
        var toC = await DoneItemOfFlowAAsync(scenario, withSignoff: true);

        // Act
        var b = await _host.Items.ChangeTypeAsync(actor, toB.Key, "item_b", CancellationToken.None);
        var c = await _host.Items.ChangeTypeAsync(actor, toC.Key, "item_c", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(b.Value.State, Is.EqualTo("intake"));
            Assert.That(c.Value.State, Is.EqualTo("start"));
        }
    }

    // 自訂藍圖：item_a 與 item_b 兩個流程共用 done；B 進 done 只有專案經理、要先填客戶驗收日；item_c 的 done 有兩條轉換進得去，其中一條限定專案經理。
    private async Task<Scenario> SharedDoneAsync()
    {
        var scenario = await AgencyAsync();
        var proposed = await ChangeSetDrafts.ProposeAsync(scenario, "WEB", b => b with
        {
            Workflows =
            [
                .. b.Workflows,
                Flow("flow_a", "start", "wip", null),
                Flow("flow_b", "intake", "waiting", new WorkflowTransition { From = "waiting", To = "done", AllowedRoles = ["project_manager"], RequireFields = ["client_signoff_date"] }),
                Walk("flow_g", new WorkflowTransition { From = "start", To = "wip", AllowedRoles = ["project_manager"] }, new WorkflowTransition { From = "wip", To = "done" }),
                Walk("flow_h", new WorkflowTransition { From = "start", To = "wip", RequireFields = ["client_signoff_date"] }, new WorkflowTransition { From = "wip", To = "done" }),
                Walk(
                    "flow_l",
                    new WorkflowTransition { From = "start", To = "wip" },
                    new WorkflowTransition { From = "wip", To = "start" },
                    new WorkflowTransition { From = "wip", To = "done" }),
                Flow(
                    "flow_c",
                    "start",
                    "wip",
                    new WorkflowTransition { From = "wip", To = "done" },
                    new WorkflowTransition { From = "start", To = "done", AllowedRoles = ["project_manager"] }),
            ],
            ItemTypes =
            [
                .. b.ItemTypes,
                new ItemType { Key = "item_a", Name = "甲", Workflow = "flow_a", Fields = ["client_signoff_date"] },
                new ItemType { Key = "item_b", Name = "乙", Workflow = "flow_b", Fields = ["client_signoff_date"] },
                new ItemType { Key = "item_c", Name = "丙", Workflow = "flow_c", Fields = ["client_signoff_date"] },
                new ItemType { Key = "item_g", Name = "庚", Workflow = "flow_g", Fields = ["client_signoff_date"] },
                new ItemType { Key = "item_h", Name = "辛", Workflow = "flow_h", Fields = ["client_signoff_date"] },
                new ItemType { Key = "item_l", Name = "壬", Workflow = "flow_l", Fields = ["client_signoff_date"] },
            ],
        });
        await ChangeSetDrafts.ApplyAsync(scenario, "WEB", proposed.ChangeSet.Number);
        return scenario;
    }

    // start、wip、done 三個狀態，轉換照給的列。
    private static Workflow Walk(string key, params WorkflowTransition[] transitions) => new()
    {
        Key = key,
        Name = key,
        States =
        [
            new WorkflowState { Key = "start", Name = "start", Category = StateCategory.NotStarted },
            new WorkflowState { Key = "wip", Name = "wip", Category = StateCategory.InProgress },
            new WorkflowState { Key = "done", Name = "完成", Category = StateCategory.Done },
        ],
        Transitions = transitions,
    };

    private static Workflow Flow(string key, string first, string middle, WorkflowTransition? intoDone, WorkflowTransition? extra = null) => new()
    {
        Key = key,
        Name = key,
        States =
        [
            new WorkflowState { Key = first, Name = first, Category = StateCategory.NotStarted },
            new WorkflowState { Key = middle, Name = middle, Category = StateCategory.InProgress },
            new WorkflowState { Key = "done", Name = "完成", Category = StateCategory.Done },
        ],
        Transitions = intoDone is null
            ? null
            : [new WorkflowTransition { From = first, To = middle }, intoDone, .. extra is null ? Array.Empty<WorkflowTransition>() : [extra]],
    };

    private async Task<WorkItem> DoneItemOfFlowAAsync(Scenario scenario, bool withSignoff)
    {
        var item = await scenario.CreateItemAsync(
            "WEB",
            new NewWorkItem
            {
                Type = "item_a",
                Title = "共用完成",
                Fields = withSignoff ? Scenario.Fields(("client_signoff_date", "2026-01-01")) : Scenario.Fields(),
            });
        await TransitionAsync(scenario.Admin, item.Key, "done");
        return item;
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
