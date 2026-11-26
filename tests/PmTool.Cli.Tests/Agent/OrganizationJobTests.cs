using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>不選專案的組織對話：<c>projectKey</c> 是 null，MCP 不帶專案標頭，多兩個工具與專案建議的編號。</summary>
[TestFixture]
public sealed class OrganizationJobTests
{
    private const string Server = "https://pm.example.com";
    private const string ProposalId = "7c1e5a90-2b3d-4f68-9a01-5d8e6f7a8b92";
    private static readonly string[] OrganizationTools = ["list_templates", "get_template", "list_projects", "propose_project"];

    private static readonly Guid JobId = Guid.Parse("3f0c6a52-8a53-4a53-9d2e-0a7f4a1f9c11");
    private static readonly string WorkDirectory = Path.Combine(Path.GetTempPath(), "kumi-org-job-work");

    private static AgentJob OrganizationJob(string engine = EngineKinds.ClaudeCode) =>
        TestJobs.Make(engine, id: JobId) with { ProjectKey = null, AllowedTools = OrganizationTools };

    private static AgentJob? Parse(JsonObject wire)
    {
        using var document = JsonDocument.Parse(wire.ToJsonString());
        return AgentJob.Parse(document.RootElement);
    }

    // 解析

    [Test]
    public void Parse_WithANullProjectKey_IsAnOrganizationJob()
    {
        // Arrange
        var wire = TestJobs.ToWire(OrganizationJob());

        // Act
        var job = Parse(wire);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.ProjectKey, Is.Null);
            Assert.That(job.AllowedTools, Is.EqualTo(OrganizationTools));
        }
    }

    [Test]
    public void Parse_WithoutTheProjectKeyField_IsAnOrganizationJob()
    {
        // Arrange
        var wire = TestJobs.ToWire(OrganizationJob());
        wire.Remove("projectKey");

        // Act
        var job = Parse(wire);

        // Assert
        Assert.That(job?.ProjectKey, Is.Null);
        Assert.That(job, Is.Not.Null);
    }

    [Test]
    public void Parse_WithAProjectKey_KeepsIt()
    {
        // Act
        var job = Parse(TestJobs.ToWire(TestJobs.Make()));

        // Assert
        Assert.That(job!.ProjectKey, Is.EqualTo("DEMO"));
    }

    [Test]
    public void Parse_WithAProjectKeyThatIsNotAString_IsSkippedInsteadOfBecomingAnOrganizationJob()
    {
        // Arrange：寫錯型別的欄位不能被當成「沒有專案」而放寬標頭。
        var wire = TestJobs.ToWire(TestJobs.Make());
        wire["projectKey"] = 12;

        // Assert
        Assert.That(Parse(wire), Is.Null);
    }

    // 驗證

    [Test]
    public void Check_OrganizationJob_PassesWithoutAProjectKey()
    {
        // Assert
        Assert.That(JobValidation.Check(OrganizationJob(), Server), Is.Null);
    }

    [Test]
    public void Check_WithAProjectKeyThatIsBroken_StillRefuses()
    {
        // Assert
        Assert.That(JobValidation.Check(OrganizationJob() with { ProjectKey = "demo\r\n" }, Server), Is.EqualTo("專案代號不正確"));
    }

    [Test]
    public void KnownTools_IncludeTheOrganizationToolsAndStillRefuseUnknownOnes()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JobValidation.KnownTools, Is.SupersetOf(OrganizationTools));
            Assert.That(JobValidation.Check(OrganizationJob() with { AllowedTools = ["list_projects", "create_project"] }, Server), Is.EqualTo("允許的工具不正確"));
        }
    }

    // MCP 設定

    [Test]
    public void Claude_McpConfig_WithoutAProject_OmitsTheProjectHeaderButKeepsTheJobHeader()
    {
        // Act
        var plan = new ClaudeCodeSpec().Plan(OrganizationJob(), WorkDirectory);

        // Assert
        var headers = JsonNode.Parse(plan.Files["mcp.json"])!["mcpServers"]!["kumi"]!["headers"]!.AsObject();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(headers.ContainsKey("X-Kumi-Project"), Is.False);
            Assert.That(headers["X-Kumi-Job"]!.GetValue<string>(), Is.EqualTo(JobId.ToString()));
            Assert.That(headers["Authorization"]!.GetValue<string>(), Is.EqualTo("Bearer ${KUMI_MCP_TOKEN}"));
        }
    }

    [Test]
    public void Codex_Plan_WithoutAProject_HasNoProjectHeaderNorVariable()
    {
        // Act
        var plan = new CodexSpec([]).Plan(OrganizationJob(EngineKinds.Codex), WorkDirectory);

        // Assert
        var configs = plan.Arguments.Where((_, index) => index > 0 && plan.Arguments[index - 1] == "-c").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(configs, Does.Contain("mcp_servers.kumi.env_http_headers={ \"X-Kumi-Job\" = \"KUMI_MCP_JOB\" }"));
            Assert.That(string.Join('\n', plan.Arguments), Does.Not.Contain("X-Kumi-Project"));
            Assert.That(plan.Variables, Is.EqualTo(new Dictionary<string, string> { ["KUMI_MCP_JOB"] = JobId.ToString() }));
            Assert.That(configs, Does.Contain("mcp_servers.kumi.enabled_tools=[\"list_templates\",\"get_template\",\"list_projects\",\"propose_project\"]"));
        }
    }

    [TestCase(EngineKinds.ClaudeCode)]
    [TestCase(EngineKinds.Codex)]
    public void Plan_WithAnEmptyJobId_StillRefusesForAnOrganizationJob(string engine)
    {
        // Arrange
        var job = OrganizationJob(engine) with { JobId = Guid.Empty };

        // Assert
        Assert.That(() => engine == EngineKinds.Codex ? new CodexSpec([]).Plan(job, WorkDirectory) : new ClaudeCodeSpec().Plan(job, WorkDirectory), Throws.ArgumentException);
    }

    // 翻譯器

    [Test]
    public void ProposeProject_Claude_ReportsTheProposalId()
    {
        // Act
        var tools = TranslatorRun.Play(new ClaudeCodeTranslator(), "claude-project-proposal.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools.Select(tool => (tool.Name, tool.Status)), Is.EqualTo(new[] { ("propose_project", "running"), ("propose_project", "done") }));
            Assert.That(tools[0].ProjectProposalId, Is.Null);
            Assert.That(tools[1].ProjectProposalId, Is.EqualTo(ProposalId));
            Assert.That(tools[1].ItemProposalId, Is.Null);
        }
    }

    [Test]
    public void ProposeProject_Codex_ReportsTheProposalId()
    {
        // Act
        var tools = TranslatorRun.Play(new CodexTranslator(), "codex-project-proposal.jsonl").Tools;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tools[0].ProjectProposalId, Is.Null);
            Assert.That(tools[1].ProjectProposalId, Is.EqualTo(ProposalId));
        }
    }

    [TestCase("claude-project-proposal-bad.jsonl")]
    [TestCase("codex-project-proposal-bad.jsonl")]
    public void ProposeProject_WithAnIdThatIsNotAGuid_OmitsIt(string fixture)
    {
        // Arrange
        IEngineTranslator translator = fixture.StartsWith("codex", StringComparison.Ordinal) ? new CodexTranslator() : new ClaudeCodeTranslator();

        // Act
        var done = TranslatorRun.Play(translator, fixture).Tools.Last();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(done.Status, Is.EqualTo("done"));
            Assert.That(done.ProjectProposalId, Is.Null);
        }
    }

    [Test]
    public void ToolEvent_SerializesTheNewItemProposalId()
    {
        // Act
        var json = new ToolEvent("c1", "propose_new_items", "done", NewItemProposalId: ProposalId).ToJson();

        // Assert
        Assert.That(json["newItemProposalId"]!.GetValue<string>(), Is.EqualTo(ProposalId));
    }

    [Test]
    public void ToolEvent_SerializesTheMemberProposalId()
    {
        // Act
        var json = new ToolEvent("c1", "propose_member_changes", "done", MemberProposalId: ProposalId).ToJson();

        // Assert
        Assert.That(json["memberProposalId"]!.GetValue<string>(), Is.EqualTo(ProposalId));
    }

    [Test]
    public void ToolEvent_SerializesTheProjectProposalId()
    {
        // Act
        var json = new ToolEvent("c1", "propose_project", "done", ProjectProposalId: ProposalId).ToJson();

        // Assert
        Assert.That(json["projectProposalId"]!.GetValue<string>(), Is.EqualTo(ProposalId));
    }

    // 主控台

    [Test]
    public void Console_ShowsNoProjectForAnOrganizationJob()
    {
        // Arrange
        var output = new StringWriter();
        var console = new AgentConsole(output, new StringWriter(), json: false);

        // Act
        console.JobFinished(new JobSummary(OrganizationJob(), new JobOutcome(JobStatus.Complete), TimeSpan.FromSeconds(1), DateTimeOffset.Now));

        // Assert
        Assert.That(output.ToString(), Does.Contain("不選專案"));
    }

    [Test]
    public void Console_Json_UsesNullForTheProjectOfAnOrganizationJob()
    {
        // Arrange
        var output = new StringWriter();
        var console = new AgentConsole(output, new StringWriter(), json: true);

        // Act
        console.JobFinished(new JobSummary(OrganizationJob(), new JobOutcome(JobStatus.Complete), TimeSpan.FromSeconds(1), DateTimeOffset.Now));

        // Assert
        Assert.That(JsonNode.Parse(output.ToString())!["project"], Is.Null);
    }

    // 完整跑一次

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task Run_OrganizationJobThroughAFakeClaude_WritesNoProjectHeaderAndReportsTheProposal()
    {
        // Arrange
        using var scripts = new FakeEngineScripts();
        var executable = scripts.Recording("claude", "claude-project-proposal.jsonl");
        var quick = new AgentOptions { InterruptGrace = TimeSpan.FromMilliseconds(300), ExitGrace = TimeSpan.FromMilliseconds(300), DrainTimeout = TimeSpan.FromMilliseconds(500) };
        var engine = new ProcessEngine(new ClaudeCodeSpec(), executable, "2.1.330", ClaudeCodeSpec.Models, "pmt_mcp_token_for_tests", Server, quick);
        var sink = new ListSink();

        // Act
        var outcome = await engine.RunAsync(OrganizationJob(), sink, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(scripts.Read("mcp.json"), Does.Not.Contain("X-Kumi-Project").And.Contain("X-Kumi-Job"));
            Assert.That(sink.Events.OfType<ToolEvent>().Last().ProjectProposalId, Is.EqualTo(ProposalId));
        }
    }
}
