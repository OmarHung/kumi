using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class AgentCommandTests
{
    private FakeEngineScripts _scripts = null!;

    [SetUp]
    public void SetUp() => _scripts = new FakeEngineScripts();

    [TearDown]
    public void TearDown() => _scripts.Dispose();

    private static object Session(params string[] scopes) => new
    {
        user = new { personId = "0190a000-0000-7000-8000-000000000001", displayName = "王小明", email = "ming@example.com", organizationName = "範例公司", role = "member" },
        scopes,
    };

    private string FakeClaude() => _scripts.Script("claude", $$$"""
        case "$1 $2" in
          "--version ") echo "2.1.330 (Claude Code)"; exit 0 ;;
          "auth status") echo '{"loggedIn": true}'; exit 0 ;;
          "--help ") echo "{{{EngineDiscoveryTests.ClaudeHelp}}}"; exit 0 ;;
        esac
        cat > /dev/null
        cat "{{{FakeEngineScripts.FixturePath("claude-success.jsonl")}}}"
        """);

    private CliHarness NewHarness()
    {
        var cli = new CliHarness
        {
            AgentOptions = new AgentOptions { MinPollGap = TimeSpan.FromMilliseconds(30), FlushInterval = TimeSpan.FromMilliseconds(20) },
        }.LoggedInByEnvironment();
        cli.Environment[ExecutableLocator.ClaudeVariable] = FakeClaude();
        cli.Environment[ExecutableLocator.CodexVariable] = Path.Combine(_scripts.Directory, "no-codex");
        return cli;
    }

    [Test]
    public async Task Start_WithATokenMissingScopes_SaysWhichOnesAndWhereToGetTheRightOne()
    {
        // Arrange
        using var cli = NewHarness();
        cli.Server.On("GET", "/v1/session", Session("blueprint:read"));

        // Act
        var result = await cli.RunAsync("agent", "start");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("agent:run").And.Contain("blueprint:write").And.Contain("kumi agent login"));
            Assert.That(cli.Server.To("POST", "/v1/agent/check-in"), Is.Empty);
        }
    }

    [Test]
    public async Task Start_WithATokenThatCanAlsoWriteItems_RefusesWithoutRetryingAndSaysWhy()
    {
        // Arrange（引擎拿得到這把權杖，帶 items:write 等於讓引擎改得動項目）
        using var cli = NewHarness();
        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read", "items:write"));

        // Act
        var result = await cli.RunAsync("agent", "start");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("items:write").And.Contain("不多不少").And.Contain("kumi agent login"));
            Assert.That(cli.Server.To("POST", "/v1/agent/check-in"), Is.Empty);
        }
    }

    [Test]
    public void IsTransient_ForAnOverScopedToken_IsFalse() =>
        Assert.That(AgentLoop.IsTransient(new CliException(ExitCodes.Error, "x", code: AgentLoop.OverScopedCode)), Is.False);

    [Test]
    public async Task Start_WhenNoEngineIsUsable_ExitsWithOneAndExplainsEachOne()
    {
        // Arrange
        using var cli = NewHarness();
        cli.Environment[ExecutableLocator.ClaudeVariable] = null;
        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));

        // Act
        var result = await cli.RunAsync("agent", "start");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("找不到 Claude Code").And.Contain("找不到 Codex").And.Contain("沒有東西可以提供"));
            Assert.That(File.Exists(MachineIdentity.PathOf(cli.ConfigPath)), Is.False, "沒有引擎就不用登記，也不留電腦編號");
        }
    }

    [Test]
    public async Task Start_WithAnUnknownEngineOption_IsAUsageError()
    {
        // Arrange
        using var cli = NewHarness();

        // Act
        var result = await cli.RunAsync("agent", "start", "--engine", "gemini");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("claude-code 或 codex"));
    }

    [Test]
    public async Task Start_RunsAJobEndToEndThenStopsOnInterruptAndPrintsOneLinePerJob()
    {
        // Arrange
        using var cli = NewHarness();
        var job = TestJobs.Make();
        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        var calls = 0;
        cli.Server.On("POST", "/v1/agent/check-in", _ => Interlocked.Increment(ref calls) == 1
            ? FakeAgentServer.JobResponse(job)
            : FakeAgentServer.Ok(new { job = (object?)null }));
        cli.Server.On("POST", $"/v1/agent/jobs/{job.JobId}/events", _ => FakeAgentServer.Ok(new { cancelled = false }));
        cli.Server.On("POST", $"/v1/agent/jobs/{job.JobId}/finish", _ =>
        {
            cli.Interrupt!();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        // Act
        var result = await cli.RunAsync("agent", "start", "--name", "小明的 Mac", "--engine", "claude-code");

        // Assert
        var checkIn = cli.Server.To("POST", "/v1/agent/check-in")[0].Json!;
        var finish = cli.Server.To("POST", $"/v1/agent/jobs/{job.JobId}/finish").Single().Json!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(checkIn["machineName"]!.GetValue<string>(), Is.EqualTo("小明的 Mac"));
            Assert.That(checkIn["machineKey"]!.GetValue<string>(), Is.EqualTo(File.ReadAllText(MachineIdentity.PathOf(cli.ConfigPath)).Split('"')[3]));
            Assert.That(checkIn["engines"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(checkIn["engines"]![0]!["version"]!.GetValue<string>(), Is.EqualTo("2.1.330"));
            Assert.That(finish["status"]!.GetValue<string>(), Is.EqualTo("complete"));
            Assert.That(finish["usage"]!["inputTokens"]!.GetValue<long>(), Is.EqualTo(160));
            Assert.That(result.Out, Does.Contain("已連線").And.Contain("DEMO").And.Contain("完成"));
            Assert.That(result.Out, Does.Not.Contain("藍圖"), "不印對話的內容");
            Assert.That(cli.Server.Requests.All(request => request.Authorization == $"Bearer {CliHarness.Token}"), Is.True);
            Assert.That(result.Out + result.Error, Does.Not.Contain(CliHarness.Token));
        }
    }

    [Test]
    public async Task Start_WithJson_PrintsReadyAndJobLines()
    {
        // Arrange
        using var cli = NewHarness();
        var job = TestJobs.Make();
        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        var calls = 0;
        cli.Server.On("POST", "/v1/agent/check-in", _ => Interlocked.Increment(ref calls) == 1
            ? FakeAgentServer.JobResponse(job)
            : FakeAgentServer.Ok(new { job = (object?)null }));
        cli.Server.On("POST", $"/v1/agent/jobs/{job.JobId}/events", _ => FakeAgentServer.Ok(new { cancelled = false }));
        cli.Server.On("POST", $"/v1/agent/jobs/{job.JobId}/finish", _ =>
        {
            cli.Interrupt!();
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        // Act
        var result = await cli.RunAsync("agent", "start", "--json");

        // Assert
        var lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines.Select(line => line["event"]!.GetValue<string>()), Is.EqualTo(new[] { "ready", "job" }));
            Assert.That(lines[0]["engines"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("claude_code"));
            Assert.That(lines[1]["status"]!.GetValue<string>(), Is.EqualTo("complete"));
        }
    }

    [Test]
    public async Task Start_WhenTheServerRejectsTheToken_ExitsWithOneAndTheTokenMessage()
    {
        // Arrange
        using var cli = NewHarness();
        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        cli.Server.OnError("POST", "/v1/agent/check-in", HttpStatusCode.Unauthorized, "auth.unauthorized", "x");

        // Act
        var result = await cli.RunAsync("agent", "start");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("權杖無效"));
    }

    [Test]
    public async Task Help_ListsTheAgentGroup()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var result = await cli.RunAsync("--help");

        // Assert
        Assert.That(result.Out, Does.Contain("agent：").And.Contain("agent start"));
    }
}
