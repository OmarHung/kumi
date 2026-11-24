using System.Xml.Linq;
using PmTool.Cli.Agent.Autostart;

namespace PmTool.Cli.Tests.Agent.Autostart;

[TestFixture]
public sealed class PlatformFilesTests
{
    private static AutostartSpec Spec(string? name = null, string[]? engines = null, bool insecure = false) => new(
        "/opt/kumi bin/kumi", "https://kumi.example.com", name, engines ?? [], insecure,
        new Dictionary<string, string> { ["PATH"] = "/usr/bin:/opt/a&b", ["KUMI_AGENT_CLAUDE_PATH"] = "/x/claude" },
        "/Users/example/Library/Logs/Kumi/agent.log");

    private static IReadOnlyList<string> ArrayOf(XElement dict, string key) =>
        [.. dict.Elements().SkipWhile(element => element.Value != key).Skip(1).First().Elements("string").Select(element => element.Value)];

    private static string StringOf(XElement dict, string key) =>
        dict.Elements().SkipWhile(element => element.Value != key).Skip(1).First().Value;

    [Test]
    public void Plist_HasTheLabelArgumentsEnvironmentAndLogs()
    {
        // Arrange
        var spec = Spec(name: "小明的 <Mac> & \"筆電\"", engines: ["claude-code", "codex"], insecure: true);

        // Act
        var text = LaunchAgentBackend.RenderPlist(spec);
        var dict = XDocument.Parse(text).Root!.Element("dict")!;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>"));
            Assert.That(StringOf(dict, "Label"), Is.EqualTo("kumi.local-agent"));
            Assert.That(ArrayOf(dict, "ProgramArguments"), Is.EqualTo(new[]
            {
                "/opt/kumi bin/kumi", "agent", "start", "--service", "--server", "https://kumi.example.com",
                "--name", "小明的 <Mac> & \"筆電\"", "--engine", "claude-code", "--engine", "codex", "--insecure",
            }));
            Assert.That(dict.Elements("key").Select(key => key.Value), Does.Contain("RunAtLoad"));
            Assert.That(dict.Elements("true").Count(), Is.EqualTo(1));
            Assert.That(dict.Elements("key").Select(key => key.Value), Does.Not.Contain("KeepAlive"));
            Assert.That(StringOf(dict, "StandardOutPath"), Is.EqualTo(spec.LogPath));
            Assert.That(StringOf(dict, "StandardErrorPath"), Is.EqualTo(spec.LogPath));
        }
    }

    [Test]
    public void Plist_EscapesEnvironmentValuesAndCarriesNoToken()
    {
        // Arrange
        var spec = Spec();

        // Act
        var text = LaunchAgentBackend.RenderPlist(spec);
        var environment = XDocument.Parse(text).Root!.Element("dict")!.Elements("dict").Single();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("/opt/a&amp;b"));
            Assert.That(StringOf(environment, "PATH"), Is.EqualTo("/usr/bin:/opt/a&b"));
            Assert.That(StringOf(environment, "KUMI_AGENT_CLAUDE_PATH"), Is.EqualTo("/x/claude"));
            Assert.That(text, Does.Not.Contain("pmt_").And.Not.Contain("TOKEN"));
        }
    }

    [Test]
    public void ParsePid_ReadsTheRunningPidFromLaunchctlPrint()
    {
        // Arrange
        const string running = "gui/501/kumi.local-agent = {\n\tactive count = 1\n\tpath = /x\n\tstate = running\n\n\tpid = 4242\n\tlast exit code = (never exited)\n}";
        const string waiting = "gui/501/kumi.local-agent = {\n\tstate = not running\n\tlast exit code = 1\n}";

        // Act & Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(LaunchAgentBackend.ParsePid(running), Is.EqualTo(4242));
            Assert.That(LaunchAgentBackend.ParsePid(waiting), Is.Null);
            Assert.That(LaunchAgentBackend.ParsePid(string.Empty), Is.Null);
        }
    }

    [Test]
    public void RunCommandLine_QuotesTheExecutableAndArgumentsWithSpacesAndQuotes()
    {
        // Arrange
        var spec = Spec(name: "小明 的\"電腦\"", engines: ["codex"]) with { KumiPath = @"C:\Program Files\Kumi\kumi.exe" };

        // Act
        var line = WindowsRunBackend.CommandLine(spec);

        // Assert
        Assert.That(line, Is.EqualTo(
            @"""C:\Program Files\Kumi\kumi.exe"" agent start --service --server https://kumi.example.com --name ""小明 的\""電腦\"""" --engine codex"));
    }

    [TestCase("plain", "plain")]
    [TestCase("", "\"\"")]
    [TestCase("a b", "\"a b\"")]
    [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [TestCase(@"C:\dir with space\", @"""C:\dir with space\\""")]
    [TestCase(@"back\slash", @"back\slash")]
    [TestCase(@"a\""b", @"""a\\\""b""")]
    public void Quote_FollowsTheWindowsCommandLineRules(string argument, string expected) =>
        Assert.That(WindowsRunBackend.Quote(argument), Is.EqualTo(expected));
}
