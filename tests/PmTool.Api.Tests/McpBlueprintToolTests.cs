using ModelContextProtocol;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Blueprint;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;
using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>讀設定與變更集的工具；變更集只能提出，來源一律是 agent。</summary>
[TestFixture]
public sealed class McpBlueprintToolTests
{
    private const string Document = """{"baseVersion":1,"summary":"加一個欄位","blueprint":{},"migrations":[]}""";

    [Test]
    public async Task GetBlueprint_ReturnsTheVersionTheFullBlueprintAndWhatTheActorMayDo()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Projects.OnGet = (_, key) => new ProjectDetail(
            TestData.Project(key), TestData.Blueprint(), [], [WorkPermissions.ItemRead, WorkPermissions.BlueprintPropose]);

        // Act
        var (isError, body) = await mcp.CallAsync("get_blueprint", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(body["version"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(body["blueprint"]!["metadata"]!["name"]!.GetValue<string>(), Is.EqualTo("Example 網站"));
            Assert.That(body["canPropose"]!.GetValue<bool>(), Is.True);
            Assert.That(body["canApply"]!.GetValue<bool>(), Is.False);
            Assert.That(mcp.Modules.Projects.GetCalls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB")));
        }
    }

    [Test]
    public async Task GetBlueprint_ProjectTheActorCannotSee_IsNotFound()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Projects.OnGet = (_, _) => new Failure(FailureKind.NotFound, WorkErrorCodes.ProjectNotFound, "找不到這個專案。");

        // Act
        var (isError, body) = await mcp.CallAsync("get_blueprint", new { projectKey = "SECRET" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Is.EqualTo("找不到這個專案。"));
        }
    }

    [Test]
    public async Task Templates_ListThenReadTheBuiltInOnes()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (listError, list) = await mcp.CallAsync("list_templates");
        var key = list["templates"]![0]!["key"]!.GetValue<string>();
        var (getError, template) = await mcp.CallAsync("get_template", new { templateKey = key });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(listError, Is.False);
            Assert.That(key, Is.EqualTo("simple_tasks"));
            Assert.That(list["templates"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("簡單任務"));
            Assert.That(getError, Is.False);
            Assert.That(template["key"]!.GetValue<string>(), Is.EqualTo(key));
            Assert.That(template["blueprint"]!["metadata"]!["name"]!.GetValue<string>(), Is.EqualTo("Example 網站"));
        }
    }

