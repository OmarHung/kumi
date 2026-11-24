using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>「我的工作」：只有自己負責的、還沒結束的、看得到的專案裡的項目，依到期日排序。</summary>
[TestFixture]
public sealed class MyWorkTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAssignedToMeAsync_AcrossProjects_ListsOnlyMyOpenItemsByDueDate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("APP", Scenario.AgencyProject);
        var me = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "APP", me.PersonId, "viewer", CancellationToken.None);
        var someoneElse = await scenario.MemberAsync("WEB", "editor");

        await Assigned(scenario, "WEB", "沒有到期日", me.PersonId, due: null);
        await Assigned(scenario, "WEB", "下週到期", me.PersonId, due: new DateOnly(2026, 11, 20));
        var done = await Assigned(scenario, "WEB", "已經完成", me.PersonId, due: new DateOnly(2026, 11, 1));
        await _host.Items.TransitionAsync(scenario.Admin, done.Key, "done", CancellationToken.None);
        await Assigned(scenario, "WEB", "別人的", someoneElse.PersonId, due: new DateOnly(2026, 11, 2));
        await Assigned(scenario, "WEB", "沒人負責", null, due: new DateOnly(2026, 11, 3));
        await scenario.CreateItemAsync("APP", new NewWorkItem
        {
            Type = "requirement",
            Title = "明天到期",
            AssigneeId = me.PersonId,
            DueDate = new DateOnly(2026, 11, 14),
        });

        // Act
        var mine = await _host.Items.ListAssignedToMeAsync(me, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(i => i.Title), Is.EqualTo(new[] { "明天到期", "下週到期", "沒有到期日" }));
        Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { "APP-1", "WEB-2", "WEB-1" }));
    }

    [TestCase("done")]
    [TestCase("canceled")]
    public async Task ListAssignedToMeAsync_CanceledOrDone_AreLeftOut(string closedState)
    {
        // Arrange：簡單任務看板的「完成」是 done 類別；模板沒有 canceled 類別的狀態，換一版加上「已取消」。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await PublishAsync(scenario, "WEB", blueprint => blueprint with
        {
            Workflows = [.. blueprint.Workflows.Select(w => w with
            {
                States = [.. w.States, new WorkflowState { Key = "canceled", Name = "已取消", Category = StateCategory.Canceled }],
            })],
        });
        var item = await Assigned(scenario, "WEB", "收尾了", scenario.Admin.PersonId, due: null);
        await MoveAsync(scenario, item.Key, "doing");
        var inProgress = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        await MoveAsync(scenario, item.Key, closedState);

        // Act
        var afterClosing = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(inProgress.Select(i => i.Key), Is.EqualTo(new[] { item.Key }));
        Assert.That(afterClosing, Is.Empty);
    }

    [Test]
    public async Task ListAssignedToMeAsync_ProjectWhereMyRoleCannotReadItems_IsLeftOut()
    {
        // Arrange：我在 WEB 的角色只能留言、讀不到項目；在 APP 是檢視者。兩邊都有指派給我的項目。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await PublishAsync(scenario, "WEB", blueprint => blueprint with
        {
            Roles = [new RoleDefinition { Key = "guest", Name = "訪客", Permissions = [WorkPermissions.CommentCreate] }],
        });
        await scenario.CreateProjectAsync("APP");
        var me = await scenario.MemberAsync("WEB", "guest");
        await _host.Projects.SetMemberAsync(scenario.Admin, "APP", me.PersonId, "viewer", CancellationToken.None);
        await Assigned(scenario, "WEB", "讀不到的", me.PersonId, due: null);
        await Assigned(scenario, "APP", "讀得到的", me.PersonId, due: null);

        // Act
        var mine = await _host.Items.ListAssignedToMeAsync(me, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { "APP-1" }));
    }

    [Test]
    public async Task ListAssignedToMeAsync_SamePersonInAnotherOrganization_SeesNothingFromThisOne()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Assigned(scenario, "WEB", "我的", scenario.Admin.PersonId, due: null);
        var elsewhere = new Scenario(_host).Admin with { PersonId = scenario.Admin.PersonId };

        // Act
        var result = await _host.Items.ListAssignedToMeAsync(elsewhere, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAssignedToMeAsync_MoreThanTheLimit_ReturnsAtMostMaxLimit()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var n in Enumerable.Range(1, WorkItemQuery.MaxLimit + 1))
        {
            await Assigned(scenario, "WEB", $"任務 {n}", scenario.Admin.PersonId, due: null);
        }

        // Act
        var result = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(result, Has.Count.EqualTo(WorkItemQuery.MaxLimit));
    }

    private static Task<WorkItem> Assigned(Scenario scenario, string projectKey, string title, Guid? assignee, DateOnly? due) =>
        scenario.CreateItemAsync(projectKey, new NewWorkItem { Type = "task", Title = title, AssigneeId = assignee, DueDate = due });

    /// <summary>以專案目前的藍圖為底改一版，直接存進資料庫：這裡要測的是「我的工作」，不必經過變更集。</summary>
    private async Task PublishAsync(Scenario scenario, string projectKey, Func<ProjectBlueprint, ProjectBlueprint> change)
    {
        var current = await _host.Projects.GetAsync(scenario.Admin, projectKey, CancellationToken.None);
        await StoredBlueprints.PublishAsync(_host, scenario, projectKey, change(current.Value.Blueprint));
    }

    private async Task MoveAsync(Scenario scenario, string itemKey, string state)
    {
        var result = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }
}
