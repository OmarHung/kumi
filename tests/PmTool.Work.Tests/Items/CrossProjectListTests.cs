using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>跨專案的項目清單：看得到哪些專案、專案代號的限縮、條件、排序、分頁與不合法的查詢。</summary>
[TestFixture]
public sealed class CrossProjectListTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAcrossProjectsAsync_Admin_SeesEveryActiveProjectAndLeavesOutDeletedItems()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var deleted = await scenario.CreateTaskAsync("WEB", "刪掉的");
        await _host.Items.DeleteAsync(scenario.Admin, deleted.Key, CancellationToken.None);

        // Act
        var page = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery());

        // Assert：專案依建立的先後（WEB 先），再依編號
        Assert.That(page.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2", "APP-1", "APP-2" }));
        Assert.That(page.NextCursor, Is.Null);
    }

    [Test]
    public async Task ListAcrossProjectsAsync_ArchivedProject_IsLeftOutUntilItIsRestored()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "APP", CancellationToken.None);

        // Act
        var archived = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery());
        await _host.Projects.UnarchiveAsync(scenario.Admin, "APP", CancellationToken.None);
        var restored = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery());

        // Assert
        Assert.That(archived.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
        Assert.That(restored.Items, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_Member_SeesOnlyProjectsWhereTheRoleCanReadItems()
    {
        // Arrange：我在 WEB 是編輯者、在 APP 是只能留言的訪客、不是 OPEN 的成員。
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("OPEN");
        await scenario.CreateTaskAsync("OPEN", "我不是成員");
        await PublishAsync(scenario, "APP", blueprint => blueprint with
        {
            Roles = [.. blueprint.Roles, new RoleDefinition { Key = "guest", Name = "訪客", Permissions = [WorkPermissions.CommentCreate] }],
        });
        var me = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "APP", me.PersonId, "guest", CancellationToken.None);

        // Act
        var page = await ListAsync(me, CrossProjectScope.All, new WorkItemQuery());

        // Assert
        Assert.That(page.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SamePersonInAnotherOrganization_SeesNothing()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var elsewhere = new Scenario(_host).Admin with { PersonId = scenario.Admin.PersonId };

        // Act
        var page = await ListAsync(elsewhere, CrossProjectScope.All, new WorkItemQuery());

        // Assert
        Assert.That(page.Items, Is.Empty);
    }

    [Test]
    public async Task ListAcrossProjectsAsync_ProjectKeys_NarrowTheScopeIgnoringCase()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var one = await ListAsync(scenario.Admin, new CrossProjectScope(["app"]), new WorkItemQuery());
        var both = await ListAsync(scenario.Admin, new CrossProjectScope(["App", "WEB"]), new WorkItemQuery());

        // Assert
        Assert.That(one.Items.Select(i => i.Key), Is.EqualTo(new[] { "APP-1", "APP-2" }));
        Assert.That(both.Items, Has.Count.EqualTo(4));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_UnknownArchivedOrUnreadableProjectKeys_AreSilentlyLeftOut()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("OLD");
        await scenario.CreateTaskAsync("OLD", "封存的");
        await _host.Projects.ArchiveAsync(scenario.Admin, "OLD", CancellationToken.None);
        await scenario.CreateProjectAsync("HID");
        await scenario.CreateTaskAsync("HID", "看不到的");
        var me = await scenario.MemberAsync("WEB", "editor");

        // Act
        var page = await ListAsync(me, new CrossProjectScope(["WEB", "NOPE", "OLD", "HID", "bad key!"]), new WorkItemQuery());
        var onlyHidden = await ListAsync(me, new CrossProjectScope(["HID"]), new WorkItemQuery());

        // Assert
        Assert.That(page.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
        Assert.That(onlyHidden.Items, Is.Empty, "指定了看不到的專案，結果是空的，不是全部");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_MoreProjectKeysThanTheLimit_IsQueryInvalidAtProject()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var keys = Enumerable.Range(1, CrossProjectScope.MaxProjects + 1).Select(n => $"P{n}").ToArray();

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, new CrossProjectScope(keys), new WorkItemQuery(), CancellationToken.None);
        var atLimit = await _host.Items.ListAcrossProjectsAsync(
            scenario.Admin, new CrossProjectScope(keys.Take(CrossProjectScope.MaxProjects).ToArray()), new WorkItemQuery(), CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, "project");
        Assert.That(atLimit.Succeeded, Is.True);
    }

    [TestCase("WE\0B")]
    [TestCase("WEB\n")]
    public async Task ListAcrossProjectsAsync_ProjectKeyWithControlCharacters_IsQueryInvalidAtProject(string key)
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, new CrossProjectScope([key]), new WorkItemQuery(), CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, "project");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_CustomFieldFilter_IsQueryInvalidAtThatField()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var query = new WorkItemQuery { FieldFilters = [new FieldFilter("client", ["client_a"])] };

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, CrossProjectScope.All, query, CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, "field.client");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SortByCustomField_IsQueryInvalidAtSort()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var query = new WorkItemQuery { SortBy = WorkItemSortField.Field, SortFieldKey = "client" };

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, CrossProjectScope.All, query, CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, "sort");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_NullProjectKeyList_IsQueryInvalidAtProject()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, new CrossProjectScope(null!), new WorkItemQuery(), CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, "project");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_TypesStatesAndCategories_AreOrWithinAndAndBetween()
    {
        // Arrange：WEB 的任務在 todo；APP 的需求 APP-1 移到 quoting、APP-2 是任務移到 doing。
        var scenario = await TwoProjectsAsync();
        await MoveAsync(scenario, "APP-1", "quoting");
        await MoveAsync(scenario, "APP-2", "doing");

        // Act
        var byType = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Types = ["requirement"] });
        var byState = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { States = ["quoting", "doing"] });
        var byCategory = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { StateCategories = [StateCategory.InProgress] });
        var combined = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Types = ["task"], StateCategories = [StateCategory.NotStarted] });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Keys(byType), Is.EqualTo(new[] { "APP-1" }));
            Assert.That(Keys(byState), Is.EqualTo(new[] { "APP-1", "APP-2" }));
            Assert.That(Keys(byCategory), Is.EqualTo(new[] { "APP-2" }));
            Assert.That(Keys(combined), Is.EqualTo(new[] { "WEB-1", "WEB-2" }));
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SameStateKeyInDifferentProjects_FilterByStateMatchesBoth()
    {
        // Arrange：兩個專案都有 doing（流程不同，key 相同）。
        var scenario = await TwoProjectsAsync();
        await MoveAsync(scenario, "WEB-1", "doing");
        await MoveAsync(scenario, "APP-2", "doing");

        // Act
        var page = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { States = ["doing"] });

        // Assert
        Assert.That(Keys(page), Is.EqualTo(new[] { "WEB-1", "APP-2" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_AssigneePriorityAndDueDate_Filter()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateProjectAsync("APP");
        var someone = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "APP", someone.PersonId, "editor", CancellationToken.None);
        await Create(scenario, "WEB", "甲", assignee: someone.PersonId, priority: Priority.High, due: new DateOnly(2026, 11, 20));
        await Create(scenario, "WEB", "乙", assignee: null, priority: Priority.Low, due: null);
        await Create(scenario, "APP", "丙", assignee: someone.PersonId, priority: Priority.Low, due: new DateOnly(2026, 11, 25));
        await Create(scenario, "APP", "丁", assignee: null, priority: Priority.High, due: new DateOnly(2026, 11, 30));

        // Act
        var assigned = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { AssigneeId = someone.PersonId });
        var unassigned = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { OnlyUnassigned = true });
        var high = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Priorities = [Priority.High] });
        var due = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { DueFrom = new DateOnly(2026, 11, 21), DueTo = new DateOnly(2026, 11, 29) });
        var noDue = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { OnlyWithoutDueDate = true });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Titles(assigned), Is.EqualTo(new[] { "甲", "丙" }));
            Assert.That(Titles(unassigned), Is.EqualTo(new[] { "乙", "丁" }));
            Assert.That(Titles(high), Is.EqualTo(new[] { "甲", "丁" }));
            Assert.That(Titles(due), Is.EqualTo(new[] { "丙" }));
            Assert.That(Titles(noDue), Is.EqualTo(new[] { "乙" }));
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_DatesRange_MatchesItemsWhosePeriodOverlaps()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-1", new WorkItemPatch { StartDate = new DateOnly(2026, 11, 1), DueDate = new DateOnly(2026, 11, 10) }, CancellationToken.None);
        await _host.Items.UpdateAsync(scenario.Admin, "APP-1", new WorkItemPatch { DueDate = new DateOnly(2026, 11, 12) }, CancellationToken.None);

        // Act
        var page = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { DatesFrom = new DateOnly(2026, 11, 9), DatesTo = new DateOnly(2026, 11, 12) });

        // Assert
        Assert.That(Keys(page), Is.EqualTo(new[] { "WEB-1", "APP-1" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_PlanningFilters_ApplyAcrossProjects()
    {
        // Arrange：WEB-1 排進一個週期；別的專案的項目不會指到它。
        var scenario = await TwoProjectsAsync();
        var cycle = await _host.Planning.CreateCycleAsync(
            scenario.Admin, "WEB", new CycleDraft("衝刺 1", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 15)), CancellationToken.None);
        Assert.That(cycle.Succeeded, Is.True, () => cycle.Failure!.Message);
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-1", new WorkItemPatch { CycleId = cycle.Value.Id }, CancellationToken.None);

        // Act
        var inCycle = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { CycleId = cycle.Value.Id });
        var withoutCycle = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { OnlyWithoutCycle = true });

        // Assert
        Assert.That(Keys(inCycle), Is.EqualTo(new[] { "WEB-1" }));
        Assert.That(Keys(withoutCycle), Is.EqualTo(new[] { "WEB-2", "APP-1", "APP-2" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_ParentKey_ListsOnlyDirectChildrenOfThatItem()
    {
        // Arrange：APP-1 是需求、APP-2 是任務；任務掛在需求底下。WEB 也有 WEB-1。
        var scenario = await TwoProjectsAsync();
        await _host.Items.UpdateAsync(scenario.Admin, "APP-2", new WorkItemPatch { ParentKey = "APP-1" }, CancellationToken.None);

        // Act
        var children = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { ParentKey = "APP-1" });
        var other = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { ParentKey = "WEB-1" });
        var malformed = await _host.Items.ListAcrossProjectsAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { ParentKey = "oops" }, CancellationToken.None);

        // Assert
        Assert.That(Keys(children), Is.EqualTo(new[] { "APP-2" }));
        Assert.That(children.Items[0].ParentKey, Is.EqualTo("APP-1"));
        Assert.That(other.Items, Is.Empty);
        AssertQueryInvalid(malformed, "parent");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SearchText_MatchesTitlesAcrossProjectsAndTreatsWildcardsAsText()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateProjectAsync("APP");
        await scenario.CreateTaskAsync("WEB", "會員登入");
        await scenario.CreateTaskAsync("APP", "登入頁");
        await scenario.CreateTaskAsync("APP", "100% 完成");
        await scenario.CreateTaskAsync("WEB", "1000 件");

        // Act
        var login = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Search = "登入" });
        var percent = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Search = "100%" });

        // Assert
        Assert.That(Titles(login), Is.EqualTo(new[] { "會員登入", "登入頁" }));
        Assert.That(Titles(percent), Is.EqualTo(new[] { "100% 完成" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SearchTextThatIsAnItemKey_FindsThatItemInItsOwnProject()
    {
        // Arrange：WEB 與 APP 都有編號 1、2；搜尋 APP-2 不能把 WEB-2 帶出來。
        var scenario = await TwoProjectsAsync();
        var me = await scenario.MemberAsync("WEB", "editor");

        // Act
        var upper = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Search = "APP-2" });
        var lower = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { Search = "app-2" });
        var hidden = await ListAsync(me, CrossProjectScope.All, new WorkItemQuery { Search = "APP-2" });

        // Assert
        Assert.That(Keys(upper), Is.EqualTo(new[] { "APP-2" }));
        Assert.That(Keys(lower), Is.EqualTo(new[] { "APP-2" }));
        Assert.That(hidden.Items, Is.Empty, "看不到的專案，用編號也找不到");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SortByNumber_OrdersByProjectCreationThenNumber()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var ascending = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.Number });
        var descending = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.Number, Descending = true });

        // Assert
        Assert.That(Keys(ascending), Is.EqualTo(new[] { "WEB-1", "WEB-2", "APP-1", "APP-2" }));
        Assert.That(Keys(descending), Is.EqualTo(new[] { "APP-2", "APP-1", "WEB-2", "WEB-1" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SortByState_OrdersByCategoryThenStateKey()
    {
        // Arrange：done(WEB-1) < ... 依類別 backlog → canceled，再依狀態 key。
        var scenario = await TwoProjectsAsync();
        await MoveAsync(scenario, "WEB-1", "doing", "done");
        await MoveAsync(scenario, "WEB-2", "doing");
        await MoveAsync(scenario, "APP-1", "quoting");

        // Act
        var ascending = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.State });
        var descending = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.State, Descending = true });

        // Assert：not_started：quoting(APP-1)、todo(APP-2)；in_progress：doing(WEB-2)；done：WEB-1
        Assert.That(Keys(ascending), Is.EqualTo(new[] { "APP-1", "APP-2", "WEB-2", "WEB-1" }));
        Assert.That(Keys(descending), Is.EqualTo(new[] { "WEB-1", "WEB-2", "APP-2", "APP-1" }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_ParentKeyInUnreadableProject_ReturnsAnEmptyPageNotAnError()
    {
        // Arrange：OPEN 有上層與子項目，但我只是 WEB 的成員。
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("OPEN", Scenario.AgencyProject);
        await scenario.CreateItemAsync("OPEN", new NewWorkItem { Type = "requirement", Title = "上層" });
        await scenario.CreateItemAsync("OPEN", new NewWorkItem { Type = "task", Title = "子項目", ParentKey = "OPEN-1" });
        var me = await scenario.MemberAsync("WEB", "editor");

        // Act
        var hidden = await ListAsync(me, CrossProjectScope.All, new WorkItemQuery { ParentKey = "OPEN-1" });
        var asAdmin = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { ParentKey = "OPEN-1" });

        // Assert
        Assert.That(hidden.Items, Is.Empty);
        Assert.That(hidden.NextCursor, Is.Null);
        Assert.That(Keys(asAdmin), Is.EqualTo(new[] { "OPEN-2" }), "對照：看得到的人查得到");
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SearchForAnotherProjectsItemKey_IsNotFoundWhenScopeIsNarrowed()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var scope = new CrossProjectScope(["WEB"]);

        // Act
        var otherProject = await ListAsync(scenario.Admin, scope, new WorkItemQuery { Search = "APP-2" });
        var sameProject = await ListAsync(scenario.Admin, scope, new WorkItemQuery { Search = "WEB-2" });

        // Assert
        Assert.That(otherProject.Items, Is.Empty);
        Assert.That(Keys(sameProject), Is.EqualTo(new[] { "WEB-2" }), "對照：範圍內的編號找得到");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAcrossProjectsAsync_SortByStateWithinOneCategory_FlipsTheStateKeyAndKeepsTiesInProjectOrder(bool descending)
    {
        // Arrange：WEB-1、WEB-2、APP-2 都在 todo；APP-1 在 quoting（同屬 not_started）。
        var scenario = await TwoProjectsAsync();
        await MoveAsync(scenario, "APP-1", "quoting");

        // Act
        var page = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.State, Descending = descending });

        // Assert
        var expected = descending
            ? new[] { "WEB-1", "WEB-2", "APP-2", "APP-1" }
            : new[] { "APP-1", "WEB-1", "WEB-2", "APP-2" };
        Assert.That(Keys(page), Is.EqualTo(expected));
    }

    [TestCase(WorkItemSortField.StartDate, false)]
    [TestCase(WorkItemSortField.StartDate, true)]
    [TestCase(WorkItemSortField.Estimate, false)]
    [TestCase(WorkItemSortField.Estimate, true)]
    public async Task ListAcrossProjectsAsync_SortByStartDateOrEstimate_PutsEmptyLastInBothDirections(WorkItemSortField field, bool descending)
    {
        // Arrange：WEB-1 沒有值、WEB-2 與 APP-1 同值、APP-2 比較大。
        var scenario = await TwoProjectsAsync();
        await SetAsync(scenario, field, ("WEB-2", 1), ("APP-1", 1), ("APP-2", 2));

        // Act
        var page = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = field, Descending = descending });

        // Assert
        var expected = descending
            ? new[] { "APP-2", "WEB-2", "APP-1", "WEB-1" }
            : new[] { "WEB-2", "APP-1", "APP-2", "WEB-1" };
        Assert.That(Keys(page), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAcrossProjectsAsync_SortByCreatedAt_OrdersByTimeAndBreaksTiesByProjectThenNumber(bool descending)
    {
        // Arrange：APP-1 最早；WEB-1 與 WEB-2 同一刻；APP-2 最晚。
        var scenario = await TwoProjectsAsync(withItems: false);
        await scenario.CreateTaskAsync("APP", "最早");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateTaskAsync("WEB", "中一");
        await scenario.CreateTaskAsync("WEB", "中二");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateTaskAsync("APP", "最晚");

        // Act
        var page = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.CreatedAt, Descending = descending });

        // Assert
        var expected = descending
            ? new[] { "APP-2", "WEB-1", "WEB-2", "APP-1" }
            : new[] { "APP-1", "WEB-1", "WEB-2", "APP-2" };
        Assert.That(Keys(page), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAcrossProjectsAsync_SortByUpdatedAt_OrdersByLastChangeAndBreaksTiesByProjectThenNumber(bool descending)
    {
        // Arrange：四個一起建立；之後 WEB-2 先改、APP-1 再改。沒改過的 WEB-1 與 APP-2 同值。
        var scenario = await TwoProjectsAsync();
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-2", new WorkItemPatch { Title = "網站二改" }, CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await _host.Items.UpdateAsync(scenario.Admin, "APP-1", new WorkItemPatch { Title = "需求一改" }, CancellationToken.None);

        // Act
        var page = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.UpdatedAt, Descending = descending });

        // Assert
        var expected = descending
            ? new[] { "APP-1", "WEB-2", "WEB-1", "APP-2" }
            : new[] { "WEB-1", "APP-2", "WEB-2", "APP-1" };
        Assert.That(Keys(page), Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAcrossProjectsAsync_SortByDueDate_PutsEmptyLastAndBreaksTiesByProjectThenNumber(bool descending)
    {
        // Arrange：WEB-1 沒有到期日、WEB-2 與 APP-1 同一天、APP-2 比較晚。
        var scenario = await TwoProjectsAsync();
        await _host.Items.UpdateAsync(scenario.Admin, "WEB-2", new WorkItemPatch { DueDate = new DateOnly(2026, 11, 20) }, CancellationToken.None);
        await _host.Items.UpdateAsync(scenario.Admin, "APP-1", new WorkItemPatch { DueDate = new DateOnly(2026, 11, 20) }, CancellationToken.None);
        await _host.Items.UpdateAsync(scenario.Admin, "APP-2", new WorkItemPatch { DueDate = new DateOnly(2026, 11, 25) }, CancellationToken.None);

        // Act
        var page = await ListAsync(
            scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.DueDate, Descending = descending });

        // Assert
        var expected = descending
            ? new[] { "APP-2", "WEB-2", "APP-1", "WEB-1" }
            : new[] { "WEB-2", "APP-1", "APP-2", "WEB-1" };
        Assert.That(Keys(page), Is.EqualTo(expected));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_SortByTitleAndPriority_TiesFallBackToProjectThenNumber()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateProjectAsync("APP");
        await Create(scenario, "APP", "Same", priority: Priority.High);
        await Create(scenario, "WEB", "Same", priority: Priority.High);
        await Create(scenario, "WEB", "Alpha", priority: Priority.None);

        // Act
        var byTitle = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.Title });
        var byPriority = await ListAsync(scenario.Admin, CrossProjectScope.All, new WorkItemQuery { SortBy = WorkItemSortField.Priority });

        // Assert
        Assert.That(Keys(byTitle), Is.EqualTo(new[] { "WEB-2", "WEB-1", "APP-1" }), "同標題先依專案再依編號");
        Assert.That(Keys(byPriority), Is.EqualTo(new[] { "WEB-1", "APP-1", "WEB-2" }), "同優先度先依專案再依編號，「無」排最後");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ListAcrossProjectsAsync_Paging_WalksEveryItemOnceInTheSortedOrder(bool descending)
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await Create(scenario, "WEB", "三", due: new DateOnly(2026, 11, 20));
        await Create(scenario, "APP", "四", due: new DateOnly(2026, 11, 20));
        var query = new WorkItemQuery { SortBy = WorkItemSortField.DueDate, Descending = descending, Limit = 2 };

        // Act
        var all = await ListAsync(scenario.Admin, CrossProjectScope.All, query with { Limit = 50 });
        var first = await ListAsync(scenario.Admin, CrossProjectScope.All, query);
        var second = await ListAsync(scenario.Admin, CrossProjectScope.All, query with { Cursor = first.NextCursor });
        var third = await ListAsync(scenario.Admin, CrossProjectScope.All, query with { Cursor = second.NextCursor });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(all.Items, Has.Count.EqualTo(6));
            Assert.That(Keys(first).Concat(Keys(second)).Concat(Keys(third)), Is.EqualTo(Keys(all)));
            Assert.That(first.NextCursor, Is.Not.Null);
            Assert.That(third.NextCursor, Is.Null);
        }
    }

    [Test]
    public async Task ListAcrossProjectsAsync_ItemsKeepTheirOwnProjectKeyAndParentKey()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await _host.Items.UpdateAsync(scenario.Admin, "APP-2", new WorkItemPatch { ParentKey = "APP-1" }, CancellationToken.None);

        // Act
        var page = await ListAsync(scenario.Admin, new CrossProjectScope(["APP"]), new WorkItemQuery());

        // Assert
        Assert.That(page.Items.Select(i => (i.ProjectKey, i.Number, i.ParentKey)), Is.EqualTo(new (string, long, string?)[]
        {
            ("APP", 1, null),
            ("APP", 2, "APP-1"),
        }));
    }

    [Test]
    public async Task ListAcrossProjectsAsync_NoReadableProject_ReturnsAnEmptyPage()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        var stranger = scenario.Person();

        // Act
        var page = await ListAsync(stranger, CrossProjectScope.All, new WorkItemQuery());

        // Assert
        Assert.That(page.Items, Is.Empty);
        Assert.That(page.NextCursor, Is.Null);
    }

    private static IEnumerable<TestCaseData> InvalidQueries()
    {
        yield return new TestCaseData(new WorkItemQuery { Limit = 0 }, "limit").SetName("Limit0");
        yield return new TestCaseData(new WorkItemQuery { Limit = WorkItemQuery.MaxLimit + 1 }, "limit").SetName("LimitTooBig");
        yield return new TestCaseData(new WorkItemQuery { Cursor = "not-a-cursor" }, "cursor").SetName("BadCursor");
        yield return new TestCaseData(new WorkItemQuery { SortBy = (WorkItemSortField)999 }, "sort").SetName("BadSort");
        yield return new TestCaseData(new WorkItemQuery { Types = ["a\0b"] }, "type").SetName("NulInType");
        yield return new TestCaseData(new WorkItemQuery { Search = "a\u0001b" }, "q").SetName("ControlInSearch");
        yield return new TestCaseData(new WorkItemQuery { OnlyWithoutDueDate = true, DueFrom = new DateOnly(2026, 11, 1) }, "noDue").SetName("NoDueWithRange");
    }

    [TestCaseSource(nameof(InvalidQueries))]
    public async Task ListAcrossProjectsAsync_InvalidQuery_IsQueryInvalidAtTheSameParameterAsTheProjectList(WorkItemQuery query, string parameter)
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var result = await _host.Items.ListAcrossProjectsAsync(scenario.Admin, CrossProjectScope.All, query, CancellationToken.None);

        // Assert
        AssertQueryInvalid(result, parameter);
    }

    /// <summary>WEB（簡單任務，先建）有兩個任務；APP（客戶專案）有 APP-1 需求與 APP-2 任務。</summary>
    private async Task<Scenario> TwoProjectsAsync(bool withItems = true)
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        _host.Clock.Advance(TimeSpan.FromSeconds(1));
        await scenario.CreateProjectAsync("APP", Scenario.AgencyProject);
        if (!withItems)
        {
            return scenario;
        }

        await scenario.CreateTaskAsync("WEB", "網站一");
        await scenario.CreateTaskAsync("WEB", "網站二");
        await scenario.CreateItemAsync("APP", new NewWorkItem { Type = "requirement", Title = "需求一" });
        await scenario.CreateItemAsync("APP", new NewWorkItem { Type = "task", Title = "任務二" });
        return scenario;
    }

    private async Task SetAsync(Scenario scenario, WorkItemSortField field, params (string Key, int Value)[] values)
    {
        foreach (var (key, value) in values)
        {
            var patch = field == WorkItemSortField.StartDate
                ? new WorkItemPatch { StartDate = new DateOnly(2026, 11, 1).AddDays(value) }
                : new WorkItemPatch { Estimate = value };
            var result = await _host.Items.UpdateAsync(scenario.Admin, key, patch, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }
    }

    private static Task<WorkItem> Create(
        Scenario scenario,
        string projectKey,
        string title,
        Guid? assignee = null,
        Priority priority = Priority.None,
        DateOnly? due = null) =>
        scenario.CreateItemAsync(
            projectKey,
            new NewWorkItem { Type = "task", Title = title, AssigneeId = assignee, Priority = priority, DueDate = due });

    private async Task<WorkItemPage> ListAsync(Actor actor, CrossProjectScope scope, WorkItemQuery query)
    {
        var result = await _host.Items.ListAcrossProjectsAsync(actor, scope, query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }

    private async Task MoveAsync(Scenario scenario, string itemKey, params string[] states)
    {
        foreach (var state in states)
        {
            var result = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
            Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        }
    }

    private async Task PublishAsync(Scenario scenario, string projectKey, Func<ProjectBlueprint, ProjectBlueprint> change)
    {
        var current = await _host.Projects.GetAsync(scenario.Admin, projectKey, CancellationToken.None);
        await StoredBlueprints.PublishAsync(_host, scenario, projectKey, change(current.Value.Blueprint));
    }

    private static string[] Keys(WorkItemPage page) => [.. page.Items.Select(i => i.Key)];

    private static string[] Titles(WorkItemPage page) => [.. page.Items.Select(i => i.Title)];

    private static void AssertQueryInvalid(Result<WorkItemPage> result, string parameter)
    {
        Assert.That(result.Failure?.Code, Is.EqualTo(WorkErrorCodes.QueryInvalid));
        Assert.That(result.Failure?.Details.Select(d => d.Path), Does.Contain(parameter));
    }
}
