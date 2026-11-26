using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary>開機自動啟動的安全面：路徑、記錄、啟動時間比對、本機上限的旗標與狀態。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class AutostartSafetyTests
{
    private FakeEngineScripts _scripts = null!;
    private FakeAutostart _os = null!;
    private CliHarness _cli = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        _os = new FakeAutostart();
        _cli = NewCli(_os);
    }

    [TearDown]
    public void TearDown()
    {
        _cli.Dispose();
        _os.Dispose();
        _scripts.Dispose();
    }

    private CliHarness NewCli(FakeAutostart os)
    {
        var cli = new CliHarness();
        cli.Autostart = os.Build(cli.ConfigPath);
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        cli.Environment["PATH"] = "/usr/bin:/bin";
        cli.Environment[ExecutableLocator.ClaudeVariable] = _scripts.Script("claude", "exit 0");
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        return cli;
    }

    [Test]
    public async Task Enable_WhenKumiSitsInTheDownloadsFolder_RefusesAndSaysWhereToMoveIt()
    {
        // Arrange
        _os.KumiPath = Path.Combine(_os.Home, "Downloads", "kumi");
        _cli.Autostart = _os.Build(_cli.ConfigPath);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("下載資料夾").And.Contain("搬到").And.Contain("~/.local/bin"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
            Assert.That(_os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Enable_WhenKumiSitsInTheTempFolder_Refuses()
    {
        // Arrange
        _os.KumiPath = Path.Combine(_os.Home, "..", "tmp", "build", "kumi");
        _cli.Autostart = _os.Build(_cli.ConfigPath);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("暫存資料夾"));
    }

    [Test]
    public async Task Enable_WhenTheFolderIsWritableByOtherUsers_Refuses()
    {
        // Arrange
        _os.KumiPath = "/opt/shared/kumi";
        _os.UnsafeDirectories.Add("/opt/shared");
        _cli.Autostart = _os.Build(_cli.ConfigPath);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("別的使用者也寫得進去"));
    }

    [Test]
    public async Task Enable_WhenTheDownloadsFolderIsSpelledWithOtherCaseOnMac_StillRefuses()
    {
        // Arrange（macOS 的檔案系統不分大小寫）
        _os.KumiPath = Path.Combine(_os.Home, "DOWNLOADS", "kumi");
        _cli.Autostart = _os.Build(_cli.ConfigPath);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.Error, Does.Contain("下載資料夾"));
    }

    [Test]
    public async Task Enable_WhenKumiIsASymlinkIntoTheDownloadsFolder_Refuses()
    {
        // Arrange
        var downloads = Directory.CreateDirectory(Path.Combine(_os.Home, "Downloads")).FullName;
        var real = Path.Combine(downloads, "kumi-real");
        File.WriteAllText(real, "x");
        var bin = Directory.CreateDirectory(Path.Combine(_os.Home, ".local", "bin")).FullName;
        var link = Path.Combine(bin, "kumi");
        File.CreateSymbolicLink(link, real);
        _os.KumiPath = link;
        _cli.Autostart = _os.Build(_cli.ConfigPath);

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.Error, Does.Contain("下載資料夾"));
    }

    [Test]
    public async Task Enable_WhenTheKumiFileItselfIsWritableByOthers_Refuses()
    {
        // Arrange
        var path = _os.KumiPath;
        _cli.Autostart = _os.Build(_cli.ConfigPath) with { IsWritableByOthers = candidate => candidate == path };

        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        Assert.That(result.Error, Does.Contain("別的使用者也寫得進去的執行檔"));
    }

    [Test]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public void IsWritableByOthers_CountsAGroupWritableFolder()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("kumi-group-").FullName;
        try
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupWrite);

            // Act
            var unsafeFolder = AutostartSystem.IsWritableByOthers(folder);

            // Assert
            Assert.That(unsafeFolder, Is.True);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    [Test]
    public async Task Enable_FromAnOrdinaryInstallFolder_SucceedsAndTellsAboutTheLimits()
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("AI 訂閱").And.Contain("每小時 30 件").And.Contain("每天 200 件"));
            Assert.That(result.Out, Does.Not.Contain("實驗性"));
        }
    }

    [Test]
    public async Task Enable_WithLimitFlags_WritesThemIntoTheLaunchEntryAndOnlyThose()
    {
        // Act
        await _cli.RunAsync("agent", "enable", "--max-jobs-per-hour", "10", "--max-jobs-per-day", "0");
        var plist = File.ReadAllText(_os.PlistPath);
        await _cli.RunAsync("agent", "enable");
        var defaults = File.ReadAllText(_os.PlistPath);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plist, Does.Contain("<string>--max-jobs-per-hour</string>").And.Contain("<string>10</string>"));
            Assert.That(plist, Does.Contain("<string>--max-jobs-per-day</string>").And.Contain("<string>0</string>"));
            Assert.That(defaults, Does.Not.Contain("max-jobs"));
        }
    }

    [TestCase("abc")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("100001")]
    public async Task Enable_WithAnInvalidLimit_IsAUsageErrorAndChangesNothing(string value)
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable", "--max-jobs-per-hour", value);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--max-jobs-per-hour"));
            Assert.That(File.Exists(_os.PlistPath), Is.False);
        }
    }

    [Test]
    public async Task Status_ShowsHowManyJobsWereTakenThisHourAndToday_AlsoAsJson()
    {
        // Arrange
        var quota = new JobQuota(_cli.ConfigPath, 5, 0, () => DateTimeOffset.Now);
        quota.TryReserve();
        quota.TryReserve();
        using var running = _os.Occupy(background: true, pid: 9002);

        // Act
        var text = await _cli.RunAsync("agent", "status");
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Out, Does.Contain("這一小時 2 / 5 件").And.Contain("今天 2 件（不限）"));
            Assert.That(json["usage"]!["hour"]!["used"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(json["usage"]!["hour"]!["limit"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(json["usage"]!["day"]!["limit"]!.GetValue<int>(), Is.Zero);
        }
    }

    [Test]
    public async Task Status_WhenTheAgentIsNotRunning_ShowsTheDefaultLimitsAndSaysSo()
    {
        // Arrange
        _ = new JobQuota(_cli.ConfigPath, 5, 0, () => DateTimeOffset.Now);

        // Act
        var text = await _cli.RunAsync("agent", "status");
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Out, Does.Contain("/ 30 件").And.Contain("預設值"));
            Assert.That(json["usage"]!["hour"]!["limit"]!.GetValue<int>(), Is.EqualTo(30));
            Assert.That(json["usage"]!["limitsAreDefaults"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Disable_WhenTheStartTimeOfTheProcessIsNotTheRecordedOne_DoesNotKillIt()
    {
        // Arrange（編號被別的 kumi 重用了：名稱對，啟動時間不對）
        await _cli.RunAsync("agent", "enable");
        _os.Stubborn = true;
        _os.SetProcessStart(FakeAutostart.BackgroundPid, "Fri Nov 27 08:00:00 2026");
        _os.Calls.Clear();

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Calls, Does.Not.Contain($"kill {FakeAutostart.BackgroundPid}"));
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("停不下來"));
        }
    }

    [Test]
    public async Task Disable_WhenTheLockRecordsNoStartTime_DoesNotKillAndTellsTheUserWhatToDo()
    {
        // Arrange（舊版 kumi 寫的鎖檔沒有啟動時間：無從確認，不退回只看名稱）
        using var legacy = _os.Occupy(background: true, pid: 9001);
        _os.SetProcessStart(9001, "不重要");

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Calls, Does.Not.Contain("kill 9001"));
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("重開機"));
        }
    }

    [Test]
    public async Task Disable_WhenTheCurrentStartTimeCannotBeRead_DoesNotKill()
    {
        // Arrange（鎖檔有記，但現在查不出來）
        await _cli.RunAsync("agent", "enable");
        _os.Stubborn = true;
        _cli.Autostart = _cli.Autostart with { GetProcessStart = _ => null };
        _os.Calls.Clear();

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_os.Calls, Does.Not.Contain($"kill {FakeAutostart.BackgroundPid}"));
            Assert.That(result.Error, Does.Contain("重開機"));
        }
    }

    [Test]
    public async Task Service_WhenTheTokenLacksScopes_ExitsWithOneWithoutRetryingForever()
    {
        // Arrange
        var delays = new List<TimeSpan>();
        _cli.AgentOptions = new AgentOptions { Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; } };
        _cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run"));
        _cli.Environment[ExecutableLocator.CodexVariable] = Path.Combine(_scripts.Directory, "no-codex");

        // Act
        var result = await _cli.RunAsync("agent", "start", "--service");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("缺少範圍").And.Contain("kumi agent login"));
            Assert.That(delays, Is.Empty, "缺範圍不是暫時的，不重試");
        }
    }

    [Test]
    public async Task Service_OnMac_TrimsAnOversizedLogWhenItStarts()
    {
        // Arrange
        Directory.CreateDirectory(Path.GetDirectoryName(_os.LogPath)!);
        File.WriteAllBytes(_os.LogPath, new byte[5 * 1024 * 1024 + 1]);
        _cli.Server.On("GET", "/v1/session", _ => FakeAgentServer.Failure(HttpStatusCode.Unauthorized, "auth.token_invalid", "權杖無效"));

        // Act
        await _cli.RunAsync("agent", "start", "--service");

        // Assert
        Assert.That(new FileInfo(_os.LogPath).Length, Is.Zero);
    }

    [Test]
    public async Task EnableOnWindows_SaysTheAutostartIsExperimental()
    {
        // Arrange
        using var os = new FakeAutostart(AutostartOs.Windows) { KumiPath = @"C:\Program Files\Kumi\kumi.exe" };
        using var cli = NewCli(os);
        cli.Environment[ExecutableLocator.CodexVariable] = _scripts.Script("codex", "exit 0");

        // Act
        var result = await cli.RunAsync("agent", "enable", "--engine", "codex");

        // Assert
        Assert.That(result.Out, Does.Contain("實驗性"));
    }
}
