using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 用上層項目篩選：只回直接下層。agency_project 的任務可以掛在需求或需求變更單底下，
/// 這裡另外開一版藍圖加上掛在任務底下的子任務，才有孫層可以測。
/// </summary>
[TestFixture]
public sealed class ItemChildrenQueryTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListAsync_ParentKey_ListsOnlyTheDirectChildren()
    {
        // Arrange
        var scenario = await TreeAsync();

        // Act
        var children = await ListAsync(scenario, new WorkItemQuery { ParentKey = "WEB-1" });
        var lowerCase = await ListAsync(scenario, new WorkItemQuery { ParentKey = "web-1" });
        var grandchildren = await ListAsync(scenario, new WorkItemQuery { ParentKey = "WEB-2" });

        // Assert：WEB-4 掛在 WEB-2 底下，是 WEB-1 的孫層，不算。
        Assert.That(children.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2", "WEB-3" }));
        Assert.That(lowerCase.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-2", "WEB-3" }));
        Assert.That(grandchildren.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-4" }));
    }

    [Test]
    public async Task ListAsync_ParentKeyWithOtherConditions_AppliesAllOfThem()
    {
        // Arrange
        var scenario = await TreeAsync();
        await MoveAsync(scenario, "WEB-3", "doing");

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { ParentKey = "WEB-1", States = ["doing"] });

        // Assert
        Assert.That(page.Items.Select(i => i.Key), Is.EqualTo(new[] { "WEB-3" }));
    }

    [Test]
    public async Task ListAsync_ParentWithoutChildren_IsEmpty()
    {
        // Arrange
        var scenario = await TreeAsync();

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { ParentKey = "WEB-4" });

        // Assert
        Assert.That(page.Items, Is.Empty);
    }

    [TestCase("WEB-99")]
    [TestCase("APP-1")]
    [TestCase("ZZZ-1")]
    public async Task ListAsync_ParentThatDoesNotExistOrIsInAnotherProject_IsEmpty(string parentKey)
    {
        // Arrange：APP-1 存在，但在別的專案。
        var scenario = await TreeAsync();
        await scenario.CreateProjectAsync("APP", Scenario.AgencyProject);
        await scenario.CreateItemAsync("APP", new NewWorkItem { Type = "requirement", Title = "別的專案的需求" });

        // Act
        var page = await ListAsync(scenario, new WorkItemQuery { ParentKey = parentKey });

        // Assert
        Assert.That(page.Items, Is.Empty);
    }

    [TestCase("")]
    [TestCase("WEB")]
    [TestCase("WEB-0")]
    [TestCase("WEB-x")]
    [TestCase("-1")]
    [TestCase("WEB-1\n")]
    [TestCase("WEB-1\t")]
    public async Task ListAsync_ParentKeyThatIsNotAnItemKey_IsQueryInvalid(string parentKey)
    {
        // Arrange
        var scenario = await TreeAsync();

        // Act
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery { ParentKey = parentKey }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "parent");
    }

    [Test]
    public async Task ListAsync_ParentKeyWithNul_IsQueryInvalid()
    {
        // Arrange
        var scenario = await TreeAsync();
        var parentKey = "WEB-1" + (char)0x0000;

        // Act
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", new WorkItemQuery { ParentKey = parentKey }, CancellationToken.None);

        // Assert
        ItemAssert.Invalid(result, WorkErrorCodes.QueryInvalid, "parent");
    }

    /// <summary>WEB-1 需求；WEB-2、WEB-3 任務掛在 WEB-1 底下；WEB-4 子任務掛在 WEB-2 底下；WEB-5 任務在最上層。</summary>
    private async Task<Scenario> TreeAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "登入頁", ParentKey = "WEB-1" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "忘記密碼", ParentKey = "WEB-1" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "subtask", Title = "登入頁的樣式", ParentKey = "WEB-2" });
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "task", Title = "不相干的任務" });
        return scenario;
    }

    private async Task MoveAsync(Scenario scenario, string itemKey, string state)
    {
        var result = await _host.Items.TransitionAsync(scenario.Admin, itemKey, state, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private async Task<WorkItemPage> ListAsync(Scenario scenario, WorkItemQuery query)
    {
        var result = await _host.Items.ListAsync(scenario.Admin, "WEB", query, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        return result.Value;
    }
}
