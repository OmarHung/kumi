using System.Net;
using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Agent.Autostart;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class ServiceStartTests
{
    private FakeEngineScripts _scripts = null!;
    private FakeAutostart _os = null!;
    private List<TimeSpan> _delays = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        _os = new FakeAutostart();
        _delays = [];
    }

    [TearDown]
    public void TearDown()
    {
        _os.Dispose();
        _scripts.Dispose();
    }

    private string FakeClaude() => _scripts.Script("claude", $$$"""
        case "$1 $2" in
          "--version ") echo "2.1.330 (Claude Code)"; exit 0 ;;
          "auth status") echo '{"loggedIn": true}'; exit 0 ;;
          "--help ") echo "{{{EngineDiscoveryTests.ClaudeHelp}}}"; exit 0 ;;
        esac
        exit 0
        """);

    private CliHarness NewCli(FakeAutostart? os = null)
    {
        var cli = new CliHarness
        {
            AgentOptions = new AgentOptions
            {
                MinPollGap = TimeSpan.FromMilliseconds(5),
                // 讀引擎輸出的收尾等待（預設 2、5 秒）也走 Delay：改成不到 1 秒，才不會被下面「1 秒以上就立刻回來」吃掉。
                DrainTimeout = TimeSpan.FromMilliseconds(700),
                ExitGrace = TimeSpan.FromMilliseconds(500),
                // 只把退避的等待（1 秒以上）換成立刻回來並記下；引擎探測等短的等待照舊真的等，不然讀輸出會被截斷。
                Delay = (delay, cancellation) =>
                {
                    if (delay < TimeSpan.FromSeconds(1))
                    {
                        return Task.Delay(delay, cancellation);
                    }

                    lock (_delays)
                    {
                        _delays.Add(delay);
                    }

                    return Task.CompletedTask;
                },
            },
        };
        cli.Autostart = (os ?? _os).Build(cli.ConfigPath);
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        cli.Environment[ExecutableLocator.ClaudeVariable] = FakeClaude();
        cli.Environment[ExecutableLocator.CodexVariable] = Path.Combine(_scripts.Directory, "no-codex");
        return cli;
    }

    private static HttpResponseMessage Unavailable() =>
        FakeAgentServer.Failure(HttpStatusCode.ServiceUnavailable, "server.unavailable", "伺服器暫時不能用");

    /// <summary>session 先失敗 <paramref name="failures"/> 次，之後成功；第一次登記時代表按了 Ctrl-C。</summary>
    private static void FailSessionThenStop(CliHarness cli, int failures, HttpStatusCode status = HttpStatusCode.ServiceUnavailable)
    {
        var calls = 0;
        cli.Server.On("GET", "/v1/session", _ => Interlocked.Increment(ref calls) <= failures
            ? FakeAgentServer.Failure(status, "server.unavailable", "伺服器暫時不能用")
            : FakeServer.Envelope(HttpStatusCode.OK, new { scopes = new[] { "agent:run", "blueprint:read", "blueprint:write" } }));
        cli.Server.On("POST", "/v1/agent/check-in", _ =>
        {
            cli.Interrupt!();
            return FakeAgentServer.Ok(new { job = (object?)null });
        });
    }

    [Test]
    public async Task Service_WhenTheServerIsUnreachableAtFirst_RetriesWithBackoffInsteadOfExiting()
    {
        // Arrange
        using var cli = NewCli();
        FailSessionThenStop(cli, failures: 3);

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_delays.Take(3), Is.EqualTo(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }));
            Assert.That(result.Error, Does.Contain("4 秒後重試"));
            Assert.That(cli.Server.To("POST", "/v1/agent/check-in"), Is.Not.Empty);
        }
    }

    [Test]
    public async Task Service_BackoffNeverWaitsMoreThanSixtySeconds()
    {
        // Arrange
        using var cli = NewCli();
        FailSessionThenStop(cli, failures: 9, HttpStatusCode.BadGateway);

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(_delays.Take(9).Select(delay => (int)delay.TotalSeconds), Is.EqualTo(new[] { 1, 2, 4, 8, 16, 32, 60, 60, 60 }));
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task Service_WhenTheTokenIsRejected_ExitsWithOneWithoutRetrying(HttpStatusCode status)
    {
        // Arrange
        using var cli = NewCli();
        cli.Server.On("GET", "/v1/session", _ => FakeAgentServer.Failure(status, "auth.token_invalid", "權杖無效"));

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("權杖無效"));
            Assert.That(_delays, Is.Empty);
        }
    }

    [Test]
    public async Task Service_WithoutAnyUsableEngine_ExitsWithOne()
    {
        // Arrange
        using var cli = NewCli();
        cli.Environment[ExecutableLocator.ClaudeVariable] = null;
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write"));

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("沒有東西可以提供"));
    }

    [Test]
    public async Task Service_WhenCheckInKeepsFailingWith5xx_KeepsRunningAndBacksOff()
    {
        // Arrange
        using var cli = NewCli();
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write"));
        var calls = 0;
        cli.Server.On("POST", "/v1/agent/check-in", _ =>
        {
            if (Interlocked.Increment(ref calls) < 8)
            {
                return Unavailable();
            }

            cli.Interrupt!();
            return FakeAgentServer.Ok(new { job = (object?)null });
        });

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(_delays.Select(delay => (int)delay.TotalSeconds).Take(7),
            Is.EqualTo(new[] { 1, 2, 4, 8, 16, 32, 60 }));
    }

    [Test]
    public async Task Start_WhileAnotherAgentHoldsTheLock_ExitsWithOneAndNamesTheBackgroundOne()
    {
        // Arrange
        using var cli = NewCli();
        using var background = _os.Occupy(background: true, pid: 4242);

        // Act
        var result = await cli.RunAsync("agent", "start");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("PID 4242").And.Contain("kumi agent disable"));
            Assert.That(cli.Server.Requests, Is.Empty, "拿不到鎖就不必連伺服器");
        }
    }

    [Test]
    public async Task Start_WhileAForegroundAgentHoldsTheLock_TellsToCloseThatWindow()
    {
        // Arrange
        using var cli = NewCli();
        using var foreground = _os.Occupy(background: false, pid: 3131);

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("PID 3131").And.Contain("關掉那個視窗"));
    }

    [Test]
    public async Task Start_ReleasesTheLockWhenItStops()
    {
        // Arrange
        using var cli = NewCli();
        FailSessionThenStop(cli, failures: 0);

        // Act
        await cli.RunAsync("agent", "start");

        // Assert
        Assert.That(AgentLock.Peek(cli.ConfigPath), Is.Null);
    }

    [Test]
    public async Task Start_WhileRunning_RecordsItselfAsTheForegroundHolder()
    {
        // Arrange
        using var cli = NewCli();
        AgentHolder? seen = null;
        FailSessionThenStop(cli, failures: 0);
        cli.Server.On("POST", "/v1/agent/check-in", _ =>
        {
            seen = AgentLock.Peek(cli.ConfigPath);
            cli.Interrupt!();
            return FakeAgentServer.Ok(new { job = (object?)null });
        });

        // Act
        await cli.RunAsync("agent", "start");

        // Assert
        Assert.That(seen, Is.EqualTo(new AgentHolder(7001, false)));
    }

    [Test]
    public async Task ServiceOnWindows_HidesTheConsoleAndWritesTheLogFileWithTimestamps()
    {
        // Arrange
        using var os = new FakeAutostart(AutostartOs.Windows);
        using var cli = NewCli(os);
        FailSessionThenStop(cli, failures: 1);

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        var log = File.ReadAllText(os.LogPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.HideConsoleCalls, Is.EqualTo(1));
            Assert.That(result.Out + result.Error, Is.Empty, "輸出都進記錄檔");
            Assert.That(log, Does.Contain("1 秒後重試").And.Contain("已連線"));
            Assert.That(log, Does.Not.Contain("Ctrl-C").And.Contain("kumi agent disable"), "背景執行沒有視窗可以按 Ctrl-C");
            Assert.That(log.Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.All.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}  "));
        }
    }

    [Test]
    public async Task ServiceOnWindows_WritesTheReasonForAFatalFailureIntoTheLog()
    {
        // Arrange
        using var os = new FakeAutostart(AutostartOs.Windows);
        using var cli = NewCli(os);
        cli.Server.On("GET", "/v1/session", _ => FakeAgentServer.Failure(HttpStatusCode.Unauthorized, "auth.token_invalid", "權杖無效"));

        // Act
        var result = await cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(File.ReadAllText(os.LogPath), Does.Contain("權杖無效"));
    }
}
