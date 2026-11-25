using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class EngineSpecTests
{
    private const string Server = "https://pm.example.com";
    private const string WorkDirectory = "/tmp/kumi-agent-work";
    private static readonly Guid JobId = Guid.Parse("3f0c6a52-8a53-4a53-9d2e-0a7f4a1f9c11");

    private static string[] Args(LaunchPlan plan) => [.. plan.Arguments];

    private static string ValueAfter(string[] args, string flag) => args[Array.IndexOf(args, flag) + 1];

    [Test]
    public void Claude_Plan_LocksDownToKumiMcpToolsOnly()
    {
        // Act
        var plan = new ClaudeCodeSpec().Plan(TestJobs.Make(), WorkDirectory);

        // Assert
        var args = Args(plan);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args[0], Is.EqualTo("-p"));
            Assert.That(args, Does.Contain("--strict-mcp-config").And.Contain("--no-session-persistence").And.Contain("--disable-slash-commands"));
            Assert.That(ValueAfter(args, "--tools"), Is.Empty, "關掉所有內建工具");
            Assert.That(ValueAfter(args, "--allowedTools"), Is.EqualTo("mcp__kumi__get_blueprint,mcp__kumi__propose_changeset"));
            Assert.That(ValueAfter(args, "--permission-mode"), Is.EqualTo("dontAsk"));
            Assert.That(ValueAfter(args, "--setting-sources"), Is.EqualTo("project"));
            Assert.That(ValueAfter(args, "--mcp-config"), Is.EqualTo($"{WorkDirectory}/mcp.json"));
            Assert.That(ValueAfter(args, "--append-system-prompt-file"), Is.EqualTo($"{WorkDirectory}/system.md"));
            Assert.That(ValueAfter(args, "--max-turns"), Is.EqualTo("9"));
            Assert.That(args, Does.Not.Contain("--safe-mode").And.Not.Contain("--model").And.Not.Contain("--effort"));
            Assert.That(plan.StandardInput, Is.EqualTo("使用者：列出狀態"));
            Assert.That(plan.Files["system.md"], Is.EqualTo("系統提示：只讀藍圖。"));
        }
    }

    [Test]
    public void Claude_Plan_PassesModelAndEffortWhenGiven()
    {
        // Act
        var args = Args(new ClaudeCodeSpec().Plan(TestJobs.Make(model: "opus", effort: "max"), WorkDirectory));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ValueAfter(args, "--model"), Is.EqualTo("opus"));
            Assert.That(ValueAfter(args, "--effort"), Is.EqualTo("max"));
        }
    }

    [Test]
    public void Claude_McpConfig_ReferencesTheTokenVariableInsteadOfEmbeddingIt()
    {
        // Act
        var job = TestJobs.Make(id: JobId);
        var config = new ClaudeCodeSpec().Plan(job, WorkDirectory).Files["mcp.json"];

        // Assert
        Assert.That(config, Is.EqualTo("""{"mcpServers":{"kumi":{"type":"http","url":"https://pm.example.com/mcp","headers":{"Authorization":"Bearer ${KUMI_MCP_TOKEN}","X-Kumi-Project":"DEMO","X-Kumi-Job":"3f0c6a52-8a53-4a53-9d2e-0a7f4a1f9c11"}}}}"""));
    }

    [Test]
    public void Claude_Models_AreTheFixedListWithHaikuLimitedToThreeEfforts()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ClaudeCodeSpec.Models.Select(model => model.Id), Is.EqualTo(new[] { "default", "opus", "sonnet", "haiku" }));
            Assert.That(ClaudeCodeSpec.Models[3].Efforts, Is.EqualTo(new[] { "low", "medium", "high" }));
            Assert.That(ClaudeCodeSpec.Models[1].Efforts, Is.EqualTo(new[] { "low", "medium", "high", "xhigh", "max" }));
        }
    }

    [Test]
    public void Codex_Plan_UsesTheValidatedFlagSetAndOnlyTheListedDisables()
    {
        // Arrange
        var spec = new CodexSpec(["shell_tool", "view_image"]);

        // Act
        var plan = spec.Plan(TestJobs.Make(EngineKinds.Codex, id: JobId), WorkDirectory);

        // Assert
        var args = Args(plan);
        var configs = args.Where((_, index) => index > 0 && args[index - 1] == "-c").ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Take(9), Is.EqualTo(new[] { "exec", "--json", "--strict-config", "--skip-git-repo-check", "--ignore-user-config", "--ignore-rules", "--ephemeral", "-s", "read-only" }));
            Assert.That(ValueAfter(args, "-C"), Is.EqualTo(WorkDirectory));
            Assert.That(args.Where((_, index) => index > 0 && args[index - 1] == "--disable"), Is.EqualTo(new[] { "shell_tool", "view_image" }));
            Assert.That(configs, Does.Contain("agents.enabled=false").And.Contain("web_search=\"disabled\"").And.Contain("project_doc_max_bytes=0")
                .And.Contain("mcp_servers.kumi.url=\"https://pm.example.com/mcp\"")
                .And.Contain("mcp_servers.kumi.bearer_token_env_var=\"KUMI_MCP_TOKEN\"")
                .And.Contain("mcp_servers.kumi.http_headers={ \"X-Kumi-Project\" = \"DEMO\", \"X-Kumi-Job\" = \"3f0c6a52-8a53-4a53-9d2e-0a7f4a1f9c11\" }")
                .And.Contain("mcp_servers.kumi.required=true")
                .And.Contain("mcp_servers.kumi.enabled_tools=[\"get_blueprint\",\"propose_changeset\"]")
                .And.Contain("mcp_servers.kumi.default_tools_approval_mode=\"auto\""));
            Assert.That(args[^1], Is.EqualTo("-"), "提示從標準輸入來");
            Assert.That(plan.StandardInput, Is.EqualTo("使用者：列出狀態"));
            Assert.That(args, Does.Not.Contain("-m"));
        }
    }

    [Test]
    public void Codex_Plan_DeliversTheSystemPromptThroughDeveloperInstructionsAsATomlString()
    {
        // Arrange
        var job = TestJobs.Make(EngineKinds.Codex, model: "gpt-6.1-sol", effort: "xhigh") with { SystemPrompt = "第一行\n他說「\"好\"」\\" };

        // Act
        var args = Args(new CodexSpec([]).Plan(job, WorkDirectory));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("developer_instructions=\"第一行\\n他說「\\\"好\\\"」\\\\\""));
            Assert.That(args, Does.Contain("model_reasoning_effort=\"xhigh\""));
            Assert.That(ValueAfter(args, "-m"), Is.EqualTo("gpt-6.1-sol"));
        }
    }

    [Test]
    public void Toml_KeepsChineseReadableAndDropsDel()
    {
        // Assert
        Assert.That(CodexSpec.Toml("中文\u007f"), Is.EqualTo("\"中文\""));
    }

    [Test]
    public void Validation_AcceptsRealisticJobs()
    {
        // Assert
        Assert.That(JobValidation.Check(TestJobs.Make(EngineKinds.Codex, model: "gpt-6.1-sol", effort: "max"), Server), Is.Null);
        Assert.That(JobValidation.Check(TestJobs.Make(model: "claude-opus-4-1[1m]"), Server), Is.Null);
    }

    [TestCase(0)]
    [TestCase(5000)]
    public void Validation_RejectsAMaxTurnsOutsideTheSaneRange(int turns)
    {
        // Assert
        Assert.That(JobValidation.Check(TestJobs.Make() with { MaxTurns = turns }, Server), Is.EqualTo("來回上限不正確"));
    }

    [Test]
    public void Validation_RejectsNoToolsAndUrlsWithCredentials()
    {
        // Assert
        Assert.That(JobValidation.Check(TestJobs.Make() with { AllowedTools = [] }, Server), Is.EqualTo("允許的工具不正確"));
        Assert.That(JobValidation.Check(TestJobs.Make() with { McpUrl = "https://user:pw@pm.example.com/mcp" }, Server), Is.EqualTo("MCP 網址不正確"));
    }

    [Test]
    public void Job_Parse_ReadsTheWireShapeAndRejectsMissingFields()
    {
        // Arrange
        var job = TestJobs.Make(model: "sonnet", effort: "low");
        using var good = System.Text.Json.JsonDocument.Parse(TestJobs.ToWire(job).ToJsonString());
        using var bad = System.Text.Json.JsonDocument.Parse("""{"jobId":"not-a-guid"}""");

        // Assert
        Assert.That(AgentJob.Parse(good.RootElement), Is.EqualTo(job with { AllowedTools = job.AllowedTools }).Using<AgentJob>(
            (left, right) => left.JobId == right.JobId && left.Model == right.Model && left.Effort == right.Effort
                && left.AllowedTools.SequenceEqual(right.AllowedTools) && left.MaxTurns == right.MaxTurns && left.McpUrl == right.McpUrl));
        Assert.That(AgentJob.Parse(bad.RootElement), Is.Null);
    }

    [TestCase("https://pm.example.com/mcp", true)]
    [TestCase("HTTPS://PM.EXAMPLE.COM:443/mcp", true)]
    [TestCase("https://pm.example.com/mcp/", false)]
    [TestCase("https://evil.example.com/mcp", false)]
    [TestCase("http://pm.example.com/mcp", false)]
    [TestCase("https://pm.example.com:8443/mcp", false)]
    [TestCase("https://pm.example.com/other", false)]
    [TestCase("https://pm.example.com/mcp?x=1", false)]
    [TestCase("https://pm.example.com/mcp#f", false)]
    [TestCase("https://pm.example.com/${HOME}/mcp", false)]
    [TestCase("https://pm.example.com/mcp\"", false)]
    [TestCase("https://pm.example.com/m cp", false)]
    [TestCase("https://pm.example.com/mcp\n", false)]
    [TestCase("https://pm.example.com.evil.example/mcp", false)]
    [TestCase("https://user:pw@pm.example.com/mcp", false)]
    [TestCase("https://pm.example.com%2f@evil/mcp", false)]
    public void McpUrl_MustBeTheLoggedInServersOwnMcpEndpoint(string url, bool accepted)
    {
        // Act
        var normalized = JobValidation.NormalizeMcpUrl(url, Server);

        // Assert
        Assert.That(normalized is not null, Is.EqualTo(accepted));
        if (accepted)
        {
            Assert.That(normalized, Is.EqualTo("https://pm.example.com/mcp"));
        }
    }

    [Test]
    public void McpUrl_FollowsTheServersBasePath()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JobValidation.NormalizeMcpUrl("https://pm.example.com/kumi/mcp", "https://pm.example.com/kumi/"), Is.EqualTo("https://pm.example.com/kumi/mcp"));
            Assert.That(JobValidation.NormalizeMcpUrl("https://pm.example.com/mcp", "https://pm.example.com/kumi"), Is.Null);
        }
    }

    [Test]
    public void KnownTools_IncludeTheItemToolsButNotAnyWriteTool()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JobValidation.KnownTools, Is.SupersetOf(new[] { "get_item", "search_items", "propose_item_changes" }));
            Assert.That(JobValidation.Check(TestJobs.Make() with { AllowedTools = ["get_item", "search_items", "propose_item_changes"] }, Server), Is.Null);
            Assert.That(JobValidation.Check(TestJobs.Make() with { AllowedTools = ["get_item", "update_item"] }, Server), Is.EqualTo("允許的工具不正確"));
        }
    }

    [TestCase("DEMO", true)]
    [TestCase("A", true)]
    [TestCase("ABCDEFGHIJ0123456789", true)]
    [TestCase("", false)]
    [TestCase("demo", false)]
    [TestCase("1ABC", false)]
    [TestCase("ABCDEFGHIJ01234567890", false)]
    [TestCase("DEMO\n", false)]
    [TestCase("DEMO\"}", false)]
    [TestCase("A-B", false)]
    [TestCase("DEMO\r\nX-Evil: 1", false)]
    public void ProjectKey_MustBeAStrictProjectCodeBecauseItGoesIntoAHeader(string key, bool valid)
    {
        // Act
        var result = JobValidation.Check(TestJobs.Make() with { ProjectKey = key }, Server);

        // Assert
        Assert.That(result, valid ? Is.Null : Is.EqualTo("專案代號不正確"));
    }

    [Test]
    public void AllowedTools_MustBeASubsetOfTheClientsOwnList()
    {
        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JobValidation.Check(TestJobs.Make() with { AllowedTools = [.. JobValidation.KnownTools] }, Server), Is.Null);
            Assert.That(JobValidation.Check(TestJobs.Make() with { AllowedTools = ["get_blueprint", "create_item"] }, Server), Is.EqualTo("允許的工具不正確"));
        }
    }

    [Test]
    public void ModelPattern_RejectsATrailingNewline()
    {
        // Assert
        Assert.That(JobValidation.Check(TestJobs.Make(model: "sonnet\n"), Server), Is.EqualTo("模型代號不正確"));
    }
}
