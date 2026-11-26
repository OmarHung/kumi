using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class HelpFlagsTests
{
    [TestCase("  --tools <tools...>   Specify the list of available tools", "--tools", true)]
    [TestCase("  --allowedTools, --allowed-tools <tools...>  Comma-separated", "--allowedTools", true)]
    [TestCase("      --strict-config   fail on unknown keys", "--strict-config", true)]
    [TestCase("  --tools-extra <x>   something else", "--tools", false)]
    [TestCase("  --no--tools   something else", "--tools", false)]
    [TestCase("  --toolsmith   something else", "--tools", false)]
    [TestCase("  see the docs about --tools_v2 for details", "--tools", false)]
    [TestCase("--tools", "--tools", true)]
    [TestCase("", "--tools", false)]
    [TestCase("  --ephemeral-session   x\n  --ephemeral   y", "--ephemeral", true)]
    [TestCase("  --foo=<value>", "--foo", true)]
    public void Contains_MatchesOnlyTheWholeFlag(string help, string flag, bool expected)
    {
        // Assert
        Assert.That(HelpFlags.Contains(help, flag), Is.EqualTo(expected));
    }

    [Test]
    public void Contains_UnderstandsClaudeCodesMergedAppendSystemPromptFile()
    {
        // Arrange
        const string help = "  --append-system-prompt[-file] <prompt>  Append a system prompt";

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HelpFlags.Contains(help, "--append-system-prompt-file"), Is.True);
            Assert.That(HelpFlags.Contains("  --append-system-prompt <prompt>", "--append-system-prompt-file"), Is.False);
        }
    }
}
