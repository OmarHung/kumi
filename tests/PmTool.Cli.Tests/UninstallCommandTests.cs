using System.Net;
using System.Text.Json.Nodes;
using PmTool.Cli.Agent;
using PmTool.Cli.Commands;
using PmTool.Cli.Settings;
using PmTool.Cli.Tests.Agent;
using PmTool.Cli.Tests.Agent.Autostart;
using PmTool.Cli.Tests.Login;

namespace PmTool.Cli.Tests;

/// <summary>
/// kumi uninstall：每一步先問（或 --yes 照預設），只動測試自己的資料夾、假的引擎與假的開機自動啟動。
/// </summary>
[TestFixture]
public sealed class UninstallCommandTests
{
    private const string CliToken = "pmt_cli_example";
    private const string AgentToken = "pmt_agent_example";
    private const string RevokePath = "/v1/account/tokens/current";

    private static CliHarness NewCli(bool interactive = true, string answers = "", bool withTokens = true)
    {
        var cli = new CliHarness { IsInteractive = interactive, StandardInput = answers };
        new ConfigStore(cli.ConfigPath).Save(withTokens
            ? new StoredConfig { Server = CliHarness.ServerUrl, Token = CliToken, AgentToken = AgentToken }
            : new StoredConfig { Server = CliHarness.ServerUrl });
        cli.Server.On("DELETE", RevokePath, new { });
        cli.Environment[ExecutableLocator.ClaudeVariable] = cli.PathOf("no-such-claude");
        cli.Environment[ExecutableLocator.CodexVariable] = cli.PathOf("no-such-codex");
        return cli;
    }

