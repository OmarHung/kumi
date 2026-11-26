using System.Text.Json;
using PmTool.Cli.Login;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Security;

[TestFixture]
public sealed class AgentTokenScopeTests
{
    private static JsonElement Scopes(params string[] scopes) => JsonSerializer.SerializeToElement(scopes);

    [Test]
    public void IsExactAgentScopes_TheFixedSetInAnyOrder_IsTrue() =>
        Assert.That(LoginCommand.IsExactAgentScopes(Scopes("items:read", "blueprint:write", "agent:run", "blueprint:read")), Is.True);

    [TestCase("agent:run", "blueprint:read", "blueprint:write", "items:read", "items:write")]
    [TestCase("agent:run", "blueprint:read", "blueprint:write")]
    [TestCase("agent:run", "blueprint:read", "blueprint:write", "items:read", "items:read")]
    [TestCase("agent:run", "items:read")]
    public void IsExactAgentScopes_MoreOrFewerOrRepeated_IsFalse(params string[] scopes) =>
        Assert.That(LoginCommand.IsExactAgentScopes(Scopes(scopes)), Is.False);

    [Test]
    public void IsExactAgentScopes_MissingOrNotAnArray_IsFalse()
    {
        using var document = JsonDocument.Parse("\"agent:run\"");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LoginCommand.IsExactAgentScopes(null), Is.False);
            Assert.That(LoginCommand.IsExactAgentScopes(document.RootElement), Is.False);
        }
    }

    [Test]
    public async Task AgentLogin_WithAPastedTokenThatAlsoCanWriteItems_IsRefusedAndNotStored()
    {
        // Arrange（伺服器建得出 agent:run 加 items:write 的混合權杖）
        using var kit = new LoginKit { Cli = { StandardInput = "pmt_mixed\n" } };
        kit.Cli.Server.On("GET", "/v1/session", new
        {
            user = new { displayName = "王小明", email = "ming@example.com" },
            scopes = new[] { "agent:run", "blueprint:read", "blueprint:write", "items:read", "items:write" },
        });

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("不多不少"));
            Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
        }
    }

    [Test]
    public async Task AgentLogin_InTheBrowserWhenTheServerIssuesAnOverBroadToken_DoesNotStoreIt()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { IsInteractive = true }, AgentScopes = ["agent:run", "blueprint:read", "blueprint:write", "items:read", "items:write"] }
            .WithRedeemEndpoint();

        // Act
        var result = await kit.RunAsync("agent", "login", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("不多不少").And.Contain("撤銷"));
            Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
        }
    }

    [Test]
    public async Task AuthLogin_StoredTokenWithAgentScopeButOver_IsNotAdoptedAsTheAgentToken()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { IsInteractive = true } }.WithRedeemEndpoint();
        kit.Cli.Server.On("GET", "/v1/session", new
        {
            user = new { displayName = "王小明", email = "ming@example.com" },
            scopes = new[] { "agent:run", "blueprint:read", "blueprint:write", "items:read", "items:write" },
        });
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_mixed" });

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(stored.AgentToken, Is.Null);
            Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray().Select(node => node!.GetValue<string>()), Does.Contain("pmt_mixed"));
        }
    }
}
