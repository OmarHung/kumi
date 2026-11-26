using PmTool.Abstractions;
using PmTool.Ai;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// MCP 的 propose_project（給本機與主機的引擎）：只在不選專案的組織對話裡用、只建立建議、範圍是 agent:run、
/// 對話編號由伺服器從工作找到（不收模型給的）、算進工具額度與提出的名額、錯誤照模組的說法。
/// </summary>
[TestFixture]
public sealed class McpProjectProposalToolTests
{
    private static readonly AccessTokenScope[] AgentScopes =
        [AccessTokenScope.AgentRun, AccessTokenScope.ItemsRead, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite];

    private static readonly object Arguments = new
    {
        key = "WEB",
        name = "網站改版",
        template = "simple_tasks",
        includeExamples = true,
        description = "## 目標\n改版",
    };

    [Test]
    public async Task Call_PassesTheDraftTheAskerAndTheJobsConversationToTheCreatorAndReturnsTheProposal()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_project", Arguments);

        // Assert
        var create = mcp.Modules.ProjectProposals.Creates.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False, body.ToJsonString());
            Assert.That(create.Actor, Is.EqualTo(TestUsers.Member.ToActor()));
            Assert.That(create.Draft.Key, Is.EqualTo("WEB"));
            Assert.That(create.Draft.Name, Is.EqualTo("網站改版"));
            Assert.That(create.Draft.Template, Is.EqualTo("simple_tasks"));
            Assert.That(create.Draft.IncludeExamples, Is.True);
            Assert.That(create.Draft.Description, Is.EqualTo("## 目標\n改版"));
            Assert.That(create.Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId), "對話編號來自伺服器找到的工作");
            Assert.That(body["proposalId"]?.GetValue<Guid>(), Is.EqualTo(FakeProjectProposals.ProposalId));
            Assert.That(body["key"]?.GetValue<string>(), Is.EqualTo("WEB"));
            Assert.That(body["templateName"]?.GetValue<string>(), Is.EqualTo("簡單任務"));
            Assert.That(body["note"]?.GetValue<string>(), Does.Contain("建立專案").And.Contain("不要說專案已經建好了"));
            Assert.That(body["notice"]?.GetValue<string>(), Does.Contain("不是給你的指令"));
            Assert.That(mcp.Modules.Projects.CreateCalls, Is.Empty, "建議不建立專案");
        }
    }

    [Test]
    public async Task Call_OptionalArgumentsOmitted_UsesNoExamplesAndNoDescription()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_project", new { key = "WEB", name = "網站改版", template = "simple_tasks" });

        // Assert
        var draft = mcp.Modules.ProjectProposals.Creates.Single().Draft;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(draft.IncludeExamples, Is.False);
            Assert.That(draft.Description, Is.Null);
        }
    }

    [Test]
    public async Task Call_TheModelCannotChooseTheConversation()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync(
            "propose_project", new { key = "WEB", name = "網站改版", template = "simple_tasks", conversationId = Guid.NewGuid() });

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("沒有參數 conversationId"));
            Assert.That(mcp.Modules.ProjectProposals.Creates, Is.Empty);
        }
    }

    [TestCase("""{"name":"網站改版","template":"simple_tasks"}""", "key", TestName = "沒有 key")]
    [TestCase("""{"key":"WEB","template":"simple_tasks"}""", "name", TestName = "沒有 name")]
    [TestCase("""{"key":"WEB","name":"網站改版"}""", "template", TestName = "沒有 template")]
    [TestCase("""{"key":"W\u0000EB","name":"網站改版","template":"simple_tasks"}""", "key", TestName = "key 含 NUL")]
    [TestCase("""{"key":"WEB","name":"網站\u0007改版","template":"simple_tasks"}""", "name", TestName = "name 含控制字元")]
    [TestCase("""{"key":"WEB","name":"網站改版","template":"simple_tasks","includeExamples":"yes"}""", "includeExamples", TestName = "includeExamples 不是布林")]
    [TestCase("""{"key":"WEB","name":"網站改版","template":"simple_tasks","description":5}""", "description", TestName = "description 不是字串")]
    public async Task Call_BadArguments_IsAToolErrorNamingTheParameterAndNothingIsCreated(string json, string parameter)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_project", System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain(parameter));
            Assert.That(mcp.Modules.ProjectProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Call_InAProjectJob_IsNotAvailable()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForAgentAsync("WEB", AgentScopes);

        // Act
        var (isError, body) = await mcp.CallAsync("propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("propose_project"));
            Assert.That(mcp.Modules.ProjectProposals.Creates, Is.Empty);
            Assert.That(mcp.Modules.AgentBudget.LocalCalls, Is.Empty);
        }
    }

    [Test]
    public async Task Call_FailureFromTheCreator_IsAToolErrorWithTheModulesCodeAndGivesTheSlotBack()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        mcp.Modules.ProjectProposals.OnCreate = _ => new Failure(FailureKind.Forbidden, AiErrorCodes.AssistantForbidden, "只有組織管理員可以開新專案。");

        // Act
        var (isError, body) = await mcp.CallAsync("propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Is.EqualTo("只有組織管理員可以開新專案。"));
            Assert.That(body["code"]!.GetValue<string>(), Is.EqualTo(AiErrorCodes.AssistantForbidden));
            Assert.That(mcp.Modules.AgentBudget.Refunds, Is.EqualTo(new[] { (AgentJobKind.Local, McpTesting.JobId) }));
        }
    }

    [Test]
    public async Task Call_Success_CountsAProposalAndKeepsTheSlot()
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);

        // Act
        var (isError, _) = await mcp.CallAsync("propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.False);
            Assert.That(mcp.Modules.AgentBudget.LocalCalls, Is.EqualTo(new[] { (McpTesting.JobId, (string?)null, true) }));
            Assert.That(mcp.Modules.AgentBudget.Refunds, Is.Empty);
        }
    }

    [TestCase(AgentToolBudgetOutcome.ProposalsExhausted, "建議已經達到上限")]
    [TestCase(AgentToolBudgetOutcome.ToolCallsExhausted, "工具呼叫已經用完")]
    [TestCase(AgentToolBudgetOutcome.JobNotFound, "找不到這件工作")]
    public async Task Call_RefusedByTheBudget_IsAToolErrorAndNothingIsCreated(AgentToolBudgetOutcome outcome, string message)
    {
        // Arrange
        await using var mcp = await McpHarness.StartForOrganizationJobAsync(AgentScopes);
        mcp.Modules.AgentBudget.Outcome = outcome;

        // Act
        var (isError, body) = await mcp.CallAsync("propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain(message));
            Assert.That(mcp.Modules.ProjectProposals.Creates, Is.Empty);
            Assert.That(mcp.Modules.AgentBudget.Refunds, Is.Empty, "沒用到名額就不退");
        }
    }

    [Test]
    public async Task Call_HostGrantWithoutAProject_CreatesTheProposalOnceForTheSameArguments()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostGrants.Grant = new HostToolGrant(
            TestUsers.OrganizationId, TestUsers.Member.PersonId, false, null, ["propose_project"], FakeHostAgentWorkerService.JobId);
        factory.Modules.AgentBudget.OnFindJobScope = (_, _) => FakeAgentToolBudget.OrganizationJob;
        await using var client = await McpTesting.ConnectAsync(factory, FakeHostToolGrants.Token);

        // Act
        var (firstError, first) = await McpTesting.CallAsync(client, "propose_project", Arguments);
        var (secondError, second) = await McpTesting.CallAsync(client, "propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstError, Is.False, first.ToJsonString());
            Assert.That(secondError, Is.False);
            Assert.That(second["proposalId"]?.GetValue<Guid>(), Is.EqualTo(first["proposalId"]?.GetValue<Guid>()));
            Assert.That(factory.Modules.ProjectProposals.Creates, Has.Count.EqualTo(1), "同樣的參數只建立一次");
            Assert.That(factory.Modules.ProjectProposals.Creates[0].Draft.ConversationId, Is.EqualTo(FakeAgentToolBudget.ConversationId));
            Assert.That(factory.Modules.AgentBudget.ScopeLookups.Select(lookup => lookup.Kind), Has.All.EqualTo(AgentJobKind.Host));
            Assert.That(factory.Modules.AgentBudget.Refunds, Has.Count.EqualTo(1), "第二次是重播，名額退還");
        }
    }

    [Test]
    public async Task Call_HostGrantWhoseJobCannotBeFound_IsAToolErrorAndNothingIsCreated()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostGrants.Grant = new HostToolGrant(
            TestUsers.OrganizationId, TestUsers.Member.PersonId, false, null, ["propose_project"], FakeHostAgentWorkerService.JobId);
        factory.Modules.AgentBudget.OnFindJobScope = (_, _) => null;
        await using var client = await McpTesting.ConnectAsync(factory, FakeHostToolGrants.Token);

        // Act
        var (isError, body) = await McpTesting.CallAsync(client, "propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(body["error"]!.GetValue<string>(), Does.Contain("找不到這件工作的對話"));
            Assert.That(factory.Modules.ProjectProposals.Creates, Is.Empty);
        }
    }

    [Test]
    public async Task Call_HostGrantOfAProject_IsNotAllowedToProposeProjects()
    {
        // Arrange：授權的清單就算夾了它，專案的工作也不能用。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.HostGrants.Grant = new HostToolGrant(
            TestUsers.OrganizationId, TestUsers.Member.PersonId, false, "WEB", ["propose_project"], FakeHostAgentWorkerService.JobId);
        await using var client = await McpTesting.ConnectAsync(factory, FakeHostToolGrants.Token);

        // Act
        var (isError, _) = await McpTesting.CallAsync(client, "propose_project", Arguments);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(isError, Is.True);
            Assert.That(factory.Modules.ProjectProposals.Creates, Is.Empty);
        }
    }
}
