using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>新增、修改、改狀態各留一筆活動，和修改在同一個交易裡；被擋下來的操作什麼都不留。</summary>
[TestFixture]
public sealed class ActivityRecordingTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_RecordsACreatedActivityByTheCreator()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務" }, editor);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, "web-1");

        // Assert
        Assert.That(activities, Has.Count.EqualTo(1));
        var created = activities[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That((created.ItemKey, created.Kind, created.ActorId, created.At), Is.EqualTo((item.Key, ActivityKind.Created, editor.PersonId, item.CreatedAt)));
            Assert.That(created.Changes, Is.Empty);
            Assert.That((created.FromState, created.ToState, created.Comment), Is.EqualTo(((string?)null, (string?)null, (string?)null)));
            Assert.That(created.Id, Is.Not.EqualTo(Guid.Empty));
        }
    }

    [Test]
    public async Task UpdateAsync_RecordsAnUpdatedActivityWithOnlyTheFieldsThatChanged()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "原本的標題", Priority = Priority.High });
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var patch = new WorkItemPatch { Title = "新的標題", Priority = Priority.High, DueDate = new DateOnly(2026, 12, 24) };

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert：優先順序給了和原本一樣的值，不算有改。
        Assert.That((activity.Kind, activity.ActorId, activity.At), Is.EqualTo((ActivityKind.Updated, scenario.Admin.PersonId, updated.Value.UpdatedAt)));
        Assert.That(activity.At, Is.EqualTo(item.CreatedAt.AddMinutes(5)));
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[]
        {
            ("title", "\"原本的標題\"", "\"新的標題\""),
            ("due_date", null, "\"2026-12-24\""),
        }));
    }

    [Test]
    public async Task TransitionAsync_RecordsTheStatesItMovedBetween()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var moved = await _host.Items.TransitionAsync(editor, item.Key, "doing", CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(
            (activity.Kind, activity.ActorId, activity.At, activity.FromState, activity.ToState),
            Is.EqualTo((ActivityKind.Transitioned, editor.PersonId, moved.Value.UpdatedAt, "todo", "doing")));
        Assert.That(activity.Changes, Is.Empty);
        Assert.That(activity.Comment, Is.Null);
    }

    [Test]
    public async Task EveryWrite_IsListedOldestFirst()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "改過的標題" }, CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.TransitionAsync(scenario.Admin, item.Key, "done", CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "做完了", null, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[]
        {
            ActivityKind.Created, ActivityKind.Updated, ActivityKind.Transitioned, ActivityKind.Commented,
        }));
        Assert.That(activities.Select(a => a.At), Is.Ordered.Ascending);
    }

    [Test]
    public async Task UpdateAsync_SameValuesAsBefore_RecordsNothingAndKeepsUpdatedAt()
    {
        // Arrange：每個值都換一種寫法給：頭尾的空白、數字多帶 0、多選換順序、清掉本來就沒有的欄位。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Description = "說明",
            AssigneeId = scenario.Admin.PersonId,
            Priority = Priority.Low,
            StartDate = new DateOnly(2026, 11, 1),
            Estimate = 2.5m,
            Fields = Scenario.Fields(("tags", new[] { "docs", "backend" }), ("story_points", 3), ("blocked", true), ("summary", "摘要")),
        });
        _host.Clock.Advance(TimeSpan.FromMinutes(5));
        var patch = new WorkItemPatch
        {
            Title = "  任務 ",
            Description = "說明\n",
            AssigneeId = scenario.Admin.PersonId,
            Priority = Priority.Low,
            StartDate = new DateOnly(2026, 11, 1),
            DueDate = null,
            Estimate = 2.50m,
            ParentKey = null,
            Fields = Scenario.Fields(("tags", new[] { "backend", "docs" }), ("story_points", 3.0m), ("blocked", true), ("summary", "摘要"), ("notes", null)),
        };

        // Act
        var result = await _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(result.Value.UpdatedAt, Is.EqualTo(item.CreatedAt));
        Assert.That(readBack.Value.UpdatedAt, Is.EqualTo(item.CreatedAt));
        Assert.That(ActivityLog.Text(readBack.Value.Fields["tags"]), Is.EqualTo("[\"docs\",\"backend\"]"));
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task UpdateAsync_InvalidChange_RecordsNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Title = "會被退回", Fields = Scenario.Fields(("review_date", "明天")) }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "review_date");
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task WritesWithoutPermission_RecordNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var update = await _host.Items.UpdateAsync(viewer, item.Key, new WorkItemPatch { Title = "改不了" }, CancellationToken.None);
        var transition = await _host.Items.TransitionAsync(viewer, item.Key, "doing", CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(viewer, item.Key, "留不了", null, CancellationToken.None);
        var outsider = await _host.Items.UpdateAsync(scenario.Person(), item.Key, new WorkItemPatch { Title = "看不到" }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Failed(update, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(transition, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(comment, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        ItemAssert.Failed(outsider, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task TransitionAsync_Blocked_RecordsNothing()
    {
        // Arrange：agency_project 的需求確認不能直接跳到設計；內部驗收到客戶驗收限定專案經理。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var skipped = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "designing", CancellationToken.None);
        var sameState = await _host.Items.TransitionAsync(editor, item.Key, "confirming", CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Invalid(skipped, WorkErrorCodes.TransitionNotAllowed, "state");
        ItemAssert.Invalid(sameState, WorkErrorCodes.TransitionNotAllowed, "state");
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
    }

    [Test]
    public async Task TransitionAsync_RoleNotAllowed_RecordsNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        foreach (var state in new[] { "quoting", "designing", "developing", "internal_qa" })
        {
            _host.Clock.Advance(TimeSpan.FromMinutes(1));
            await _host.Items.TransitionAsync(scenario.Admin, item.Key, state, CancellationToken.None);
        }

        // Act
        var result = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "client_qa", CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.TransitionRoleRequired);
        Assert.That(activities.Select(a => a.ToState), Is.EqualTo(new[] { null, "quoting", "designing", "developing", "internal_qa" }));
    }
}
