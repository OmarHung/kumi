using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

[TestFixture]
public sealed class ItemCreationTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_ValidItem_StartsInTheFirstStateAndReadsBack()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = new NewWorkItem
        {
            Type = "task",
            Title = "  首頁改版  ",
            AssigneeId = scenario.Admin.PersonId,
            Priority = Priority.High,
            StartDate = new DateOnly(2026, 11, 3),
            DueDate = new DateOnly(2026, 11, 20),
            Estimate = 3.5m,
        };

        // Act
        var created = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, "WEB-1", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        var expected = new WorkItem(
            "WEB-1", "WEB", 1, "task", "首頁改版", "todo", StateCategory.NotStarted, scenario.Admin.PersonId, Priority.High,
            new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 20), 3.5m, null, created.Value.Fields,
            WorkTestHost.StartTime, WorkTestHost.StartTime);
        Assert.That(created.Value, Is.EqualTo(expected));
        Assert.That(readBack.Value, Is.EqualTo(expected with { Fields = readBack.Value.Fields }));
        Assert.That(readBack.Value.Fields, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_SeveralItems_NumbersThemInOrderPerProject()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("APP");

        // Act
        var first = await scenario.CreateTaskAsync("WEB");
        var second = await scenario.CreateTaskAsync("WEB");
        var other = await scenario.CreateTaskAsync("APP");

        // Assert
        Assert.That(new[] { first.Key, second.Key, other.Key }, Is.EqualTo(new[] { "WEB-1", "WEB-2", "APP-1" }));
    }

    [Test]
    public async Task CreateAsync_UnknownType_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "epic", Title = "大功能" }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownType, "type");
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task CreateAsync_BlankTitle_IsInvalid(string title)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = title }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemTitleRequired, "title");
    }

    [Test]
    public async Task CreateAsync_TitleLongerThanLimit_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var title = new string('字', WorkRules.MaxTitleLength + 1);

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = title }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemTitleTooLong, "title");
    }

    [Test]
    public async Task CreateAsync_TitleWithControlCharacter_IsInvalid()
    {
        // Arrange：NUL 存不進 PostgreSQL，換行不該出現在標題裡。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var title = "壞掉的" + (char)0 + "標題";

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = title }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "title");
    }

    [Test]
    public async Task CreateAsync_UndefinedPriority_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Priority = (Priority)42 }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "priority");
    }

    [Test]
    public async Task CreateAsync_NegativeEstimate_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Estimate = -1m }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "estimate");
    }

    [Test]
    public async Task CreateAsync_NullFields_IsInvalidAndCreatesNothing()
    {
        // Arrange：合約的預設是空的字典，但 CLI 或 MCP 直接呼叫時可能明確給 null。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Fields = null! }, CancellationToken.None);
        var next = await scenario.CreateTaskAsync("WEB");

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "fields");
        Assert.That(next.Number, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_AssigneeWhoIsNotAMember_IsInvalid()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", AssigneeId = scenario.Person().PersonId }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemUnknownAssignee, "assignee");
    }

    [Test]
    public async Task CreateAsync_AssigneeWhoIsAMember_IsKept()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", AssigneeId = viewer.PersonId }, CancellationToken.None);

        // Assert
        Assert.That(result.Value.AssigneeId, Is.EqualTo(viewer.PersonId));
    }

    [Test]
    public async Task CreateAsync_SeveralProblems_ReportsEveryFieldAndKeepsNothing()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = new NewWorkItem { Type = "task", Title = "", Estimate = -2m, AssigneeId = Guid.NewGuid() };

        // Act
        var result = await _host.Items.CreateAsync(scenario.Admin, "WEB", item, CancellationToken.None);
        var page = await scenario.CreateTaskAsync("WEB");

        // Assert：被擋下的新增沒有用掉流水號。
        Assert.That(result.Failure!.Details.Select(d => d.Path), Is.EquivalentTo(new[] { "title", "estimate", "assignee" }));
        Assert.That(page.Number, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAsync_ProjectTheCallerCannotSee_IsProjectNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Person(), "WEB", new NewWorkItem { Type = "task", Title = "偷偷新增" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.NotFound));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.ProjectNotFound));
    }

    [TestCase("viewer")]
    [TestCase("commenter")]
    public async Task CreateAsync_RoleWithoutItemCreate_IsForbidden(string role)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var member = await scenario.MemberAsync("WEB", role);

        // Act
        var result = await _host.Items.CreateAsync(
            member, "WEB", new NewWorkItem { Type = "task", Title = "沒權限" }, CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
        Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
    }

    [Test]
    public async Task CreateAsync_Editor_CanCreate()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var editor = await scenario.MemberAsync("WEB", "editor");

        // Act
        var result = await _host.Items.CreateAsync(
            editor, "WEB", new NewWorkItem { Type = "task", Title = "編輯者新增" }, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True);
    }
}
