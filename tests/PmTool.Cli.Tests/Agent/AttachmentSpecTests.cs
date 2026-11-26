using System.Text.Json;
using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>附件進引擎的兩種寫法：Claude 的一行串流 JSON、Codex 的 <c>--image=</c>。</summary>
[TestFixture]
public sealed class AttachmentSpecTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp() => _directory = Directory.CreateTempSubdirectory("kumi-spec-test-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private StagedAttachment StageImage(string directory, string name = "attachment-1.png")
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return new StagedAttachment(path, "image/png", AttachmentKinds.Image);
    }

    [TestCase("\u2028")]
    [TestCase("\u2029")]
    [TestCase("\u0085")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    [TestCase("\n")]
    public void Claude_StreamInput_StaysOneLineWhateverLineBreakThePromptHas(string lineBreak)
    {
        // Arrange
        var prompt = $"第一行{lineBreak}第二行{lineBreak}結尾";
        var staged = StageImage(_directory);

        // Act
        var input = ClaudeCodeSpec.StreamInput(prompt, [staged]);

        // Assert
        using var document = JsonDocument.Parse(input);
        var text = document.RootElement.GetProperty("message").GetProperty("content")[0].GetProperty("text").GetString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(input.Count(character => character == '\n'), Is.EqualTo(1));
            Assert.That(input, Does.EndWith("\n"));
            Assert.That(input.IndexOfAny(['\r', '\u2028', '\u2029', '\u0085']), Is.EqualTo(-1));
            Assert.That(text, Is.EqualTo(prompt));
        }
    }

    [Test]
    public void Codex_Plan_PassesOneImageOptionPerImageRelativeToTheWorkDirectoryEvenWhenThePathHasACommaOrSpaces()
    {
        // Arrange: clap 的 --image 用逗號切值，所以絕對路徑裡有逗號會被切開；用資料夾裡的相對檔名。
        var work = Path.Combine(_directory, "a,b c");
        var first = StageImage(work);
        var second = StageImage(work, "attachment-2.png");
        var job = TestJobs.Make(EngineKinds.Codex);

        // Act
        var args = new CodexSpec([]).Plan(job, work, [first, second]).Arguments;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Where(argument => argument.StartsWith("--image=", StringComparison.Ordinal)),
                Is.EqualTo(new[] { "--image=attachment-1.png", "--image=attachment-2.png" }));
            Assert.That(args[^1], Is.EqualTo("-"));
            Assert.That(args, Does.Not.Contain("-i"));
        }
    }

    [Test]
    public void Codex_NeedsStandardInputInstructions_CountsTheImageArgumentsTowardsTheWindowsLimit()
    {
        // Arrange
        var promptLength = CodexSpec.MaxCommandLineInstructions - 10;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CodexSpec.NeedsStandardInputInstructions(isWindows: true, promptLength, imageArgumentLength: 5), Is.False);
            Assert.That(CodexSpec.NeedsStandardInputInstructions(isWindows: true, promptLength, imageArgumentLength: 11), Is.True);
            Assert.That(CodexSpec.NeedsStandardInputInstructions(isWindows: false, promptLength, imageArgumentLength: 5_000), Is.False);
        }
    }
}
