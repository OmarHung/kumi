using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>MCP 的 propose_people_changes、propose_attachment_changes（agent:run，只建立動作建議）。</summary>
[TestFixture]
public sealed class McpPeopleAttachmentToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    [Test]
    public async Task ListTools_LocalAgentToken_IncludesBothToolsWithTheAgentRunScope()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", [.. AccessTokenScopes.All]);

        // Act
        var tools = (await mcp.Client.ListToolsAsync()).Select(tool => tool.Name).ToArray();

        // Assert
        Assert.That(tools, Is.SupersetOf(new[] { "propose_people_changes", "propose_attachment_changes" }));
    }

    [Test]
    public async Task ProposePeopleChanges_Description_SaysCreateTakesTitleAndResponsibilities()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var tool = (await mcp.Client.ListToolsAsync()).Single(t => t.Name == "propose_people_changes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tool.Description, Does.Contain("create {displayName, email, role, jobTitle?, responsibilities?}"));
            Assert.That(tool.JsonSchema.GetProperty("properties").GetProperty("changes").GetProperty("items").GetProperty("properties").GetProperty("jobTitle").GetProperty("description").GetString(), Does.StartWith("create、set_profile"));
        }
    }

    [Test]
    public async Task ProposePeopleChanges_PassesTheChangesToTheCreatorWithoutAProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        var personId = Guid.NewGuid();
        var changes = new object[]
        {
            new { action = "create", displayName = "王小明", email = "ming@example.com", role = "member" },
            new { action = "set_profile", person = personId, jobTitle = "後端工程師" },
        };

        // Act
        var (isError, body) = await mcp.CallAsync("propose_people_changes", new { changes, reason = "新同事到職" });

        // Assert
        var (actor, draft) = mcp.Modules.ActionProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeActionProposals.ProposalId));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("按「套用」").And.Contain("初始密碼"));
            Assert.That(actor.PersonId, Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That((draft.Kind, draft.ProjectKey, draft.Reason), Is.EqualTo((ActionKinds.People, (string?)null, "新同事到職")));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(draft.Payload.GetProperty("changes").GetArrayLength(), Is.EqualTo(2));
            Assert.That(draft.Payload.GetProperty("changes")[0].GetProperty("email").GetString(), Is.EqualTo("ming@example.com"));
        }
    }

    [Test]
    public async Task ProposePeopleChanges_PasswordInTheArguments_IsPassedOnSoTheHandlerCanRejectItButNeverEchoed()
    {
        // Arrange：處理器會擋下 password 屬性；這裡確認 MCP 這一層不把它吞掉、也不放進回應。
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        mcp.Modules.ActionProposals.OnCreate = _ => new Failure(FailureKind.Invalid, AiErrorCodes.FieldInvalid, "人員建議不合規定，沒有建立。")
        {
            Details = [new FailureDetail(AiErrorCodes.FieldInvalid, "changes[0].password", "不認得的屬性：password。")],
        };

        // Act
        var (isError, body) = await mcp.CallAsync("propose_people_changes", new
        {
            changes = new object[] { new { action = "create", displayName = "甲", email = "a@example.com", role = "member", password = "hunter2hunter2" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates.Single().Draft.Payload.GetRawText(), Does.Contain("password"));
            Assert.That(body.ToJsonString(), Does.Not.Contain("hunter2hunter2"));
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ForAnItem_TakesTheProjectFromTheItemKey()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_attachment_changes", new
        {
            changes = new object[]
            {
                new { action = "attach", attachmentId = "規格.pdf", itemKey = "WEB-12" },
                new { action = "delete", attachmentId = "舊圖.png", itemKey = "web-13" },
            },
            reason = "補上規格",
        });

        // Assert
        var draft = mcp.Modules.ActionProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeActionProposals.ProposalId));
            Assert.That((draft.Kind, draft.ProjectKey, draft.Reason), Is.EqualTo((ActionKinds.Attachments, (string?)"WEB", "補上規格")));
            Assert.That(draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(draft.Payload.GetProperty("changes")[1].GetProperty("attachmentId").GetString(), Is.EqualTo("舊圖.png"));
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ForAPage_HasNoProject()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_attachment_changes", new
        {
            changes = new object[] { new { action = "attach", attachmentId = "規格.pdf", spaceKey = "HR", pageNumber = 12 } },
        });

        // Assert
        var draft = mcp.Modules.ActionProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(draft.ProjectKey, Is.Null);
            Assert.That(draft.Payload.GetProperty("changes")[0].GetProperty("pageNumber").GetInt32(), Is.EqualTo(12));
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ItemOutsideTheTokensProject_IsNotFoundAndNeverReachesTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_attachment_changes", new
        {
            changes = new object[] { new { action = "attach", attachmentId = "規格.pdf", itemKey = "OPS-1" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("找不到項目"));
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ItemsFromTwoProjects_IsRefusedBeforeTheCreator()
    {
        // Arrange：不限專案的權杖（一般 OAuth 的那種不能呼叫它，這裡用代理的專案權杖以外的方式驗證形狀）。
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_attachment_changes", new
        {
            changes = new object[]
            {
                new { action = "attach", attachmentId = "a.pdf", itemKey = "WEB-1" },
                new { action = "attach", attachmentId = "a.pdf", itemKey = "OPS-1" },
            },
        });

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        });
    }

    [TestCase("""{}""")]
    [TestCase("""{"changes":"nope"}""")]
    [TestCase("""{"changes":[],"extra":1}""")]
    public async Task BadArguments_NeverReachTheCreator(string json)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var people = await mcp.CallAsync("propose_people_changes", JsonSerializer.Deserialize<JsonElement>(json));
        var attachments = await mcp.CallAsync("propose_attachment_changes", JsonSerializer.Deserialize<JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(people.IsError, Is.True);
            Assert.That(attachments.IsError, Is.True);
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_ItemKeyWithoutADash_IsAToolErrorBeforeTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_attachment_changes", new
        {
            changes = new object[] { new { action = "attach", attachmentId = "x", itemKey = "nodash" } },
        });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain("itemKey"));
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task ProposeAttachmentChanges_MoreThanTheLimit_IsRefusedBeforeTheCreator()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);
        var changes = Enumerable.Range(0, AiRules.MaxActionSteps + 1).Select(_ => new { action = "delete", attachmentId = "x", itemKey = "WEB-1" }).ToArray();

        // Act
        var (isError, body) = await mcp.CallAsync("propose_attachment_changes", new { changes });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]?.GetValue<string>(), Does.Contain(AiRules.MaxActionSteps.ToString()));
            Assert.That(mcp.Modules.ActionProposals.Creates, Is.Empty);
        }
    }
}
