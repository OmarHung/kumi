using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class CredentialsForAgentTests
{
    private static CliContext Context(CliHarness cli, Dictionary<string, string?> environment) => new()
    {
        Http = new HttpClient(),
        Out = TextWriter.Null,
        Error = TextWriter.Null,
        In = TextReader.Null,
        ConfigPath = cli.ConfigPath,
        GetEnvironmentVariable = name => environment.GetValueOrDefault(name),
    };

    [Test]
    public void ResolveForAgent_PrefersTheEnvironmentThenTheAgentTokenThenTheCliToken()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli", AgentToken = "pmt_agent" });

        // Act
        var fromFile = Credentials.ResolveForAgent(Context(cli, []), null);
        var fromEnvironment = Credentials.ResolveForAgent(Context(cli, new() { ["KUMI_TOKEN"] = "pmt_env" }), null);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That((fromFile.Token, fromFile.Source), Is.EqualTo(("pmt_agent", TokenSource.ConfigFile)));
            Assert.That((fromEnvironment.Token, fromEnvironment.Source), Is.EqualTo(("pmt_env", TokenSource.Environment)));
        }
    }

    [Test]
    public void ResolveForAgent_WithoutAnAgentToken_FallsBackToTheCliToken()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli" });

        // Act
        var credentials = Credentials.ResolveForAgent(Context(cli, []), null);

        // Assert
        Assert.That(credentials.Token, Is.EqualTo("pmt_cli"));
    }

    [Test]
    public void Resolve_NeverUsesTheAgentToken()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, AgentToken = "pmt_agent" });

        // Act
        var exception = Assert.Throws<CliException>(() => Credentials.Resolve(Context(cli, []), null));

        // Assert
        Assert.That(exception!.Message, Does.Contain("kumi auth login"));
    }

    [Test]
    public void ResolveForAgent_ForAnotherServer_DoesNotHandOverTheStoredToken()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, AgentToken = "pmt_agent" });

        // Act
        var exception = Assert.Throws<CliException>(() => Credentials.ResolveForAgent(Context(cli, []), "https://other.example.com"));

        // Assert
        Assert.That(exception!.Message, Does.Contain("不會送給").And.Contain("kumi agent login"));
    }

    [Test]
    public void ResolveForAgent_NothingStored_TellsToRunAgentLogin()
    {
        // Arrange
        using var cli = new CliHarness();

        // Act
        var exception = Assert.Throws<CliException>(() => Credentials.ResolveForAgent(Context(cli, new() { ["KUMI_SERVER"] = CliHarness.ServerUrl }), null));

        // Assert
        Assert.That(exception!.Message, Does.Contain("kumi agent login"));
    }

    [Test]
    public void ClearTokens_RemovesBothAndKeepsTheServer()
    {
        // Arrange
        using var cli = new CliHarness();
        var store = new ConfigStore(cli.ConfigPath);
        store.Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_a", AgentToken = "pmt_b" });

        // Act
        var first = store.ClearTokens();
        var second = store.ClearTokens();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(new ClearedTokens(true, true)));
            Assert.That(second.Any, Is.False);
            Assert.That(store.Load(), Is.EqualTo(new StoredConfig { Server = CliHarness.ServerUrl }));
        }
    }
}
