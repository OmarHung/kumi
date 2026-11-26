using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>引擎版本不在驗證過的範圍：預設不提供，<c>--allow-untested-engine</c> 才放行。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class EngineVersionDiscoveryTests
{
    private FakeEngineScripts _scripts = null!;

    [SetUp]
    public void SetUp() => _scripts = new FakeEngineScripts();

    [TearDown]
    public void TearDown() => _scripts.Dispose();

    private string Claude(string version) => _scripts.Script("claude", $$"""
        case "$1 $2" in
          "--version ") echo "{{version}} (Claude Code)"; exit 0 ;;
          "auth status") echo '{"loggedIn": true}'; exit 0 ;;
          "--help ") echo "{{EngineDiscoveryTests.ClaudeHelp}}"; exit 0 ;;
        esac
        exit 0
        """);

    private string Codex(string version) => _scripts.Script("codex", $$"""
        case "$1 $2" in
          "--version ") echo "codex-cli {{version}}"; exit 0 ;;
          "login status") echo "Logged in" >&2; exit 0 ;;
          "exec --help") echo "--strict-config --ignore-user-config --ignore-rules --ephemeral --json"; exit 0 ;;
          "features list") echo 'shell_tool                            stable             true'; exit 0 ;;
        esac
        exit 0
        """);

    private static Task<DiscoveryResult> Discover(string kind, string executable, bool allowUntested = false) =>
        new EngineDiscovery(
            name => name == (kind == EngineKinds.Codex ? ExecutableLocator.CodexVariable : ExecutableLocator.ClaudeVariable) ? executable : null,
            "pmt_x", "https://pm.example.com",
            new AgentOptions { ProbeTimeout = TimeSpan.FromSeconds(10), AllowUntestedEngine = allowUntested })
            .DiscoverAsync([kind], CancellationToken.None);

    [TestCase("2.1.329")]
    [TestCase("3.0.0")]
    public async Task Claude_OutsideTheTestedRange_IsNotOfferedAndTheNoteNamesTheFlag(string version)
    {
        // Act
        var result = await Discover(EngineKinds.ClaudeCode, Claude(version));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Engines, Is.Empty);
            Assert.That(result.Notes.Single(), Does.Contain(version).And.Contain("--allow-untested-engine"));
        }
    }

    [TestCase("0.162.0")]
    [TestCase("1.0.0")]
    public async Task Codex_OutsideTheTestedRange_IsNotOffered(string version)
    {
        // Act
        var result = await Discover(EngineKinds.Codex, Codex(version));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Engines, Is.Empty);
            Assert.That(result.Notes.Single(), Does.Contain("--allow-untested-engine"));
        }
    }

    [Test]
    public async Task AllowUntestedEngine_OffersAnEngineOutsideTheRange()
    {
        // Act
        var claude = await Discover(EngineKinds.ClaudeCode, Claude("3.4.0"), allowUntested: true);
        var codex = await Discover(EngineKinds.Codex, Codex("1.2.0"), allowUntested: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claude.Engines.Single().Version, Is.EqualTo("3.4.0"));
            Assert.That(codex.Engines.Single().Version, Is.EqualTo("1.2.0"));
        }
    }

    [Test]
    public async Task AllowUntestedEngine_StillRequiresTheSafetyFlags()
    {
        // Arrange（版本放行不等於旗標也放行）
        var executable = _scripts.Script("claude", """
            case "$1 $2" in
              "--version ") echo "3.4.0 (Claude Code)"; exit 0 ;;
              "--help ") echo "--tools"; exit 0 ;;
            esac
            exit 0
            """);

        // Act
        var result = await Discover(EngineKinds.ClaudeCode, executable, allowUntested: true);

        // Assert
        Assert.That(result.Engines, Is.Empty);
    }
}
