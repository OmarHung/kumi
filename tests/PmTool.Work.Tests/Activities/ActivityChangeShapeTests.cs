namespace PmTool.Work.Tests.Activities;

/// <summary>
/// 修改活動裡欄位前後值的形狀，和項目對外的形狀一樣：人員是編號字串、上層是項目編號、優先度是 snake_case、
/// 日期是 YYYY-MM-DD、數字是正規化後的數字。說明與長文字只記有改，前後值一律是 null。
/// 每次修改之前讓時鐘走一下：同一時間的活動依編號排，看不出先後。
/// </summary>
[TestFixture]
public sealed class ActivityChangeShapeTests
{
    private static readonly Guid Reviewer = new("0b7e3c58-1d2a-4f6e-9a41-5c2d8e7f9a10");

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task UpdateAsync_EveryCoreFieldSet_RecordsTheirShapesInOrder()
    {
        // Arrange
        var (scenario, assignee) = await AgencyAsync();
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁" });
        var patch = new WorkItemPatch
        {
            Title = "做登入頁與註冊頁",
            Description = "照設計稿做",
            AssigneeId = assignee,
            Priority = Priority.Urgent,
            StartDate = new DateOnly(2026, 11, 20),
            DueDate = new DateOnly(2026, 11, 30),
            Estimate = 2.50m,
            ParentKey = "web-1",
        };

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, task.Key, patch, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);

        // Assert
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[]
        {
            ("title", "\"做登入頁\"", "\"做登入頁與註冊頁\""),
            ("description", null, null),
            ("assignee", null, "\"9f1c2a7e-3b4d-4e5f-8a6b-7c8d9e0f1a2b\""),
            ("priority", null, "\"urgent\""),
            ("start_date", null, "\"2026-11-20\""),
            ("due_date", null, "\"2026-11-30\""),
            ("estimate", null, "2.5"),
            ("parent", null, "\"WEB-1\""),
        }));
    }

    [Test]
    public async Task UpdateAsync_EveryCoreFieldCleared_RecordsTheOldValuesAndNull()
    {
        // Arrange
        var (scenario, assignee) = await AgencyAsync();
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "做登入頁",
            Description = "照設計稿做",
            AssigneeId = assignee,
            Priority = Priority.Medium,
            StartDate = new DateOnly(2026, 11, 20),
            DueDate = new DateOnly(2026, 11, 30),
            Estimate = 0.0001m,
            ParentKey = "WEB-1",
        });
        var patch = new WorkItemPatch
        {
            Description = "  ",
            AssigneeId = null,
            Priority = Priority.None,
            StartDate = null,
            DueDate = null,
            Estimate = null,
            ParentKey = null,
        };

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, task.Key, patch, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);

        // Assert：優先度「無」算沒有值。
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[]
        {
            ("description", null, null),
            ("assignee", "\"9f1c2a7e-3b4d-4e5f-8a6b-7c8d9e0f1a2b\"", null),
            ("priority", "\"medium\"", null),
            ("start_date", "\"2026-11-20\"", null),
            ("due_date", "\"2026-11-30\"", null),
            ("estimate", "0.0001", null),
            ("parent", "\"WEB-1\"", null),
        }));
    }

    [Test]
    public async Task UpdateAsync_MovedToAnotherParent_RecordsBothItemKeys()
    {
        // Arrange
        var (scenario, _) = await AgencyAsync();
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "change_request", Title = "改需求" });
        var task = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "做登入頁", ParentKey = "WEB-1" });

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, task.Key, new WorkItemPatch { ParentKey = "WEB-2" }, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, task.Key);

        // Assert
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[] { ("parent", "\"WEB-1\"", "\"WEB-2\"") }));
    }

    [Test]
    public async Task UpdateAsync_CustomFieldsSetChangedAndCleared_RecordTheirShapesInTheTypesOrder()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", Reviewer, "editor", CancellationToken.None);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Fields = Scenario.Fields(
                ("category", "bug"), ("tags", new[] { "docs" }), ("story_points", 3), ("blocked", true),
                ("reference_url", "https://example.com/old"), ("reviewer", Reviewer), ("notes", "舊的備註")),
        });
        var patch = new WorkItemPatch
        {
            Fields = Scenario.Fields(
                ("notes", "新的備註"), ("summary", "新的摘要"), ("reviewer", null), ("reference_url", "  "), ("blocked", false),
                ("budget", 1200.50m), ("story_points", 1e1), ("review_date", "2026-12-01"), ("tags", new[] { "frontend", "docs" }),
                ("category", "chore")),
        };

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert：勾選取消、空白的網址都是清掉；長文字只記有改。
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[]
        {
            ("category", "\"bug\"", "\"chore\""),
            ("tags", "[\"docs\"]", "[\"frontend\",\"docs\"]"),
            ("review_date", null, "\"2026-12-01\""),
            ("story_points", "3", "10"),
            ("budget", null, "1200.5"),
            ("blocked", "true", null),
            ("reference_url", "\"https://example.com/old\"", null),
            ("reviewer", "\"0b7e3c58-1d2a-4f6e-9a41-5c2d8e7f9a10\"", null),
            ("summary", null, "\"新的摘要\""),
            ("notes", null, null),
        }));
    }

    [Test]
    public async Task UpdateAsync_OnlyTheDescriptionOrALongTextChanged_RecordsThatTheyChangedWithoutTheText()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Description = "舊的說明",
            Fields = Scenario.Fields(("notes", "舊的備註")),
        });

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "新的說明" }, CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Fields = Scenario.Fields(("notes", null)) }, CancellationToken.None);
        var activities = await ActivityLog.AllAsync(_host, scenario.Admin, item.Key);

        // Assert
        Assert.That(activities.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Updated, ActivityKind.Updated }));
        Assert.That(ActivityLog.Shapes(activities[1]), Is.EqualTo(new (string, string?, string?)[] { ("description", null, null) }));
        Assert.That(ActivityLog.Shapes(activities[2]), Is.EqualTo(new (string, string?, string?)[] { ("notes", null, null) }));
    }

    [Test]
    public async Task UpdateAsync_MultiSelectReordered_IsNotAChangeButOtherFieldsInThePatchAre()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "任務",
            Fields = Scenario.Fields(("tags", new[] { "docs", "backend" })),
        });
        var patch = new WorkItemPatch { Title = "新標題", Fields = Scenario.Fields(("tags", new[] { "backend", "docs" })) };

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, patch, CancellationToken.None);
        var activity = await ActivityLog.LastAsync(_host, scenario.Admin, item.Key);

        // Assert：選了哪些沒變，存著的順序也維持原樣。
        Assert.That(ActivityLog.Shapes(activity), Is.EqualTo(new (string, string?, string?)[] { ("title", "\"任務\"", "\"新標題\"") }));
        Assert.That(ActivityLog.Text(updated.Value.Fields["tags"]), Is.EqualTo("[\"docs\",\"backend\"]"));
    }

    /// <summary>agency_project 才有上層項目；負責人是固定編號的成員，預期值才能寫死。</summary>
    private async Task<(Scenario Scenario, Guid Assignee)> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var assignee = new Guid("9f1c2a7e-3b4d-4e5f-8a6b-7c8d9e0f1a2b");
        await _host.Projects.SetMemberAsync(scenario.Admin, "WEB", assignee, "editor", CancellationToken.None);
        return (scenario, assignee);
    }
}
