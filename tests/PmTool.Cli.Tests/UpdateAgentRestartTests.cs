using System.Text.Json.Nodes;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Tests.Agent.Autostart;
using static PmTool.Cli.Tests.UpdateCommandTests;

namespace PmTool.Cli.Tests;

/// <summary>
/// kumi update 換好執行檔之後，背景的本機代理還在用記憶體裡的舊版：要重新啟動它，伺服器才會看到新的版本。
/// </summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class UpdateAgentRestartTests
{
    private static CliHarness NewCli(FakeAutostart os)
    {
        var cli = ServerWithUpdate();
        cli.Autostart = os.Build(cli.ConfigPath);
        Install(cli);
        UseSmokeVersion(cli);
        return cli;
    }

    [Test]
    public async Task Update_BackgroundAgentRunning_RestartsItAfterReplacingTheFile()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        string? bodyWhenRestarted = null;
        var build = cli.Autostart;
        cli.Autostart = build with
        {
            RunCommand = async (file, arguments, cancellation) =>
            {
                bodyWhenRestarted ??= File.ReadAllText(cli.Update.ExecutablePath!);
                return await build.RunCommand(file, arguments, cancellation);
            },
        };

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(bodyWhenRestarted, Is.EqualTo(NewBody), "換好檔案之後才重啟，新的程序才會是新版");
            Assert.That(os.Calls, Does.Contain("launchctl kickstart -k gui/501/kumi.local-agent"));
            Assert.That(os.RunningPid, Is.EqualTo(FakeAutostart.RestartedPid));
            Assert.That(result.Out, Does.Contain("已更新到 kumi").And.Contain("背景的本機代理已經用新版重新啟動"));
        }
    }

    [Test]
    public async Task Update_NoAgentRunning_DoesNotStartOne()
    {
        // Arrange：設了自動啟動但程序沒在跑（使用者自己停的、或權杖失效結束的），更新不替他開。
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        os.StopBackground();

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.Calls, Is.Empty);
            Assert.That(os.BackgroundRunning, Is.False);
            Assert.That(result.Out + result.Error, Does.Not.Contain("本機代理"));
        }
    }

    [Test]
    public async Task Update_ForegroundAgentRunning_WarnsToRestartItButSucceeds()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        using var foreground = os.Occupy(background: false, pid: 3131);

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("舊版").And.Contain("PID 3131").And.Contain("Ctrl-C").And.Contain("kumi agent start"));
            Assert.That(os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Update_RestartFails_WarnsButTheUpdateStillSucceeds()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        os.LaunchSucceeds = false;

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已更新到 kumi"));
            Assert.That(result.Error, Does.Contain("警告").And.Contain("kumi agent status"));
        }
    }

    [Test]
    public async Task Update_OnWindowsWithBackgroundAgent_SaysToSignOutAndBackIn()
    {
        // Arrange
        using var os = new FakeAutostart(AutostartOs.Windows);
        using var cli = NewCli(os);
        using var background = os.Occupy(background: true, pid: 9002, startedAt: "2026-11-26T01:00:00Z");

        // Act
        var result = await cli.RunAsync("update");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("舊版").And.Contain("登出再登入"));
            Assert.That(os.Calls, Is.Empty);
        }
    }

    [Test]
    public async Task Update_Json_ReportsWhatHappenedToTheAgent()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();

        // Act
        var result = await cli.RunAsync("update", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        Assert.That(json["agentRestart"]!.GetValue<string>(), Is.EqualTo("restarted"));
    }

    [Test]
    public async Task Update_JsonWithoutAgent_ReportsNotRunning()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);

        // Act
        var result = await cli.RunAsync("update", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        Assert.That(json["agentRestart"]!.GetValue<string>(), Is.EqualTo("not_running"));
    }

    [Test]
    public async Task Update_AlreadyLatest_HasNoAgentRestartValue()
    {
        // Arrange
        using var os = new FakeAutostart();
        using var cli = NewCli(os);
        os.StartInBackground();
        cli.Server.On("GET", "/v1/cli", new { version = PmTool.Cli.Commands.HelpText.VersionNumber, packages = Array.Empty<object>() });

        // Act
        var result = await cli.RunAsync("update", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json["agentRestart"], Is.Null);
            Assert.That(os.Calls, Is.Empty);
            Assert.That(os.RunningPid, Is.EqualTo(FakeAutostart.BackgroundPid));
        }
    }
}
