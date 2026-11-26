using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class EngineVersionRangeTests
{
    [TestCase("2.1.330")]
    [TestCase("2.1.332")]
    [TestCase("2.9.0")]
    [TestCase("2.99.99")]
    public void ClaudeCode_InsideTheTestedRange_IsAccepted(string version) =>
        Assert.That(EngineVersionRange.ClaudeCode.Check(version), Is.Null);

    [TestCase("2.1.329", "請更新")]
    [TestCase("2.0.0", "請更新")]
    [TestCase("1.9.9", "請更新")]
    [TestCase("3.0.0", "還沒驗證過")]
    [TestCase("4.2.1", "還沒驗證過")]
    public void ClaudeCode_OutsideTheTestedRange_IsRefusedWithAReason(string version, string hint)
    {
        // Act
        var note = EngineVersionRange.ClaudeCode.Check(version);

        // Assert
        Assert.That(note, Does.Contain(version).And.Contain(hint).And.Contain("--allow-untested-engine"));
    }

    [TestCase("0.163.0", true)]
    [TestCase("0.200.5", true)]
    [TestCase("0.162.9", false)]
    [TestCase("0.100.0", false)]
    [TestCase("1.0.0", false)]
    [TestCase("1.2.3", false)]
    public void Codex_IsAcceptedFromTheTestedVersionUpToTheNextMajor(string version, bool accepted) =>
        Assert.That(EngineVersionRange.Codex.Check(version) is null, Is.EqualTo(accepted));

    [TestCase("unknown")]
    [TestCase("")]
    [TestCase("abc")]
    [TestCase("2.1.330.7")]
    public void AnUnreadableVersion_IsRefusedByDefault(string version) =>
        Assert.That(EngineVersionRange.ClaudeCode.Check(version), Does.Contain("讀不出"));

    [TestCase("2.1.330-beta.1", 2, 1, 330)]
    [TestCase("v0.163.0", 0, 163, 0)]
    [TestCase("2.1", 2, 1, 0)]
    public void TryParse_ReadsTheLeadingNumbersAndIgnoresTheRest(string text, int major, int minor, int patch)
    {
        // Act
        var parsed = EngineVersionRange.TryParse(text, out var version);

        // Assert
        Assert.That((parsed, version), Is.EqualTo((true, new Version(major, minor, patch))));
    }
}
