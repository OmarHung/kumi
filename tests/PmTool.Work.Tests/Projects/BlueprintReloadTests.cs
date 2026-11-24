using System.Text.Json.Nodes;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;

namespace PmTool.Work.Tests.Projects;

/// <summary>
/// 重新啟動之後快取是空的，藍圖要從資料庫的 jsonb 讀回來再驗證一次，這是每次重啟之後的正式路徑。
/// 開專案時一律先放進快取，所以要換一個新的服務容器（新的快取）對同一個資料庫才走得到。
/// </summary>
[TestFixture]
public sealed class BlueprintReloadTests
{
    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [TestCase(Scenario.AgencyProject)]
    [TestCase(Scenario.SimpleTasks)]
    public async Task GetAsync_AfterRestart_ReadsBackTheSameBlueprint(string template)
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", template);
        var original = await _host.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        await using var restarted = _host.Restart();

        // Act
        var reloaded = await restarted.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);

        // Assert：模型的清單是參考相等，所以比寫出來的文件。
        Assert.That(reloaded.Value.Blueprint, Is.Not.SameAs(original.Value.Blueprint), "應該是從資料庫重新讀的");
        Assert.That(BlueprintSerializer.ToJson(reloaded.Value.Blueprint), Is.EqualTo(BlueprintSerializer.ToJson(original.Value.Blueprint)));
        Assert.That(reloaded.Value.Project, Is.EqualTo(original.Value.Project));
    }

    [Test]
    public async Task ItemOperations_AfterRestart_StillReadABlueprintStoredWithAFieldNamedCycle()
    {
        // Arrange：保留字是後來才加的；之前存進去的藍圖可能已經有 cycle 欄位，讀回來不能因此整個專案壞掉。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.SimpleTasks);
        var stored = JsonNode.Parse(await StoredBlueprints.ReadCurrentAsync(_host, scenario.OrganizationId, "WEB"))!.AsObject();
        var fields = stored["fields"]?.AsArray() ?? [];
        fields.Add(new JsonObject { ["key"] = "cycle", ["name"] = "舊的欄位", ["type"] = "text" });
        stored["fields"] = fields;
        var document = stored.ToJsonString();
        var strict = BlueprintValidator.CreateDefault().ValidateBlueprint(document, DocumentFormat.Json);
        Assert.That(strict.Report.Issues.Select(i => i.Code), Does.Contain(IssueCodes.FieldCoreNameConflict), "新的文件應該被擋下");
        await StoredBlueprints.PublishDocumentAsync(_host, scenario, "WEB", document);
        await using var restarted = _host.Restart();

        // Act
        var project = await restarted.Projects.GetAsync(scenario.Admin, "WEB", CancellationToken.None);
        var created = await restarted.Items.CreateAsync(
            scenario.Admin, "WEB", new NewWorkItem { Type = "task", Title = "舊欄位的專案" }, CancellationToken.None);

        // Assert
        Assert.That(project.Succeeded, Is.True, () => project.Failure!.Message);
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
    }

    [Test]
    public async Task ItemOperations_AfterRestart_UseTheBlueprintReadFromTheDatabase()
    {
        // Arrange：專案經理是藍圖裡的自訂角色，他的權限只有讀得到藍圖才知道。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        await using var restarted = _host.Restart();
        var item = new NewWorkItem
        {
            Type = "requirement",
            Title = "會員登入",
            AssigneeId = manager.PersonId,
            Fields = Scenario.Fields(("quote_amount", 1200)),
        };

        // Act
        var created = await restarted.Items.CreateAsync(manager, "WEB", item, CancellationToken.None);
        var moved = await restarted.Items.TransitionAsync(manager, "WEB-1", "quoting", CancellationToken.None);
        var readBack = await restarted.Items.GetAsync(manager, "WEB-1", CancellationToken.None);

        // Assert
        Assert.That(created.Succeeded, Is.True, () => created.Failure!.Message);
        Assert.That(moved.Succeeded, Is.True, () => moved.Failure!.Message);
        Assert.That(
            (readBack.Value.State, readBack.Value.StateCategory, readBack.Value.Fields["quote_amount"].GetRawText()),
            Is.EqualTo(("quoting", StateCategory.NotStarted, "1200")));
    }

    [Test]
    public async Task Activity_AfterRestart_IsReadAndWrittenWithTheCustomRoleFromTheDatabase()
    {
        // Arrange：客戶是藍圖裡的自訂角色，能讀能留言；重新啟動之後要從資料庫讀回藍圖才知道。
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var client = await scenario.MemberAsync("WEB", "client");
        var item = await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入" });
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        await _host.Items.UpdateAsync(scenario.Admin, item.Key, new WorkItemPatch { Description = "登入與註冊" }, CancellationToken.None);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var before = await ActivityLog.AllAsync(_host, client, item.Key);
        await using var restarted = _host.Restart();

        // Act
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var comment = await restarted.Activity.AddCommentAsync(client, item.Key, "重啟之後的留言", null, CancellationToken.None);
        var after = await ActivityLog.AllAsync(restarted, client, item.Key);

        // Assert
        Assert.That(comment.Succeeded, Is.True, () => comment.Failure!.Message);
        Assert.That(after.Select(a => (a.Id, a.Kind, a.At)), Is.EqualTo(before.Select(a => (a.Id, a.Kind, a.At)).Append((comment.Value.Id, ActivityKind.Commented, comment.Value.At))));
        Assert.That(after.Select(a => a.Kind), Is.EqualTo(new[] { ActivityKind.Created, ActivityKind.Updated, ActivityKind.Commented }));
        Assert.That(ActivityLog.Shapes(after[1]), Is.EqualTo(new (string, string?, string?)[] { ("description", null, null) }));
    }

    [Test]
    public async Task ListAssignedToMeAsync_AfterRestart_ReadsTheCustomRolePermissionsFromTheDatabase()
    {
        // Arrange
        var scenario = new Scenario(_host);
        await scenario.CreateProjectAsync("WEB", Scenario.AgencyProject);
        var manager = await scenario.MemberAsync("WEB", "project_manager");
        await scenario.CreateItemAsync("WEB", new NewWorkItem { Type = "requirement", Title = "會員登入", AssigneeId = manager.PersonId });
        await using var restarted = _host.Restart();

        // Act
        var mine = await restarted.Items.ListAssignedToMeAsync(manager, CancellationToken.None);

        // Assert
        Assert.That(mine.Select(i => i.Key), Is.EqualTo(new[] { "WEB-1" }));
    }
}
