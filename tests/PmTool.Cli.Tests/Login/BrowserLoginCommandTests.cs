using System.Net;
using System.Text.Json;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class BrowserLoginCommandTests
{
    private static readonly object Session = new
    {
        user = new { personId = "0190a000-0000-7000-8000-000000000001", displayName = "王小明", email = "ming@example.com", organizationName = "範例公司", role = "member" },
        scopes = new[] { "items:read" },
    };

    private static LoginKit Kit(bool interactive = true) => new LoginKit { Cli = { IsInteractive = interactive } }.WithRedeemEndpoint();

    [Test]
    public async Task Login_InATerminal_OpensTheAuthorizePageAndStoresTheTokenFromTheRedemption()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", $"{CliHarness.ServerUrl}/");

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        var url = new Uri(kit.Browser.Urls.Single());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("王小明").And.Contain("ming@example.com").And.Contain(CliHarness.ServerUrl));
            Assert.That(result.Out + result.Error, Does.Not.Contain(LoginKit.CliSecret));
            Assert.That(url.GetLeftPart(UriPartial.Path), Is.EqualTo($"{CliHarness.ServerUrl}/cli/authorize"));
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("cli"));
            Assert.That(kit.Browser.Query("machine"), Is.EqualTo("MacBook-Pro"));
            Assert.That(kit.Browser.Query("scope"), Is.EqualTo("items:read items:write blueprint:read"));
            Assert.That(kit.Browser.Query("challenge"), Has.Length.EqualTo(43), "S256 的 base64url");
            Assert.That((stored.Server, stored.Token, stored.AgentToken), Is.EqualTo((CliHarness.ServerUrl, LoginKit.CliSecret, (string?)null)));
            Assert.That(kit.Cli.Server.Requests.Single().Authorization, Is.Null, "兌換不用登入，不帶 Authorization");
            Assert.That(result.Error, Does.Contain("正在開瀏覽器").And.Contain("/cli/authorize"));
        }
    }

    [Test]
    public async Task Login_Redemption_SendsTheVerifierThatMatchesTheChallengeTheBrowserWasGiven()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert（fake 伺服器在 challenge 對不上時回 code_invalid，登入就不會成功）
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Redeems.Single()["codeVerifier"]!.GetValue<string>(), Has.Length.EqualTo(43));
    }

    [Test]
    public async Task Login_WithScopeOptions_AsksForExactlyThoseScopes()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--scope", "items:read", "--scope", "blueprint:write,items:read");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Query("scope"), Is.EqualTo("items:read blueprint:write"));
    }

    [TestCase("agent:run", "kumi agent login")]
    [TestCase("items:everything", "不認得的範圍")]
    public async Task Login_WithAScopeThatIsNotAllowed_IsAUsageErrorBeforeOpeningTheBrowser(string scope, string expected)
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--scope", scope);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain(expected));
        Assert.That(kit.Browser.Urls, Is.Empty);
    }

    [Test]
    public async Task Login_WhenTheUserDeniesInTheBrowser_ExitsWithErrorAndStoresNothing()
    {
        // Arrange
        using var kit = Kit();
        kit.Browser.Behavior = FakeBrowser.Deny;

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("拒絕"));
            Assert.That(kit.Cli.Server.Requests, Is.Empty, "沒有碼就不呼叫伺服器");
            Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
        }
    }

    [Test]
    public async Task Login_WhenTheBrowserNeverComesBack_GivesUpAfterTheTimeout()
    {
        // Arrange
        using var kit = Kit();
        kit.Browser.Behavior = FakeBrowser.DoNothing;
        kit.Timeout = TimeSpan.FromMilliseconds(300);

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Error, Does.Contain("沒有回應"));
        Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_AnOlderLoginPageWithTheWrongState_GetsAnExplanationAndTheRealOneStillWorks()
    {
        // Arrange
        using var kit = Kit();
        kit.Browser.Behavior = FakeBrowser.StaleThenApprove;

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().Token, Is.EqualTo(LoginKit.CliSecret));
    }

    [Test]
    public async Task Login_NoBrowser_OnlyPrintsTheUrlAndStillWaitsOnThisComputer()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var run = kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--no-browser");
        var url = await WaitForUrlAsync(kit);
        kit.Browser.ManualUrl = url.AbsoluteUri;
        await FakeBrowser.Approve(url);
        var result = await run;

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Urls, Is.Empty, "不自己開瀏覽器");
        Assert.That(result.Error, Does.Contain("請用瀏覽器開啟"));
    }

    [Test]
    public async Task Login_AgainOnTheSameServer_AsksTheServerToReplaceTheTokenItStoredBefore()
    {
        // Arrange
        using var kit = Kit();
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_old_cli", AgentToken = "pmt_old_agent" });

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray().Select(node => node!.GetValue<string>()), Is.EqualTo(new[] { "pmt_old_cli" }),
                "命令列的登入只換命令列的那一把");
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((LoginKit.CliSecret, "pmt_old_agent")), "本機代理的權杖留著");
        }
    }

    [Test]
    public async Task Login_WhenTheStoredTokenIsAnOldLocalAgentToken_DoesNotReplaceItAndKeepsItAsTheAgentToken()
    {
        // Arrange（0.6.0 以前本機代理的權杖存在 Token、帶 agent:run；撤銷它會讓正在跑的本機代理 401）
        using var kit = Kit();
        kit.Cli.Server.On("GET", "/v1/session", request => request.Authorization == "Bearer pmt_old_agent"
            ? FakeServer.Envelope(HttpStatusCode.OK, new { user = new { displayName = "王小明", email = "ming@example.com" }, scopes = new[] { "agent:run", "items:read" } })
            : LoginKit.Failure(HttpStatusCode.Unauthorized, "auth.unauthorized", "未登入"));
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_old_agent" });

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray(), Is.Empty);
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((LoginKit.CliSecret, "pmt_old_agent")));
        }
    }

    [Test]
    public async Task Login_ToAnotherServer_DoesNotSendTheOldServersTokensAndDropsThem()
    {
        // Arrange
        using var kit = Kit();
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = "https://other.example.com", Token = "pmt_other", AgentToken = "pmt_other_agent" });

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray(), Is.Empty);
        Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((LoginKit.CliSecret, (string?)null)));
    }

    [Test]
    public async Task Login_WhenTheServerRejectsTheCode_ShowsTheServersMessageAndStoresNothing()
    {
        // Arrange
        using var kit = Kit();
        kit.Cli.Server.On("POST", "/v1/cli-login/token", _ => LoginKit.Failure(HttpStatusCode.UnprocessableEntity, "cli_login.code_invalid", "登入已經過期，請重新執行登入。"));

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("登入已經過期"));
        Assert.That(File.Exists(kit.Cli.ConfigPath), Is.False);
    }

    [Test]
    public async Task Login_Json_PrintsWhoLoggedInWithoutAnySecret()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--json");

        // Assert
        var json = JsonDocument.Parse(result.Out).RootElement;
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(json.GetProperty("user").GetProperty("email").GetString(), Is.EqualTo("ming@example.com"));
        Assert.That(json.GetProperty("cliToken").GetBoolean(), Is.True);
        Assert.That(result.Out, Does.Not.Contain(LoginKit.CliSecret));
    }

    [Test]
    public async Task Login_PasteOption_KeepsTheOldPastedFlow()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { IsInteractive = true, Secret = CliHarness.Token } };
        kit.Cli.Server.On("GET", "/v1/session", Session);

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--paste");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Urls, Is.Empty);
        Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().Token, Is.EqualTo(CliHarness.Token));
    }

    [Test]
    public async Task Login_PasteKeepsTheAgentTokenOfTheSameServer()
    {
        // Arrange
        using var kit = new LoginKit { Cli = { IsInteractive = true, Secret = CliHarness.Token } };
        kit.Cli.Server.On("GET", "/v1/session", Session);
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_old", AgentToken = "pmt_agent" });

        // Act
        await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, "--paste");

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((CliHarness.Token, "pmt_agent")));
    }

    [TestCase("--paste", "--with-token")]
    [TestCase("--paste", "--scope=items:read")]
    [TestCase("--with-token", "--no-browser")]
    public async Task Login_ConflictingOptions_AreAUsageError(string first, string second)
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl, first, second);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(kit.Browser.Urls, Is.Empty);
    }

    [Test]
    public async Task Login_NotInATerminalWithoutOptions_AsksForWithToken()
    {
        // Arrange
        using var kit = Kit(interactive: false);

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--with-token"));
        Assert.That(kit.Browser.Urls, Is.Empty);
    }

    [Test]
    public async Task Login_AnInsecureRemoteServer_IsRefusedBeforeOpeningTheBrowser()
    {
        // Arrange
        using var kit = Kit();

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", "http://pm.example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--insecure"));
        Assert.That(kit.Browser.Urls, Is.Empty);
    }

    [Test]
    public async Task Login_LongMachineNameWithControlCharacters_IsCleanedAndTruncatedTo60()
    {
        // Arrange
        using var kit = Kit();
        kit.MachineName = "  " + new string('A', 80) + "\u0007  ";

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Query("machine"), Is.EqualTo(new string('A', 60)));
    }

    [Test]
    public async Task Login_MachineNameWithInvisibleFormattingAndEmoji_IsFilteredLikeTheServerAndCutOnRuneBoundaries()
    {
        // Arrange（Format、行分隔字元伺服器會整個拒絕；60 個 rune 的界線不能切在代理對中間）
        using var kit = Kit();
        kit.MachineName = "小明\u202E的\u200B電腦\u2028" + string.Concat(Enumerable.Repeat("😀", 70));

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        var machine = kit.Browser.Query("machine")!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(machine, Does.StartWith("小明的電腦😀"));
            Assert.That(machine.EnumerateRunes().Count(), Is.EqualTo(60));
            Assert.That(machine.IsNormalized(), Is.True);
        }
    }

    [Test]
    public async Task Login_MachineNameThatIsEmptyAfterFiltering_UsesAFixedName()
    {
        // Arrange
        using var kit = Kit();
        kit.MachineName = "\u200B\u0007 ";

        // Act
        var result = await kit.RunAsync("auth", "login", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Query("machine"), Is.EqualTo("computer"));
    }

    private static async Task<Uri> WaitForUrlAsync(LoginKit kit)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            string text;
            try
            {
                text = kit.ErrorWriter.ToString();
            }
            catch (ArgumentOutOfRangeException)
            {
                text = string.Empty;
            }

            // 網址印在說明文字的下一行，而且以換行結尾才算印完。
            var found = text.Split('\n')[..^1].Select(item => item.Trim()).FirstOrDefault(item => item.Contains("/cli/authorize", StringComparison.Ordinal));
            if (found is not null)
            {
                return new Uri(found);
            }

            await Task.Delay(25);
        }

        throw new TimeoutException("沒有看到授權網址。");
    }
}
