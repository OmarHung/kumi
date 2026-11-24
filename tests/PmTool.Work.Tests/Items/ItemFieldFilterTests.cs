using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>依自訂欄位篩選：四種型別、沒有填、me、值之間的「或」、欄位之間的「而且」、類型沒有的欄位，以及不能用的條件。</summary>
[TestFixture]
public sealed class ItemFieldFilterTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_SingleSelect_MatchesAnyOfTheValues()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var one = await ListAsync(world, new FieldFilter("category", ["bug"]));
        var either = await ListAsync(world, new FieldFilter("category", ["bug", "chore"]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(one, Is.EqualTo(new long[] { 1, 4 }));
            Assert.That(either, Is.EqualTo(new long[] { 1, 3, 4 }));
        }
    }

    [Test]
    public async Task ListAsync_MultiSelect_MatchesItemsWithAnyChosenOption()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var frontend = await ListAsync(world, new FieldFilter("tags", ["frontend"]));
        var docsOrBackend = await ListAsync(world, new FieldFilter("tags", ["docs", "backend"]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frontend, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(docsOrBackend, Is.EqualTo(new long[] { 2, 3 }));
        }
    }

    [Test]
    public async Task ListAsync_Person_MatchesTheIdAndMeMeansTheCaller()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var byId = await ListAsync(world, new FieldFilter("reviewer", [world.Reviewer.PersonId.ToString().ToUpperInvariant()]));
        var me = await ListAsync(world, world.Reviewer, new FieldFilter("reviewer", ["me"]));
        var meAsAdmin = await ListAsync(world, new FieldFilter("reviewer", ["me"]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(byId, Is.EqualTo(new long[] { 2 }));
            Assert.That(me, Is.EqualTo(new long[] { 2 }));
            Assert.That(meAsAdmin, Is.Empty);
        }
    }

    [Test]
    public async Task ListAsync_MeOnASingleSelect_IsAnOptionKeyNotTheCaller()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var result = await ListAsync(world, new FieldFilter("category", ["me"]));

        // Assert：沒有叫 me 的選項，自然找不到；不會換成人員編號。
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAsync_Checkbox_TrueIsCheckedAndFalseIsNotStored()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var on = await ListAsync(world, new FieldFilter("blocked", ["true"]));
        var off = await ListAsync(world, new FieldFilter("blocked", ["false"]));
        var both = await ListAsync(world, new FieldFilter("blocked", ["true", "false"]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(on, Is.EqualTo(new long[] { 3 }));
            Assert.That(off, Is.EqualTo(new long[] { 1, 2, 4 }));
            Assert.That(both, Is.EqualTo(new long[] { 1, 2, 3, 4 }));
        }
    }

    [Test]
    public async Task ListAsync_IncludeEmpty_MatchesItemsWithoutTheValueAlone_OrTogetherWithValues()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var empty = await ListAsync(world, new FieldFilter("category", [], IncludeEmpty: true));
        var bugOrEmpty = await ListAsync(world, new FieldFilter("category", ["bug"], IncludeEmpty: true));
        var noTags = await ListAsync(world, new FieldFilter("tags", [], IncludeEmpty: true));
        var noReviewer = await ListAsync(world, new FieldFilter("reviewer", [], IncludeEmpty: true));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(empty, Is.EqualTo(new long[] { 2 }));
            Assert.That(bugOrEmpty, Is.EqualTo(new long[] { 1, 2, 4 }));
            Assert.That(noTags, Is.EqualTo(new long[] { 4 }));
            Assert.That(noReviewer, Is.EqualTo(new long[] { 1, 3, 4 }));
        }
    }

    [Test]
    public async Task ListAsync_DifferentFields_AreAnd_AndAlsoCombineWithOtherConditions()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var both = await ListAsync(world, new FieldFilter("category", ["bug"]), new FieldFilter("tags", ["frontend"]));
        var withState = await _host.Items.ListAsync(
            world.Scenario.Admin, "WEB",
            new WorkItemQuery { States = ["doing"], FieldFilters = [new FieldFilter("category", ["bug"])] },
            CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(both, Is.EqualTo(new long[] { 1 }));
            Assert.That(withState.Value.Items.Select(i => i.Number), Is.EqualTo(new long[] { 4 }));
        }
    }

    [Test]
    public async Task ListAsync_ItemsOfATypeWithoutTheField_OnlyMatchIncludeEmpty()
    {
        // Arrange：產品路線圖的「目標」沒有「影響」這個欄位，「計畫」有。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("RM", "product_roadmap");
        var goal = await scenario.CreateItemAsync("RM", new NewWorkItem { Type = "goal", Title = "目標" });
        var plan = await scenario.CreateItemAsync(
            "RM", new NewWorkItem { Type = "plan", Title = "計畫", ParentKey = goal.Key, Fields = Scenario.Fields(("impact", "high")) });

        // Act
        var high = await _host.Items.ListAsync(
            scenario.Admin, "RM", new WorkItemQuery { FieldFilters = [new FieldFilter("impact", ["high"])] }, CancellationToken.None);
        var empty = await _host.Items.ListAsync(
            scenario.Admin, "RM", new WorkItemQuery { FieldFilters = [new FieldFilter("impact", [], IncludeEmpty: true)] }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(high.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { plan.Key }));
            Assert.That(empty.Value.Items.Select(i => i.Key), Is.EqualTo(new[] { goal.Key }));
        }
    }

    [Test]
    public async Task ListAsync_SqlLookingValue_IsRejectedAsAnOptionKeyNeverRunAsSql()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var injected = await _host.Items.ListAsync(
            world.Scenario.Admin, "WEB",
            new WorkItemQuery { FieldFilters = [new FieldFilter("category", ["bug'; drop table work.items;--"])] },
            CancellationToken.None);

        // Assert：格式不合的 key 被擋下，不是被當成 SQL。
        Assert.That(injected.Failure!.Code, Is.EqualTo(WorkErrorCodes.QueryInvalid));
    }

    [Test]
    public async Task ListAsync_UnknownOptionKey_IsEmptyNotAnError()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var result = await ListAsync(world, new FieldFilter("category", ["removed_option"]));

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task ListAsync_DeletedItems_AreNotListed()
    {
        // Arrange
        var world = await WorldAsync();
        var deleted = await _host.Items.DeleteAsync(world.Scenario.Admin, "WEB-1", CancellationToken.None);

        // Act
        var result = await ListAsync(world, new FieldFilter("category", ["bug"]));

        // Assert
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);
        Assert.That(result, Is.EqualTo(new long[] { 4 }));
    }

    [TestCase("story_points", "不能篩選")]
    [TestCase("summary", "不能篩選")]
    [TestCase("review_date", "不能篩選")]
    [TestCase("nope", "沒有")]
    public async Task ListAsync_FieldThatCannotBeFilteredOrDoesNotExist_IsQueryInvalidAtItsPath(string key, string reason)
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var result = await Run(world, new FieldFilter(key, ["x1"]));

        // Assert
        AssertInvalid(result, $"field.{key}", reason);
    }

    [TestCase("Not A Key")]
    [TestCase("a")]
    public async Task ListAsync_KeyThatIsNotShapedLikeAKey_IsQueryInvalidWithoutEchoingIt(string key)
    {
        // Arrange：key 長度不限、內容不限，原樣放進路徑與訊息會把任意文字反射回去。
        var world = await WorldAsync();
        var longKey = key + new string('x', 500);

        // Act
        var result = await Run(world, new FieldFilter(longKey, ["x1"]));

        // Assert
        AssertInvalid(result, "field", "欄位的名稱");
        Assert.That(result.Failure!.Details.Single().Message, Does.Not.Contain(longKey));
    }

    [Test]
    public async Task ListAsync_TooManyFieldsOrValues_IsQueryInvalid()
    {
        // Arrange
        var world = await WorldAsync();
        var tooManyValues = Enumerable.Range(0, FieldFilter.MaxValues + 1).Select(i => $"opt{i}").ToArray();
        var tooManyFields = Enumerable.Range(0, FieldFilter.MaxPerQuery + 1).Select(i => new FieldFilter($"fld{i}", ["a1"])).ToArray();

        // Act
        var values = await Run(world, new FieldFilter("category", tooManyValues));
        var fields = await Run(world, tooManyFields);

        // Assert
        AssertInvalid(values, "field.category", "最多 50 個值");
        AssertInvalid(fields, "field", "最多 10 個");
    }

    [Test]
    public async Task ListAsync_BadValuesAndShapes_AreQueryInvalid()
    {
        // Arrange
        var world = await WorldAsync();

        // Act
        var badPerson = await Run(world, new FieldFilter("reviewer", ["not-a-guid"]));
        var badCheckbox = await Run(world, new FieldFilter("blocked", ["yes"]));
        var checkboxEmpty = await Run(world, new FieldFilter("blocked", [], IncludeEmpty: true));
        var noCondition = await Run(world, new FieldFilter("category", []));
        var control = await Run(world, new FieldFilter("category", ["bug\0"]));
        var duplicate = await Run(world, new FieldFilter("category", ["bug"]), new FieldFilter("category", ["chore"]));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            AssertInvalid(badPerson, "field.reviewer", "me");
            AssertInvalid(badCheckbox, "field.blocked", "true");
            AssertInvalid(checkboxEmpty, "field.blocked", "勾選");
            AssertInvalid(noCondition, "field.category", "至少一個值");
            AssertInvalid(control, "field.category", "控制字元");
            AssertInvalid(duplicate, "field.category", "只能出現一次");
        }
    }

    private async Task<Result<WorkItemPage>> Run(World world, params FieldFilter[] filters) =>
        await _host.Items.ListAsync(world.Scenario.Admin, "WEB", new WorkItemQuery { FieldFilters = filters }, CancellationToken.None);

    private async Task<long[]> ListAsync(World world, params FieldFilter[] filters) => await ListAsync(world, world.Scenario.Admin, filters);

    private async Task<long[]> ListAsync(World world, Actor actor, params FieldFilter[] filters)
    {
        var result = await _host.Items.ListAsync(actor, "WEB", new WorkItemQuery { FieldFilters = filters }, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return [.. result.Value.Items.Select(i => i.Number).Order()];
    }

    private static void AssertInvalid(Result<WorkItemPage> result, string path, string messagePart)
    {
        Assert.That(result.Failure, Is.Not.Null);
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Invalid));
        Assert.That(result.Failure.Details.Select(d => (d.Code, d.Path)), Does.Contain((WorkErrorCodes.QueryInvalid, path)));
        Assert.That(result.Failure.Details.Single(d => d.Path == path).Message, Does.Contain(messagePart));
    }

    // 1: bug、frontend、沒勾、沒審查者；2: 沒有分類、frontend+docs、審查者；3: chore、docs+backend、卡住；4: bug、沒有標籤、doing
    private async Task<World> WorldAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var reviewer = await scenario.MemberAsync("WEB", "editor");
        await Add(scenario, ("category", "bug"), ("tags", new[] { "frontend" }));
        await Add(scenario, ("tags", new[] { "frontend", "docs" }), ("reviewer", reviewer.PersonId.ToString()));
        await Add(scenario, ("category", "chore"), ("tags", new[] { "docs", "backend" }), ("blocked", true));
        var fourth = await Add(scenario, ("category", "bug"));
        var moved = await _host.Items.TransitionAsync(scenario.Admin, fourth.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        return new World(scenario, reviewer);
    }

    private static async Task<WorkItem> Add(Scenario scenario, params (string Key, object? Value)[] fields) =>
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = Scenario.Fields(fields) });

    private sealed record World(Scenario Scenario, Actor Reviewer);
}
