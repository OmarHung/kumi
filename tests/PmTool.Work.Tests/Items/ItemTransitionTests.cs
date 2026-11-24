using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 改狀態。agency_project 的交付流程有列轉換：內部驗收到客戶驗收只有專案經理可以做，
/// 客戶驗收到結案還要先填客戶驗收日。simple_tasks 沒有列轉換，任何兩個不同的狀態都可以。
/// </summary>
[TestFixture]
public sealed class ItemTransitionTests
{
    private static readonly string[] CoreFieldsRequired = ["description", "parent"];

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("doing", StateCategory.InProgress)]
    [TestCase("done", StateCategory.Done)]
    public async Task TransitionAsync_WorkflowWithoutTransitions_AllowsAnyOtherState(string to, StateCategory category)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.TransitionAsync(editor, item.Key, to, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(editor, item.Key, CancellationToken.None);

        // Assert：狀態類別跟著更新。
        Assert.That((result.Value.State, result.Value.StateCategory), Is.EqualTo((to, category)));
        Assert.That((readBack.Value.State, readBack.Value.StateCategory), Is.EqualTo((to, category)));
    }

    [Test]
    public async Task TransitionAsync_BackToAnEarlierStateWithoutTransitions_IsAllowed()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await _host.Items.TransitionAsync(scenario.Admin, item.Key, "done", CancellationToken.None);

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "todo", CancellationToken.None);

        // Assert
        Assert.That(result.Value.StateCategory, Is.EqualTo(StateCategory.NotStarted));
    }

    [TestCase("todo")]
    [TestCase("archived")]
    public async Task TransitionAsync_SameOrUnknownState_IsNotAllowed(string to)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, to, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.TransitionNotAllowed, "state");
    }

    [Test]
    public async Task TransitionAsync_ListedTransition_IsAllowed()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "quoting", CancellationToken.None);

        // Assert
        Assert.That(result.Value.State, Is.EqualTo("quoting"));
    }

    [Test]
    public async Task TransitionAsync_TransitionThatIsNotListed_IsNotAllowedAndSaysWhereItCanGo()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "designing", CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.TransitionNotAllowed, "state");
        Assert.That(result.Failure!.Message, Does.Contain("「需求確認」").And.Contain("「設計」").And.Contain("「報價」"));
        Assert.That(readBack.Value.State, Is.EqualTo("confirming"));
    }

    [Test]
    public async Task TransitionAsync_RestrictedTransitionByProjectAdmin_RequiresTheListedRole()
    {
        // Arrange：建立者是專案的 admin，也是組織管理員；轉換限定的角色連他也要遵守。
        var scenario = await AgencyAsync();
        var item = await InternalQaAsync(scenario);

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "client_qa", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.TransitionRoleRequired);
        Assert.That(result.Failure!.Message, Does.Contain("只有「專案經理」可以做"));
        Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Is.EqualTo(new[] { (WorkErrorCodes.TransitionRoleRequired, "role") }));
        Assert.That(result.Failure.Details[0].Message, Does.Contain("project_manager"));
    }

    [Test]
    public async Task TransitionAsync_RestrictedTransitionByOrganizationAdminWhoIsNotAMember_RequiresTheListedRole()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await InternalQaAsync(scenario);
        var otherAdmin = scenario.Admin with { PersonId = Guid.NewGuid() };

        // Act
        var result = await _host.Items.TransitionAsync(otherAdmin, item.Key, "client_qa", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.TransitionRoleRequired);
    }

    [Test]
    public async Task TransitionAsync_RestrictedTransitionByOrganizationAdminAssignedTheRole_IsAllowed()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await InternalQaAsync(scenario);
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", scenario.Admin.PersonId, "project_manager", CancellationToken.None);

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "client_qa", CancellationToken.None);

        // Assert
        Assert.That(result.Value.State, Is.EqualTo("client_qa"));
    }

    [Test]
    public async Task TransitionAsync_RequiredFieldMissing_ListsTheFieldAndKeepsTheState()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var item = await ClientQaAsync(scenario, manager);

        // Act
        var result = await _host.Items.TransitionAsync(manager, item.Key, "closed", CancellationToken.None);
        var readBack = await _host.Items.GetAsync(manager, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.TransitionFieldsRequired, "client_signoff_date");
        Assert.That(result.Failure!.Message, Does.Contain("要先填「客戶驗收日」").And.Contain("「結案」"));
        Assert.That(readBack.Value.State, Is.EqualTo("client_qa"));
    }

    [Test]
    public async Task TransitionAsync_RequiredFieldFilledByTheRightRole_Closes()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        var item = await ClientQaAsync(scenario, manager);
        await _host.Items.UpdateAsync(
            manager, item.Key, new WorkItemPatch { Fields = Scenario.Fields(("client_signoff_date", "2026-11-20")) }, CancellationToken.None);

        // Act
        var result = await _host.Items.TransitionAsync(manager, item.Key, "closed", CancellationToken.None);

        // Assert
        Assert.That((result.Value.State, result.Value.StateCategory), Is.EqualTo(("closed", StateCategory.Done)));
    }

    [TestCase("viewer")]
    [TestCase("client")]
    public async Task TransitionAsync_RoleWithoutItemTransition_IsForbidden(string role)
    {
        // Arrange
        var scenario = await AgencyAsync();
        var member = await scenario.MemberAsync("WEB", role);
        var item = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var result = await _host.Items.TransitionAsync(member, item.Key, "quoting", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }

    [Test]
    public async Task TransitionAsync_ItemTheCallerCannotSee_IsItemNotFound()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", Requirement());

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Person(), item.Key, "quoting", CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task TransitionAsync_DescriptionAndParentRequired_AreCheckedAgainstTheStoredItem()
    {
        // Arrange：換一版藍圖，任務從待辦轉到進行中要先有說明與上層項目。
        var scenario = await AgencyAsync();
        await RequireOnTaskStartAsync(scenario, CoreFieldsRequired);
        var requirement = await scenario.CreateItemAsync("WEB", Requirement());
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁" });

        // Act
        var blocked = await _host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);
        await _host.Items.UpdateAsync(
            scenario.Admin, task.Key, new WorkItemPatch { Description = "照設計稿做", ParentKey = requirement.Key }, CancellationToken.None);
        var allowed = await _host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);

        // Assert
        Assert.That(blocked.Failure!.Code, Is.EqualTo(WorkErrorCodes.TransitionFieldsRequired));
        Assert.That(blocked.Failure.Details.Select(d => d.Path), Is.EqualTo(new[] { "description", "parent" }));
        Assert.That(blocked.Failure.Message, Does.Contain("「說明」").And.Contain("「上層項目」"));
        Assert.That(allowed.Value.State, Is.EqualTo("doing"));
    }

    [Test]
    public async Task TransitionAsync_DescriptionClearedAfterBeingFilled_IsRequiredAgain()
    {
        // Arrange
        var scenario = await AgencyAsync();
        await RequireOnTaskStartAsync(scenario, ["description"]);
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁", Description = "照設計稿做" });
        await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { Description = "   " }, CancellationToken.None);

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, task.Key, "doing", CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.TransitionFieldsRequired, "description");
    }

    /// <summary>任務流程本來沒有列轉換；改成列出來，待辦到進行中要先填指定的欄位。</summary>
    private async Task RequireOnTaskStartAsync(Scenario scenario, IReadOnlyList<string> fields)
    {
        var current = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var blueprint = current.Value.Blueprint;
        // 其餘狀態一律從進行中進得去、走得到完成：驗證要求每個狀態都走得到、也走得出去。
        string[] others = ["internal_review", "waiting_client", "to_release", "on_hold", "canceled"];
        WorkflowTransition[] transitions =
        [
            new() { From = "todo", To = "doing", RequireFields = fields },
            new() { From = "doing", To = "done" },
            .. others.Select(state => new WorkflowTransition { From = "doing", To = state }),
            .. others.Where(state => state != "canceled").Select(state => new WorkflowTransition { From = state, To = "done" }),
        ];
        await StoredBlueprints.PublishAsync(_host, scenario, "WEB", blueprint with
        {
            Workflows = [.. blueprint.Workflows.Select(w => w.Key == "task_flow" ? w with { Transitions = transitions } : w)],
        });
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Requirement() => new() { Type = "requirement", Title = "會員登入" };

    /// <summary>一路走到內部驗收；前面這幾步沒有限定角色。</summary>
    private async Task<WorkItem> InternalQaAsync(Scenario scenario)
    {
        var item = await scenario.CreateItemAsync("WEB", Requirement());
        foreach (var state in new[] { "quoting", "designing", "developing", "internal_qa" })
        {
            var step = await _host.Items.TransitionAsync(scenario.Admin, item.Key, state, CancellationToken.None);
            Assert.That(step.Succeeded, Is.True, () => step.Failure!.Message);
        }

        return item;
    }

    private async Task<WorkItem> ClientQaAsync(Scenario scenario, Actor manager)
    {
        var item = await InternalQaAsync(scenario);
        var step = await _host.Items.TransitionAsync(manager, item.Key, "client_qa", CancellationToken.None);
        Assert.That(step.Succeeded, Is.True, () => step.Failure!.Message);
        return item;
    }
}
