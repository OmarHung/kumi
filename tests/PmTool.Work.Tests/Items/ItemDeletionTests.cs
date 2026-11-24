using PmTool.Abstractions;

namespace PmTool.Work.Tests.Items;

/// <summary>
/// 刪除項目＝封存：記下誰、什麼時候刪的，留一筆活動，資料留著；還有沒刪除的下層時擋下，不連帶刪除。
/// 用 agency_project：任務可以掛在需求底下。
/// </summary>
[TestFixture]
public sealed class ItemDeletionTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task DeleteAsync_ItemWithoutChildren_ReturnsTheKeyAndTimeAndKeepsTheRowWithADeletedActivity()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var editor = await scenario.MemberAsync("WEB", "editor");
        var item = await scenario.CreateItemAsync("WEB", Requirement("要刪掉的需求"));
        _host.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var result = await _host.Items.DeleteAsync(editor, "web-1", CancellationToken.None);
        var stored = await DeletedItems.ReadAsync(_host, scenario, item.Key);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Value, Is.EqualTo(new DeletedWorkItem("WEB-1", _host.Clock.GetUtcNow())));
            Assert.That((stored.DeletedAt, stored.DeletedBy), Is.EqualTo(((DateTimeOffset?)_host.Clock.GetUtcNow(), (Guid?)editor.PersonId)));
            Assert.That(stored.ActivityKinds, Is.EqualTo(new[] { "created", "deleted" }));
        }
    }

    [Test]
    public async Task DeleteAsync_AlreadyDeleted_IsNotFound()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", Requirement("刪兩次"));
        await DeleteAsync(scenario, item.Key);

        // Act
        var again = await _host.Items.DeleteAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(again, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task DeleteAsync_WithChildren_IsHasChildrenWithOneDetailPerChildAndDeletesNothing()
    {
        // Arrange：兩個任務掛在需求底下，另一個需求底下的任務不算。
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement("首頁改版"));
        var other = await scenario.CreateItemAsync("WEB", Requirement("會員中心"));
        await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await scenario.CreateItemAsync("WEB", Task("會員中心的登入", other.Key));
        await scenario.CreateItemAsync("WEB", Task("首頁的文案", requirement.Key));

        // Act
        var result = await _host.Items.DeleteAsync(scenario.Admin, requirement.Key, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, requirement.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Conflict, WorkErrorCodes.ItemHasChildren);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Failure!.Details.Select(d => (d.Code, d.Path)),
                Is.EqualTo(new[] { (WorkErrorCodes.ItemHasChildren, "children/WEB-3"), (WorkErrorCodes.ItemHasChildren, "children/WEB-5") }));
            Assert.That(result.Failure.Details[0].Message, Does.Contain("WEB-3").And.Contain("首頁的版面"));
            Assert.That(result.Failure.Message, Does.Contain("2 個"));
            Assert.That(readBack.Succeeded, Is.True, "沒有刪掉");
        }
    }

    [Test]
    public async Task DeleteAsync_ChildrenAllDeleted_DeletesTheParent()
    {
        // Arrange
        var scenario = await AgencyAsync();
        var requirement = await scenario.CreateItemAsync("WEB", Requirement("首頁改版"));
        var child = await scenario.CreateItemAsync("WEB", Task("首頁的版面", requirement.Key));
        await DeleteAsync(scenario, child.Key);

        // Act
        var result = await _host.Items.DeleteAsync(scenario.Admin, requirement.Key, CancellationToken.None);

        // Assert
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    [TestCase("commenter")]
    [TestCase("viewer")]
    [TestCase("project_manager")]
    public async Task DeleteAsync_RoleWithoutItemDelete_IsForbiddenAndDeletesNothing(string role)
    {
        // Arrange：接案專案的專案經理能改項目、核准設定，但沒有 item.delete。
        var scenario = await AgencyAsync();
        var member = await scenario.MemberAsync("WEB", role);
        var item = await scenario.CreateItemAsync("WEB", Requirement("不能刪"));

        // Act
        var result = await _host.Items.DeleteAsync(member, item.Key, CancellationToken.None);
        var readBack = await _host.Items.GetAsync(scenario.Admin, item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(result.Failure!.Message, Does.Contain("刪除項目"));
        Assert.That(readBack.Succeeded, Is.True);
    }

    [TestCase("WEB-99")]
    [TestCase("not-a-key")]
    public async Task DeleteAsync_ItemThatDoesNotExist_IsNotFound(string itemKey)
    {
        // Arrange
        var scenario = await AgencyAsync();

        // Act
        var result = await _host.Items.DeleteAsync(scenario.Admin, itemKey, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task DeleteAsync_ProjectTheCallerCannotSee_IsNotFound()
    {
        // Arrange：不是成員的人問的是項目，不能從代碼分辨出專案存不存在。
        var scenario = await AgencyAsync();
        var item = await scenario.CreateItemAsync("WEB", Requirement("看不到"));

        // Act
        var result = await _host.Items.DeleteAsync(scenario.Person(), item.Key, CancellationToken.None);

        // Assert
        ItemAssert.Failed(result, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task CreateAsync_AfterTheLastItemIsDeleted_DoesNotReuseItsNumber()
    {
        // Arrange
        var scenario = await AgencyAsync();
        await scenario.CreateItemAsync("WEB", Requirement("第一個"));
        var last = await scenario.CreateItemAsync("WEB", Requirement("第二個"));
        await DeleteAsync(scenario, last.Key);

        // Act
        var next = await scenario.CreateItemAsync("WEB", Requirement("第三個"));

        // Assert
        Assert.That(next.Key, Is.EqualTo("WEB-3"));
    }

    private async Task DeleteAsync(Scenario scenario, string itemKey)
    {
        var result = await _host.Items.DeleteAsync(scenario.Admin, itemKey, CancellationToken.None);
        Assert.That(result.Succeeded, Is.True, () => result.Failure!.Message);
    }

    private async Task<Scenario> AgencyAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        return scenario;
    }

    private static NewWorkItem Requirement(string title) => new() { Type = "requirement", Title = title };

    private static NewWorkItem Task(string title, string? parentKey) => new() { Type = "task", Title = title, ParentKey = parentKey };
}
