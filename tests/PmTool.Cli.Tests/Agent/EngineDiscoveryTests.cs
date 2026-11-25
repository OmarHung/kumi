using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class EngineDiscoveryTests
{
    private FakeEngineScripts _scripts = null!;

    [SetUp]
    public void SetUp() => _scripts = new FakeEngineScripts();

    [TearDown]
    public void TearDown() => _scripts.Dispose();

    private Func<string, string?> Environment(string? claude = null, string? codex = null, string? codexHome = null) => name => name switch
    {
        ExecutableLocator.ClaudeVariable => claude,
        ExecutableLocator.CodexVariable => codex,
        "CODEX_HOME" => codexHome,
        _ => null,
    };

    private Task<DiscoveryResult> Discover(Func<string, string?> environment, params string[] kinds) =>
        new EngineDiscovery(environment, "pmt_x", "https://pm.example.com", new AgentOptions { ProbeTimeout = TimeSpan.FromSeconds(10) })
            .DiscoverAsync(kinds.Length == 0 ? [EngineKinds.ClaudeCode, EngineKinds.Codex] : kinds, CancellationToken.None);

    /// <summary>真的 claude --help 裡這些旗標的寫法（append-system-prompt-file 是 [-file] 的合寫）。</summary>
    internal const string ClaudeHelp = "--tools --allowedTools --setting-sources --strict-mcp-config --disable-slash-commands --append-system-prompt[-file] --no-session-persistence --permission-mode --mcp-config";

    private string FakeClaude(bool loggedIn = true, int versionExit = 0, string statusJson = """{"loggedIn": true}""") => _scripts.Script("claude", $$"""
        case "$1 $2" in
          "--version ") echo "2.1.330 (Claude Code)"; exit {{versionExit}} ;;
          "auth status") echo '{{statusJson}}'; exit {{(loggedIn ? 0 : 1)}} ;;
          "--help ") echo "{{ClaudeHelp}}"; exit 0 ;;
          "-p --output-format") echo '{"type":"system","subtype":"init","model":"claude-opus-5-5"}'; sleep 300 ;;
        esac
        echo "不該呼叫：$@" > "{{_scripts.Out}}/unexpected"
        exit 9
        """);

    private string FakeCodex(bool loggedIn = true, bool hasShellTool = true, bool oldHelp = false, string features = "") => _scripts.Script("codex", $$"""
        case "$1 $2" in
          "--version ") echo "codex-cli 0.163.0"; exit 0 ;;
          "login status") echo "{{(loggedIn ? "Logged in using ChatGPT" : "Not logged in")}}" >&2; exit {{(loggedIn ? 0 : 1)}} ;;
          "exec --help") echo "{{(oldHelp ? "--json" : "--strict-config --ignore-user-config --ignore-rules --ephemeral --json")}}"; exit 0 ;;
          "features list")
            {{(hasShellTool ? "echo 'shell_tool                            stable             true'" : string.Empty)}}
            echo 'view_image                            stable             true'
            echo 'apps                                  stable             true'
            echo 'something_new                         stable             true'
            {{features}}
            exit 0 ;;
        esac
        echo "不該呼叫：$@" > "{{_scripts.Out}}/unexpected"
        exit 9
        """);

    [Test]
    public async Task Claude_WhenInstalledAndLoggedIn_IsOfferedWithItsVersionAndTheFixedModels()
    {
        // Act
        var result = await Discover(Environment(claude: FakeClaude()), EngineKinds.ClaudeCode);

        // Assert
        var engine = result.Engines.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Kind, Is.EqualTo("claude_code"));
            Assert.That(engine.Version, Is.EqualTo("2.1.330"));
            Assert.That(engine.Models.Select(model => model.Id), Is.EqualTo(new[] { "default", "opus", "sonnet", "haiku" }));
            Assert.That(result.Notes, Is.Empty);
            Assert.That(_scripts.Exists("unexpected"), Is.False, "只跑不送提示的指令");
        }
    }

    [Test]
    public async Task Claude_WhenAuthStatusFailsOrSaysLoggedOut_IsSkippedWithLoginInstructions()
    {
        // Act
        var failed = await Discover(Environment(claude: FakeClaude(loggedIn: false)), EngineKinds.ClaudeCode);
        var loggedOut = await Discover(Environment(claude: FakeClaude(statusJson: """{"loggedIn": false}""")), EngineKinds.ClaudeCode);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failed.Engines, Is.Empty);
            Assert.That(failed.Notes.Single(), Does.Contain("沒有登入").And.Contain("claude"));
            Assert.That(loggedOut.Engines, Is.Empty);
        }
    }

    [Test]
    public async Task Claude_WhenMissingOrBroken_IsSkippedWithAReason()
    {
        // Act
        var missing = await Discover(Environment(claude: Path.Combine(_scripts.Directory, "nope")), EngineKinds.ClaudeCode);
        var broken = await Discover(Environment(claude: FakeClaude(versionExit: 1)), EngineKinds.ClaudeCode);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Notes.Single(), Does.Contain("找不到 Claude Code"));
            Assert.That(broken.Notes.Single(), Does.Contain("跑不起來"));
        }
    }

    [Test]
    public async Task Codex_DisablesOnlyTheFeaturesItListsAndReadsModelsFromTheCache()
    {
        // Arrange
        var home = Directory.CreateDirectory(Path.Combine(_scripts.Directory, "codex-home")).FullName;
        await File.WriteAllTextAsync(Path.Combine(home, "models_cache.json"), """
            {"models":[
              {"slug":"gpt-b","display_name":"B","visibility":"list","priority":2,"default_reasoning_level":"medium","supported_reasoning_levels":[{"effort":"low"},{"effort":"high"},{"effort":"ultra"}]},
              {"slug":"gpt-a","display_name":"A","visibility":"list","priority":1,"default_reasoning_level":"high","supported_reasoning_levels":[{"effort":"low"},{"effort":"medium"},{"effort":"high"},{"effort":"max"}]},
              {"slug":"gpt-hidden","visibility":"hide","priority":0,"supported_reasoning_levels":[]}
            ]}
            """);
        var executable = FakeCodex();

        // Act
        var result = await Discover(Environment(codex: executable, codexHome: home), EngineKinds.Codex);

        // Assert
        var engine = result.Engines.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Version, Is.EqualTo("0.163.0"));
            Assert.That(engine.Models.Select(model => model.Id), Is.EqualTo(new[] { "default", "gpt-a", "gpt-b" }), "預設在最前面、依優先順序、不列隱藏的");
            Assert.That(engine.Models[1].Efforts, Is.EqualTo(new[] { "low", "medium", "high", "max" }));
            Assert.That(engine.Models[2].Efforts, Is.EqualTo(new[] { "low", "high" }), "ultra 要子代理，略過");
            Assert.That(engine.Models[0].Efforts, Is.EqualTo(new[] { "low", "high" }), "預設只列每個模型都有的");
            Assert.That(engine.Models.Select(model => model.DefaultEffort), Is.EqualTo(new[] { null, "high", null }),
                "gpt-b 的預設 medium 不在它的選項裡所以是 null；default 實際用哪個模型不確定，一律 null");
        }

        var plan = new CodexSpec([.. CodexSpec.DisableCandidates.Where(name => name is "shell_tool" or "view_image" or "apps")]).Plan(TestJobs.Make(EngineKinds.Codex), "/tmp/w");
        Assert.That(plan.Arguments.Where((_, index) => index > 0 && plan.Arguments[index - 1] == "--disable"), Is.EqualTo(new[] { "shell_tool", "view_image", "apps" }));
    }

    [Test]
    public async Task Codex_RunsEngineWithOnlyTheDisableFlagsItsFeatureListShows()
    {
        // Arrange
        var executable = _scripts.Script("codex", $$$"""
            case "$1 $2" in
              "--version ") echo "codex-cli 0.163.0"; exit 0 ;;
              "login status") exit 0 ;;
              "exec --help") echo "--strict-config --ignore-user-config --ignore-rules --ephemeral"; exit 0 ;;
              "features list") echo 'shell_tool stable true'; echo 'view_image stable true'; echo 'unrelated stable true'; exit 0 ;;
            esac
            cat > /dev/null
            printf '%s\n' "$@" > "{{{_scripts.Out}}}/args.txt"
            echo '{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}'
            """);
        var result = await Discover(Environment(codex: executable, codexHome: _scripts.Directory), EngineKinds.Codex);

        // Act
        var outcome = await result.Engines.Single().RunAsync(TestJobs.Make(EngineKinds.Codex), new ListSink(), CancellationToken.None);

        // Assert
        var args = _scripts.Read("args.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(args.Where((_, index) => index > 0 && args[index - 1] == "--disable"), Is.EqualTo(new[] { "shell_tool", "view_image" }));
        }
    }

    [Test]
    public async Task Codex_WithoutAShellToolFeature_IsNotOfferedBecauseItCouldReadAnyFile()
    {
        // Act
        var result = await Discover(Environment(codex: FakeCodex(hasShellTool: false)), EngineKinds.Codex);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Engines, Is.Empty);
            Assert.That(result.Notes.Single(), Does.Contain("shell_tool").And.Contain("為了安全不提供 Codex"));
        }
    }

    [Test]
    public async Task Codex_WhenLoggedOutOrTooOld_IsSkippedWithAReason()
    {
        // Act
        var loggedOut = await Discover(Environment(codex: FakeCodex(loggedIn: false)), EngineKinds.Codex);
        var old = await Discover(Environment(codex: FakeCodex(oldHelp: true)), EngineKinds.Codex);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loggedOut.Notes.Single(), Does.Contain("codex login"));
            Assert.That(old.Notes.Single(), Does.Contain("太舊").And.Contain("--strict-config"));
        }
    }

    [Test]
    public async Task Codex_WithoutAReadableModelCache_OffersOnlyTheDefaultModel()
    {
        // Act
        var result = await Discover(Environment(codex: FakeCodex(), codexHome: Path.Combine(_scripts.Directory, "no-such-home")), EngineKinds.Codex);

        // Assert
        Assert.That(result.Engines.Single().Models.Select(model => model.Id), Is.EqualTo(new[] { "default" }));
    }

    [Test]
    public async Task Discover_OfBothEngines_ListsWhatWorksAndExplainsWhatDoesNot()
    {
        // Act
        var result = await Discover(Environment(claude: FakeClaude(), codex: Path.Combine(_scripts.Directory, "nope")));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Engines.Select(engine => engine.Kind), Is.EqualTo(new[] { "claude_code" }));
            Assert.That(result.Notes.Single(), Does.Contain("找不到 Codex"));
        }
    }

    [TestCase("tools")]
    [TestCase(".")]
    [TestCase(@"C:tools")]
    public void ExecutableLocator_SkipsPathEntriesThatAreNotFullPaths(string entry)
    {
        Assert.That(ExecutableLocator.FindOnPath("tool", entry), Is.Null);
    }

    [Test]
    public void ExecutableLocator_FindsOnPathOnlyExecutableFiles()
    {
        // Arrange
        var plain = Path.Combine(_scripts.Directory, "tool");
        File.WriteAllText(plain, "#!/bin/sh\n");
        var path = _scripts.Directory + Path.PathSeparator + "/no/such/dir";

        // Act
        var notExecutable = ExecutableLocator.FindOnPath("tool", path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(plain, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        var executable = ExecutableLocator.FindOnPath("tool", path);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(notExecutable, Is.Null);
            Assert.That(executable, Is.EqualTo(plain));
            Assert.That(ExecutableLocator.FindOnPath("tool", null), Is.Null);
            Assert.That(ExecutableLocator.Find(EngineKinds.Codex, _ => null), Is.Null);
        }
    }

    [Test]
    public async Task Claude_WithAnOldHelpMissingSafetyFlags_IsNotOffered()
    {
        // Arrange
        var old = _scripts.Script("claude", """
            case "$1 $2" in
              "--version ") echo "1.0.0"; exit 0 ;;
              "--help ") echo "--tools --allowedTools"; exit 0 ;;
            esac
            exit 9
            """);

        // Act
        var result = await Discover(Environment(claude: old), EngineKinds.ClaudeCode);

        // Assert
        Assert.That(result.Engines, Is.Empty);
        Assert.That(result.Notes.Single(), Does.Contain("太舊").And.Contain("為了安全不提供 Claude Code"));
    }

    [TestCase("claude.cmd")]
    [TestCase("claude.BAT")]
    public async Task BatchScriptEngines_AreRefusedBecauseCmdExeEscapingAllowsCommandInjection(string name)
    {
        // Arrange
        var script = _scripts.Script(name, "exit 0");

        // Act
        var result = await Discover(Environment(claude: script), EngineKinds.ClaudeCode);

        // Assert
        Assert.That(result.Engines, Is.Empty);
        Assert.That(result.Notes.Single(), Does.Contain(".cmd／.bat").And.Contain(".exe"));
    }

    [Test]
    public async Task Probes_RunInAFreshPrivateEmptyDirectoryEachTime()
    {
        // Arrange
        var script = _scripts.Script("claude", $$"""
            [ "$1" = "-p" ] && exit 1
            pwd >> "{{_scripts.Out}}/dirs.txt"
            ls -ld . | cut -c1-10 >> "{{_scripts.Out}}/dirs.txt"
            ls -A | wc -l >> "{{_scripts.Out}}/dirs.txt"
            case "$1 $2" in
              "--version ") echo "2.1.330"; exit 0 ;;
              "--help ") echo "{{ClaudeHelp}}"; exit 0 ;;
              "auth status") echo '{"loggedIn": true}'; exit 0 ;;
            esac
            """);

        // Act
        await Discover(Environment(claude: script), EngineKinds.ClaudeCode);

        // Assert
        var lines = _scripts.Read("dirs.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var directories = lines.Where((_, index) => index % 3 == 0).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(directories, Has.Length.EqualTo(3));
            Assert.That(directories.Distinct().Count(), Is.EqualTo(3), "每次新建");
            Assert.That(lines.Where((_, index) => index % 3 == 1), Is.All.EqualTo("drwx------"));
            Assert.That(lines.Where((_, index) => index % 3 == 2).Select(count => count.Trim()), Is.All.EqualTo("0"));
            Assert.That(directories.Any(Directory.Exists), Is.False, "用完就刪");
        }
    }

    [Test]
    public void ExecutableLocator_SkipsRelativePathEntriesAndEnvOverridesMustExist()
    {
        // Arrange
        var path = "." + Path.PathSeparator + string.Empty + Path.PathSeparator + "relative/bin";

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ExecutableLocator.FindOnPath("sh", path), Is.Null);
            Assert.That(ExecutableLocator.Find(EngineKinds.Codex, name => name == ExecutableLocator.CodexVariable ? "/no/such/codex" : null), Is.Null);
            Assert.That(ExecutableLocator.IsBatchScript("C:\\x\\codex.CMD"), Is.True);
            Assert.That(ExecutableLocator.IsBatchScript("/usr/bin/codex"), Is.False);
        }
    }

    private string ProbingClaude() => _scripts.Script("claude", $$$"""
        case "$1 $2" in
          "--version ") echo "2.1.330 (Claude Code)"; exit 0 ;;
          "auth status") echo '{"loggedIn": true}'; exit 0 ;;
          "--help ") echo "{{{ClaudeHelp}}}"; exit 0 ;;
        esac
        cat > /dev/null
        printf '%s\n' "$@" > "{{{_scripts.Out}}}/probe-$$.txt"
        env > "{{{_scripts.Out}}}/probe-env-$$.txt"
        pwd >> "{{{_scripts.Out}}}/probe-$$.txt"
        model=""
        while [ $# -gt 0 ]; do [ "$1" = "--model" ] && model="$2"; shift; done
        case "$model" in
          opus) echo '{"type":"system","subtype":"init","model":"claude-opus-5-5"}' ;;
          sonnet) echo '{"type":"system","subtype":"init","model":"claude-sonnet-5-5-20260101"}' ;;
          haiku) sleep 300 ;;
          *) echo '{"type":"system","subtype":"init","model":"claude-fable-5-2"}' ;;
        esac
        sleep 1
        echo leaked > "{{{_scripts.Out}}}/leaked"
        sleep 300
        """);

    [Test]
    public async Task Claude_ModelNamesCarryTheResolvedVersionsWhileIdsStayAliases_AndAProbeThatTimesOutFallsBack()
    {
        // Arrange
        var options = new AgentOptions { ProbeTimeout = TimeSpan.FromSeconds(10), ModelProbeTimeout = TimeSpan.FromSeconds(3) };
        var timer = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var result = await new EngineDiscovery(Environment(claude: ProbingClaude()), "pmt_x", "https://pm.example.com", options)
            .DiscoverAsync([EngineKinds.ClaudeCode], CancellationToken.None);

        // Assert
        var engine = result.Engines.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(engine.Models.Select(model => model.Id), Is.EqualTo(new[] { "default", "opus", "sonnet", "haiku" }));
            Assert.That(engine.Models.Select(model => model.DisplayName), Is.EqualTo(new[] { "預設（Fable 5.2）", "Opus 5.5", "Sonnet 5.5", "Haiku" }));
            Assert.That(engine.Models[3].Efforts, Is.EqualTo(new[] { "low", "medium", "high" }), "思考深度不變");
            Assert.That(engine.Detail, Is.EqualTo("預設 Fable 5.2"));
            Assert.That(_scripts.Exists("leaked"), Is.False, "讀到 init 就結束，後面的事不會發生");
            Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)), "四個平行跑，逾時的退回原名");
        }
    }

    [Test]
    public async Task Claude_ModelProbe_RunsIsolatedLikeARealJobWithoutSecrets()
    {
        // Arrange
        Environment_SetSecret("ANTHROPIC_API_KEY");
        try
        {
            var options = new AgentOptions { ModelProbeTimeout = TimeSpan.FromSeconds(3) };

            // Act
            await new EngineDiscovery(Environment(claude: ProbingClaude()), "pmt_x", "https://pm.example.com", options)
                .DiscoverAsync([EngineKinds.ClaudeCode], CancellationToken.None);

            // Assert
            var files = Directory.GetFiles(_scripts.Out, "probe-*.txt").Where(file => !Path.GetFileName(file).StartsWith("probe-env", StringComparison.Ordinal)).ToArray();
            var all = files.Select(File.ReadAllText).ToArray();
            var opus = all.Single(text => text.Contains("opus", StringComparison.Ordinal));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(files, Has.Length.EqualTo(4));
                Assert.That(opus, Does.Contain("--tools\n\n").And.Contain("--strict-mcp-config").And.Contain("--disable-slash-commands")
                    .And.Contain("--no-session-persistence").And.Contain("dontAsk").And.Contain("project"));
                Assert.That(all.Count(text => text.Contains("--model", StringComparison.Ordinal)), Is.EqualTo(3), "預設不帶 --model");
                Assert.That(Directory.GetFiles(_scripts.Out, "probe-env-*.txt").Select(File.ReadAllText), Is.All.Not.Contain("ANTHROPIC_API_KEY"));
            }
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        }
    }

    private static void Environment_SetSecret(string name) => System.Environment.SetEnvironmentVariable(name, "secret-for-test");
}
