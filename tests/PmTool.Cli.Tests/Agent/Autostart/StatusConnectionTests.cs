using System.Text.Json.Nodes;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent.Autostart;

/// <summary><c>kumi agent status</c> 的輸出：連線狀態與記錄檔不見了的提醒。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class StatusConnectionTests
{
    private FakeEngineScripts _scripts = null!;
    private FakeAutostart _os = null!;
    private CliHarness _cli = null!;

    [SetUp]
    public void SetUp()
    {
        _scripts = new FakeEngineScripts();
        _os = new FakeAutostart();
        _cli = AutostartCommandTests.NewEnabledCli(_os, _scripts);
    }

    [TearDown]
    public void TearDown()
    {
        _cli.Dispose();
        _os.Dispose();
        _scripts.Dispose();
    }

    private void CreateLogFile()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_os.LogPath)!);
        File.WriteAllText(_os.LogPath, string.Empty);
    }

    [Test]
    public async Task Status_WhenTheBackgroundAgentKeepsFailingToConnect_SaysSoWithTheReasonAndThatItRecoversByItself()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        CreateLogFile();
        _os.ReportBackground(new AgentConnection(
            AgentConnectionState.Retrying, new DateTimeOffset(2026, 11, 26, 12, 22, 28, TimeSpan.Zero), "連不上伺服器"));

        // Act
        var result = await _cli.RunAsync("agent", "status");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Out, Does.Contain("連線：連不上或伺服器有問題").And.Contain("一直在重試").And.Contain("：連不上伺服器"));
            Assert.That(result.Out, Does.Contain("VPN").And.Contain("自己接上"));
        }
    }

    [Test]
    public async Task Status_WhenConnected_SaysConnected()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        CreateLogFile();
        _os.ReportBackground(new AgentConnection(AgentConnectionState.Connected, DateTimeOffset.Now, null));

        // Act
        var result = await _cli.RunAsync("agent", "status");

        // Assert
        Assert.That(result.Out, Does.Contain("連線：已連上伺服器"));
        Assert.That(result.Out, Does.Not.Contain("重試"));
    }

    [Test]
    public async Task Status_AsJson_IncludesTheConnection()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        var since = new DateTimeOffset(2026, 11, 26, 4, 22, 28, TimeSpan.Zero);
        _os.ReportBackground(new AgentConnection(AgentConnectionState.Retrying, since, "連不上伺服器"));

        // Act
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        var connection = json["connection"]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(connection["state"]!.GetValue<string>(), Is.EqualTo("retrying"));
            Assert.That(DateTimeOffset.Parse(connection["since"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(since));
            Assert.That(connection["error"]!.GetValue<string>(), Is.EqualTo("連不上伺服器"));
        }
    }

    [Test]
    public async Task Status_WhenNotRunning_HasNoConnection()
    {
        // Act
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;
        var text = (await _cli.RunAsync("agent", "status")).Out;

        // Assert
        Assert.That(json["connection"], Is.Null);
        Assert.That(text, Does.Not.Contain("連線："));
    }

    [Test]
    public async Task Status_WhenEnabledButTheLogFileIsGone_SaysHowToRebuildIt()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        if (File.Exists(_os.LogPath))
        {
            File.Delete(_os.LogPath);
        }

        // Act
        var text = (await _cli.RunAsync("agent", "status")).Out;
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("記錄檔不見了").And.Contain("kumi agent enable"));
            Assert.That(json["logMissing"]!.GetValue<bool>(), Is.True);
        }
    }

    [Test]
    public async Task Status_WhenTheLogFileExists_DoesNotWarn()
    {
        // Arrange
        await _cli.RunAsync("agent", "enable");
        CreateLogFile();

        // Act
        var text = (await _cli.RunAsync("agent", "status")).Out;
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        Assert.That(text, Does.Not.Contain("記錄檔不見了"));
        Assert.That(json["logMissing"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public async Task Status_WhenNotEnabled_DoesNotWarnAboutAMissingLog()
    {
        // Act
        var json = JsonNode.Parse((await _cli.RunAsync("agent", "status", "--json")).Out)!;

        // Assert
        Assert.That(json["logMissing"]!.GetValue<bool>(), Is.False);
    }
}
