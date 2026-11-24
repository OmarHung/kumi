using System.Net;
using System.Text.Json;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests;

[TestFixture]
public sealed class AuthCommandTests
{
    private static readonly object Session = new
    {
        user = new
        {
            personId = "0190a000-0000-7000-8000-000000000001", displayName = "王小明", email = "ming@example.com",
            organizationName = "範例公司", role = "member",
        },
    };

    [Test]
    public async Task Login_WithTokenFromStandardInput_ChecksItWithTheServerAndStoresServerAndToken()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = $"  {CliHarness.Token}\n" };
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", $"{CliHarness.ServerUrl}/", "--with-token");

        // Assert
        var stored = JsonDocument.Parse(await File.ReadAllTextAsync(cli.ConfigPath)).RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("王小明").And.Contain("ming@example.com").And.Contain(CliHarness.ServerUrl));
            Assert.That(result.Out, Does.Not.Contain(CliHarness.Token));
            Assert.That(cli.Server.Requests.Single().Authorization, Is.EqualTo($"Bearer {CliHarness.Token}"));
            Assert.That(stored.GetProperty("server").GetString(), Is.EqualTo(CliHarness.ServerUrl), "尾端的斜線去掉");
            Assert.That(stored.GetProperty("token").GetString(), Is.EqualTo(CliHarness.Token));
        }
    }

    [Test]
    public async Task Login_Interactive_AsksForTheServerAndReadsTheTokenWithoutEcho()
    {
        // Arrange
        using var cli = new CliHarness { IsInteractive = true, Secret = CliHarness.Token, StandardInput = $"{CliHarness.ServerUrl}\n" };
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--paste");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Error, Does.Contain("伺服器網址").And.Contain("不會顯示"));
            Assert.That(result.Out + result.Error, Does.Not.Contain(CliHarness.Token));
            Assert.That(new ConfigStore(cli.ConfigPath).Load().Token, Is.EqualTo(CliHarness.Token));
        }
    }

    [Test]
    public async Task Login_InteractiveAndTheSecretPromptIsCancelled_ExitsWithoutStoring()
    {
        // Arrange
        using var cli = new CliHarness { IsInteractive = true, Secret = null };
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;

        // Act
        var result = await cli.RunAsync("auth", "login", "--paste");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(File.Exists(cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_NotInteractiveWithoutWithToken_AsksForWithToken()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;

        // Act
        var result = await cli.RunAsync("auth", "login");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--with-token"));
    }

    [Test]
    public async Task Login_NotInteractiveWithoutAServer_AsksForOne()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };

        // Act
        var result = await cli.RunAsync("auth", "login", "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("KUMI_SERVER"));
    }

    [TestCase("not-a-token")]
    [TestCase("pmt_has space")]
    [TestCase("")]
    public async Task Login_TextThatIsNotAToken_IsRejectedWithoutCallingTheServer(string pasted)
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = pasted };

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(cli.Server.Requests, Is.Empty);
        Assert.That(File.Exists(cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_ServerRejectsTheToken_ExitsWithOneAndStoresNothing()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Server.OnError("GET", "/v1/session", HttpStatusCode.Unauthorized, "auth.required", "需要登入。");

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("權杖無效"));
        Assert.That(File.Exists(cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_PlainHttpToARemoteServer_WarnsAboutClearText()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", "http://pm.example.com", "--with-token", "--insecure");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(result.Error, Does.Contain("不是 https"));
    }

    [Test]
    public async Task Login_TokenEnvironmentVariableIsSet_SaysItWillTakePriority()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Environment["KUMI_TOKEN"] = "pmt_other";
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token");

        // Assert
        Assert.That(result.Error, Does.Contain("KUMI_TOKEN"));
    }

    [Test]
    public async Task Login_JsonFlag_PrintsTheSessionDataAsIs()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--with-token", "--json");

        // Assert
        var printed = JsonDocument.Parse(result.Out).RootElement;
        Assert.That(printed.GetProperty("user").GetProperty("displayName").GetString(), Is.EqualTo("王小明"));
    }

    [Test]
    public async Task Login_ServerFromTheSavedConfig_IsReusedWhenNoneIsGiven()
    {
        // Arrange
        using var cli = new CliHarness { StandardInput = CliHarness.Token };
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl });
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "login", "--with-token");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(cli.Server.Requests.Single().Uri.Host, Is.EqualTo("pm.example.com"));
    }

    [Test]
    public async Task Status_ShowsTheServerWhoAndWhereTheTokenComesFrom()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "status");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain(CliHarness.ServerUrl).And.Contain("王小明").And.Contain("範例公司").And.Contain("KUMI_TOKEN"));
            Assert.That(result.Out, Does.Not.Contain(CliHarness.Token));
        }
    }

    [Test]
    public async Task Status_FromTheConfigFile_NamesTheFileAndShowsScopesWhenTheServerSendsThem()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });
        cli.Server.On("GET", "/v1/session", new { user = new { displayName = "王小明", email = "ming@example.com" }, scopes = new[] { "items:read", "blueprint:read" } });

        // Act
        var result = await cli.RunAsync("auth", "status");

        // Assert
        Assert.That(result.Out, Does.Contain(cli.ConfigPath).And.Contain("items:read、blueprint:read"));
    }

    [Test]
    public async Task Status_ShowsTheOrganizationRoleInChinese_AndJsonKeepsTheCode()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/session", new
        {
            user = new { displayName = "王小明", email = "ming@example.com", organizationName = "範例組織", role = "owner" },
        });

        // Act
        var text = await cli.RunAsync("auth", "status");
        var json = await cli.RunAsync("auth", "status", "--json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text.Out, Does.Contain("組織：範例組織（擁有者）").And.Not.Contain("owner"));
            Assert.That(json.Out, Does.Contain("\"role\": \"owner\""));
        }
    }

    [Test]
    public async Task Status_NotLoggedIn_ExitsWithOneAndPointsToLogin()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_SERVER"] = CliHarness.ServerUrl;

        // Act
        var result = await cli.RunAsync("auth", "status");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("kumi auth login"));
    }

    [Test]
    public async Task Status_JsonFlag_PrintsTheSessionData()
    {
        // Arrange
        using var cli = new CliHarness().LoggedInByEnvironment();
        cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await cli.RunAsync("auth", "status", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(result.Out).RootElement.GetProperty("user").GetProperty("email").GetString(), Is.EqualTo("ming@example.com"));
    }

    [Test]
    public async Task Logout_RemovesTheTokenButKeepsTheServerAndExplainsItIsNotRevoked()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        var stored = new ConfigStore(cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
            Assert.That(result.Out, Does.Contain("還沒有被撤銷").And.Contain("存取權杖"));
            Assert.That((stored.Server, stored.Token), Is.EqualTo((CliHarness.ServerUrl, (string?)null)));
            Assert.That(cli.Server.Requests, Is.Empty, "登出不連伺服器");
        }
    }

    [Test]
    public async Task Logout_NothingStored_SaysSoAndWarnsWhenTheEnvironmentStillHasAToken()
    {
        // Arrange
        using var cli = new CliHarness();
        cli.Environment["KUMI_TOKEN"] = Token();

        // Act
        var result = await cli.RunAsync("auth", "logout");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success));
        Assert.That(result.Out, Does.Contain("沒有存權杖"));
        Assert.That(result.Error, Does.Contain("KUMI_TOKEN"));
    }

    [Test]
    public async Task Logout_JsonFlag_ReportsWhetherSomethingWasRemoved()
    {
        // Arrange
        using var cli = new CliHarness();
        new ConfigStore(cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = CliHarness.Token });

        // Act
        var first = await cli.RunAsync("auth", "logout", "--json");
        var second = await cli.RunAsync("auth", "logout", "--json");

        // Assert
        Assert.That(JsonDocument.Parse(first.Out).RootElement.GetProperty("removed").GetBoolean(), Is.True);
        Assert.That(JsonDocument.Parse(second.Out).RootElement.GetProperty("removed").GetBoolean(), Is.False);
    }

    private static string Token() => CliHarness.Token;
}
