using Npgsql;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Projects;

/// <summary>AI 助理用的使用狀況統計：各（類型、狀態）的項目數與各欄位有值的項目數，只有數字。</summary>
[TestFixture]
public sealed class BlueprintUsageTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DescribeAsync_NoItems_ListsEveryFieldWithZeroAndNoStates()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");

        // Act
        var result = await _host.Usage.DescribeAsync(scenario.Admin, "USE", CancellationToken.None);

        // Assert
        var usage = result.Value;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.ItemCount, Is.Zero);
            Assert.That(usage.States, Is.Empty);
            Assert.That(usage.Fields.Select(f => f.Field), Is.EqualTo(usage.Fields.Select(f => f.Field).Order(StringComparer.Ordinal)));
            Assert.That(usage.Fields.Select(f => f.Field), Does.Contain("category").And.Contain("tags").And.Contain("summary"));
            Assert.That(usage.Fields.Select(f => f.FilledItems), Has.All.Zero);
        }
    }

    [Test]
    public async Task DescribeAsync_GroupsItemsByTypeAndState_AndCountsFilledFields()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        await AddAsync(scenario, "A", ("category", "bug"), ("tags", new[] { "frontend" }));
        await AddAsync(scenario, "B", ("category", "bug"), ("blocked", true));
        var third = await AddAsync(scenario, "C", ("summary", "有內容"));
        var moved = await _host.Items.TransitionAsync(scenario.Admin, third.Key, "doing", CancellationToken.None);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);

        // Act
        var usage = (await _host.Usage.DescribeAsync(scenario.Admin, "USE", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.BlueprintVersion, Is.EqualTo(1));
            Assert.That(usage.ItemCount, Is.EqualTo(3));
            Assert.That(usage.States, Is.EqualTo(new[] { new StateUsage("task", "doing", 1), new StateUsage("task", "todo", 2) }));
            Assert.That(FilledOf(usage, "category"), Is.EqualTo(2));
            Assert.That(FilledOf(usage, "tags"), Is.EqualTo(1));
            Assert.That(FilledOf(usage, "blocked"), Is.EqualTo(1));
            Assert.That(FilledOf(usage, "summary"), Is.EqualTo(1));
            Assert.That(FilledOf(usage, "budget"), Is.Zero);
        }
    }

    [Test]
    public async Task DescribeAsync_EmptyStringEmptyArrayAndNullValues_AreNotCountedAsFilled()
    {
        // Arrange：服務寫入時不存空值，這裡直接寫進資料庫，確認統計自己也不把它們算進去。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        await AddAsync(scenario, "空的", ("summary", "x"));
        await AddAsync(scenario, "有的", ("summary", "y"));
        await SetFieldsAsync(scenario, "空的", """{"summary":"","tags":[],"notes":null,"category":"bug"}""");

        // Act
        var usage = (await _host.Usage.DescribeAsync(scenario.Admin, "USE", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FilledOf(usage, "summary"), Is.EqualTo(1));
            Assert.That(FilledOf(usage, "tags"), Is.Zero);
            Assert.That(FilledOf(usage, "notes"), Is.Zero);
            Assert.That(FilledOf(usage, "category"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DescribeAsync_DeletedItemsAndOtherProjects_AreNotCounted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        await scenario.CreateProjectAsync("OTH");
        await AddAsync(scenario, "留著", ("category", "bug"));
        var gone = await AddAsync(scenario, "刪掉", ("category", "bug"));
        await scenario.CreateItemAsync("OTH", new NewWorkItem { Type = "task", Title = "別的專案", Fields = Scenario.Fields(("category", "bug")) });
        var deleted = await _host.Items.DeleteAsync(scenario.Admin, gone.Key, CancellationToken.None);
        Assert.That(deleted.Succeeded, Is.True, () => deleted.Failure!.Message);

        // Act
        var usage = (await _host.Usage.DescribeAsync(scenario.Admin, "USE", CancellationToken.None)).Value;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(usage.ItemCount, Is.EqualTo(1));
            Assert.That(FilledOf(usage, "category"), Is.EqualTo(1));
        }
    }

    [Test]
    public async Task DescribeAsync_AnotherOrganization_SeesTheProjectAsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        var outsider = new Scenario(_host).Admin;

        // Act
        var result = await _host.Usage.DescribeAsync(outsider, "USE", CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.NotFound));
    }

    [Test]
    public async Task DescribeAsync_NonMember_SeesTheProjectAsNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");

        // Act
        var result = await _host.Usage.DescribeAsync(scenario.Person(), "USE", CancellationToken.None);

        // Assert
        Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.NotFound));
    }

    [Test]
    public async Task DescribeAsync_MemberWhoSeesAllItems_CanRead()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        await AddAsync(scenario, "A");
        var viewer = await scenario.MemberAsync("USE", "viewer");

        // Act
        var result = await _host.Usage.DescribeAsync(viewer, "USE", CancellationToken.None);

        // Assert
        Assert.That(result.Value.ItemCount, Is.EqualTo(1));
    }

    [Test]
    public async Task DescribeAsync_ContributorWhoOnlySeesOwnItems_IsForbidden()
    {
        // Arrange：數字會洩漏看不到的項目有多少，和變更集同一條規則。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("USE");
        var contributor = await scenario.MemberAsync("USE", "contributor");

        // Act
        var result = await _host.Usage.DescribeAsync(contributor, "USE", CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Failure!.Kind, Is.EqualTo(FailureKind.Forbidden));
            Assert.That(result.Failure.Code, Is.EqualTo(WorkErrorCodes.Forbidden));
        }
    }

    private static long FilledOf(BlueprintUsage usage, string field) => usage.Fields.Single(f => f.Field == field).FilledItems;

    private static async Task<WorkItem> AddAsync(Scenario scenario, string title, params (string Key, object? Value)[] fields) =>
        await scenario.CreateItemAsync("USE", new NewWorkItem { Type = "task", Title = title, Fields = Scenario.Fields(fields) });

    private async Task SetFieldsAsync(Scenario scenario, string title, string fieldsJson)
    {
        await using var connection = new NpgsqlConnection(_host.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "UPDATE work.items SET fields = @fields::jsonb WHERE organization_id = @org AND title = @title", connection);
        command.Parameters.AddWithValue("fields", fieldsJson);
        command.Parameters.AddWithValue("org", scenario.OrganizationId);
        command.Parameters.AddWithValue("title", title);
        Assert.That(await command.ExecuteNonQueryAsync(), Is.EqualTo(1));
    }
}
