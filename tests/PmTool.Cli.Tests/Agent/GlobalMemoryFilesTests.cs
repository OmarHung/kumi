using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

[TestFixture]
public sealed class GlobalMemoryFilesTests
{
    private string _home = null!;

    [SetUp]
    public void SetUp() => _home = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"kumi-codex-home-{Guid.NewGuid():N}")).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_home, recursive: true);

    private Func<string, string?> Environment => name => name == "CODEX_HOME" ? _home : null;

    private static IAgentEngine Engine(string kind) => new FakeEngine((_, _, _) => Task.FromResult(JobOutcome.Stopped(null)), kind);

    [Test]
    public void Describe_WhenCodexHasAGlobalAgentsFile_NamesTheFileAndSaysItCannotBeTurnedOff()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_home, "AGENTS.md"), "請一律用繁體中文回答。");

        // Act
        var notes = GlobalMemoryFiles.Describe([Engine(EngineKinds.Codex)], Environment);

        // Assert
        Assert.That(notes.Single(), Does.Contain(Path.Combine(_home, "AGENTS.md")).And.Contain("關掉"));
    }

    [Test]
    public void Describe_WithoutTheFileOrWithoutCodex_SaysNothing()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_home, "AGENTS.md"), "x");

        // Act
        var withoutFile = GlobalMemoryFiles.Describe([Engine(EngineKinds.Codex)], _ => Path.Combine(_home, "不存在"));
        var withoutCodex = GlobalMemoryFiles.Describe([Engine(EngineKinds.ClaudeCode)], Environment);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutFile, Is.Empty);
            Assert.That(withoutCodex, Is.Empty);
        }
    }

    [Test]
    public void Describe_IgnoresAnEmptyFile()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_home, "AGENTS.md"), string.Empty);

        // Assert
        Assert.That(GlobalMemoryFiles.Describe([Engine(EngineKinds.Codex)], Environment), Is.Empty);
    }
}
