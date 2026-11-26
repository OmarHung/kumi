using PmTool.Ai.LocalAgents;

namespace PmTool.Ai.Tests.LocalAgents;

/// <summary>kumi 的版本比較：照語意版本，數字依大小比（0.9.12 比 0.10.0 舊），解析不出來的一律當作太舊。</summary>
[TestFixture]
public sealed class AgentVersionsTests
{
    [TestCase("0.10.0", true)]
    [TestCase("0.10.1", true)]
    [TestCase("0.11.0", true)]
    [TestCase("1.0.0", true)]
    [TestCase("0.10", true)]
    [TestCase("v0.10.0", true)]
    [TestCase("0.10.0+build5", true)]
    [TestCase("0.9.0", false)]
    [TestCase("0.9.12", false)]
    [TestCase("0.10.0-beta.1", false)]
    [TestCase("0.1.0", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase("dev", false)]
    [TestCase("0.10.x", false)]
    [TestCase("0.-1.0", false)]
    [TestCase("1.2.3.4", false)]
    public void IsAtLeast_AgainstTheAttachmentMinimum_FollowsSemanticVersioning(string version, bool expected)
    {
        // Act
        var result = AgentVersions.IsAtLeast(version, AiRules.MinAgentVersionForAttachments);

        // Assert
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsAtLeast_NullVersion_IsFalse() =>
        Assert.That(AgentVersions.IsAtLeast(null, AiRules.MinAgentVersionForAttachments), Is.False);
}