    private static string Executable(CliHarness cli)
    {
        var path = cli.Update.ExecutablePath!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "kumi");
        return path;
    }

    private static string ConfigDirectory(CliHarness cli) => Path.GetDirectoryName(cli.ConfigPath)!;

    private static int Revocations(CliHarness cli) => cli.Server.To("DELETE", RevokePath).Count;

    private static JsonNode? Step(JsonNode json, string name) =>
        json["steps"]!.AsArray().FirstOrDefault(step => step!["step"]!.GetValue<string>() == name);

    [Test]
    public async Task WithoutATerminalOrYes_RefusesAndChangesNothing()
    {
        // Arrange
        using var cli = NewCli(interactive: false);
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
            Assert.That(result.Error, Does.Contain("--yes"));
            Assert.That(File.Exists(cli.ConfigPath), Is.True);
            Assert.That(File.Exists(executable), Is.True);
            Assert.That(Revocations(cli), Is.Zero);
        }
    }

    [Test]
    public async Task JsonWithoutYes_RefusesBecauseQuestionsWouldMixIntoTheJson()
    {
        // Arrange
        using var cli = NewCli();

        // Act
        var result = await cli.RunAsync("uninstall", "--json");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(File.Exists(cli.ConfigPath), Is.True);
    }

    [Test]
    public async Task UnknownSkipValue_IsAUsageError()
    {
        // Arrange
        using var cli = NewCli();

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "everything");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Invalid));
        Assert.That(result.Error, Does.Contain("everything"));
        Assert.That(File.Exists(cli.ConfigPath), Is.True);
    }

    [Test]
    public async Task AnsweringYesToEachStep_RevokesTheTokensThenDeletesTheConfigAndTheExecutable()
    {
        // Arrange：問題依序是撤銷權杖、刪設定、刪執行檔（沒有的步驟不問）。
        using var cli = NewCli(answers: "y\ny\ny\n");
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Revocations(cli), Is.EqualTo(2));
            Assert.That(Directory.Exists(ConfigDirectory(cli)), Is.False);
            Assert.That(File.Exists(executable), Is.False);
            Assert.That(result.Out, Does.Contain("撤銷伺服器").And.Contain("設定").And.Contain("執行檔"));
            Assert.That(result.Out, Does.Contain("OAuth"), "MCP 的授權只能在網頁撤銷，要提醒");
        }
    }

    [Test]
    public async Task EnterAcceptsTheDefaultOfEachStep()
    {
        // Arrange
        using var cli = NewCli(answers: "\n\n\n");
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Revocations(cli), Is.EqualTo(2));
            Assert.That(File.Exists(cli.ConfigPath), Is.False);
            Assert.That(File.Exists(executable), Is.False);
        }
    }

    [Test]
    public async Task DecliningTheRevocation_KeepsTheConfigByDefaultSoTheTokensAreNotLost()
    {
        // Arrange：不撤銷；刪設定那一題按 Enter（這時預設是不刪）；執行檔答否。
        using var cli = NewCli(answers: "n\n\nn\n");
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Revocations(cli), Is.Zero);
            Assert.That(new ConfigStore(cli.ConfigPath).Load().Token, Is.EqualTo(CliToken));
            Assert.That(File.Exists(executable), Is.True);
            Assert.That(result.Out, Does.Contain("[y/N]"), "權杖沒撤銷時，刪設定的預設要改成不刪");
        }
    }

    [Test]
    public async Task TokensTheServerNoLongerAccepts_CountAsRevokedSoTheConfigCanGo()
    {
        // Arrange：權杖早就過期或被撤銷（伺服器回 401）：它已經不能用了，不必擋著刪設定。
        using var cli = NewCli(interactive: false);
        cli.Server.OnError("DELETE", RevokePath, HttpStatusCode.Unauthorized, "auth.unauthorized", "未登入");

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Contain("已經失效"));
            Assert.That(File.Exists(cli.ConfigPath), Is.False);
        }
    }

    [Test]
    public async Task RevocationFails_ReportsItAndStillLetsYouContinue()
    {
        // Arrange
        using var cli = NewCli(interactive: false);
        cli.Server.OnError("DELETE", RevokePath, HttpStatusCode.ServiceUnavailable, "server.unavailable", "暫時無法服務");

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("沒有撤銷成功"));
            Assert.That(File.Exists(cli.ConfigPath), Is.True, "權杖沒撤銷成功，--yes 不刪設定（預設是不刪）");
        }
    }

    [Test]
    public async Task Yes_RunsEveryStepWithItsDefaultWithoutAsking()
    {
        // Arrange
        using var cli = NewCli(interactive: false);
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(result.Out, Does.Not.Contain("[Y/n]"));
            Assert.That(Revocations(cli), Is.EqualTo(2));
            Assert.That(File.Exists(cli.ConfigPath), Is.False);
            Assert.That(File.Exists(executable), Is.False);
        }
    }

    [Test]
    public async Task Skip_LeavesThatStepAlone()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var executable = Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable", "--skip", "files");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(cli.ConfigPath), Is.True);
            Assert.That(File.Exists(executable), Is.True);
        }
    }

    [Test]
    public async Task Json_ReportsEachStep()
    {
        // Arrange
        using var cli = NewCli(interactive: false);
        Executable(cli);

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--json", "--skip", "executable");

        // Assert
        var json = JsonNode.Parse(result.Out)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Step(json, "agent")!["status"]!.GetValue<string>(), Is.EqualTo("not_found"));
            Assert.That(Step(json, "tokens")!["status"]!.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(Step(json, "files")!["status"]!.GetValue<string>(), Is.EqualTo("done"));
            Assert.That(Step(json, "executable")!["status"]!.GetValue<string>(), Is.EqualTo("skipped"));
            Assert.That(json["reminders"]!.AsArray(), Is.Not.Empty);
        }
    }

    [Test]
    public async Task AlsoRemovesTheLegacyConfigFolderAndTheAgentsLeftoverWorkFolders()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var legacy = Path.Combine(Path.GetDirectoryName(ConfigDirectory(cli))!, "pm-tool");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "config.json"), "{}");
        var work = Directory.CreateDirectory(Path.Combine(cli.Autostart.TempDirectory, WorkDirectories.Prefix + "example")).FullName;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.SetLastWriteTimeUtc(work, DateTime.UtcNow.AddHours(-1));
        var fresh = Directory.CreateDirectory(Path.Combine(cli.Autostart.TempDirectory, WorkDirectories.Prefix + "running")).FullName;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fresh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(legacy), Is.False);
            Assert.That(Directory.Exists(work), Is.False);
            Assert.That(Directory.Exists(fresh), Is.True, "剛建的可能是別的設定檔跑的代理正在用的，不刪");
        }
    }

    [Test]
    public async Task ConfigInAFolderNotNamedKumi_DeletesOnlyKumisOwnFiles()
    {
        // Arrange：設定檔被指到共用的資料夾時，不能把整個資料夾刪掉。
        using var cli = new CliHarness { IsInteractive = false };
        var shared = Directory.CreateDirectory(cli.PathOf("shared")).FullName;
        var other = Path.Combine(shared, "someone-else.txt");
        File.WriteAllText(other, "keep");
        File.WriteAllText(Path.Combine(shared, "config.json"), "{}");
        File.WriteAllText(Path.Combine(shared, AgentLock.InfoFileName), "{}");
        using var context = new SharedConfigCli(cli, Path.Combine(shared, "config.json"));

        // Act
        var result = await context.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(other), Is.True);
            Assert.That(File.Exists(Path.Combine(shared, "config.json")), Is.False);
            Assert.That(File.Exists(Path.Combine(shared, AgentLock.InfoFileName)), Is.False);
        }
    }

    [Test]
    public async Task InstalledWithDotnetTool_DoesNotDeleteItselfAndSaysHow()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var executable = Executable(cli);
        cli.Update = cli.Update with { IsStandalone = false };

        // Act
        var result = await cli.RunAsync("uninstall", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(executable), Is.True);
            Assert.That(result.Out, Does.Contain("dotnet tool uninstall"));
        }
    }

    [Test]
    public async Task OnWindows_CannotDeleteTheRunningExecutableSoExplainsHow()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var executable = Executable(cli);
        File.WriteAllText(executable + ".old", "previous");
        cli.Update = cli.Update with { Os = UpdateOs.Windows };

        // Act
        var result = await cli.RunAsync("uninstall", "--yes");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(File.Exists(executable), Is.True);
            Assert.That(File.Exists(executable + ".old"), Is.False, "kumi update 留下的舊檔沒在執行，可以刪");
            Assert.That(result.Out, Does.Contain(executable).And.Contain("PATH"));
        }
    }

    [Test]
    public async Task DecliningToDeleteTheExecutable_AlsoKeepsTheOldCopy()
    {
        // Arrange
        using var cli = NewCli(answers: "n\n", withTokens: false);
        var executable = Executable(cli);
        File.WriteAllText(executable + ".old", "previous");

        // Act
        var result = await cli.RunAsync("uninstall", "--skip", "files");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(File.Exists(executable + ".old"), Is.True);
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task WorkFolderThatCannotBeRemoved_IsReportedInsteadOfClaimingSuccess()
    {
        // Arrange：不是 700 的暫存資料夾不是 kumi 建的樣子，清理時不碰；那就不能說「已刪掉」。
        using var cli = NewCli(interactive: false, withTokens: false);
        var work = Directory.CreateDirectory(Path.Combine(cli.Autostart.TempDirectory, WorkDirectories.Prefix + "odd")).FullName;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(work, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        }

        Directory.SetLastWriteTimeUtc(work, DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(result.Error, Does.Contain("暫存工作資料夾"));
            Assert.That(Directory.Exists(work), Is.True);
        }
    }

    [Test]
    public void ClaudeMcpOutsideTheUserScope_IsRecognised()
    {
        // Arrange
        const string local = "kumi:\n  Scope: Local config (private to you in this project)\n  Type: http\n  URL: https://pm.example.com/mcp\n";
        const string user = "kumi:\n  Scope: User config (available in all your projects)\n  Type: http\n  URL: https://pm.example.com/mcp\n";

        // Act
        var fromLocal = PmTool.Cli.Setup.McpConfigurator.ParseClaudeGet(local);
        var fromUser = PmTool.Cli.Setup.McpConfigurator.ParseClaudeGet(user);

        // Assert
        Assert.That((fromLocal.IsUserScope, fromUser.IsUserScope), Is.EqualTo((false, true)));
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task ConfigFolderThatIsALink_OnlyTheLinkGoesAndThatIsSaid()
    {
        // Arrange：dotfiles 之類把 ~/.config/kumi 連到別處時，只刪連結，而且要說它指到的沒動。
        using var cli = new CliHarness { IsInteractive = false };
        var real = Directory.CreateDirectory(cli.PathOf("dotfiles-kumi")).FullName;
        File.WriteAllText(Path.Combine(real, "config.json"), "{}");
        var link = Path.Combine(cli.PathOf("links"), "kumi");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        Directory.CreateSymbolicLink(link, real);
        using var context = new SharedConfigCli(cli, Path.Combine(link, "config.json"));

        // Act
        var result = await context.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(link), Is.False);
            Assert.That(File.Exists(Path.Combine(real, "config.json")), Is.True);
            Assert.That(result.Out, Does.Contain(real));
        }
    }

    [Test]
    public async Task ExecutableThatIsNotKumi_IsNeverDeleted()
    {
        // Arrange：程序的路徑不是 kumi（例如被別的主程式載入）時，不能刪到別人的檔案。
        using var cli = NewCli(interactive: false, withTokens: false);
        var host = cli.PathOf("dotnet");
        File.WriteAllText(host, "host");
        cli.Update = cli.Update with { ExecutablePath = host };

        // Act
        var result = await cli.RunAsync("uninstall", "--yes");

        // Assert
        Assert.That(File.Exists(host), Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task ForegroundAgentRunning_KeepsTheConfigAndFails()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var (held, _) = AgentLock.TryAcquire(cli.ConfigPath, pid: 4242, background: false);
        using var agentLock = held;

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Error));
            Assert.That(File.Exists(cli.ConfigPath), Is.True);
            Assert.That(result.Error, Does.Contain("本機代理").And.Contain("4242"));
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task BackgroundAgent_IsStoppedAndItsLogsDeleted()
    {
        // Arrange
        using var scripts = new FakeEngineScripts();
        using var os = new FakeAutostart();
        using var cli = NewCli(answers: "y\n");
        cli.Autostart = os.Build(cli.ConfigPath);
        cli.Environment["PATH"] = "/usr/bin:/bin";
        cli.Environment[ExecutableLocator.ClaudeVariable] = scripts.Script("claude", "exit 0");
        cli.Server.On("GET", "/v1/session", AutostartCommandTests.Session("agent:run", "blueprint:read", "blueprint:write", "items:read"));
        await cli.RunAsync("agent", "enable");
        var logs = cli.Autostart.LogDirectory!;
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "agent.log"), "log");
        cli.IsInteractive = false;

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(os.BackgroundRunning, Is.False);
            Assert.That(File.Exists(os.PlistPath), Is.False);
            Assert.That(Directory.Exists(logs), Is.False);
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task McpPointingAtThisServer_IsRemovedWhenYouSayYes()
    {
        // Arrange：問題依序是撤銷權杖、Claude Code 的 MCP、刪設定、刪執行檔。
        using var engines = new FakeEngines();
        using var cli = NewCli(answers: "n\ny\nn\nn\n");
        cli.Environment[ExecutableLocator.ClaudeVariable] = engines.ClaudePath;
        engines.SetExisting("claude", $"{CliHarness.ServerUrl}/mcp");

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(engines.Calls("claude"), Does.Contain("mcp remove kumi --scope user"));
            Assert.That(engines.ExistingUrl("claude"), Is.Null);
        }
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task McpPointingAtThisServer_IsKeptWhenYouSayNo()
    {
        // Arrange
        using var engines = new FakeEngines();
        using var cli = NewCli(answers: "n\nn\nn\nn\n");
        cli.Environment[ExecutableLocator.ClaudeVariable] = engines.ClaudePath;
        engines.SetExisting("claude", $"{CliHarness.ServerUrl}/mcp");

        // Act
        var result = await cli.RunAsync("uninstall");

        // Assert
        Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
        Assert.That(engines.ExistingUrl("claude"), Is.EqualTo($"{CliHarness.ServerUrl}/mcp"));
    }

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    public async Task Yes_KeepsAnMcpThatPointsAtAnotherServer()
    {
        // Arrange
        using var engines = new FakeEngines();
        using var cli = NewCli(interactive: false, withTokens: false);
        cli.Environment[ExecutableLocator.ClaudeVariable] = engines.ClaudePath;
        cli.Environment[ExecutableLocator.CodexVariable] = engines.CodexPath;
        engines.SetExisting("claude", $"{CliHarness.ServerUrl}/mcp");
        engines.SetExisting("codex", "https://other.example.com/mcp");

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(engines.ExistingUrl("claude"), Is.Null);
            Assert.That(engines.ExistingUrl("codex"), Is.EqualTo("https://other.example.com/mcp"));
            Assert.That(result.Out, Does.Contain("other.example.com"));
        }
    }

    [Test]
    public async Task InstalledSkills_AreRemovedButTheUsersOwnAreNot()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        var skills = Path.Combine(cli.Environment["HOME"]!, ".claude", "skills");
        Directory.CreateDirectory(Path.Combine(cli.Environment["HOME"]!, ".claude"));
        await cli.RunAsync("skill", "install", "--agent", "claude-code");
        var mine = Directory.CreateDirectory(Path.Combine(skills, "my-own")).FullName;
        File.WriteAllText(Path.Combine(mine, "SKILL.md"), "---\nname: my-own\n---\n");

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ExitCode, Is.EqualTo(ExitCodes.Success), result.Error);
            Assert.That(Directory.Exists(Path.Combine(skills, "kumi")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(skills, "kumi-planning")), Is.False);
            Assert.That(Directory.Exists(mine), Is.True);
        }
    }

    [Test]
    public async Task EnvironmentStillHasAToken_IsMentionedInTheReminders()
    {
        // Arrange
        using var cli = NewCli(interactive: false, withTokens: false);
        cli.Environment["KUMI_TOKEN"] = CliToken;

        // Act
        var result = await cli.RunAsync("uninstall", "--yes", "--skip", "executable");

        // Assert
        Assert.That(result.Out, Does.Contain("KUMI_TOKEN"));
    }

    /// <summary>設定檔放在別的位置：CliHarness 固定用 kumi 資料夾，這裡換掉設定檔的路徑跑一次。</summary>
    private sealed class SharedConfigCli(CliHarness cli, string configPath) : IDisposable
    {
        public async Task<CliResult> RunAsync(params string[] args)
        {
            using var http = new HttpClient(cli.Server, disposeHandler: false);
            var output = new StringWriter();
            var error = new StringWriter();
            var context = new CliContext
            {
                Http = http,
                Out = output,
                Error = error,
                In = new StringReader(string.Empty),
                ConfigPath = configPath,
                WorkingDirectory = cli.WorkingDirectory,
                GetEnvironmentVariable = name => cli.Environment.GetValueOrDefault(name),
                Update = cli.Update,
                Autostart = cli.Autostart,
            };
            var exitCode = await CliRunner.RunAsync(args, context, CancellationToken.None);
            return new CliResult(exitCode, output.ToString(), error.ToString());
        }

        public void Dispose()
        {
        }
    }
}
