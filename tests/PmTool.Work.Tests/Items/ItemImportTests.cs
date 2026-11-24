using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 試算表匯入：全部或全部不；每一列的規則和新增一樣；編號照列的順序；狀態直接設定（非起始狀態需要 project.manage）；
/// 乾跑只檢查；每一列留一筆 Created 活動與一筆 Imported 的 item_created 事件。
/// </summary>
[TestFixture]
public sealed class ItemImportTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ImportAsync_ValidRows_CreatesThemInOrderWithConsecutiveNumbers()
    {
        // Arrange
        var scenario = await ProjectAsync();
        await scenario.CreateTaskAsync("WEB", "原有的");

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(Row("甲"), Row("乙"), Row("丙")), CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-2", "WEB-3", "WEB-4" }));
            Assert.That((result.Value.DryRun, result.Value.Count), Is.EqualTo((false, 3)));
            Assert.That(listed.Value.Items.Select(i => i.Title), Is.EquivalentTo(new[] { "原有的", "甲", "乙", "丙" }));
        }
    }

    [Test]
    public async Task ImportAsync_RowWithEveryField_StoresTheSameValuesAsCreate()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var assignee = await scenario.MemberAsync("WEB", "editor");
        var row = new NewWorkItem
        {
            Type = "task",
            Title = "完整的一列",
            Description = "第一行\n第二行",
            AssigneeId = assignee.PersonId,
            Priority = Priority.High,
            StartDate = new DateOnly(2026, 11, 2),
            DueDate = new DateOnly(2026, 11, 20),
            Estimate = 3.5m,
        };

        // Act
        var imported = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(new ImportedItem { Item = row }), CancellationToken.None);
        var stored = await _host.Items.GetAsync(scenario.Admin, imported.Value.CreatedKeys[0], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.Value.Title, Is.EqualTo("完整的一列"));
            Assert.That(stored.Value.Description, Is.EqualTo("第一行\n第二行"));
            Assert.That(stored.Value.AssigneeId, Is.EqualTo(assignee.PersonId));
            Assert.That(stored.Value.Priority, Is.EqualTo(Priority.High));
            Assert.That((stored.Value.StartDate, stored.Value.DueDate), Is.EqualTo((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 20))));
            Assert.That(stored.Value.Estimate, Is.EqualTo(3.5m));
            Assert.That(stored.Value.State, Is.EqualTo("todo"), "沒給狀態是流程的第一個狀態");
        }
    }

    [Test]
    public async Task ImportAsync_DryRun_ChecksWithoutCreatingAndDoesNotConsumeNumbers()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var dry = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(dryRun: true, Row("甲"), Row("乙")), CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        var real = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(Row("甲")), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((dry.Value.DryRun, dry.Value.Count, dry.Value.CreatedKeys), Is.EqualTo((true, 2, Array.Empty<string>())));
            Assert.That(listed.Value.Items, Is.Empty);
            Assert.That(real.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-1" }), "乾跑不發編號");
        }
    }

    [Test]
    public async Task ImportAsync_DryRunWithABadRow_StillReportsTheProblems()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(dryRun: true, Row("好"), Row("")), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "rows/1/title" }));
    }

    [Test]
    public async Task ImportAsync_OneBadRow_CreatesNothingAndReportsRowPaths()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var rows = Import(
            Row("好的"),
            Row(""),
            new ImportedItem { Item = new NewWorkItem { Type = "nope", Title = "壞類型" } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "壞日期", StartDate = new DateOnly(2026, 11, 20), DueDate = new DateOnly(2026, 11, 1) } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "不認得的負責人", AssigneeId = Guid.NewGuid() } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "壞上層", ParentKey = "WEB-99" } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "壞狀態" }, State = "nope" });

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", rows, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Invalid, WorkErrorCodes.ImportInvalid)));
            Assert.That(
                result.Failure!.Details.Select(d => d.Path),
                Is.EqualTo(new[] { "rows/1/title", "rows/2/type", "rows/3/due_date", "rows/4/assignee", "rows/5/parent", "rows/6/state" }));
            Assert.That(result.Failure.Details[1].Code, Is.EqualTo(WorkErrorCodes.ItemUnknownType));
            Assert.That(listed.Value.Items, Is.Empty, "全部或全部不");
        }
    }

    [Test]
    public async Task ImportAsync_BadRow_DoesNotConsumeItemNumbers()
    {
        // Arrange
        var scenario = await ProjectAsync();
        await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(Row("好"), Row("")), CancellationToken.None);

        // Act
        var next = await scenario.CreateTaskAsync("WEB");

        // Assert
        Assert.That(next.Key, Is.EqualTo("WEB-1"));
    }

    [Test]
    public async Task ImportAsync_RowWithSeveralProblems_ReportsEachOfThem()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var row = new ImportedItem
        {
            Item = new NewWorkItem { Type = "task", Title = "", StartDate = new DateOnly(2026, 11, 20), DueDate = new DateOnly(2026, 11, 1) },
        };

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(row), CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "rows/0/title", "rows/0/due_date" }));
    }

    [Test]
    public async Task ImportAsync_MoreThanTheDetailLimitOfProblems_TruncatesAndSaysHowManyAreLeft()
    {
        // Arrange：每一列兩個問題（標題與日期），一千列共兩千個。
        var scenario = await ProjectAsync();
        var bad = new ImportedItem
        {
            Item = new NewWorkItem { Type = "task", Title = "", StartDate = new DateOnly(2026, 11, 20), DueDate = new DateOnly(2026, 11, 1) },
        };
        var rows = new ItemImport { Rows = [.. Enumerable.Repeat(bad, ItemImport.MaxRows)] };

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", rows, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Details, Has.Count.EqualTo(500));
            Assert.That(result.Failure.Message, Does.Contain("1500"), "還有 1500 個沒有列出");
            Assert.That(result.Failure.Message, Does.Contain("2000"));
        }
    }

    [Test]
    public async Task ImportAsync_NoRows_FailsWithRowsPath()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", new ItemImport { Rows = [] }, CancellationToken.None);

        // Assert
        Assert.That((result.Failure?.Code, result.Failure?.Details.Single().Path), Is.EqualTo((WorkErrorCodes.ImportInvalid, "rows")));
    }

    [Test]
    public async Task ImportAsync_ExactlyTheMaximumRows_Succeeds()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var rows = new ItemImport { Rows = [.. Enumerable.Range(1, ItemImport.MaxRows).Select(i => Row($"項目 {i}"))] };

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", rows, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value.Count, Is.EqualTo(ItemImport.MaxRows));
            Assert.That(result.Value.CreatedKeys[^1], Is.EqualTo($"WEB-{ItemImport.MaxRows}"));
        }
    }

    [Test]
    public async Task ImportAsync_OneMoreThanTheMaximum_FailsWithRowsPathAndCreatesNothing()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var rows = new ItemImport { Rows = [.. Enumerable.Range(1, ItemImport.MaxRows + 1).Select(i => Row($"項目 {i}"))] };

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", rows, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Code, result.Failure?.Details.Single().Path), Is.EqualTo((WorkErrorCodes.ImportInvalid, "rows")));
            Assert.That(listed.Value.Items, Is.Empty);
        }
    }

    [Test]
    public async Task ImportAsync_ParentThatAlreadyExists_AttachesIt()
    {
        // Arrange
        var scenario = await ProjectAsync(Scenario.AgencyProject);
        var requirement = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });

        // Act
        var result = await _host.Import.ImportAsync(
            scenario.Admin, "WEB", Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "任務", ParentKey = requirement.Key } }), CancellationToken.None);
        var stored = await _host.Items.GetAsync(scenario.Admin, result.Value.CreatedKeys[0], CancellationToken.None);

        // Assert
        Assert.That(stored.Value.ParentKey, Is.EqualTo(requirement.Key));
    }

    [Test]
    public async Task ImportAsync_ParentThatIsAnotherRowOfTheSameFile_IsRejected()
    {
        // Arrange：上層只能是已經存在的項目；WEB-1 在匯入的當下還不存在。
        var scenario = await ProjectAsync(Scenario.AgencyProject);

        // Act
        var result = await _host.Import.ImportAsync(
            scenario.Admin,
            "WEB",
            Import(
                new ImportedItem { Item = new NewWorkItem { Type = "requirement", Title = "需求" } },
                new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "任務", ParentKey = "WEB-1" } }),
            CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Details.Select(d => d.Path), Is.EqualTo(new[] { "rows/1/parent" }));
    }

    [Test]
    public async Task ImportAsync_StateGivenByProjectManager_IsSetDirectlyWithoutTransitionActivities()
    {
        // Arrange：todo → done 在任務流程裡本來就是同一個流程的狀態，直接設定，不留轉換的活動。
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Import.ImportAsync(
            scenario.Admin, "WEB", Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "已完成的舊資料" }, State = "done" }), CancellationToken.None);
        var stored = await _host.Items.GetAsync(scenario.Admin, "WEB-1", CancellationToken.None);
        var activity = await ActivityLog.AllAsync(_host, scenario.Admin, "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That((stored.Value.State, stored.Value.StateCategory), Is.EqualTo(("done", StateCategoryOf("done"))));
            Assert.That(activity.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
        }
    }

    [Test]
    public async Task ImportAsync_StateIsTheStartState_DoesNotNeedProjectManage()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Import.ImportAsync(
            editor, "WEB", Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "待辦" }, State = "todo" }), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task ImportAsync_NonStartStateWithoutProjectManage_IsForbiddenAndNothingIsChecked()
    {
        // Arrange：第二列的標題是空的，但沒有權限時整個 Forbidden，不逐列檢查。
        var scenario = await ProjectAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var rows = Import(
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "舊的" }, State = "done" },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "" } });

        // Act
        var result = await _host.Import.ImportAsync(editor, "WEB", rows, CancellationToken.None);
        var listed = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((result.Failure?.Kind, result.Failure?.Code), Is.EqualTo((FailureKind.Forbidden, WorkErrorCodes.Forbidden)));
            Assert.That(result.Failure?.Details, Is.Empty);
            Assert.That(listed.Value.Items, Is.Empty);
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ImportAsync_BlankState_IsTreatedAsNotGivenEvenForAnEditor(string blank)
    {
        // Arrange
        var scenario = await ProjectAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Import.ImportAsync(
            editor, "WEB", Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "空白的狀態" }, State = blank }), CancellationToken.None);
        var stored = await _host.Items.GetAsync(scenario.Admin, "WEB-1", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
            Assert.That(stored.Value.State, Is.EqualTo("todo"), "空白的狀態是流程的第一個狀態");
        }
    }

    [Test]
    public async Task ImportAsync_ProjectAdminWhoIsNotAnOrganizationAdmin_CanSetStates()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var manager = await scenario.MemberAsync("WEB", "admin");

        // Act
        var result = await _host.Import.ImportAsync(
            manager, "WEB", Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "進行中" }, State = "doing" }), CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [Test]
    public async Task ImportAsync_ViewerOrCommenter_IsForbidden()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Import.ImportAsync(viewer, "WEB", Import(Row("甲")), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task ImportAsync_NotAMember_IsNotFound()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        var result = await _host.Import.ImportAsync(scenario.Person(), "WEB", Import(Row("甲")), CancellationToken.None);

        // Assert
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [Test]
    public async Task ImportAsync_EachRow_LeavesACreatedActivityAndAnImportedItemCreatedEvent()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var assignee = await scenario.MemberAsync("WEB", "editor");
        var before = (await EventFeed.OfAsync(_host, scenario)).Count;

        // Act
        await _host.Import.ImportAsync(
            scenario.Admin,
            "WEB",
            Import(new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "甲", AssigneeId = assignee.PersonId } }, Row("乙")),
            CancellationToken.None);
        var events = (await EventFeed.OfAsync(_host, scenario)).Skip(before).ToArray();
        var activity = await ActivityLog.AllAsync(_host, scenario.Admin, "WEB-1");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(events.Select(e => (e.Kind, e.Item.ItemKey, e.Item.Imported)), Is.EqualTo(new[]
            {
                (WorkEventKind.ItemCreated, "WEB-1", true),
                (WorkEventKind.ItemCreated, "WEB-2", true),
            }));
            Assert.That(events[0].Item.ActivityId, Is.EqualTo(activity.Single().Id), "事件指到那一筆建立的活動");
            Assert.That(activity.Single().Kind, Is.EqualTo(ActivityKind.Created));
        }
    }

    [Test]
    public async Task CreateAsync_SingleItem_IsNotMarkedImported()
    {
        // Arrange
        var scenario = await ProjectAsync();

        // Act
        await scenario.CreateTaskAsync("WEB");
        var last = await EventFeed.LastAsync(_host, scenario);

        // Assert
        Assert.That(last.Item.Imported, Is.False);
    }

    [Test]
    public async Task ImportAsync_RowsInASecondImport_ContinueTheNumbering()
    {
        // Arrange
        var scenario = await ProjectAsync();
        await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(Row("甲"), Row("乙")), CancellationToken.None);

        // Act
        var second = await _host.Import.ImportAsync(scenario.Admin, "WEB", Import(Row("丙")), CancellationToken.None);

        // Assert
        Assert.That(second.Value.CreatedKeys, Is.EqualTo(new[] { "WEB-3" }));
    }

    [Test]
    public async Task ImportAsync_ControlCharactersInTitleOrDescription_AreRejectedAsRowProblems()
    {
        // Arrange
        var scenario = await ProjectAsync();
        var rows = Import(
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "有\0的標題" } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "好", Description = "說明\0" } },
            new ImportedItem { Item = new NewWorkItem { Type = "task", Title = "好", Description = "可以\n換行" } });

        // Act
        var result = await _host.Import.ImportAsync(scenario.Admin, "WEB", rows, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "rows/0/title", "rows/1/description" }));
    }

    private async Task<Scenario> ProjectAsync(string template = Scenario.SimpleTasks)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        return scenario;
    }

    private static ImportedItem Row(string title) => new() { Item = new NewWorkItem { Type = "task", Title = title } };

    private static ItemImport Import(params ImportedItem[] rows) => new() { Rows = rows };

    private static ItemImport Import(bool dryRun, params ImportedItem[] rows) => new() { Rows = rows, DryRun = dryRun };

    private static PmTool.Blueprint.Model.StateCategory StateCategoryOf(string state) =>
        state == "done" ? PmTool.Blueprint.Model.StateCategory.Done : PmTool.Blueprint.Model.StateCategory.NotStarted;
}