    [Test]
    public async Task GetTemplate_UnknownKey_PointsTheAgentToListTemplates()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("get_template", new { templateKey = "no_such_template" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("list_templates"));
        }
    }

    [Test]
    public async Task DescribeUsage_ReturnsOnlyCounts()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Usage.OnDescribe = (_, _) => new BlueprintUsage(
            3, 12, [new StateUsage("task", "doing", 5)], [new FieldUsage("impact", 4), new FieldUsage("unused", 0)]);

        // Act
        var (isError, body) = await mcp.CallAsync("describe_usage", new { projectKey = "WEB" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Usage.Calls.Single(), Is.EqualTo((TestUsers.Member.ToActor(), "WEB")));
            Assert.That(body["itemCount"]!.GetValue<int>(), Is.EqualTo(12));
            Assert.That(body["states"]![0]!["items"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(body["fields"]![1]!["filledItems"]!.GetValue<int>(), Is.Zero);
        }
    }

    [Test]
    public async Task PreviewChangeSet_PassesTheDocumentAsJson_AndReturnsChangesImpactsAndBlockers()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Changes.OnPreview = _ => new ChangeSetPreview(
            1,
            1,
            TestData.Blueprint(),
            [new BlueprintChange { Kind = ChangeKind.Modified, Resource = ResourceKind.Field, Key = "impact", ChangedProperties = ["name"] }],
            [new PlannedImpact(new FieldDropImpact("legacy"), 4, BlocksApply: false)],
            [new FailureDetail(WorkErrorCodes.ChangeSetApplyBlocked, "blueprint", "還有項目在用這個類型。")],
            CanApply: false);

        // Act
        var (isError, body) = await mcp.CallAsync("preview_changeset", new { projectKey = "WEB", changeset = System.Text.Json.JsonDocument.Parse(Document).RootElement });

        // Assert
        var call = mcp.Modules.Changes.PreviewCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(call.Format, Is.EqualTo(DocumentFormat.Json));
            Assert.That(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(call.Document), System.Text.Json.Nodes.JsonNode.Parse(Document)), Is.True, call.Document);
            Assert.That(body["canApply"]!.GetValue<bool>(), Is.False);
            Assert.That(body["changes"]![0]!["key"]!.GetValue<string>(), Is.EqualTo("impact"));
            Assert.That(body["impacts"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("field_drop"));
            Assert.That(body["impacts"]![0]!["affected"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(body["blockers"]![0]!["code"]!.GetValue<string>(), Is.EqualTo(WorkErrorCodes.ChangeSetApplyBlocked));
        }
    }

    [Test]
    public async Task PreviewChangeSet_ADocumentWrappedInAString_IsAccepted()
    {
        // Arrange：模型常把物件包成字串送來。
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, _) = await mcp.CallAsync("preview_changeset", new { projectKey = "WEB", changeset = Document });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Changes.PreviewCalls.Single().Document, Is.EqualTo(Document));
        }
    }

    [TestCase("not json")]
    [TestCase("[1,2]")]
    public async Task PreviewChangeSet_NotAnObject_IsAToolErrorBeforeTheModule(string changeset)
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("preview_changeset", new { projectKey = "WEB", changeset });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("changeset"));
            Assert.That(mcp.Modules.Changes.TotalCalls, Is.Zero);
        }
    }

    [Test]
    public async Task PreviewChangeSet_InvalidDocument_ListsEveryIssue()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Changes.OnPreview = _ => new Failure(FailureKind.Invalid, WorkErrorCodes.ChangeSetInvalid, "變更集不合格。")
        {
            Details =
            [
                new FailureDetail("blueprint.unknown_state", "blueprint.workflows[0]", "狀態不存在。"),
                new FailureDetail("changeset.base_version", "baseVersion", "基準版本不是目前的版本。"),
            ],
        };

        // Act
        var (isError, body) = await mcp.CallAsync("preview_changeset", new { projectKey = "WEB", changeset = Document });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["issues"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(body["issues"]![1]!["path"]!.GetValue<string>(), Is.EqualTo("baseVersion"));
        }
    }

    [Test]
    public async Task GetChangeSet_ReturnsStatusOriginAndProposerWithoutTheFullBlueprints()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Changes.OnGet = call => TestData.ChangeSetDetail(
            TestData.ChangeSet(call.Number, TestUsers.Owner.PersonId) with { Origin = ChangeSetOrigin.Agent });

        // Act
        var (isError, body) = await mcp.CallAsync("get_changeset", new { projectKey = "WEB", number = 4 });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.Changes.GetCalls.Single(), Is.EqualTo(new ChangeSetCall(TestUsers.Member.ToActor(), "WEB", 4)));
            Assert.That(body["changeSet"]!["number"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(body["changeSet"]!["origin"]!.GetValue<string>(), Is.EqualTo("agent"));
            Assert.That(body["changeSet"]!["status"]!.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(body["changeSet"]!["proposedBy"]!["displayName"]!.GetValue<string>(), Is.EqualTo(TestUsers.Owner.DisplayName));
            Assert.That(body["canApply"]!.GetValue<bool>(), Is.True);
            Assert.That(body.AsObject().ContainsKey("blueprint"), Is.False);
        }
    }

    [Test]
    public async Task GetChangeSet_NumberMustBeAnInteger()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var (isError, body) = await mcp.CallAsync("get_changeset", new { projectKey = "WEB", number = "four" });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("number"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_IsMarkedAsAnAgent_AndReturnsTheWebUrlOfTheIssuer()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Changes.OnPropose = call => TestData.ChangeSetDetail(
            TestData.ChangeSet(7, call.Actor.PersonId) with { Origin = call.Origin });

        // Act
        var (isError, body) = await mcp.CallAsync("propose_changeset", new { projectKey = "WEB", changeset = Document });

        // Assert
        var call = mcp.Modules.Changes.ProposeCalls.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(call.Origin, Is.EqualTo(ChangeSetOrigin.Agent));
            Assert.That(call.Actor, Is.EqualTo(TestUsers.Member.ToActor()), "權限用授權的人，不因為是代理而多");
            Assert.That(call.Format, Is.EqualTo(DocumentFormat.Json));
            Assert.That(body["changeSet"]!["number"]!.GetValue<int>(), Is.EqualTo(7));
            Assert.That(body["changeSet"]!["origin"]!.GetValue<string>(), Is.EqualTo("agent"));
            Assert.That(body["url"]!.GetValue<string>(), Is.EqualTo("http://localhost/projects/WEB/changesets/7"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_WithAPublicHost_TheUrlUsesHttpsAndThatHost_NotTheRequestsHost()
    {
        // Arrange：網址要交給使用者點，不能由用戶端填的 Host 標頭決定。
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Security:PublicHost"] = "pm.example.com" } });
        var secret = McpTesting.GrantToken(factory, TestUsers.Member, McpTesting.AllScopes);
        await using var client = await McpTesting.ConnectAsync(factory, secret);

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, "propose_changeset", new { projectKey = "web", changeset = Document });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(body["url"]!.GetValue<string>(), Is.EqualTo("https://pm.example.com/projects/WEB/changesets/1"));
        }
    }

    [Test]
    public async Task ProposeChangeSet_ActorMayNotPropose_GetsTheModulesRefusalAndNothingIsStored()
    {
        // Arrange
        await using var mcp = await McpHarness.StartAsync();
        mcp.Modules.Changes.OnPropose = _ => new Failure(FailureKind.Forbidden, WorkErrorCodes.Forbidden, "你沒有提出變更集的權限。");

        // Act
        var (isError, body) = await mcp.CallAsync("propose_changeset", new { projectKey = "WEB", changeset = Document });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Is.EqualTo("你沒有提出變更集的權限。"));
            Assert.That(body.AsObject().ContainsKey("url"), Is.False);
        }
    }

    [Test]
    public async Task ThereIsNoToolThatAppliesOrRejectsAChangeSet()
    {
        // Arrange：規劃書：AI 只提案，人核准。
        await using var mcp = await McpHarness.StartAsync();

        // Act
        var tools = await mcp.Client.ListToolsAsync();

        // Assert
        Assert.That(tools.Select(tool => tool.Name), Has.None.Contains("apply").And.None.Contains("reject"));
        Assert.ThrowsAsync<McpProtocolException>(async () => await mcp.CallAsync("apply_changeset", new { projectKey = "WEB", number = 1 }));
    }
}
