using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Agent.Autostart;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary>Windows 的作法在 macOS 上用假的登錄與假的背景啟動跑（沒有 Windows 機器可以真的驗證）。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class WindowsAutostartTests
{
    private FakeEngineScripts _scripts = null!;
    private FakeAutostart _os = null!;
    private CliHarness _cli = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        _os = new FakeAutostart(AutostartOs.Windows) { KumiPath = @"C:\Program Files\Kumi\kumi.exe" };
        _cli = new CliHarness();
        _cli.Autostart = _os.Build(_cli.ConfigPath);
        new ConfigStore(_cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        _cli.Environment[ExecutableLocator.CodexVariable] = _scripts.Script("codex", "exit 0");
        _cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
    }

    [TearDown]
    public void TearDown()
    {
        _cli.Dispose();
        _os.Dispose();
        _scripts.Dispose();
    }

    [Test]
    public async Task Enable_WritesTheRunValueWithQuotedArgumentsAndStartsTheProcessInTheBackground()
    {
        // Act
        var result = await _cli.RunAsync("agent", "enable", "--name", "辦公室 電腦", "--engine", "codex");

        // Assert
        var expected = @"""C:\Program Files\Kumi\kumi.exe"" agent start --service --server https://pm.example.com --name ""辦公室 電腦"" --engine codex";
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_os.Registry["KumiLocalAgent"], Is.EqualTo(expected));
            Assert.That(_os.Registry["KumiLocalAgent"], Does.Not.Contain(CliHarness.Token));
            Assert.That(_os.Calls, Has.Count.EqualTo(1));
            Assert.That(_os.Calls[0], Does.StartWith(@"start C:\Program Files\Kumi\kumi.exe agent start --service"));
            Assert.That(result.Out, Does.Contain(_os.LogPath));
        }
    }

    [Test]
    public async Task Disable_KillsTheBackgroundProcessAfterConfirmingItsNameThenDeletesTheRunValue()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_os.Calls, Does.Contain($"kill {FakeAutostart.BackgroundPid}"));
            Assert.That(_os.Registry, Is.Empty);
            Assert.That(_os.BackgroundRunning, Is.False);
        }
    }

    [Test]
    public async Task Disable_DoesNotKillAProcessWhoseNameIsNotKumi()
    {
        // Arrange：鎖裡的 PID 後來被別的程式用掉了。
        using var impostor = _os.Occupy(background: true, pid: 9001);
        var environment = _cli.Autostart with { GetProcessName = _ => "notepad" };
        _cli.Autostart = environment;

        // Act
        var result = await _cli.RunAsync("agent", "disable");

        // Assert
        Assert.That(_os.Calls, Does.Not.Contain("kill 9001"));
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("停不下來"));
    }

    [Test]
    public async Task Status_WhenEnabled_ShowsRunningWithoutLaunchctl()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");

        // Act
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json["enabled"]!.GetValue<bool>(), Is.True);
            Assert.That(json["running"]!.GetValue<bool>(), Is.True);
            Assert.That(json["background"]!.GetValue<bool>(), Is.True);
            Assert.That(_os.Calls.Any(call => call.StartsWith("launchctl")), Is.False);
        }
    }
}
