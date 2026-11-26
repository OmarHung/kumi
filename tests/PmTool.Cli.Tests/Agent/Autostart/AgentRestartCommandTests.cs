using System.Text.Json.Nodes;
using PmTool.Cli.Agent.Autostart;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary>kumi agent restart：讓背景的本機代理照啟動項目重新開一次（換了執行檔之後用）。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class AgentRestartCommandTests
{
    private static CliHarness NewCli(FakeAutostart os)
    {
        var cli = new CliHarness();
        cli.Autostart = os.Build(cli.ConfigPath);
        return cli;
    }

    [Test]
    public async Task Restart_BackgroundAgentRunning_KickstartsItAndWaitsForTheNewProcess()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.Calls, Is.EqualTo(new[] { "launchctl kickstart -k gui/501/kumi.local-agent" }));
            Assert.That(os.RunningPid, Is.EqualTo(FakeAutostart.RestartedPid));
            Assert.That(result.Out, Does.Contain("已經重新啟動").And.Contain($"PID {FakeAutostart.RestartedPid}"));
        }
    }

    [Test]
    public async Task Restart_Json_ReportsTheNewPid()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();

        // Act
        var result = await cli.RunAsync("agent", "restart", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(json["restarted"]!.GetValue<bool>(), Is.True);
            Assert.That(json["pid"]!.GetValue<int>(), Is.EqualTo(FakeAutostart.RestartedPid));
        }
    }

    [Test]
    public async Task Restart_EnabledButNotRunning_StartsItAgain()
    {
        // Arrange：啟動項目載入著，但程序已經結束了（例如權杖失效後自己結束）。
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        os.StopBackground();
        File.WriteAllText(os.PlistPath.EnsureDirectory(), "<plist/>");

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.RunningPid, Is.EqualTo(FakeAutostart.RestartedPid));
        }
    }

    [Test]
    public async Task Restart_ForegroundAgent_RefusesAndSaysToRestartThatWindow()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        using var foreground = os.Occupy(background: false, pid: 3131);

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("PID 3131").And.Contain("Ctrl-C").And.Contain("kumi agent start"));
            Assert.That(os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Restart_NothingEnabledOrRunning_FailsAndPointsToEnable()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有設定開機自動啟動").And.Contain("kumi agent enable"));
            Assert.That(os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Restart_NewProcessNeverComesUp_FailsWithTheLogTail()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        os.LaunchSucceeds = false;
        Directory.CreateDirectory(Path.GetDirectoryName(os.LogPath)!);
        File.WriteAllText(os.LogPath, "2026-11-26 21:00:00  權杖已經失效\n");

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有起來").And.Contain("權杖已經失效"));
        }
    }

    [Test]
    public async Task Restart_LaunchdDoesNotKnowTheService_FailsWithLaunchctlsMessage()
    {
        // Arrange：鎖說有背景程序，但 launchd 裡沒有載入（例如有人手動 bootout 過）。
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        using var orphan = os.Occupy(background: true, pid: 9001, startedAt: "Thu Nov 26 09:00:00 2026");

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("Could not find service").And.Contain("kumi agent enable"));
        }
    }

    [Test]
    public async Task Restart_OnWindows_ExplainsToSignOutAndBackIn()
    {
        // Arrange
        using var os = new FakeAutostart(AutostartOs.Windows);
        using var cli = NewCli(os);
        using var background = os.Occupy(background: true, pid: 9002, startedAt: "2026-11-26T01:00:00Z");

        // Act
        var result = await cli.RunAsync("agent", "restart");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("登出再登入"));
            Assert.That(os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Help_ListsTheRestartCommand()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);

        // Act
        var result = await cli.RunAsync("agent", "--help");

        // Assert
        Assert.That(result.Out, Does.Contain("restart"));
    }
}

internal static class PathTestExtensions
{
    /// <summary>建好檔案所在的資料夾，回傳原本的路徑。</summary>
    public static string EnsureDirectory(this string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
