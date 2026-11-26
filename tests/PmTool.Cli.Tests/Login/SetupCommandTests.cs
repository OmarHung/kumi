using System.Net;
using PmTool.Cli.Settings;

namespace PmTool.Cli.Tests.Login;

[TestFixture]
public sealed class SetupCommandTests
{
    private const string McpUrl = $"{CliHarness.ServerUrl}/mcp";

    private FakeEngines _engines = null!;

    [SetUp]
    public void SetUp() => _engines = new FakeEngines();

    [TearDown]
    public void TearDown() => _engines.Dispose();

    /// <summary>
    /// <paramref name="answers"/> 是 skill 那一題之後的回答：精靈在有引擎時第一題問要不要裝 skill，這裡替各測試先答好（預設「否」，不碰檔案）。
    /// </summary>
    private LoginKit Kit(string answers, bool withClaude = true, bool withCodex = true, bool installSkills = false, bool skillAnswerFirst = true)
    {
        var skillAnswer = skillAnswerFirst && (withClaude || withCodex) ? (installSkills ? "y\n" : "n\n") : string.Empty;
        var kit = new LoginKit { Cli = { IsInteractive = true, StandardInput = skillAnswer + answers } }.WithRedeemEndpoint();
        kit.Cli.Environment["KUMI_AGENT_CLAUDE_PATH"] = withClaude ? _engines.ClaudePath : Path.Combine(Path.GetTempPath(), "no-such-claude");
        kit.Cli.Environment["KUMI_AGENT_CODEX_PATH"] = withCodex ? _engines.CodexPath : Path.Combine(Path.GetTempPath(), "no-such-codex");
        return kit;
    }

    private static void ServeSession(LoginKit kit, params (string Token, string[] Scopes)[] tokens) =>
        kit.Cli.Server.On("GET", "/v1/session", request =>
        {
            var known = tokens.FirstOrDefault(item => request.Authorization == $"Bearer {item.Token}");
            return known.Token is null
                ? LoginKit.Failure(HttpStatusCode.Unauthorized, "auth.unauthorized", "未登入")
                : FakeServer.Envelope(HttpStatusCode.OK, new
                {
                    user = new { displayName = "王小明", email = "ming@example.com", organizationName = "範例公司", role = "member" },
                    scopes = known.Scopes,
                });
        });

