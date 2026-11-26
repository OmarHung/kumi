using PmTool.Cli.Agent;

namespace PmTool.Cli.Tests.Agent;

/// <summary>用假的 claude／codex 腳本驗：附件下載到工作的暫存資料夾、引擎收到的命令列與標準輸入、失敗時不啟動、結束時資料夾連附件一起刪。</summary>
[TestFixture]
[Platform(Include = "Linux,MacOsX")]
public sealed class ProcessEngineAttachmentTests
{
    private const string McpToken = "pmt_mcp_token_for_tests";
    private const string Server = "https://pm.example.com";

    private FakeEngineScripts _scripts = null!;

    private static AgentOptions Quick => new()
    {
        InterruptGrace = TimeSpan.FromMilliseconds(300),
        ExitGrace = TimeSpan.FromMilliseconds(300),
        DrainTimeout = TimeSpan.FromMilliseconds(500),
    };

    [SetUp]
    public void SetUp() => _scripts = new FakeEngineScripts();

    [TearDown]
    public void TearDown() => _scripts.Dispose();

    private static ProcessEngine Claude(string executable) =>
        new(new ClaudeCodeSpec(), executable, "2.1.332", ClaudeCodeSpec.Models, McpToken, Server, Quick);

    private static ProcessEngine Codex(string executable) =>
        new(new CodexSpec(["shell_tool", "view_image"]), executable, "0.163.0", [new(EngineModel.DefaultId, "預設", [])], McpToken, Server, Quick);

    [Test]
    public async Task Run_ForClaudeDownloadsAttachmentsIntoTheWorkDirectoryAndSendsThemAsStreamJson()
    {
        // Arrange
        var (image, imageBytes) = TestAttachments.Image();
        var (pdf, pdfBytes) = TestAttachments.Pdf();
        var source = new FakeAttachmentSource().Add(image, imageBytes).Add(pdf, pdfBytes);
        var job = TestAttachments.With(TestJobs.Make(), image, pdf);

        // Act
        var outcome = await Claude(_scripts.Recording("claude", "claude-success.jsonl")).RunAsync(job, new ListSink(), CancellationToken.None, source);

        // Assert
        var directory = _scripts.Read("cwd.txt").Trim();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(_scripts.Read("args.txt"), Does.Contain("--input-format\nstream-json"));
            Assert.That(_scripts.Read("stdin.txt"), Does.Contain("\"type\":\"image\"").And.Contain("\"type\":\"document\"").And.Contain(job.Prompt));
            Assert.That(_scripts.Read("cwd-files.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries),
                Is.EqualTo(new[] { "attachment-1.png", "attachment-2.pdf", "mcp.json", "system.md" }));
            Assert.That(Directory.Exists(directory), Is.False, "附件跟著暫存資料夾一起刪");
        }
    }

    [Test]
    public async Task Run_ForCodexPassesImagePathsWithDashIAndKeepsThePromptOnStandardInput()
    {
        // Arrange
        var (image, imageBytes) = TestAttachments.Image();
        var source = new FakeAttachmentSource().Add(image, imageBytes);
        var job = TestAttachments.With(TestJobs.Make(EngineKinds.Codex), image);

        // Act
        var outcome = await Codex(_scripts.Recording("codex", "codex-success.jsonl")).RunAsync(job, new ListSink(), CancellationToken.None, source);

        // Assert
        var args = _scripts.Read("args.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var directory = _scripts.Read("cwd.txt").Trim();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Complete));
            Assert.That(args[^3], Is.EqualTo("-i"));
            Assert.That(args[^2], Does.EndWith("attachment-1.png"));
            Assert.That(args[^1], Is.EqualTo("-"));
            Assert.That(_scripts.Read("stdin.txt"), Is.EqualTo(job.Prompt));
            Assert.That(Directory.Exists(directory), Is.False);
        }
    }

    [Test]
    public async Task Run_WhenADownloadFailsEndsAsFailedWithoutStartingTheEngine()
    {
        // Arrange
        var (image, _) = TestAttachments.Image();
        var job = TestAttachments.With(TestJobs.Make(), image);

        // Act
        var outcome = await Claude(_scripts.Recording("claude", "claude-success.jsonl")).RunAsync(job, new ListSink(), CancellationToken.None, new FakeAttachmentSource());

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.StartWith("下載附件失敗"));
            Assert.That(_scripts.Exists("stdin.txt"), Is.False, "引擎沒有被啟動");
        }
    }

    [Test]
    public async Task Run_WhenTheContentDoesNotMatchTheHashEndsAsFailedWithoutStartingTheEngine()
    {
        // Arrange
        var (image, bytes) = TestAttachments.Image();
        var job = TestAttachments.With(TestJobs.Make(), image);
        var source = new FakeAttachmentSource().Add(image, [.. bytes[..^1], (byte)(bytes[^1] + 1)]);

        // Act
        var outcome = await Claude(_scripts.Recording("claude", "claude-success.jsonl")).RunAsync(job, new ListSink(), CancellationToken.None, source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.StartWith("下載附件失敗"));
            Assert.That(_scripts.Exists("stdin.txt"), Is.False);
        }
    }

    [Test]
    public async Task Run_RejectsAPdfForCodexBeforeDownloadingAnything()
    {
        // Arrange
        var (pdf, bytes) = TestAttachments.Pdf();
        var source = new FakeAttachmentSource().Add(pdf, bytes);
        var job = TestAttachments.With(TestJobs.Make(EngineKinds.Codex), pdf);

        // Act
        var outcome = await Codex(_scripts.Recording("codex", "codex-success.jsonl")).RunAsync(job, new ListSink(), CancellationToken.None, source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(outcome.Status, Is.EqualTo(JobStatus.Failed));
            Assert.That(outcome.Error, Does.Contain("Codex 讀不到 PDF"));
            Assert.That(source.Opened, Is.Empty);
            Assert.That(_scripts.Exists("stdin.txt"), Is.False);
        }
    }

    [Test]
    public async Task Run_WithoutAttachmentsNeverAsksTheSource()
    {
        // Arrange
        var source = new FakeAttachmentSource();

        // Act
        await Claude(_scripts.Recording("claude", "claude-success.jsonl")).RunAsync(TestJobs.Make(), new ListSink(), CancellationToken.None, source);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Opened, Is.Empty);
            Assert.That(_scripts.Read("args.txt"), Does.Not.Contain("--input-format"));
        }
    }
}
