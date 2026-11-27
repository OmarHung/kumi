using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint.Model;
using PmTool.Identity;
using PmTool.Notifications;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>
/// MCP 的 list_saved_views、list_description_templates、get_overview、list_notifications、list_changesets（items:read）
/// 與 propose_saved_view_changes、propose_description_template_changes（agent:run，只建立建議）。
/// </summary>
[TestFixture]
public sealed class McpWorkspaceToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead];

    private static readonly string[] ReadTools =
        ["list_saved_views", "list_description_templates", "get_overview", "list_notifications", "list_changesets"];

    private static readonly string[] ProposalTools = ["propose_saved_view_changes", "propose_description_template_changes"];

    [Test]
    public async Task ListTools_AnOrdinaryTokenWithItemsRead_ListsTheReadToolsButNotTheProposalTools()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Is.SupersetOf(ReadTools));
            Assert.That(tools, Does.Not.Contain("propose_saved_view_changes"));
        }
    }

    [Test]
    public async Task ListTools_ALocalAgentToken_ListsBothProposalToolsAndOnlyWhatTheAllowedListHas()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", [.. AccessTokenScopes.All]);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools, Is.SupersetOf(ProposalTools));
            Assert.That(tools, Is.SupersetOf(ReadTools));
            Assert.That(tools, Is.EquivalentTo(LocalAgentTools.Allowed));
        }
    }

    [Test]
    public async Task ListSavedViews_WithAProjectKey_ListsTheProjectViews_AndWithoutOneMyWork()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (projectError, project) = await mcp.CallAsync("list_saved_views", new { projectKey = "WEB" });
        var (myWorkError, myWork) = await mcp.CallAsync("list_saved_views");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(projectError, Is.False, project.ToJsonString());
            Assert.That(project["scope"]?.GetValue<string>(), Is.EqualTo("project"));
            Assert.That(project["views"]![0]!["name"]?.GetValue<string>(), Is.EqualTo("我本週要交的"));
            Assert.That(project["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
            Assert.That(myWorkError, Is.False);
            Assert.That(myWork["scope"]?.GetValue<string>(), Is.EqualTo("my_work"));
            Assert.That(mcp.Modules.SavedViews.ListMyWorkCalls, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task ListSavedViews_ALocalAgentForOneProjectAskingForAnother_IsRefused()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("list_saved_views", new { projectKey = "OPS" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("找不到專案"));
        }
    }

    [Test]
    public async Task ListDescriptionTemplates_ReturnsTheTemplates()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, body) = await mcp.CallAsync("list_description_templates");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["templates"]![0]!["isBuiltIn"]?.GetValue<bool>(), Is.True);
            Assert.That(body["templates"]![0]!["name"]?.GetValue<string>(), Is.EqualTo("網站案子"));
        }
    }

    [TestCase("managed", ProjectOverviewScope.Managed)]
    [TestCase("visible", ProjectOverviewScope.Visible)]
    [TestCase(null, ProjectOverviewScope.Visible)]
    public async Task GetOverview_PassesTheScopeToTheService(string? scope, ProjectOverviewScope expected)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, _) = await mcp.CallAsync("get_overview", scope is null ? null : new { scope });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Overview.Calls.Single().Scope, Is.EqualTo(expected));
        }
    }

    [Test]
    public async Task GetOverview_AnUnknownScope_IsAnError()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);

        // Act
        var (isError, _) = await mcp.CallAsync("get_overview", new { scope = "all" });

        // Assert
        Assert.That(isError, Is.True);
    }

    [Test]
    public async Task ListNotifications_PassesTheQueryAndNeverMarksAnythingAsRead()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Notifications.OnList = (_, _) => new NotificationPage([Notification("WEB", "WEB-1"), Notification("OPS", "OPS-2")], null);

        // Act
        var (isError, body) = await mcp.CallAsync("list_notifications", new { unreadOnly = true, limit = 5 });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["notifications"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(mcp.Modules.Notifications.ListCalls.Single().Query, Is.EqualTo(new NotificationQuery(true, null, 5)));
            Assert.That(mcp.Modules.Notifications.MarkReadCalls, Is.Empty);
            Assert.That(mcp.Modules.Notifications.MarkAllReadCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ListNotifications_ALocalAgentForOneProject_OnlySeesThatProjectsNotifications()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.Notifications.OnList = (_, _) => new NotificationPage([Notification("WEB", "WEB-1"), Notification("OPS", "OPS-2")], null);

        // Act
        var (isError, body) = await mcp.CallAsync("list_notifications");

        // Assert
        var items = body["notifications"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0]!["itemKey"]?.GetValue<string>(), Is.EqualTo("WEB-1"));
        }
    }

    [Test]
    public async Task GetOverview_ALocalAgentForOneProject_OnlySeesThatProjectsRow()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.Overview.OnList = () => [Row("WEB"), Row("OPS")];

        // Act
        var (isError, body) = await mcp.CallAsync("get_overview");

        // Assert
        var projects = body["projects"]!.AsArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(projects.Select(p => p!["projectKey"]?.GetValue<string>()), Is.EqualTo(new[] { "WEB" }));
        }
    }

    [Test]
    public async Task GetOverview_AnOrdinaryToken_SeesAllRows()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        mcp.Modules.Overview.OnList = () => [Row("WEB"), Row("OPS")];

        // Act
        var (_, body) = await mcp.CallAsync("get_overview");

        // Assert
        Assert.That(body["projects"]!.AsArray(), Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListNotifications_NeverListsAccountSecurityNotifications()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.ItemsRead);
        var account = new NotificationItem(Guid.NewGuid(), NotificationKind.AccountSecurity, null, null, null, TestUsers.Owner.PersonId, null, null, null, TestData.UpdatedAt, null, null);
        mcp.Modules.Notifications.OnList = (_, _) => new NotificationPage([Notification("WEB", "WEB-1"), account], null);

        // Act
        var (_, body) = await mcp.CallAsync("list_notifications");

        // Assert
        Assert.That(body["notifications"]!.AsArray().Select(n => n!["kind"]?.GetValue<string>()), Is.EqualTo(new[] { "mentioned" }));
    }

    private static ProjectOverview Row(string key) =>
        new(key, key, false, [], 1, 0, 0, 0, 0, 0, null, null, null);

    [Test]
    public async Task ListChangeSets_ReturnsTheSummariesOfTheProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync(AccessTokenScope.BlueprintRead, AccessTokenScope.ItemsRead);
        mcp.Modules.Changes.OnList = (_, _) => new List<ChangeSetSummary> { TestData.ChangeSet(3, TestUsers.Owner.PersonId) };

        // Act
        var (isError, body) = await mcp.CallAsync("list_changesets", new { projectKey = "WEB" });

        // Assert
        var first = body["changeSets"]![0]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(first["number"]?.GetValue<long>(), Is.EqualTo(3));
            Assert.That(first["status"]?.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(first["proposedBy"]!["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Owner.PersonId));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("使用者寫的內容"));
        }
    }

    [Test]
    public async Task ProposeSavedViewChanges_BuildsTheDraftWithTheProjectReasonAndConversation()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_saved_view_changes", new
        {
            projectKey = "WEB",
            changes = new object[] { new { action = "create", name = "本週", layout = "board", due = "week" } },
            reason = "常用",
        });

        // Assert
        var draft = mcp.Modules.ActionProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeActionProposals.ProposalId));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("不要說已經改好了"));
            Assert.That((draft.Kind, draft.ProjectKey, draft.Reason), Is.EqualTo((ActionKinds.SavedViews, "WEB", "常用")));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(draft.Payload.GetProperty("changes")[0].GetProperty("name").GetString(), Is.EqualTo("本週"));
        }
    }

    [Test]
    public async Task ProposeSavedViewChanges_WithoutAProjectKey_IsAboutMyWork()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_saved_view_changes", new { changes = new object[] { new { action = "delete", viewId = Guid.NewGuid() } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.ActionProposals.Creates.Single().Draft.ProjectKey, Is.Null);
        }
    }

    [Test]
    public async Task ProposeSavedViewChanges_ForAnotherProjectThanTheAgentsOwn_IsRefusedBeforeAnyProposalIsMade()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_saved_view_changes", new { projectKey = "OPS", changes = new object[] { new { action = "create", name = "x" } } });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [TestCase("""{"changes":[{"action":"create","name":"x","surprise":1}]}""")]
    [TestCase("""{"changes":["create"]}""")]
    [TestCase("""{"reason":"沒給 changes"}""")]
    public async Task ProposeSavedViewChanges_BadShapes_AreRejectedWithoutCallingTheCreator(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_saved_view_changes", System.Text.Json.JsonDocument.Parse(json).RootElement.Clone());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeDescriptionTemplateChanges_BuildsADraftWithoutAProject_AndForwardsTheCreatorsFailure()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        mcp.Modules.ActionProposals.OnCreate = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.Forbidden, "只有組織管理員可以管理說明格式。");

        // Act
        var (isError, body) = await mcp.CallAsync(
            "propose_description_template_changes",
            new { changes = new object[] { new { action = "create", name = "新", body = "內容", isDefault = true } } });

        // Assert
        var draft = mcp.Modules.ActionProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That((draft.Kind, draft.ProjectKey), Is.EqualTo((ActionKinds.DescriptionTemplates, (string?)null)));
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("組織管理員"));
        }
    }

    private static NotificationItem Notification(string project, string itemKey) =>
        new(Guid.NewGuid(), NotificationKind.Mentioned, project, itemKey, "標題", TestUsers.Owner.PersonId, null, null, "請看", TestData.UpdatedAt, null, null);
}