    [Test]
    public async Task Setup_AnsweringYesToEverything_LogsInOnceAndSetsUpBothEnginesAndTheAgent()
    {
        // Arrange（claude、codex、本機代理用預設，開機自動啟動預設是「否」所以明講要）
        using var kit = Kit("\n\n\ny\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(kit.Browser.Urls, Has.Count.EqualTo(1), "兩把權杖只開一次瀏覽器");
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("cli,agent"));
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((LoginKit.CliSecret, LoginKit.AgentSecret)));
            Assert.That(_engines.Calls("claude"), Does.Contain($"mcp add --scope user --transport http kumi {McpUrl}").And.Contain("mcp login kumi"));
            Assert.That(_engines.Calls("codex"), Does.Contain($"mcp add kumi --url {McpUrl}").And.Contain("mcp login kumi"));
            Assert.That(kit.SubCommands.Single(), Is.EqualTo(new[] { "agent", "enable", "--server", CliHarness.ServerUrl }));
            Assert.That(result.Out, Does.Contain("Claude Code 2.1.330").And.Contain("Codex 0.163.0"));
            Assert.That(result.Out, Does.Contain("[1/4] 登入 Kumi").And.Contain("[4/4] 本機代理"));
            Assert.That(result.Out, Does.Contain("✓ 命令列工具").And.Contain("✓ Claude Code").And.Contain("✓ Codex").And.Contain("✓ 本機代理"));
            Assert.That(result.Out + result.Error, Does.Not.Contain(LoginKit.CliSecret).And.Not.Contain(LoginKit.AgentSecret));
        }
    }

    [Test]
    public async Task Setup_AnsweringNoToEverything_StillLogsInTheCommandLineButTouchesNothingElse()
    {
        // Arrange
        using var kit = Kit("n\nn\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("cli"));
            Assert.That(_engines.Calls("claude"), Is.EqualTo(new[] { "--version" }));
            Assert.That(_engines.Calls("codex"), Is.EqualTo(new[] { "--version" }));
            Assert.That(kit.SubCommands, Is.Empty);
            Assert.That(result.Out, Does.Contain("– Claude Code").And.Contain("– Codex").And.Contain("– 本機代理"));
            Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().AgentToken, Is.Null);
        }
    }

    [Test]
    public async Task Setup_WithoutAnyEngine_OnlySetsUpTheCommandLineAndPointsToTheInstallPages()
    {
        // Arrange
        using var kit = Kit(string.Empty, withClaude: false, withCodex: false);

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("https://code.claude.com").And.Contain("https://github.com/openai/codex").And.Contain("再執行一次 kumi setup"));
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("cli"));
            Assert.That(kit.SubCommands, Is.Empty);
            Assert.That(result.Out, Does.Contain("沒有安裝").And.Contain("需要先安裝 Claude Code 或 Codex"));
        }
    }

    [Test]
    public async Task Setup_AlreadyLoggedInWithBothTokens_SkipsTheBrowser()
    {
        // Arrange
        using var kit = Kit("n\nn\ny\nn\nn\n");
        ServeSession(kit, ("pmt_cli_stored", ["items:read"]), ("pmt_agent_stored", ["agent:run", "blueprint:read", "blueprint:write", "items:read"]));
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_stored", AgentToken = "pmt_agent_stored" });

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Browser.Urls, Is.Empty);
        Assert.That(result.Out, Does.Contain("已經登入：王小明"));
    }

    [Test]
    public async Task Setup_OldStyleAgentTokenStoredAsTheCliToken_KeepsItForTheAgentInsteadOfRevokingIt()
    {
        // Arrange（0.6.0 以前本機代理的權杖是用 kumi auth login 存的：它在 Token、有 agent:run、可能正被 kumi agent start 用著）
        using var kit = Kit("n\nn\ny\nn\nn\n");
        string[] agentScopes = ["agent:run", "blueprint:read", "blueprint:write", "items:read"];
        ServeSession(kit, ("pmt_old_agent", agentScopes), (LoginKit.CliSecret, ["items:read", "items:write", "blueprint:read"]));
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_old_agent" });

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        var replaced = kit.Redeems.Single()["replaceTokens"]?.AsArray().Select(node => node?.GetValue<string>()).ToArray() ?? [];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("cli"), "本機代理的權杖已經有了，只要命令列的");
            Assert.That(replaced, Does.Not.Contain("pmt_old_agent"), "不能撤銷正在給本機代理用的權杖");
            Assert.That((stored.Token, stored.AgentToken), Is.EqualTo((LoginKit.CliSecret, "pmt_old_agent")));
        }
    }

    [Test]
    public async Task Setup_NoBrowser_IsPassedOnToBothMcpLogins()
    {
        // Arrange（透過 SSH 跑精靈的人：Claude Code、Codex 的授權頁也不能自己開瀏覽器）
        using var kit = Kit("\n\nn\n");
        ServeSession(kit, ("pmt_cli_stored", ["items:read"]));
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_stored" });

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl, "--no-browser");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(_engines.Calls("claude"), Does.Contain("mcp login kumi --no-browser"));
            Assert.That(_engines.Calls("codex"), Does.Contain("mcp login kumi --no-browser"));
            Assert.That(result.Out, Does.Contain("授權網址"));
        }
    }

    [Test]
    public async Task Setup_CliTokenStoredButAgentTokenMissing_AsksOnlyForTheAgentToken()
    {
        // Arrange
        using var kit = Kit("n\nn\ny\nn\nn\n");
        kit.ReturnCli = false;
        ServeSession(kit, ("pmt_cli_stored", ["items:read"]));
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_cli_stored" });

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        var stored = new ConfigStore(kit.Cli.ConfigPath).Load();
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(kit.Browser.Query("tokens"), Is.EqualTo("agent"));
        Assert.That((stored.Token, stored.AgentToken), Is.EqualTo(("pmt_cli_stored", LoginKit.AgentSecret)));
    }

    [Test]
    public async Task Setup_StoredCliTokenThatTheServerRejects_LogsInAgainAndReplacesIt()
    {
        // Arrange
        using var kit = Kit("n\nn\nn\n");
        ServeSession(kit);
        new ConfigStore(kit.Cli.ConfigPath).Save(new StoredConfig { Server = CliHarness.ServerUrl, Token = "pmt_revoked" });

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(kit.Redeems.Single()["replaceTokens"]!.AsArray().Select(node => node!.GetValue<string>()), Is.EqualTo(new[] { "pmt_revoked" }));
        Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().Token, Is.EqualTo(LoginKit.CliSecret));
    }

    [Test]
    public async Task Setup_McpPointsElsewhere_AsksAndReplacesItWhenTheUserAgrees()
    {
        // Arrange（claude 要設定、不換前先問、答 y；codex 不要；本機代理不要）
        using var kit = Kit("y\ny\nn\nn\n");
        _engines.SetExisting("claude", "https://old.example.com/mcp");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(result.Out, Does.Contain("已經有一個叫 kumi 的 MCP 伺服器").And.Contain("https://old.example.com/mcp"));
            Assert.That(_engines.Calls("claude"), Does.Contain("mcp remove kumi --scope user"));
            Assert.That(_engines.ExistingUrl("claude"), Is.EqualTo(McpUrl));
        }
    }

    [Test]
    public async Task Setup_McpPointsElsewhere_KeepsItByDefault()
    {
        // Arrange（換不換：直接 Enter 是 N）
        using var kit = Kit("y\n\nn\nn\n");
        _engines.SetExisting("claude", "https://old.example.com/mcp");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(_engines.ExistingUrl("claude"), Is.EqualTo("https://old.example.com/mcp"));
            Assert.That(_engines.Calls("claude"), Has.None.StartsWith("mcp remove").And.None.StartsWith("mcp add").And.None.StartsWith("mcp login"));
            Assert.That(result.Out, Does.Contain("保留原來的 kumi 設定"));
        }
    }

    [Test]
    public async Task Setup_McpAlreadyPointsHere_SkipsAddButStillLogsIn()
    {
        // Arrange
        using var kit = Kit("y\nn\nn\n");
        _engines.SetExisting("claude", $"{McpUrl}/");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(_engines.Calls("claude"), Has.None.StartsWith("mcp add").And.Contain("mcp login kumi"));
    }

    [Test]
    public async Task Setup_CodexAddLogsInByItself_DoesNotAskForASecondLogin()
    {
        // Arrange（Codex 的 mcp add 遇到支援 OAuth 的伺服器會馬上開授權頁：再跑一次 login 等於叫人按兩次「允許」）
        using var kit = Kit("n\ny\nn\n");
        _engines.SetAddLogsIn("codex");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(_engines.Calls("codex"), Does.Contain($"mcp add kumi --url {McpUrl}").And.None.StartsWith("mcp login"));
            Assert.That(result.Out, Does.Contain("✓ Codex 可以使用 Kumi 了"));
        }
    }

    [Test]
    public async Task Setup_CodexAddedButNotAuthorized_RunsLoginAfterwards()
    {
        // Arrange（預設的假 codex：add 只寫設定，沒有登入，例如使用者沒按允許就關掉了授權頁）
        using var kit = Kit("n\ny\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(_engines.Calls("codex"), Does.Contain("mcp login kumi"));
    }

    [Test]
    public async Task Setup_CodexAlreadyPointsHereAndAuthorized_SkipsAddAndLogin()
    {
        // Arrange
        using var kit = Kit("n\ny\nn\n");
        _engines.SetExisting("codex", McpUrl);
        _engines.SetAuthStatus("codex", "o_auth");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(_engines.Calls("codex"), Has.None.StartsWith("mcp add").And.None.StartsWith("mcp login"));
            Assert.That(result.Out, Does.Contain("已經授權過"));
        }
    }

    [Test]
    public async Task Setup_McpLoginFails_ReportsItContinuesWithTheNextEngineAndExitsWithOne()
    {
        // Arrange
        using var kit = Kit("y\ny\nn\n");
        _engines.SetLoginExit("claude", 1);

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Out, Does.Contain("沒有完成授權").And.Contain("/mcp"));
            Assert.That(_engines.Calls("codex"), Does.Contain("mcp login kumi"), "後面的引擎照樣設定");
            Assert.That(result.Out, Does.Contain("✗ Claude Code").And.Contain("✓ Codex"));
        }
    }

    [Test]
    public async Task Setup_McpAddFails_IsAFailureWithoutTryingToLogIn()
    {
        // Arrange（claude 的 add 會失敗：把假引擎換成一律失敗的版本。claude 的 add 是捕捉輸出跑的，原因要帶進總結）
        using var kit = Kit("y\nn\nn\n");
        File.WriteAllText(_engines.ClaudePath, "#!/bin/bash\ncase \"$1\" in --version) echo \"2.1.330 (Claude Code)\";; mcp) case \"$2\" in get) exit 1;; add) echo 'boom: config is read-only' >&2; exit 3;; login) echo login >> /dev/null;; esac;; esac\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Out, Does.Contain("結束代碼 3").And.Contain("config is read-only").And.Not.Contain("已加上 MCP"));
    }

    [Test]
    public async Task Setup_CodexAddFails_ReportsTheExitCodeWithoutTryingToLogIn()
    {
        // Arrange（codex 的 add 是繼承終端機跑的：原因已經印在使用者眼前，總結只寫結束代碼）
        using var kit = Kit("n\ny\nn\n");
        File.WriteAllText(_engines.CodexPath, "#!/bin/bash\ncase \"$1\" in --version) echo \"codex-cli 0.163.0\";; mcp) case \"$2\" in list) echo '[]';; add) exit 3;; login) touch \"$0.login-called\";; esac;; esac\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Out, Does.Contain("codex mcp add 失敗（結束代碼 3）"));
            Assert.That(File.Exists(_engines.CodexPath + ".login-called"), Is.False, "add 失敗就不登入");
        }
    }

    [Test]
    public async Task Setup_OrganizationDoesNotAllowPersonalAi_SkipsTheAgentAndStillSucceeds()
    {
        // Arrange
        using var kit = Kit("n\nn\ny\ny\n");
        kit.AgentSkipped = true;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(result.Out, Does.Contain("組織沒有開放成員使用自己的 AI 帳號"));
            Assert.That(kit.SubCommands, Is.Empty, "沒有權杖就不啟動");
            Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().AgentToken, Is.Null);
        }
    }

    [Test]
    public async Task Setup_UserDeniesInTheBrowser_StillSetsUpTheEnginesAndExitsWithOne()
    {
        // Arrange
        using var kit = Kit("y\nn\nn\n");
        kit.Browser.Behavior = FakeBrowser.Deny;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Out, Does.Contain("拒絕").And.Contain("✗ 命令列工具").And.Contain("kumi auth login"));
            Assert.That(_engines.Calls("claude"), Does.Contain("mcp login kumi"));
        }
    }

    [Test]
    public async Task Setup_AutostartNotSupportedOnThisSystem_DoesNotAskAboutItAndOffersToStartInThisWindow()
    {
        // Arrange（claude、codex、本機代理，然後「現在啟動嗎」答 y）
        using var kit = Kit("n\nn\ny\ny\n");
        kit.AutostartSupported = false;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(result.Out, Does.Not.Contain("自動在背景啟動"));
            Assert.That(result.Out, Does.Contain("現在在這個視窗啟動本機代理嗎"));
            Assert.That(kit.SubCommands.Single(), Is.EqualTo(new[] { "agent", "start", "--server", CliHarness.ServerUrl }));
        }
    }

    [Test]
    public async Task Setup_PressingEnterOnTheAutostartQuestion_MeansNoAndTheQuestionSaysItUsesYourSubscription()
    {
        // Arrange（claude n、codex n、本機代理 y、自動啟動直接 Enter、現在啟動 n）
        using var kit = Kit("n\nn\ny\n\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("AI 訂閱").And.Contain("[y/N]"));
            Assert.That(kit.SubCommands, Is.Empty, "沒有明講要，就不能設開機自動啟動");
        }
    }

    [Test]
    public async Task Setup_AutostartDeclined_OffersToStartButDoesNotWhenTheUserSaysNo()
    {
        // Arrange（claude n、codex n、本機代理 y、自動啟動 n、現在啟動 n）
        using var kit = Kit("n\nn\ny\nn\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(result.Out, Does.Contain("現在在這個視窗啟動本機代理嗎"));
        Assert.That(kit.SubCommands, Is.Empty);
    }

    [Test]
    public async Task Setup_EnableFails_IsReportedWithAHintAndTheExitCodeIsOne()
    {
        // Arrange
        using var kit = Kit("n\nn\ny\ny\n");
        kit.SubCommandExit = _ => 1;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(result.Out, Does.Contain("✗ 本機代理").And.Contain("kumi agent enable 沒有成功").And.Contain("kumi agent start"));
        Assert.That(result.Out, Does.Not.Contain("現在在這個視窗啟動"));
    }

    [Test]
    public async Task Setup_InvalidAnswer_AsksAgainAndAcceptsChineseAnswers()
    {
        // Arrange（claude：「也許」→ 重問 →「否」；codex：「是」；本機代理 n）
        using var kit = Kit("maybe\n否\n是\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(result.Out, Does.Contain("請回答 y"));
        Assert.That(_engines.Calls("claude"), Is.EqualTo(new[] { "--version" }));
        Assert.That(_engines.Calls("codex"), Does.Contain("mcp login kumi"));
    }

    [Test]
    public async Task Setup_NoServerGiven_AsksForIt()
    {
        // Arrange
        // 沒給伺服器時先問伺服器，skill 那一題排在它後面，所以這個測試自己寫全部的回答。
        using var kit = Kit($"{CliHarness.ServerUrl}\nn\nn\nn\nn\n", skillAnswerFirst: false);

        // Act
        var result = await kit.RunAsync("setup");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(new ConfigStore(kit.Cli.ConfigPath).Load().Server, Is.EqualTo(CliHarness.ServerUrl));
    }

    [Test]
    public async Task Setup_AnswersEndBeforeAllQuestionsAreAnswered_CancelsWithoutChangingAnything()
    {
        // Arrange
        using var kit = Kit("y\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
        Assert.That(kit.Browser.Urls, Is.Empty);
        Assert.That(_engines.Calls("claude"), Has.None.StartsWith("mcp add").And.None.StartsWith("mcp login"));
    }

    [Test]
    public async Task Setup_NotInATerminal_IsAUsageError()
    {
        // Arrange
        using var kit = Kit(string.Empty);
        kit.Cli.IsInteractive = false;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("終端機"));
        Assert.That(kit.Browser.Urls, Is.Empty);
    }

    [Test]
    public async Task Setup_JsonOption_IsAUsageError()
    {
        // Arrange
        using var kit = Kit(string.Empty);

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl, "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("--json"));
    }

    [Test]
    public async Task Setup_ServerUrlWithUnsafeCharacters_DoesNotPassItToTheEngines()
    {
        // Arrange（底線在主機名稱裡：不在白名單內）
        using var kit = Kit("y\nn\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", "https://pm_tool.example.com");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
        Assert.That(result.Out, Does.Contain("不支援的字元"));
        Assert.That(_engines.Calls("claude"), Is.EqualTo(new[] { "--version" }));
    }

    [Test]
    public void McpParsers_ReadTheOutputOfTheRealCommands()
    {
        // Act
        var claude = PmTool.Cli.Setup.McpConfigurator.ParseClaudeGet("kumi:\n  Scope: User config\n  Status: ! Needs authentication\n  Type: http\n  URL: https://pm.example.com/mcp\n");
        var codex = PmTool.Cli.Setup.McpConfigurator.ParseCodexList("""[{"name":"other","transport":{"type":"stdio"}},{"name":"kumi","transport":{"type":"streamable_http","url":"https://pm.example.com/mcp"},"auth_status":"not_logged_in"}]""");
        var none = PmTool.Cli.Setup.McpConfigurator.ParseCodexList("[]");
        var garbage = PmTool.Cli.Setup.McpConfigurator.ParseCodexList("not json");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claude, Is.EqualTo(new PmTool.Cli.Setup.ExistingMcp(true, "https://pm.example.com/mcp")));
            Assert.That(codex, Is.EqualTo(new PmTool.Cli.Setup.ExistingMcp(true, "https://pm.example.com/mcp")));
            Assert.That(none.Exists, Is.False);
            Assert.That(garbage.Exists, Is.False);
        }
    }

    [TestCase("https://pm.example.com/mcp", true)]
    [TestCase("http://127.0.0.1:8080/mcp", true)]
    [TestCase("http://[::1]:8080/mcp", true)]
    [TestCase("https://pm.example.com/mcp; rm -rf", false)]
    [TestCase("https://pm.example.com/mcp&calc", false)]
    [TestCase("https://pm.example.com/mcp?x=1", false)]
    [TestCase("https://pm.example.com/mcp\n", false)]
    public void IsSafeUrl_OnlyAllowsAConservativeCharacterSet(string url, bool expected) =>
        Assert.That(PmTool.Cli.Setup.McpConfigurator.IsSafeUrl(url), Is.EqualTo(expected));

    [TestCase("https://pm.example.com/mcp", true)]
    [TestCase("http://127.0.0.1:8080/mcp", true)]
    [TestCase("http://localhost:8080/mcp", true)]
    [TestCase("http://[::1]:8080/mcp", true)]
    [TestCase("http://pm.example.com/mcp", false)]
    [TestCase("http://203.0.113.5:8080/mcp", false)]
    [TestCase("ftp://pm.example.com/mcp", false)]
    public void IsAllowedTransport_OnlyHttpsOrLoopback(string url, bool expected) =>
        Assert.That(PmTool.Cli.Setup.McpConfigurator.IsAllowedTransport(url), Is.EqualTo(expected));

    [Test]
    public async Task Setup_ARemoteHttpServerEvenWithInsecure_DoesNotWriteTheMcpUrlIntoTheEngine()
    {
        // Arrange（--insecure 只放行這次指令的連線；寫進引擎設定檔的網址會一直被用，不能是明文）
        using var kit = Kit("y\nn\nn\n");

        // Act
        var result = await kit.RunAsync("setup", "--server", "http://pm.example.com", "--insecure");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Out, Does.Contain("不是 https"));
            Assert.That(_engines.Calls("claude"), Has.None.Contains("mcp add"));
            Assert.That(_engines.ExistingUrl("claude"), Is.Null);
        }
    }

    [Test]
    public async Task Setup_AnsweringYesToSkills_InstallsThemForTheDetectedEnginesOnly()
    {
        // Arrange（只有 Claude Code；其餘的問題都答否）
        using var kit = Kit("n\nn\n", withCodex: false, installSkills: true);
        var home = kit.Cli.Environment["HOME"]!;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error + result.Out);
            Assert.That(File.Exists(Path.Combine(home, ".claude", "skills", "kumi", ".kumi-skill.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(home, ".claude", "skills", "kumi-planning", "SKILL.md")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(home, ".codex")), Is.False);
            Assert.That(result.Out, Does.Contain("[2/2] AI 代理的 skill").And.Contain("✓ AI 代理的 skill"));
        }
    }

    [Test]
    public async Task Setup_AnsweringNoToSkills_LeavesTheSkillFoldersAlone()
    {
        // Arrange
        using var kit = Kit("n\nn\nn\n");
        var home = kit.Cli.Environment["HOME"]!;

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(Path.Combine(home, ".claude")), Is.False);
            Assert.That(result.Out, Does.Contain("– AI 代理的 skill　略過"));
        }
    }

    [Test]
    public async Task Setup_WithoutAnyEngine_DoesNotAskAboutSkills()
    {
        // Arrange
        using var kit = Kit(string.Empty, withClaude: false, withCodex: false);

        // Act
        var result = await kit.RunAsync("setup", "--server", CliHarness.ServerUrl);

        // Assert
        Assert.That(result.Out, Does.Not.Contain("要不要").And.Not.Contain("安裝給 AI 代理用的 skill").And.Contain("– AI 代理的 skill　需要先安裝"));
    }
}
