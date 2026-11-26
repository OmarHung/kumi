using System.Text.Json;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class AgentLoginCommandTests
{
    private static LoginKit Kit() => new LoginKit { Cli = { IsInteractive = true } }.WithRedeemEndpoint();

    [Test]
    public async Task AgentLogin_AsksOnlyForTheAgentTokenAndKeepsTheCliToken()
    {
        // Arrange
        using var kit = Kit();
        kit.ReturnCli = false;
        kit.Cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王小明", email = "ming@example.com" }, scopes = new[] { "items:read" } });
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_before" });

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("agent"));
            Assert.That(kit.Browser.Query("scope"), Is.Null, "本機代理的範圍固定，不由 kumi 要");
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo(("pmt_cli_before", LoginKit.AgentSecret)));
            Assert.That(result.Out, Does.Contain("kumi agent start"));
        }
    }

    [Test]
    public async Task AgentLogin_SendsOnlyTheStoredAgentTokenForReplacement()
    {
        // Arrange
        using var kit = Kit();
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_before", AgentToken = "pmt_agent_before" });

        // Act
        await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray().Select(node => node!.GetValue<string>()), Is.EqualTo(new[] { "pmt_agent_before" }));
    }

    [Test]
    public async Task AgentLogin_WhenTheOrganizationDoesNotAllowPersonalAi_FailsAndStoresNothing()
    {
        // Arrange
        using var kit = Kit();
        kit.ReturnCli = false;
        kit.AgentSkipped = true;

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("沒有開放"));
        Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task AgentLogin_WithToken_StoresItAsTheAgentTokenAfterCheckingItHasTheAgentScope()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { StandardInput = "pmt_agent_pasted\n" } };
        kit.Cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王小明", email = "ming@example.com" }, scopes = new[] { "agent:run", "blueprint:read", "blueprint:write", "items:read" } });
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_before" });

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That((stored.Token, stored.AgentToken), Is.EqualTo(("pmt_cli_before", "pmt_agent_pasted")));
    }

    [Test]
    public async Task AgentLogin_WithATokenWithoutTheAgentScope_IsRefused()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { StandardInput = "pmt_just_cli\n" } };
        kit.Cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王小明", email = "ming@example.com" }, scopes = new[] { "items:read" } });

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("agent:run"));
        Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task AuthStatus_ShowsWhetherTheAgentTokenIsStored()
    {
        // Arrange
        using var kit = new LoginKit();
        kit.Cli.LoggedInByEnvironment();
        kit.Cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王小明", email = "ming@example.com", organizationName = "範例公司", role = "member" } });
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, AgentToken = "pmt_agent" });

        // Act
        var withAgent = await kit.RunAsync("auth", "status");
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl });
        using var second = new StringWriter();
        var withoutAgent = await kit.RunAsync("auth", "status");

        // Assert
        Assert.That(withAgent.Out, Does.Contain("本機代理的權杖：已存"));
        Assert.That(withoutAgent.Out, Does.Contain("本機代理的權杖：沒有"));
    }

    [Test]
    public async Task AuthLogout_RemovesBothTokensAndSaysWhichOnes()
    {
        // Arrange
        using var kit = new LoginKit();
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_a", AgentToken = "pmt_b" });

        // Act
        var result = await kit.RunAsync("auth", "logout", "--json");

        // Assert
        var json = JsonDocument.Parse(result.Out).RootElement;
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(json.GetProperty("removed").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("cliToken").GetBoolean(), Is.True);
            Assert.That(json.GetProperty("agentToken").GetBoolean(), Is.True);
            Assert.That((stored.Server, stored.Token, stored.AgentToken), Is.EqualTo((CliHarness.ServerUrl, (string?)null, (string?)null)));
        }
    }

    [Test]
    public async Task AuthLogout_OnlyAgentTokenStored_SaysItDeletedTheAgentToken()
    {
        // Arrange
        using var kit = new LoginKit();
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, AgentToken = "pmt_b" });

        // Act
        var result = await kit.RunAsync("auth", "logout");

        // Assert
        Assert.That(result.Out, Does.Contain("本機代理的權杖").And.Contain("還沒有被撤銷"));
    }
}
