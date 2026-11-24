using System.Diagnostics;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class ProcessEngineTests
{
    private const string McpToken = "pmt_mcp_token_for_tests";
    private const string Server = "https://pm.example.com";
    private static readonly string[] SecretVariables =
        ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "OPENAI_API_KEY", "CODEX_API_KEY", "CODEX_ACCESS_TOKEN", "KUMI_TOKEN", "PM_TOKEN", "CLAUDE_CODE_USE_BEDROCK",
         "ANTHROPIC_FOUNDRY_API_KEY", "AWS_BEARER_TOKEN_BEDROCK", "ANTHROPIC_BEDROCK_BASE_URL", "ANTHROPIC_VERTEX_BASE_URL", "ANTHROPIC_CUSTOM_HEADERS", "OPENAI_BASE_URL"];

    private FakeEngineScripts _scripts = null!;
    private readonly Dictionary<string, string?> _savedEnvironment = [];

    private static AgentOptions Quick => new()
    {
        InterruptGrace = TimeSpan.FromMilliseconds(300),
        ExitGrace = TimeSpan.FromMilliseconds(300),
        DrainTimeout = TimeSpan.FromMilliseconds(500),
    };

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        foreach (var name in SecretVariables)
        {
            _savedEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, $"secret-of-{name}");
        }
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var (name, value) in _savedEnvironment)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        _scripts.Dispose();
    }

    private static ProcessEngine ClaudeEngine(string executable, AgentOptions? options = null) =>
        new(new ClaudeCodeSpec(), executable, "2.1.330", ClaudeCodeSpec.Models, McpToken, Server, options ?? Quick);

    private static ProcessEngine CodexEngine(string executable, AgentOptions? options = null) =>
        new(new CodexSpec(["shell_tool", "view_image"]), executable, "0.163.0", [new(EngineModel.DefaultId, "預設", [])], McpToken, Server, options ?? Quick);

    [Test]
    public async Task Run_ForClaude_SendsThePromptOnStandardInputNotOnTheCommandLine()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));
        var job = TestJobs.Make(model: "sonnet", effort: "high");

        // Act
        var outcome = await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        var args = _scripts.Read("args.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(_scripts.Read("stdin.txt"), Is.EqualTo(job.Prompt));
            Assert.That(string.Join(' ', args), Does.Not.Contain(job.Prompt).And.Not.Contain(McpToken));
            Assert.That(args, Does.Contain("--model").And.Contain("sonnet").And.Contain("--effort").And.Contain("high"));
        }
    }

    [Test]
    public async Task Run_StripsApiKeysAndKumiTokensFromTheEnginesEnvironmentAndAddsTheMcpToken()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));

        // Act
        await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        var environment = _scripts.Read("env.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Split('=')[0]).ToHashSet();
        using (Assert.EnterMultipleScope())
        {
            foreach (var name in SecretVariables)
            {
                Assert.That(environment, Does.Not.Contain(name), name);
            }

            Assert.That(_scripts.Read("env.txt"), Does.Contain($"KUMI_MCP_TOKEN={McpToken}"));
            Assert.That(environment, Does.Contain("PATH"), "其他的環境變數照舊，引擎才找得到它要的東西");
        }
    }

    [Test]
    public async Task Run_UsesAFreshPrivateDirectoryWithOnlyOurFilesAndDeletesItAfterwards()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));
        var job = TestJobs.Make();

        // Act
        await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        var directory = _scripts.Read("cwd.txt").Trim();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scripts.Read("cwd-mode.txt").Trim(), Is.EqualTo("drwx------"));
            Assert.That(_scripts.Read("cwd-files.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[] { "mcp.json", "system.md" }));
            Assert.That(_scripts.Read("system.md"), Is.EqualTo(job.SystemPrompt));
            Assert.That(Directory.Exists(directory), Is.False, "結束就刪");
        }
    }

    [Test]
    public async Task Run_WritesTheMcpConfigWithAPlaceholderInsteadOfTheToken()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));

        // Act
        await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        var config = _scripts.Read("mcp.json");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(config, Does.Contain("${KUMI_MCP_TOKEN}").And.Contain("https://pm.example.com/mcp"));
            Assert.That(config, Does.Not.Contain(McpToken));
        }
    }

    [Test]
    public async Task Run_ForwardsEventsToTheSinkAndReturnsTheEnginesOutcome()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));
        var sink = new ListSink();

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), sink, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(string.Concat(sink.Events.OfType<TextEvent>().Select(text => text.Delta)), Does.EndWith("已提出變更集 #12。"));
            Assert.That(sink.Events.OfType<ToolEvent>().Last().Name, Is.EqualTo("propose_changeset"));
            Assert.That(outcome, Is.EqualTo(new JobOutcome(JobStatus.Complete, null, 160, 55)));
        }
    }

    [Test]
    public async Task Run_WhenTheEngineKeepsRunningAfterItsResult_KillsItAfterTheGracePeriod()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl", afterOutput: "sleep 300"));
        var timer = Stopwatch.StartNew();

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
        }
    }

    [Test]
    public async Task Run_WhenInitShowsBuiltInTools_KillsTheEngineAtOnceAndForwardsNothingElse()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-builtin-tools.jsonl", afterOutput: "sleep 300"));
        var sink = new ListSink();
        var timer = Stopwatch.StartNew();

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), sink, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.Contain("沒有照預期關掉內建工具"));
            Assert.That(sink.Events, Is.Empty);
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }
    }

    [Test]
    public async Task Cancel_InterruptsFirstAndAnEngineThatExitsOnInterruptEndsAsStopped()
    {
        // Arrange
        var script = _scripts.Script("claude", $"trap 'exit 0' INT\ncat > /dev/null\necho started > \"{_scripts.Out}/started\"\nsleep 300 &\nwait");
        var engine = ClaudeEngine(script, Quick with { InterruptGrace = TimeSpan.FromSeconds(10) });
        using var cancel = new CancellationTokenSource();
        var run = engine.RunAsync(TestJobs.Make(), new ListSink(), cancel.Token);
        await WaitForFileAsync("started");
        var timer = Stopwatch.StartNew();

        // Act
        await cancel.CancelAsync();
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(JobOutcome.Stopped()));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "中斷之後馬上結束，不必等強制結束");
        }
    }

    [Test]
    public async Task Cancel_KillsTheWholeProcessTreeWhenTheEngineIgnoresTheInterrupt()
    {
        // Arrange
        var script = _scripts.Script("claude", $"trap '' INT\ncat > /dev/null\nsleep 300 &\necho $! > \"{_scripts.Out}/child.pid\"\nwait");
        var engine = ClaudeEngine(script);
        using var cancel = new CancellationTokenSource();
        var run = engine.RunAsync(TestJobs.Make(), new ListSink(), cancel.Token);
        await WaitForFileAsync("child.pid");
        var child = int.Parse(_scripts.Read("child.pid").Trim());
        Assert.That(FakeEngineScripts.IsAlive(child), Is.True, "前提：孫程序還活著");

        // Act
        await cancel.CancelAsync();
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(20));

        // Assert
        await WaitUntilDeadAsync(child);
        Assert.That(outcome, Is.EqualTo(JobOutcome.Stopped()));
    }

    [Test]
    public async Task Timeout_KillsTheEngineAndFails()
    {
        // Arrange
        var script = _scripts.Script("claude", "cat > /dev/null\nsleep 300");
        var engine = ClaudeEngine(script, Quick with { EngineTimeout = TimeSpan.FromMilliseconds(600) });

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(JobOutcome.Failed("本機的 Claude Code 超過 1 秒沒有完成，已經停下。")));
    }

    [Test]
    public async Task Run_WhenTheEngineDiesWithoutEvents_FailsWithTheFirstUsefulStandardErrorLine()
    {
        // Arrange
        var script = _scripts.Script("claude", "cat > /dev/null\necho 'Error: 壞掉了 at /Users/someone/x' >&2\nexit 3");
        var engine = ClaudeEngine(script);

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.Contain("代碼 3").And.Contain("壞掉了").And.Not.Contain("/Users/someone"));
        }
    }

    [Test]
    public async Task Run_WhenTheExecutableCannotStart_Fails()
    {
        // Arrange
        var engine = ClaudeEngine(Path.Combine(_scripts.Directory, "missing"));

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        Assert.That(outcome.Error, Does.Contain("沒辦法啟動 Claude Code"));
    }

    [TestCase("--evil", "low", "get_blueprint", "https://pm.example.com/mcp", "模型代號不正確")]
    [TestCase("default", "low", "get_blueprint;rm", "https://pm.example.com/mcp", "允許的工具不正確")]
    [TestCase("default", "low", "get_blueprint", "file:///etc/passwd", "MCP 網址不正確")]
    [TestCase("default", "turbo", "get_blueprint", "https://pm.example.com/mcp", "思考深度不正確")]
    public async Task Run_RejectsJobFieldsThatCouldInjectCommandLineOptions(string model, string effort, string tool, string url, string reason)
    {
        // Arrange
        var script = _scripts.Recording("claude", null);
        var engine = ClaudeEngine(script);
        var job = TestJobs.Make(model: model, effort: effort) with { AllowedTools = [tool], McpUrl = url };

        // Act
        var outcome = await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.Contain(reason));
            Assert.That(_scripts.Exists("args.txt"), Is.False, "不合格的工作連引擎都不啟動");
        }
    }

    [Test]
    public async Task Run_ForCodex_ClosesStandardInputAfterThePromptAndUsesTheConfiguredFlags()
    {
        // Arrange
        var engine = CodexEngine(_scripts.Recording("codex", "codex-success.jsonl"));
        var job = TestJobs.Make(EngineKinds.Codex, model: "gpt-6.1-sol", effort: "low");

        // Act
        var outcome = await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        var args = _scripts.Read("args.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome, Is.EqualTo(new JobOutcome(JobStatus.Complete, null, 200, 40)), "標準輸入沒關的話指令稿的 cat 會一直等");
            Assert.That(_scripts.Read("stdin.txt"), Is.EqualTo(job.Prompt));
            Assert.That(args[^1], Is.EqualTo("-"));
            Assert.That(args, Does.Contain("--strict-config").And.Contain("--ephemeral").And.Contain("-m").And.Contain("gpt-6.1-sol"));
            Assert.That(args.Any(arg => arg.StartsWith("developer_instructions=\"系統提示", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public async Task Run_ForCodex_WhenItRunsACommand_AbortsWithoutWaiting()
    {
        // Arrange
        var engine = CodexEngine(_scripts.Recording("codex", "codex-command.jsonl", afterOutput: "sleep 300"));

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(EngineKinds.Codex), new ListSink(), CancellationToken.None);

        // Assert
        Assert.That(outcome, Is.EqualTo(JobOutcome.Failed("本機的 Codex 想用 Kumi 以外的工具，已經停下。")));
    }

    private async Task WaitForFileAsync(string name)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!_scripts.Exists(name))
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), $"等不到 {name}");
            await Task.Delay(20);
        }
    }

    private static async Task WaitUntilDeadAsync(int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (FakeEngineScripts.IsAlive(processId))
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "孫程序還活著：程序樹沒有被結束");
            await Task.Delay(50);
        }
    }

    [Test]
    public async Task Run_AfterTheResultKillsGrandchildrenTheEngineLeftBehind()
    {
        // Arrange
        var script = _scripts.Recording("claude", "claude-success.jsonl", afterOutput: "sleep 1", beforeOutput: $"sleep 300 &\necho $! > \"{_scripts.Out}/child.pid\"\nsleep 0.5");
        var engine = ClaudeEngine(script);

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        var child = int.Parse(_scripts.Read("child.pid").Trim());
        await WaitUntilDeadAsync(child);
        Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
    }

    [Test]
    public async Task Run_WhenTheTranslatorThrows_StopsTheEngineAndFailsInsteadOfWaitingForTheTimeout()
    {
        // Arrange
        var engine = new ProcessEngine(new ThrowingSpec(), _scripts.Recording("claude", "claude-success.jsonl", afterOutput: "sleep 300"), "1", [], McpToken, Server, Quick);
        var timer = Stopwatch.StartNew();

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.Contain("InvalidOperationException").And.Not.Contain("內容"));
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        }
    }

    [Test]
    public async Task Run_ScrubsTheMcpTokenAndPathsFromToolErrorsAndFailures()
    {
        // Arrange
        var events = $$$"""
            echo '{"type":"system","subtype":"init","tools":[],"mcp_servers":[{"name":"kumi","status":"connected"}]}'
            echo '{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t","name":"mcp__kumi__get_blueprint"}]}}'
            echo '{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t","is_error":true,"content":"bad {{{McpToken}}}x at \\\\server\\share\\f.txt"}]}}'
            echo '{"type":"result","subtype":"success","is_error":true,"result":"boom {{{McpToken}}}"}'
            """;
        var engine = ClaudeEngine(_scripts.Recording("claude", null, beforeOutput: events));
        var sink = new ListSink();

        // Act
        var outcome = await engine.RunAsync(TestJobs.Make(), sink, CancellationToken.None);

        // Assert
        var error = sink.Events.OfType<ToolEvent>().Single(tool => tool.Status == "failed").Error;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Does.Not.Contain(McpToken).And.Not.Contain("share"));
            Assert.That(outcome.Error, Is.EqualTo("boom …"));
        }
    }

    [Test]
    public async Task Run_WritesTheNormalizedMcpUrlIntoTheConfig()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", "claude-success.jsonl"));
        var job = TestJobs.Make() with { McpUrl = "HTTPS://PM.EXAMPLE.COM:443/mcp" };

        // Act
        await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        Assert.That(_scripts.Read("mcp.json"), Does.Contain("\"url\":\"https://pm.example.com/mcp\""));
    }

    [Test]
    public async Task Run_RejectsAnMcpUrlThatCouldLeakEnvironmentVariablesWithoutStartingTheEngine()
    {
        // Arrange
        var engine = ClaudeEngine(_scripts.Recording("claude", null));
        var job = TestJobs.Make() with { McpUrl = "https://evil.example.com/${KUMI_MCP_TOKEN}" };

        // Act
        var outcome = await engine.RunAsync(job, new ListSink(), CancellationToken.None);

        // Assert
        Assert.That(outcome.Error, Does.Contain("MCP 網址不正確"));
        Assert.That(_scripts.Exists("args.txt"), Is.False);
    }

    private sealed class ThrowingSpec : IEngineSpec
    {
        public string Kind => EngineKinds.ClaudeCode;

        public LaunchPlan Plan(AgentJob job, string workDirectory) =>
            new ClaudeCodeSpec().Plan(job, workDirectory);

        public IEngineTranslator CreateTranslator() => new Throwing();

        private sealed class Throwing : IEngineTranslator
        {
            public TranslatorStep Feed(string line) => throw new InvalidOperationException("內容");

            public JobOutcome Conclude(int exitCode, string standardError) => JobOutcome.Failed("x");
        }
    }
}
