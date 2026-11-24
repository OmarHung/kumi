using PmTool.Abstractions;

namespace PmTool.Work.Tests.Activities;

/// <summary>看得到項目（item.read）就看得到活動；留言要 comment.create。看不到的項目一律 NotFound。</summary>
[TestFixture]
public sealed class ActivityAccessTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task Viewer_CanReadTheActivityButCannotComment()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var viewer = await scenario.MemberAsync("WEB", "viewer");

        // Act
        var list = await _host.Activity.ListAsync(viewer, item.Key, new ActivityQuery(), CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(viewer, item.Key, "想留言", null, CancellationToken.None);

        // Assert
        Assert.That(list.Value.Items.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created }));
        ItemAssert.Failed(comment, FailureKind.Forbidden, WorkErrorCodes.Forbidden);
        Assert.That(comment.Failure!.Message, Does.Contain("留言"));
    }

    [TestCase("commenter")]
    [TestCase("editor")]
    [TestCase("admin")]
    public async Task BuiltInRoleWithCommentCreate_CanComment(string role)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var member = await scenario.MemberAsync("WEB", role);

        // Act
        var result = await _host.Activity.AddCommentAsync(member, item.Key, "範例留言", null, CancellationToken.None);

        // Assert
        Assert.That((result.Value.Kind, result.Value.ActorId), Is.EqualTo((ActivityKind.Commented, member.PersonId)));
    }

    [Test]
    public async Task ClientRoleOfTheAgencyTemplate_CanReadAndComment()
    {
        // Arrange：客戶是藍圖裡的自訂角色，只有 item.read 與 comment.create。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        var client = await scenario.MemberAsync("WEB", "client");

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var comment = await _host.Activity.AddCommentAsync(client, item.Key, "可以驗收了嗎？", null, CancellationToken.None);
        var list = await _host.Activity.ListAsync(client, item.Key, new ActivityQuery(), CancellationToken.None);

        // Assert
        Assert.That(comment.Value.ActorId, Is.EqualTo(client.PersonId));
        Assert.That(list.Value.Items.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Commented }));
    }

    [Test]
    public async Task OrganizationAdminWhoIsNotAMember_CanReadAndComment()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var otherAdmin = scenario.Admin with { PersonId = Guid.NewGuid() };

        // Act
        var comment = await _host.Activity.AddCommentAsync(otherAdmin, item.Key, "管理員看過了", null, CancellationToken.None);
        var list = await _host.Activity.ListAsync(otherAdmin, item.Key, new ActivityQuery(), CancellationToken.None);

        // Assert
        Assert.That(comment.Succeeded, Is.True, () => comment.Failure!.Message);
        Assert.That(list.Value.Items, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task PersonWhoIsNotAMember_GetsItemNotFound()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        var outsider = scenario.Person();

        // Act
        var list = await _host.Activity.ListAsync(outsider, item.Key, new ActivityQuery(), CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(outsider, item.Key, "看不到", null, CancellationToken.None);

        // Assert
        ItemAssert.Failed(list, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(comment, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task AdminOfAnotherOrganization_GetsItemNotFound()
    {
        // Arrange：另一個組織也有同代號的專案，看到的是自己組織的，不是這一個。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");
        await _host.Activity.AddCommentAsync(scenario.Admin, item.Key, "只有這個組織看得到", null, CancellationToken.None);
        var elsewhere = new Scenario(_host);
        await elsewhere.CreateProjectAsync("WEB");

        // Act
        var list = await _host.Activity.ListAsync(elsewhere.Admin, item.Key, new ActivityQuery(), CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(elsewhere.Admin, item.Key, "看不到", null, CancellationToken.None);
        var samePerson = await _host.Activity.ListAsync(
            elsewhere.Admin with { PersonId = scenario.Admin.PersonId }, item.Key, new ActivityQuery(), CancellationToken.None);

        // Assert
        ItemAssert.Failed(list, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(comment, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(samePerson, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [TestCase("WEB-99")]
    [TestCase("WEB-0")]
    [TestCase("not-a-key")]
    [TestCase("")]
    public async Task ItemThatDoesNotExist_GetsItemNotFound(string itemKey)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateTaskAsync("WEB");

        // Act
        var list = await _host.Activity.ListAsync(scenario.Admin, itemKey, new ActivityQuery(), CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(scenario.Admin, itemKey, "找不到", null, CancellationToken.None);

        // Assert
        ItemAssert.Failed(list, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(comment, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public async Task ItemNotFound_IsReportedBeforeAnInvalidQueryOrBody()
    {
        // Arrange：看不到的項目，不管查詢或內容對不對都是 NotFound，不透露項目存不存在。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var item = await scenario.CreateTaskAsync("WEB");

        // Act
        var list = await _host.Activity.ListAsync(scenario.Person(), item.Key, new ActivityQuery { Limit = 0 }, CancellationToken.None);
        var comment = await _host.Activity.AddCommentAsync(scenario.Person(), item.Key, "", null, CancellationToken.None);

        // Assert
        ItemAssert.Failed(list, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
        ItemAssert.Failed(comment, FailureKind.NotFound, WorkErrorCodes.ItemNotFound);
    }

    [Test]
    public void NullArguments_Throw()
    {
        // Arrange
        var scenario = new Scenario(_host);

        // Act & Assert：呼叫端寫錯了，不是使用者的問題。
        Assert.That(() => _host.Activity.ListAsync(null!, "WEB-1", new ActivityQuery(), CancellationToken.None), Throws.ArgumentNullException);
        Assert.That(() => _host.Activity.ListAsync(scenario.Admin, "WEB-1", null!, CancellationToken.None), Throws.ArgumentNullException);
        Assert.That(() => _host.Activity.AddCommentAsync(null!, "WEB-1", "留言", null, CancellationToken.None), Throws.ArgumentNullException);
    }
}
