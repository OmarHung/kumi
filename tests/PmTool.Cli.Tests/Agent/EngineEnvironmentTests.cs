using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class EngineEnvironmentTests
{
    private static KeyValuePair<string, string?> Var(string name, string? value = "x") => new(name, value);

    private static readonly IReadOnlyDictionary<string, string> NoAdditions = new Dictionary<string, string>();

    [Test]
    public void Build_KeepsWhatTheEngineNeedsToRunAndLogIn()
    {
        // Arrange
        string[] names = ["PATH", "HOME", "LANG", "LC_ALL", "TERM", "HTTPS_PROXY", "https_proxy", "NODE_EXTRA_CA_CERTS", "CODEX_HOME", "CLAUDE_CONFIG_DIR", "XDG_CONFIG_HOME", "TMPDIR"];

        // Act
        var result = EngineEnvironment.Build(names.Select(name => Var(name)), NoAdditions);

        // Assert
        Assert.That(result.Keys, Is.EquivalentTo(names));
    }

    [TestCase("ANTHROPIC_API_KEY")]
    [TestCase("OPENAI_API_KEY")]
    [TestCase("CODEX_API_KEY")]
    [TestCase("KUMI_TOKEN")]
    [TestCase("AWS_SECRET_ACCESS_KEY")]
    [TestCase("DATABASE_URL")]
    [TestCase("GITHUB_TOKEN")]
    [TestCase("SSH_AUTH_SOCK")]
    [TestCase("KUMI_ENGINE_PASSTHROUGH_ENV")]
    public void Build_DropsEverythingNotOnTheList(string name)
    {
        // Act
        var result = EngineEnvironment.Build([Var("PATH"), Var(name)], NoAdditions);

        // Assert
        Assert.That(result.Keys, Is.EqualTo(new[] { "PATH" }));
    }

    [Test]
    public void Build_AddsTheAdditionsOnTopOfTheAllowedVariables()
    {
        // Act
        var result = EngineEnvironment.Build([Var("PATH", "/bin")], new Dictionary<string, string> { ["KUMI_MCP_TOKEN"] = "pmt_x" });

        // Assert
        Assert.That(result, Is.EqualTo(new Dictionary<string, string> { ["PATH"] = "/bin", ["KUMI_MCP_TOKEN"] = "pmt_x" }));
    }

    [TestCase("KUMI_TOKEN")]
    [TestCase("kumi_token")]
    [TestCase("PM_TOKEN")]
    [TestCase("KUMI_MCP_TOKEN")]
    [TestCase("KUMI_MCP_URL")]
    public void Build_NeverPassesKumisOwnVariablesEvenWhenTheUserListsThem(string name)
    {
        // Arrange
        var source = new[] { Var("PATH"), Var(name, "leak"), Var("KUMI_ENGINE_PASSTHROUGH_ENV", $"{name},ANTHROPIC_API_KEY"), Var("ANTHROPIC_API_KEY", "k") };

        // Act
        var result = EngineEnvironment.Build(source, NoAdditions);

        // Assert
        Assert.That(result.Keys, Is.EquivalentTo(new[] { "PATH", "ANTHROPIC_API_KEY" }));
    }

    [Test]
    public void Build_LetsTheUserListExtraVariablesToPassThrough()
    {
        // Arrange
        var source = new[] { Var("PATH"), Var("MY_CORP_PROXY", "p"), Var("OTHER_SECRET"), Var("KUMI_ENGINE_PASSTHROUGH_ENV", "MY_CORP_PROXY, NOT_THERE") };

        // Act
        var result = EngineEnvironment.Build(source, NoAdditions);

        // Assert
        Assert.That(result.Keys, Is.EquivalentTo(new[] { "PATH", "MY_CORP_PROXY" }));
    }

    [Test]
    public void Build_AddsDisplayAndBrowserVariablesOnlyForInteractiveRuns()
    {
        // Arrange
        var source = new[] { Var("PATH"), Var("DISPLAY", ":0"), Var("BROWSER", "firefox") };

        // Act
        var headless = EngineEnvironment.Build(source, NoAdditions);
        var interactive = EngineEnvironment.Build(source, NoAdditions, interactive: true);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(headless.Keys, Is.EqualTo(new[] { "PATH" }));
            Assert.That(interactive.Keys, Is.EquivalentTo(new[] { "PATH", "DISPLAY", "BROWSER" }));
        }
    }

    [Test]
    public void Apply_ReplacesTheWholeEnvironmentOfTheTarget()
    {
        // Arrange
        var target = new Dictionary<string, string?> { ["SOME_CLOUD_SECRET"] = "s", ["PATH"] = "/usr/bin" };

        // Act
        EngineEnvironment.Apply(target, new Dictionary<string, string> { ["KUMI_MCP_TOKEN"] = "t" });

        // Assert
        Assert.That(target.Keys, Is.EquivalentTo(new[] { "PATH", "KUMI_MCP_TOKEN" }));
    }
}
