using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Settings;
using PmTool.Cli.Tests.Agent;
using PmTool.Cli.Tests.Agent.Autostart;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
public sealed class LogoutRevokeTests
{
    private const string CliToken = "pmt_cli_example";
    private const string AgentToken = "pmt_agent_example";
    private const string RevokePath = "/v1/account/tokens/current";

    private static CliHarness NewCli(bool interactive = false, string answers = "")
    {
        var cli = new CliHarness { IsInteractive = interactive, StandardInput = answers };
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliToken, AgentToken = AgentToken });
        cli.Server.On("DELETE", RevokePath, new { });
        return cli;
    }

    private static string[] RevokedWith(CliHarness cli) =>
        [.. cli.Server.To("DELETE", RevokePath).Select(request => request.Authorization ?? string.Empty)];

    [Test]
    public async Task Logout_WithRevoke_RevokesBothTokensOnTheServerThenDeletesThem()
    {
        // Arrange
        using var cli = NewCli();

        // Act
        var result = await cli.RunAsync("auth", "logout", "--revoke");

        // Assert
        var stored = new ConfigStore(cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(RevokedWith(cli), Is.EquivalentTo(new[] { $"Bearer {CliToken}", $"Bearer {AgentToken}" }));
            Assert.That(result.Out, Does.Contain("也撤銷了"));
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo(((string?)null, (string?)null)));
        }
    }

    [Test]
    public async Task Logout_RevokeFails_SaysSoButStillDeletesTheLocalTokens()
    {
        // Arrange
        using var cli = NewCli();
        cli.Server.OnError("DELETE", RevokePath, HttpStatusCode.Unauthorized, "auth.unauthorized", "未登入");

        // Act
        var result = await cli.RunAsync("auth", "logout", "--revoke");

        // Assert
        var stored = new ConfigStore(cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("沒有撤銷成功").And.Contain("命令列的權杖").And.Contain("本機代理的權杖").And.Contain("存取權杖"));
            Assert.That(result.Error, Does.Not.Contain(CliToken).And.Not.Contain(AgentToken));
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo(((string?)null, (string?)null)));
        }
    }

    [Test]
    public async Task Logout_WithoutRevokeAndNoTerminal_OnlyDeletesLocallyAndNeverCallsTheServer()
    {
        // Arrange
        using var cli = NewCli();

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli.Server.Requests, Is.Empty);
            Assert.That(result.Out, Does.Contain("還沒有被撤銷"));
        }
    }

    [Test]
    public async Task Logout_JsonWithoutRevoke_DoesNotAskAndDoesNotCallTheServer()
    {
        // Arrange（有終端機也一樣：--json 不問）
        using var cli = NewCli(interactive: true, answers: "y\n");

        // Act
        var result = await cli.RunAsync("auth", "logout", "--json");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cli.Server.Requests, Is.Empty);
            Assert.That(json["revoked"]!["cliToken"]!.GetValue<bool>(), Is.False);
        }
    }

    [Test]
    public async Task Logout_JsonWithRevoke_ReportsWhatWasRevoked()
    {
        // Arrange
        using var cli = NewCli();

        // Act
        var result = await cli.RunAsync("auth", "logout", "--json", "--revoke");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json["revoked"]!["cliToken"]!.GetValue<bool>(), Is.True);
            Assert.That(json["revoked"]!["agentToken"]!.GetValue<bool>(), Is.True);
            Assert.That(json["revokeFailures"]!.AsArray(), Is.Empty);
        }
    }

    [Test]
    public async Task Logout_Interactive_AsksAndRevokesOnYes()
    {
        // Arrange
        using var cli = NewCli(interactive: true, answers: "y\n");

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("要一併撤銷"));
            Assert.That(RevokedWith(cli), Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task Logout_Interactive_AnsweringNoOnlyDeletesLocally()
    {
        // Arrange
        using var cli = NewCli(interactive: true, answers: "n\n");

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(cli.Server.Requests, Is.Empty);
            Assert.That(new ConfigStore(cli.ConfigPath).Load().Token, Is.Null);
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task Logout_WithRevoke_AlsoStopsTheBackgroundLocalAgent()
    {
        // Arrange
        using var scripts = new FakeEngineScripts();
        using var os = new FakeAutostart();
        using var cli = NewCli();
        cli.Autostart = os.Build(cli.ConfigPath);
        cli.Environment["PATH"] = "/usr/bin:/bin";
        cli.Environment[ExecutableLocator.ClaudeVariable] = scripts.Script("claude", "exit 0");
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        await cli.RunAsync("agent", "enable");

        // Act
        var result = await cli.RunAsync("auth", "logout", "--revoke");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.BackgroundRunning, Is.False);
            Assert.That(File.Exists(os.PlistPath), Is.False);
            Assert.That(result.Out, Does.Not.Contain("還在執行"));
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task Logout_JsonWithRevoke_KeepsTheStopMessageOutOfTheJson()
    {
        // Arrange
        using var scripts = new FakeEngineScripts();
        using var os = new FakeAutostart();
        using var cli = NewCli();
        cli.Autostart = os.Build(cli.ConfigPath);
        cli.Environment["PATH"] = "/usr/bin:/bin";
        cli.Environment[ExecutableLocator.ClaudeVariable] = scripts.Script("claude", "exit 0");
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        await cli.RunAsync("agent", "enable");

        // Act
        var result = await cli.RunAsync("auth", "logout", "--json", "--revoke");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        Assert.That(json["agentStopped"]!.GetValue<bool>(), Is.True);
    }
}
