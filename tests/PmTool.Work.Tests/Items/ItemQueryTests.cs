using System.Buffers.Text;
using System.Text;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>項目清單：每個條件、排序（空值排最後）、分頁、不合法的查詢。</summary>
[TestFixture]
public sealed class ItemQueryTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_NoConditions_ListsEveryItemByNumber()
    {
        // Arrange
        var (scenario, _) = await AgencyWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery());

        // Assert
        Assert.That(page.Items.Select(i => i.Number), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAsync_CreatedBy_FiltersByCreatorAndCombinesWithAssignee()
    {
        // Arrange：成員建了一個自己負責的項目與一個沒人負責的項目；其餘四個是管理員建的。
        var (scenario, member) = await AgencyWithItemsAsync();
        var own = await scenario.CreateItemAsync(
            "WEB", new NewWorkItem { Type = "task", Title = "成員開的", AssigneeId = member.PersonId }, member);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "成員開的沒人做" }, member);

        // Act
        var byMember = await ListAsync(scenario, new WorkItemQuery { CreatedBy = member.PersonId });
        var byAdmin = await ListAsync(scenario, new WorkItemQuery { CreatedBy = scenario.Admin.PersonId });
        var both = await ListAsync(scenario, new WorkItemQuery { CreatedBy = scenario.Admin.PersonId, AssigneeId = member.PersonId });
        var stranger = await ListAsync(scenario, new WorkItemQuery { CreatedBy = Guid.NewGuid() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(byMember), Is.EqualTo(new long[] { 5, 6 }));
            Assert.That(Numbers(byAdmin), Is.EqualTo(new long[] { 1, 2, 3, 4 }));
            Assert.That(Numbers(both), Is.EqualTo(new long[] { 2, 4 }));
            Assert.That(stranger.Items, Is.Empty);
            Assert.That(own.CreatedBy, Is.EqualTo(member.PersonId));
            Assert.That(byAdmin.Items.Select(i => i.CreatedBy), Is.All.EqualTo(scenario.Admin.PersonId));
        }
    }

    [Test]
    public async Task ListAsync_TypesStatesAndCategories_AreOrWithinAndAndBetween()
    {
        // Arrange
        var (scenario, _) = await AgencyWithItemsAsync();

        // Act
        var byType = await ListAsync(scenario, new WorkItemQuery { Types = ["task"] });
        var byState = await ListAsync(scenario, new WorkItemQuery { States = ["quoting", "doing"] });
        var byCategory = await ListAsync(scenario, new WorkItemQuery { StateCategories = [StateCategory.InProgress] });
        var combined = await ListAsync(scenario, new WorkItemQuery { Types = ["requirement"], StateCategories = [StateCategory.NotStarted] });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(byType), Is.EqualTo(new long[] { 3, 4 }));
            Assert.That(Numbers(byState), Is.EqualTo(new long[] { 2, 4 }));
            Assert.That(Numbers(byCategory), Is.EqualTo(new long[] { 4 }));
            Assert.That(Numbers(combined), Is.EqualTo(new long[] { 1, 2 }));
        }
    }

    [Test]
    public async Task ListAsync_Assignee_FiltersByPersonOrUnassigned()
    {
        // Arrange
        var (scenario, member) = await AgencyWithItemsAsync();

        // Act
        var mine = await ListAsync(scenario, new WorkItemQuery { AssigneeId = member.PersonId });
        var unassigned = await ListAsync(scenario, new WorkItemQuery { OnlyUnassigned = true });

        // Assert
        Assert.That(Numbers(mine), Is.EqualTo(new long[] { 2, 4 }));
        Assert.That(Numbers(unassigned), Is.EqualTo(new long[] { 1, 3 }));
    }

    [TestCase("登入", new long[] { 1, 3 })]
    [TestCase("ＡＢＣ", new long[] { })]
    [TestCase("abc", new long[] { 2 })]
    [TestCase("web-4", new long[] { 4 })]
    [TestCase("APP-4", new long[] { })]
    public async Task ListAsync_Search_MatchesTitleIgnoringCaseOrTheItemKey(string search, long[] expected)
    {
        // Arrange
        var (scenario, _) = await AgencyWithItemsAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { Search = search });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(expected));
    }

    [TestCase("%", new long[] { 1 })]
    [TestCase("_", new long[] { 2 })]
    [TestCase("\\", new long[] { 3 })]
    [TestCase("a%b", new long[] { })]
    public async Task ListAsync_SearchWithWildcards_TreatsThemAsPlainCharacters(string search, long[] expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB", "完成 100%");
        await scenario.CreateTaskAsync("WEB", "snake_case 命名");
        await scenario.CreateTaskAsync("WEB", "路徑 C:\\temp");
        await scenario.CreateTaskAsync("WEB", "aXb 測試");

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { Search = search });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(expected));
    }

    [TestCase(WorkItemSortField.DueDate, false, new long[] { 3, 1, 2, 4 })]
    [TestCase(WorkItemSortField.DueDate, true, new long[] { 1, 3, 2, 4 })]
    [TestCase(WorkItemSortField.StartDate, false, new long[] { 2, 1, 3, 4 })]
    [TestCase(WorkItemSortField.StartDate, true, new long[] { 1, 2, 3, 4 })]
    [TestCase(WorkItemSortField.Estimate, false, new long[] { 4, 2, 1, 3 })]
    [TestCase(WorkItemSortField.Estimate, true, new long[] { 2, 4, 1, 3 })]
    [TestCase(WorkItemSortField.Priority, false, new long[] { 2, 3, 1, 4 })]
    [TestCase(WorkItemSortField.Priority, true, new long[] { 1, 3, 2, 4 })]
    [TestCase(WorkItemSortField.Title, false, new long[] { 2, 4, 1, 3 })]
    [TestCase(WorkItemSortField.Number, true, new long[] { 4, 3, 2, 1 })]
    public async Task ListAsync_SortByAField_PutsEmptyValuesLastInBothDirections(WorkItemSortField field, bool descending, long[] expected)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await Create(scenario, "c 第一", Priority.High, start: new DateOnly(2026, 11, 5), due: new DateOnly(2026, 11, 20), estimate: null);
        await Create(scenario, "a 第二", Priority.Low, start: new DateOnly(2026, 11, 1), due: null, estimate: 5m);
        await Create(scenario, "d 第三", Priority.Medium, start: null, due: new DateOnly(2026, 11, 10), estimate: null);
        await Create(scenario, "b 第四", Priority.None, start: null, due: null, estimate: 1m);

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { SortBy = field, Descending = descending });

        // Assert
        Assert.That(Numbers(page), Is.EqualTo(expected));
    }

    [Test]
    public async Task ListAsync_SortByState_FollowsTheWorkflowOrder()
    {
        // Arrange：交付流程的順序是需求確認、報價、設計……
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        foreach (var _ in Enumerable.Range(0, 3))
        {
            await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "需求" });
        }

        await MoveAsync(scenario, "WEB-1", "quoting", "designing");
        await MoveAsync(scenario, "WEB-3", "quoting");

        // Act
        var ascending = await ListAsync(scenario, new WorkItemQuery { SortBy = WorkItemSortField.State });
        var descending = await ListAsync(scenario, new WorkItemQuery { SortBy = WorkItemSortField.State, Descending = true });

        // Assert
        Assert.That(Numbers(ascending), Is.EqualTo(new long[] { 2, 3, 1 }));
        Assert.That(Numbers(descending), Is.EqualTo(new long[] { 1, 3, 2 }));
    }

    [Test]
    public async Task ListAsync_SortByTimestamps_UsesCreationAndModificationTimes()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await scenario.CreateTaskAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-1", new WorkItemPatch { Title = "後來改過" }, CancellationToken.None);

        // Act
        var created = await ListAsync(scenario, new WorkItemQuery { SortBy = WorkItemSortField.CreatedAt, Descending = true });
        var updated = await ListAsync(scenario, new WorkItemQuery { SortBy = WorkItemSortField.UpdatedAt, Descending = true });

        // Assert
        Assert.That(Numbers(created), Is.EqualTo(new long[] { 2, 1 }));
        Assert.That(Numbers(updated), Is.EqualTo(new long[] { 1, 2 }));
    }

    [Test]
    public async Task ListAsync_Paging_WalksEveryItemOnceThenStops()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var n in Enumerable.Range(1, 5))
        {
            await scenario.CreateTaskAsync("WEB", $"任務 {n}");
        }

        // Act
        var first = await ListAsync(scenario, new WorkItemQuery { Limit = 2 });
        var second = await ListAsync(scenario, new WorkItemQuery { Limit = 2, Cursor = first.NextCursor });
        var third = await ListAsync(scenario, new WorkItemQuery { Limit = 2, Cursor = second.NextCursor });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(first), Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(Numbers(second), Is.EqualTo(new long[] { 3, 4 }));
            Assert.That(Numbers(third), Is.EqualTo(new long[] { 5 }));
            Assert.That(first.NextCursor, Is.Not.Null);
            Assert.That(third.NextCursor, Is.Null);
        }
    }

    /// <summary>問題的路徑是 API 查詢參數的名字，網頁才知道要把錯誤標在哪個篩選上。</summary>
    public static IEnumerable<TestCaseData> InvalidQueries()
    {
        yield return Invalid(new WorkItemQuery { Limit = 0 }, "limit", "limit 0");
        yield return Invalid(new WorkItemQuery { Limit = WorkItemQuery.MaxLimit + 1 }, "limit", "limit 201");
        yield return Invalid(new WorkItemQuery { Cursor = "not-a-cursor" }, "cursor", "壞掉的游標");
        yield return Invalid(new WorkItemQuery { Cursor = "bzotMQ" }, "cursor", "負的位移量");
        yield return Invalid(new WorkItemQuery { Cursor = new string('A', 100) }, "cursor", "太長的游標");
        yield return Invalid(new WorkItemQuery { AssigneeId = Guid.NewGuid(), OnlyUnassigned = true }, "assignee", "負責人兩種條件同時用");
        yield return Invalid(new WorkItemQuery { SortBy = (WorkItemSortField)99 }, "sort", "不認得的排序欄位");
        yield return Invalid(new WorkItemQuery { Search = new string('字', WorkRules.MaxTitleLength + 1) }, "q", "搜尋太長");
        yield return Invalid(new WorkItemQuery { Search = "a" + (char)0 }, "q", "搜尋有 NUL");
        yield return Invalid(new WorkItemQuery { StateCategories = [(StateCategory)99] }, "stateCategory", "不認得的狀態類別");
        yield return Invalid(new WorkItemQuery { Types = null! }, "type", "類型的清單是 null");
        yield return Invalid(new WorkItemQuery { States = null! }, "state", "狀態的清單是 null");
        yield return Invalid(new WorkItemQuery { StateCategories = null! }, "stateCategory", "狀態類別的清單是 null");
        yield return Invalid(new WorkItemQuery { Types = [null!] }, "type", "類型裡有 null");
        yield return Invalid(new WorkItemQuery { States = [null!] }, "state", "狀態裡有 null");
        yield return Invalid(new WorkItemQuery { Types = [((char)0).ToString()] }, "type", "類型有 NUL");
        yield return Invalid(new WorkItemQuery { States = ["a" + (char)0 + "b"] }, "state", "狀態有 NUL");
        yield return Invalid(new WorkItemQuery { Types = ["task" + (char)0xD800] }, "type", "類型有落單的代理字元");
        yield return Invalid(new WorkItemQuery { States = ["todo" + (char)0x1B] }, "state", "狀態有控制字元");
        yield return Invalid(new WorkItemQuery { Cursor = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("o:2" + (char)0)) }, "cursor", "游標的位移量後面有 NUL");
        yield return Invalid(new WorkItemQuery { ParentKey = "WEB" }, "parent", "上層不是項目編號");
    }

    [Test]
    public async Task ListAsync_TypeOrStateNotInTheBlueprint_FindsNothing()
    {
        // Arrange
        var (scenario, _) = await AgencyWithItemsAsync();

        // Act
        var byType = await ListAsync(scenario, new WorkItemQuery { Types = ["epic"] });
        var byState = await ListAsync(scenario, new WorkItemQuery { States = ["archived"] });

        // Assert
        Assert.That(byType.Items, Is.Empty);
        Assert.That(byState.Items, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAsync_PagingThroughEqualSortValues_ReturnsEveryItemExactlyOnce(bool descending)
    {
        // Arrange：五個項目的到期日都一樣，只靠流水號分出先後。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        foreach (var _ in Enumerable.Range(1, 5))
        {
            await Create(scenario, "同一天", Priority.High, start: null, due: new DateOnly(2026, 11, 30), estimate: null);
        }

        var query = new WorkItemQuery { SortBy = WorkItemSortField.DueDate, Descending = descending, Limit = 2 };

        // Act
        var first = await ListAsync(scenario, query);
        var second = await ListAsync(scenario, query with { Cursor = first.NextCursor });
        var third = await ListAsync(scenario, query with { Cursor = second.NextCursor });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Numbers(first).Concat(Numbers(second)).Concat(Numbers(third)), Is.EqualTo(new long[] { 1, 2, 3, 4, 5 }));
            Assert.That(third.NextCursor, Is.Null);
        }
    }

    [TestCaseSource(nameof(InvalidQueries))]
    public async Task ListAsync_InvalidQuery_IsQueryInvalidAtTheParameter(WorkItemQuery query, string parameter)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, parameter);
    }

    [Test]
    public async Task ListAsync_ProjectTheCallerCannotSee_IsProjectNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.ListAsync(scenario.Person(), "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ProjectNotFound);
    }

    [Test]
    public async Task ListAsync_Viewer_CanList()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Items.ListAsync(viewer, "WEB", new WorkItemQuery(), CancellationToken.None);

        // Assert
        Assert.That(result.Value.Items, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// WEB-1 需求「會員登入」需求確認；WEB-2 需求「ABC 報價單」報價，給 member；
    /// WEB-3 任務「登入頁」待辦；WEB-4 任務「串金流」進行中，給 member。
    /// </summary>
    private async Task<(Scenario Scenario, Actor Member)> AgencyWithItemsAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var member = await scenario.MemberAsync("WEB", "editor");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "ABC 報價單", AssigneeId = member.PersonId });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "登入頁" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "串金流", AssigneeId = member.PersonId });
        await MoveAsync(scenario, "WEB-2", "quoting");
        await MoveAsync(scenario, "WEB-4", "doing");
        return (scenario, member);
    }

    private async Task MoveAsync(Scenario scenario, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            var result = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }
    }

    private static Task<WorkItem> Create(Scenario scenario, string title, Priority priority, DateOnly? start, DateOnly? due, decimal? estimate) =>
        scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = title,
            Priority = priority,
            StartDate = start,
            DueDate = due,
            Estimate = estimate,
        });

    private async Task<WorkItemPage> ListAsync(Scenario scenario, WorkItemQuery query)
    {
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private static long[] Numbers(WorkItemPage page) => [.. page.Items.Select(i => i.Number)];

    private static TestCaseData Invalid(WorkItemQuery query, string parameter, string name) =>
        new TestCaseData(query, parameter).SetArgDisplayNames(name);
}
