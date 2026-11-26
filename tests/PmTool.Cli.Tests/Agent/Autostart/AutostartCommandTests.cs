using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Agent.Autostart;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class AutostartCommandTests
{
    private FakeEngineScripts _scripts = null!;
    private FakeAutostart _os = null!;
    private CliHarness _cli = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        _os = new FakeAutostart();
        _cli = NewEnabledCli(_os, _scripts);
    }

    [TearDown]
    public void TearDown()
    {
        _cli.Dispose();
        _os.Dispose();
        _scripts.Dispose();
    }

    internal static object Session(params string[] scopes) => new
    {
        user = new { personId = "0190a000-0000-7000-8000-000000000001", displayName = "王小明", email = "ming@example.com", organizationName = "範例公司", role = "member" },
        scopes,
    };

    private CliHarness NewCli(FakeAutostart os, bool withEngine = true) => NewEnabledCli(os, _scripts, withEngine);

    /// <summary>登入好、權杖範圍夠、PATH 上有假的 claude：kumi agent enable 會成功的 CLI。</summary>
    internal static CliHarness NewEnabledCli(FakeAutostart os, FakeEngineScripts scripts, bool withEngine = true)
    {
        var cli = new CliHarness();
        cli.Autostart = os.Build(cli.ConfigPath);
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        cli.Environment["PATH"] = "/usr/bin:/bin";
        if (withEngine)
        {
            cli.Environment[ExecutableLocator.ClaudeVariable] = scripts.Script("claude", "exit 0");
        }

        cli.Server.On("GET", "/v1/session", Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        return cli;
    }

    [Test]
    public async Task Enable_OnMac_WritesThePlistLoadsItAndReportsSuccess()
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable", "--name", "小明的 Mac", "--engine", "claude-code");

        // Assert
        var plist = File.ReadAllText(_os.PlistPath);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已設定").And.Contain("已經在執行").And.Contain(_os.LogPath));
            Assert.That(plist, Does.Contain("<string>--service</string>").And.Contain("<string>小明的 Mac</string>").And.Contain(CliHarness.ServerUrl));
            Assert.That(plist, Does.Contain("<key>PATH</key>").And.Contain("/usr/bin:/bin"));
            Assert.That(plist, Does.Not.Contain(CliHarness.Token).And.Not.Contain("pmt_"));
            Assert.That(_os.Calls, Is.EqualTo(new[]
            {
                "launchctl bootout gui/501/kumi.local-agent",
                $"launchctl bootstrap gui/501 {_os.PlistPath}",
            }));
            Assert.That(_os.BackgroundRunning, Is.True);
        }
    }

    [Test]
    public async Task Enable_WithJson_PrintsEnabledRunningAndLogPath()
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(json["enabled"]!.GetValue<bool>(), Is.True);
            Assert.That(json["running"]!.GetValue<bool>(), Is.True);
            Assert.That(json["logPath"]!.GetValue<string>(), Is.EqualTo(_os.LogPath));
        }
    }

    [Test]
    public async Task Enable_OnAnUnsupportedOs_ExitsWithOneAndSaysSo()
    {
        // Arrange
        _cli.Autostart = new AutostartEnvironment();

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("還不支援自動啟動").And.Contain("kumi agent start"));
    }

    [Test]
    public async Task Enable_WithATokenFromTheEnvironment_ExitsWithOneAndPointsToAgentLogin()
    {
        // Arrange
        _cli.LoggedInByEnvironment();

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("環境變數").And.Contain("kumi agent login"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
        }
    }

    [Test]
    public async Task Enable_WithATokenMissingTheAgentScope_ExitsWithOneAndPointsToAgentLogin()
    {
        // Arrange
        _cli.Server.On("GET", "/v1/session", Session("items:read"));

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("agent:run").And.Contain("kumi agent login"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
            Assert.That(_os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Enable_WhenNeitherClaudeNorCodexIsOnThePath_ExitsWithOne()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os, withEngine: false);

        // Act
        var result = await cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("claude").And.Contain("codex"));
            Assert.That(File.Exists(os.PlistPath), Is.False);
        }
    }

    [Test]
    public async Task Enable_WhileAForegroundAgentIsRunning_ExitsWithOneAndNamesThePid()
    {
        // Arrange
        using var foreground = _os.Occupy(background: false, pid: 3131);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("PID 3131").And.Contain("先關掉那個視窗"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
        }
    }

    [Test]
    public async Task Enable_WhenABackgroundAgentIsAlreadyRunning_StopsItFirstThenStartsTheNewOne()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        _os.Calls.Clear();

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_os.Calls[0], Is.EqualTo("launchctl bootout gui/501/kumi.local-agent"));
            Assert.That(_os.Calls.Last(), Does.StartWith("launchctl bootstrap"));
            Assert.That(_os.BackgroundRunning, Is.True);
        }
    }

    [Test]
    public async Task Enable_WhenTheOldBackgroundProcessIgnoresBootout_KillsItAfterConfirmingItIsKumi()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        _os.Stubborn = true;
        _os.Calls.Clear();

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(_os.Calls, Does.Contain($"kill {FakeAutostart.BackgroundPid}"));
    }

    [Test]
    public async Task Enable_WhenTheBackgroundProcessNeverComesUp_KeepsTheEntryAndExitsWithOneShowingTheLog()
    {
        // Arrange
        _os.LaunchSucceeds = false;
        Directory.CreateDirectory(Path.GetDirectoryName(_os.LogPath)!);
        File.WriteAllText(_os.LogPath, "2026-11-24 09:00:00  連不上伺服器\n");

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有起來").And.Contain("連不上伺服器"));
            Assert.That(File.Exists(_os.PlistPath), Is.True);
        }
    }

    [Test]
    public async Task Enable_TrimsAnOversizedLogBeforeStarting()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_os.LogPath)!);
        File.WriteAllBytes(_os.LogPath, new byte[5 * 1024 * 1024 + 1]);

        // Act
        await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(new FileInfo(_os.LogPath).Length, Is.Zero);
    }

    [Test]
    public async Task Disable_WhenNothingWasSetUp_SaysSoAndExitsWithZero()
    {
        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("沒有設定自動啟動"));
    }

    [Test]
    public async Task Disable_AfterEnable_StopsTheBackgroundProcessAndRemovesThePlist()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已取消自動啟動").And.Contain("已經停止"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
            Assert.That(_os.BackgroundRunning, Is.False);
        }
    }

    [Test]
    public async Task Disable_WithJson_ReportsWhetherItWasEnabled()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var first = JsonNode.Parse((await _cli.RunAsync("agent", "disable", "--json")).Out)!;
        var second = JsonNode.Parse((await _cli.RunAsync("agent", "disable", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first["enabled"]!.GetValue<bool>(), Is.False);
            Assert.That(first["wasEnabled"]!.GetValue<bool>(), Is.True);
            Assert.That(second["wasEnabled"]!.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Disable_LeavesAForegroundAgentAlone()
    {
        // Arrange
        using var foreground = _os.Occupy(background: false, pid: 3131);

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(_os.Calls, Does.Not.Contain($"kill 3131"));
        Assert.That(AgentLock.Peek(_cli.ConfigPath), Is.EqualTo(new AgentHolder(3131, false)));
    }

    [Test]
    public async Task Logout_WhenAutostartIsSetUp_RemindsThatTheBackgroundAgentIsStillRunning()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var result = await _cli.RunAsync("auth", "logout");

        // Assert
        Assert.That(result.Out, Does.Contain("背景的本機代理還在執行").And.Contain("kumi agent disable"));
    }

    [Test]
    public async Task Disable_WhenNobodyHoldsTheLock_StillAsksTheLaunchManagerToUnloadTheJob()
    {
        // Arrange（背景程序剛啟動、還沒拿到鎖：看鎖看不出來，但管理員裡已經載入了）
        await _cli.RunAsync("agent", "enable");
        _os.StopBackground();
        _os.Calls.Clear();

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(_os.Calls, Does.Contain("launchctl bootout gui/501/kumi.local-agent"));
    }

    [Test]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Enable_OnMac_MakesTheLogDirectoryPrivate()
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(
            File.GetUnixFileMode(Path.GetDirectoryName(_os.LogPath)!),
            Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
    }

    [Test]
    public async Task Status_BeforeAndAfterEnable_ReportsTheJsonShape()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_os.LogPath)!);
        File.WriteAllLines(_os.LogPath, Enumerable.Range(1, 12).Select(index => $"line {index}"));

        // Act
        var before = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;
        await _cli.RunAsync("agent", "enable");
        var after = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before["supported"]!.GetValue<bool>(), Is.True);
            Assert.That(before["enabled"]!.GetValue<bool>(), Is.False);
            Assert.That(before["running"]!.GetValue<bool>(), Is.False);
            Assert.That(before["pid"], Is.Null);
            Assert.That(before["recentLog"]!.AsArray(), Has.Count.EqualTo(10));
            Assert.That(before["recentLog"]![9]!.GetValue<string>(), Is.EqualTo("line 12"));
            Assert.That(after["enabled"]!.GetValue<bool>(), Is.True);
            Assert.That(after["running"]!.GetValue<bool>(), Is.True);
            Assert.That(after["pid"]!.GetValue<int>(), Is.EqualTo(FakeAutostart.BackgroundPid));
            Assert.That(after["background"]!.GetValue<bool>(), Is.True);
            Assert.That(after["logPath"]!.GetValue<string>(), Is.EqualTo(_os.LogPath));
        }
    }

    [Test]
    public async Task Status_AsText_ExplainsEachPart()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var result = await _cli.RunAsync("agent", "status");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("自動啟動：已設定").And.Contain("執行中：是（背景，PID 4242）").And.Contain(_os.LogPath));
    }

    [Test]
    public async Task Status_OnAnUnsupportedOs_SaysNotSupported()
    {
        // Arrange
        _cli.Autostart = new AutostartEnvironment();

        // Act
        var result = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        Assert.That(result["supported"]!.GetValue<bool>(), Is.False);
        Assert.That(result["enabled"]!.GetValue<bool>(), Is.False);
    }
}
