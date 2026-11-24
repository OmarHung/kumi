using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>項目的說明：單一項目的讀寫都帶，清單與「我的工作」不帶。</summary>
[TestFixture]
public sealed class ItemDescriptionTests
{
    private const string Description = "第一段說明\n\t縮排的第二行\n第三行";

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task CreateAsync_WithDescription_ReturnsAndReadsItBackWithLineBreaks()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "  " + Description + "\n " });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert：頭尾的空白去掉，中間的換行與 Tab 留著。
        Assert.That(created.Description, Is.EqualTo(Description));
        Assert.That(readBack.Value.Description, Is.EqualTo(Description));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \n\t ")]
    public async Task CreateAsync_BlankDescription_HasNoDescription(string? description)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = description });
        var readBack = await _host.Items.GetAsync(scenario.Admin, created.Key, CancellationToken.None);

        // Assert
        Assert.That(created.Description, Is.Null);
        Assert.That(readBack.Value.Description, Is.Null);
    }

    [Test]
    public async Task ListAsync_ItemWithDescription_LeavesTheDescriptionOut()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem
        {
            Type = "task",
            Title = "我的任務",
            AssigneeId = scenario.Admin.PersonId,
            Description = Description,
        });

        // Act
        var list = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        var mine = await _host.Items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(list.Value.Items.Select(i => (i.Key, i.Description)), Is.EqualTo(new[] { ("WEB-1", (string?)null) }));
        Assert.That(mine.Select(i => (i.Key, i.Description)), Is.EqualTo(new[] { ("WEB-1", (string?)null) }));
    }

    [Test]
    public async Task ListQueries_DoNotReadTheDescriptionColumnAtAll()
    {
        // Arrange：同一個資料庫、另一個會收集 SQL 的服務容器。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", AssigneeId = scenario.Admin.PersonId, Description = Description });
        using var sql = new SqlLog();
        await using var provider = WorkTestHost.BuildProvider(
            _host.ConnectionString, _host.Clock, services => services.AddLogging(logging => logging.AddProvider(sql)));
        await using var scope = provider.CreateAsyncScope();
        var items = scope.ServiceProvider.GetRequiredService<IWorkItemService>();

        // Act
        await items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery(), CancellationToken.None);
        await items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery { ParentKey = "WEB-1", Search = "任務" }, CancellationToken.None);
        await items.ListAssignedToMeAsync(scenario.Admin, CancellationToken.None);
        var listing = sql.Commands;
        sql.Clear();
        await items.GetAsync(scenario.Admin, "WEB-1", CancellationToken.None);

        // Assert：讀單一項目時有讀這一欄，證明真的收集得到。
        Assert.That(listing, Is.Not.Empty);
        Assert.That(listing, Has.None.Contains("description"));
        Assert.That(sql.Commands, Has.Some.Contains("description"));
    }

    [Test]
    public async Task UpdateAsync_Description_ChangesItAndTheOtherOperationsKeepIt()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "舊的說明" });

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = Description }, CancellationToken.None);
        var retitled = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Title = "新標題" }, CancellationToken.None);
        var moved = await _host.Items.TransitionAsync(scenario.Admin, item.Key, "doing", CancellationToken.None);

        // Assert
        Assert.That(updated.Value.Description, Is.EqualTo(Description));
        Assert.That(retitled.Value.Description, Is.EqualTo(Description));
        Assert.That(moved.Value.Description, Is.EqualTo(Description));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task UpdateAsync_BlankDescription_ClearsIt(string? description)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "會被清掉" });

        // Act
        var updated = await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = description }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        Assert.That(updated.Value.Description, Is.Null);
        Assert.That(readBack.Value.Description, Is.Null);
    }

    [Test]
    public async Task CreateAsync_DescriptionAtTheLimit_IsAccepted()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var longest = new string('說', 50_000);

        // Act
        var created = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = longest });

        // Assert
        Assert.That(created.Description, Has.Length.EqualTo(50_000));
    }

    [Test]
    public async Task CreateAsync_DescriptionOverTheLimit_IsDescriptionTooLong()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");

        // Act
        var result = await _host.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "任務", Description = new string('說', 50_001) }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemDescriptionTooLong, "description");
        Assert.That(result.Failure!.Message, Does.Contain("50000"));
    }

    [TestCase(0x0000)]
    [TestCase(0x0007)]
    [TestCase(0x001B)]
    [TestCase(0x007F)]
    [TestCase(0xD800)]
    public async Task UpdateAsync_DescriptionWithAControlOrInvalidCharacter_IsInvalidAndNothingChanges(int character)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "任務", Description = "原本的說明" });

        // Act
        var result = await _host.Items.UpdateAsync(
            scenario.Admin, item.Key, new WorkItemPatch { Description = "前面" + (char)character + "後面" }, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.ItemInvalidValue, "description");
        Assert.That(readBack.Value.Description, Is.EqualTo("原本的說明"));
    }

    [Test]
    public async Task UpdateAsync_ViewerChangingTheDescription_IsForbidden()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var result = await _host.Items.UpdateAsync(viewer, item.Key, new WorkItemPatch { Description = "改不了" }, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
    }
}
