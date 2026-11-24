using PmTool.Abstractions;
using static PmTool.Work.Tests.SavedViews.SavedViewCrudTests;

namespace PmTool.Work.Tests.SavedViews;

/// <summary>
/// 兩種檢視的條件檢查不同：專案的檢視不收 category、project，group 是看板的分組；「我的工作」的不收 cycle、milestone、fields
/// 與依欄位的排序，group 只有 project，
/// assignee 只能是 null：「我的工作」就是自己負責的項目，沒有負責人的條件。
/// </summary>
[TestFixture]
public sealed class MyWorkViewRulesTests
{
    private const string PersonId = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase("category", null)]
    [TestCase("category", "all")]
    [TestCase("category", "backlog")]
    [TestCase("category", "not_started")]
    [TestCase("category", "in_progress")]
    [TestCase("category", "done")]
    [TestCase("category", "canceled")]
    [TestCase("assignee", null)]
    [TestCase("state", "review")]
    [TestCase("project", "CM")]
    [TestCase("project", "cm")]
    [TestCase("group", "project")]
    [TestCase("due", "week")]
    [TestCase("priority", "high")]
    [TestCase("sort", "due_date:asc")]
    [TestCase("sort", "project:asc")]
    [TestCase("sort", "project:desc")]
    public async Task CreateMyWorkAsync_AcceptedValue_Succeeds(string field, string? value)
    {
        // Arrange
        var person = new Scenario(_host).Person();

        // Act
        var result = await CreateAsync(person, Apply(Empty, field, value));

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [TestCase("category", "nope")]
    [TestCase("category", "Done")]
    [TestCase("category", "open")]
    [TestCase("assignee", "me")]
    [TestCase("assignee", "all")]
    [TestCase("assignee", "unassigned")]
    [TestCase("assignee", PersonId)]
    [TestCase("assignee", "someone")]
    [TestCase("group", "urgency")]
    [TestCase("group", "Project")]
    [TestCase("project", "web 1")]
    [TestCase("project", "W")]
    [TestCase("project", "WEB\n")]
    [TestCase("state", "Review")]
    [TestCase("cycle", "current")]
    [TestCase("cycle", "none")]
    [TestCase("milestone", "none")]
    [TestCase("due", "tomorrow")]
    [TestCase("priority", "critical")]
    [TestCase("sort", "nope:asc")]
    [TestCase("sort", "field.client:asc")]
    [TestCase("sort", "project:up")]
    [TestCase("sort", "Project:asc")]
    [TestCase("sort", "project")]
    [TestCase("group", "assignee")]
    [TestCase("group", "field.client")]
    [TestCase("fields", "client")]
    [TestCase("type", "Task")]
    public async Task CreateMyWorkAsync_BadValue_IsInvalidAtThatQueryPath(string field, string value)
    {
        // Arrange
        var person = new Scenario(_host).Person();

        // Act
        var result = await CreateAsync(person, Apply(Empty, field, value));

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { $"query.{field}" }));
        Assert.That(result.Failure.Details[0].Message, Is.Not.Empty);
    }

    [Test]
    public async Task CreateMyWorkAsync_BothStateAndCategory_IsInvalidAtCategory()
    {
        // Arrange
        var person = new Scenario(_host).Person();

        // Act
        var result = await CreateAsync(person, Empty with { State = "review", Category = "in_progress", Project = "CM" });

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { "query.category" }));
    }

    [Test]
    public async Task CreateMyWorkAsync_BadNameLayoutAndSeveralConditions_ReportsEveryProblemAtOnce()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        var draft = new SavedViewDraft(" ", (SavedViewLayout)9, Empty with { Category = "x", Group = "y", Cycle = "current" });

        // Act
        var result = await _host.SavedViews.CreateMyWorkAsync(person, draft, CancellationToken.None);

        // Assert
        Assert.That(
            result.Failure!.Details.Select(d => d.Path),
            Is.EquivalentTo(new[] { "name", "layout", "query.category", "query.group", "query.cycle" }));
    }

    [Test]
    public async Task ReplaceMyWorkAsync_BadValue_IsInvalidAndKeepsTheStoredView()
    {
        // Arrange
        var person = new Scenario(_host).Person();
        var created = (await CreateAsync(person, Empty with { Category = "done" })).Value;

        // Act
        var result = await _host.SavedViews.ReplaceMyWorkAsync(
            person, created.Id, new SavedViewDraft("改", SavedViewLayout.Table, Empty with { Category = "nope" }), CancellationToken.None);
        var listed = await _host.SavedViews.ListMyWorkAsync(person, CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(listed, Is.EqualTo(new[] { created }));
    }

    [TestCase("category", "done")]
    [TestCase("category", "all")]
    [TestCase("project", "CM")]
    [TestCase("group", "project")]
    public async Task CreateAsync_ProjectViewWithAMyWorkOnlyCondition_IsInvalidAtThatQueryPath(string field, string value)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.SavedViews.CreateAsync(
            scenario.Admin, "WEB", new SavedViewDraft("專案的", SavedViewLayout.Table, Apply(Empty, field, value)), CancellationToken.None);

        // Assert
        AssertFailure(result, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EqualTo(new[] { $"query.{field}" }));
    }

    [Test]
    public async Task CreateAsync_ProjectViewKeepsItsOwnAssigneeAndPlanningRules()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var me = await CreateProjectViewAsync(scenario.Admin, "我", Empty with { Assignee = "me", Cycle = "current", Milestone = "none" });
        var all = await CreateProjectViewAsync(scenario.Admin, "所有人", Empty with { Assignee = "all" });

        // Assert
        Assert.That(me.Succeeded, Is.True);
        AssertFailure(all, FailureKind.Invalid, WorkErrorCodes.SavedViewInvalid);
    }

    private Task<Result<SavedView>> CreateProjectViewAsync(Actor actor, string name, SavedViewQuery query) =>
        _host.SavedViews.CreateAsync(actor, "WEB", new SavedViewDraft(name, SavedViewLayout.Table, query), CancellationToken.None);

    private Task<Result<SavedView>> CreateAsync(Actor actor, SavedViewQuery query) =>
        _host.SavedViews.CreateMyWorkAsync(actor, new SavedViewDraft("檢視", SavedViewLayout.Table, query), CancellationToken.None);

    private static SavedViewQuery Apply(SavedViewQuery query, string field, string? value) => field switch
    {
        "category" => query with { Category = value },
        "assignee" => query with { Assignee = value },
        "state" => query with { State = value },
        "project" => query with { Project = value },
        "group" => query with { Group = value },
        "cycle" => query with { Cycle = value },
        "milestone" => query with { Milestone = value },
        "due" => query with { Due = value },
        "priority" => query with { Priority = value },
        "sort" => query with { Sort = value },
        "type" => query with { Type = value },
        "fields" => query with { Fields = new Dictionary<string, string> { [value!] = "client_a" } },
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };
}
