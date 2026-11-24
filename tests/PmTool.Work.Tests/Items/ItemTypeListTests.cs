using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Items;

/// <summary>跨專案的類型選項：依 key 合併、名稱去重複、只列看得到而且沒有封存的專案。</summary>
[TestFixture]
public sealed class ItemTypeListTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public async Task ListItemTypesAsync_SameKeyAndSameName_MergesIntoOneTypeWithBothProjects()
    {
        // Arrange：兩個專案都有 task（任務）；只有 APP 有需求與需求變更單。
        var scenario = await TwoProjectsAsync();

        // Act
        var types = await _host.Items.ListItemTypesAsync(scenario.Admin, CancellationToken.None);

        // Assert：照 key 排；projectKeys 照代號排
        Assert.That(types.Select(t => t.Key), Is.EqualTo(new[] { "change_request", "requirement", "subtask", "task" }));
        var task = types.Single(t => t.Key == "task");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.Names, Is.EqualTo(new[] { "任務" }));
            Assert.That(task.ProjectKeys, Is.EqualTo(new[] { "APP", "WEB" }));
            Assert.That(types.Single(t => t.Key == "requirement").ProjectKeys, Is.EqualTo(new[] { "APP" }));
        }
    }

    [Test]
    public async Task ListItemTypesAsync_SameKeyWithDifferentNames_ListsEveryNameInProjectKeyOrder()
    {
        // Arrange：WEB 把任務改叫「工作」，APP 與第三個專案 ZED 叫「任務」。名稱依專案代號（APP、WEB、ZED）第一次出現的順序。
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("ZED");
        await PublishAsync(scenario, "WEB", blueprint => blueprint with
        {
            ItemTypes = [.. blueprint.ItemTypes.Select(t => t.Key == "task" ? t with { Name = "工作" } : t)],
        });

        // Act
        var types = await _host.Items.ListItemTypesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        var task = types.Single(t => t.Key == "task");
        Assert.That(task.Names, Is.EqualTo(new[] { "任務", "工作" }));
        Assert.That(task.ProjectKeys, Is.EqualTo(new[] { "APP", "WEB", "ZED" }));
    }

    [Test]
    public async Task ListItemTypesAsync_ArchivedProject_IsLeftOut()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();
        await _host.Projects.ArchiveAsync(scenario.Admin, "APP", CancellationToken.None);

        // Act
        var types = await _host.Items.ListItemTypesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(types.Select(t => t.Key), Is.EqualTo(new[] { "task" }));
        Assert.That(types[0].ProjectKeys, Is.EqualTo(new[] { "WEB" }));
    }

    [Test]
    public async Task ListItemTypesAsync_Member_ListsOnlyProjectsTheyCanRead()
    {
        // Arrange：只是 WEB 的成員；APP 不是成員；OPEN 的角色讀不到項目。
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("OPEN", Scenario.AgencyProject);
        await PublishAsync(scenario, "OPEN", blueprint => blueprint with
        {
            Roles = [.. blueprint.Roles, new RoleDefinition { Key = "guest", Name = "訪客", Permissions = [WorkPermissions.CommentCreate] }],
        });
        var me = await scenario.MemberAsync("WEB", "editor");
        await _host.Projects.SetMemberAsync(scenario.Admin, "OPEN", me.PersonId, "guest", CancellationToken.None);

        // Act
        var types = await _host.Items.ListItemTypesAsync(me, CancellationToken.None);

        // Assert
        Assert.That(types.Select(t => t.Key), Is.EqualTo(new[] { "task" }));
        Assert.That(types[0].ProjectKeys, Is.EqualTo(new[] { "WEB" }));
    }

    [Test]
    public async Task ListItemTypesAsync_ProjectOfAnotherOrganization_IsNotListed()
    {
        // Arrange：另一個組織有客戶專案（有需求類型）；我的組織只有 WEB。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        var other = new Scenario(_host);
        await other.CreateProjectAsync("APP", Scenario.AgencyProject);

        // Act
        var types = await _host.Items.ListItemTypesAsync(scenario.Admin, CancellationToken.None);

        // Assert
        Assert.That(types.Select(t => t.Key), Is.EqualTo(new[] { "task" }));
        Assert.That(types[0].ProjectKeys, Is.EqualTo(new[] { "WEB" }));
    }

    [Test]
    public async Task ListItemTypesAsync_OrganizationAdminWhoIsNotAMember_SeesEveryActiveProjectAndNotTheArchivedOnes()
    {
        // Arrange：另一位管理員不是任何專案的成員；APP 封存了。
        var scenario = await TwoProjectsAsync();
        await scenario.CreateProjectAsync("OLD", Scenario.AgencyProject);
        await _host.Projects.ArchiveAsync(scenario.Admin, "OLD", CancellationToken.None);
        var otherAdmin = scenario.Admin with { PersonId = Guid.NewGuid() };

        // Act
        var types = await _host.Items.ListItemTypesAsync(otherAdmin, CancellationToken.None);

        // Assert：APP 的需求類型還在（APP 沒封存）；OLD 不出現
        var task = types.Single(t => t.Key == "task");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(task.ProjectKeys, Is.EqualTo(new[] { "APP", "WEB" }));
            Assert.That(types.Single(t => t.Key == "requirement").ProjectKeys, Is.EqualTo(new[] { "APP" }));
        }
    }

    [Test]
    public async Task ListItemTypesAsync_NoReadableProject_IsEmpty()
    {
        // Arrange
        var scenario = await TwoProjectsAsync();

        // Act
        var types = await _host.Items.ListItemTypesAsync(scenario.Person(), CancellationToken.None);

        // Assert
        Assert.That(types, Is.Empty);
    }

    private async Task<Scenario> TwoProjectsAsync()
    {
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB");
        await scenario.CreateProjectAsync("APP", Scenario.AgencyProject);
        return scenario;
    }

    private async Task PublishAsync(Scenario scenario, string projectKey, Func<ProjectBlueprint, ProjectBlueprint> change)
    {
        var current = await _host.Projects.GetAsync(scenario.Admin, projectKey, CancellationToken.None);
        await StoredBlueprints.PublishAsync(_host, scenario, projectKey, change(current.Value.Blueprint));
    }
}
