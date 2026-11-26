using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using PmTool.Ai.Attachments;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>
/// 本機與主機的引擎收到的提示：圖片與 PDF 換成「〔附件 N：…〕」的位置，N 依對話的順序（舊的在前）編號；
/// 預算從新的往舊的花（15 MiB、最多 50 個），放不下的換成說明。這裡直接餵參照，不讀檔（引擎的附件由 kumi 另外下載）。
/// </summary>
[TestFixture]
public sealed class AgentAttachmentReplayTests
{
    private const long MiB = 1024 * 1024;

    private static readonly AgentSource ClaudeNew = new(AiProviderKind.ClaudeCode, "0.10.0", false, "Claude Code（我的電腦）");

    private static AttachmentReplay Replay() => new(new MemoryFileStore(), NullLogger<AttachmentReplay>.Instance);

    private static AttachmentReference Ref(string name, AssistantAttachmentKind kind = AssistantAttachmentKind.Image, long size = 100) =>
        new(Guid.CreateVersion7(), name, kind == AssistantAttachmentKind.Pdf ? "application/pdf" : "image/png", kind, size);

    private static ChatMessage User(params AIContent[] contents) => new(ChatRole.User, contents);

    private static string TextOf(ChatMessage message) => string.Join("\n", message.Contents.OfType<TextContent>().Select(t => t.Text));

    [Test]
    public async Task Resolve_AgentTarget_NumbersAttachmentsInConversationOrderAcrossMessages()
    {
        // Arrange
        var old = Ref("舊的.png");
        var spec = Ref("規格.pdf", AssistantAttachmentKind.Pdf);
        var latest = Ref("最新.png");
        ChatMessage[] messages = [User(new TextContent("先看這張"), old), new(ChatRole.Assistant, [new TextContent("好")]), User(spec, latest)];

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, new ReplayOptions { Agent = ClaudeNew }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextOf(result.Messages[0]), Does.Contain("〔附件 1：舊的.png（附在這則訊息的最後）〕"));
            Assert.That(TextOf(result.Messages[2]), Does.Contain("〔附件 2：規格.pdf（附在這則訊息的最後）〕"));
            Assert.That(TextOf(result.Messages[2]), Does.Contain("〔附件 3：最新.png（附在這則訊息的最後）〕"));
            Assert.That(result.AgentAttachments.Select(a => a.Id), Is.EqualTo(new[] { old.Id, spec.Id, latest.Id }));
            Assert.That(result.HistoryBinaryIds, Is.Empty);
        }
    }

    [Test]
    public async Task Resolve_AgentTarget_SpendsTheBudgetNewestFirstAndDropsEverythingOlderOnceItRunsOut()
    {
        // Arrange
        var oldest = Ref("最舊.png", size: 4 * MiB);
        var middle = Ref("中間.png", size: 5 * MiB);
        var big = Ref("大.png", size: 5 * MiB);
        var current = Ref("這一則.png", size: 5 * MiB);
        ChatMessage[] messages = [User(oldest), User(middle), User(big), User(current)];

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, new ReplayOptions { Agent = ClaudeNew }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AgentAttachments.Select(a => a.FileName), Is.EqualTo(new[] { "中間.png", "大.png", "這一則.png" }));
            Assert.That(TextOf(result.Messages[0]), Is.EqualTo("〔附件：最舊.png（之前附的，這次沒有再送）〕"));
            Assert.That(TextOf(result.Messages[1]), Does.StartWith("〔附件 1：中間.png"));
        }
    }

    [Test]
    public async Task Resolve_AgentTarget_NeverSendsMoreThanTheCountCapAndKeepsTheNewest()
    {
        // Arrange
        var all = Enumerable.Range(1, MaxJobAttachments + 1).Select(i => Ref($"圖{i}.png")).ToList();
        ChatMessage[] messages = [.. all.Select(reference => User(reference))];

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, new ReplayOptions { Agent = ClaudeNew }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.AgentAttachments, Has.Count.EqualTo(MaxJobAttachments));
            Assert.That(result.AgentAttachments[0].FileName, Is.EqualTo("圖2.png"));
            Assert.That(TextOf(result.Messages[0]), Does.Contain("之前附的，這次沒有再送"));
        }
    }

    private const int MaxJobAttachments = PmTool.Ai.LocalAgents.LocalAgentLimits.MaxJobAttachments;

    [Test]
    public async Task Resolve_CodexHistoryPdf_BecomesANoteWhileTheImageIsKept()
    {
        // Arrange
        var codex = new AgentSource(AiProviderKind.Codex, "0.10.0", false, "Codex（我的電腦）");
        var pdf = Ref("規格.pdf", AssistantAttachmentKind.Pdf);
        var image = Ref("圖.png");
        ChatMessage[] messages = [User(pdf, image), new(ChatRole.Assistant, [new TextContent("好")]), User(new TextContent("再看一次"))];

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, new ReplayOptions { Agent = codex }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextOf(result.Messages[0]), Does.Contain("〔附件：規格.pdf（之前附的，這個來源這次讀不到）〕"));
            Assert.That(TextOf(result.Messages[0]), Does.Contain("〔附件 1：圖.png（附在這則訊息的最後）〕"));
            Assert.That(result.AgentAttachments.Single().Id, Is.EqualTo(image.Id));
        }
    }

    [Test]
    public async Task Resolve_OldAgentVersion_TurnsEveryImageAndPdfIntoANote()
    {
        // Arrange
        var old = new AgentSource(AiProviderKind.ClaudeCode, "0.9.0", false, "Claude Code（我的電腦）");
        var image = Ref("圖.png");
        ChatMessage[] messages = [User(image), User(new TextContent("只剩文字"))];

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, new ReplayOptions { Agent = old }, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextOf(result.Messages[0]), Is.EqualTo("〔附件：圖.png（之前附的，這個來源這次讀不到）〕"));
            Assert.That(result.AgentAttachments, Is.Empty);
        }
    }

    [Test]
    public async Task Resolve_HistoryAttachmentTheProviderRefusedBefore_IsOnlyANoteButTheCurrentOneIsSent()
    {
        // Arrange
        var refused = Ref("壞掉的.png");
        var current = Ref("這一則.png");
        ChatMessage[] messages = [User(refused), User(current)];
        var options = new ReplayOptions { Agent = ClaudeNew, SkippedIds = new HashSet<Guid> { refused.Id, current.Id } };

        // Act
        var result = await Replay().ResolveAsync(messages, AttachmentTarget.Agent, options, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(TextOf(result.Messages[0]), Does.Contain("模型讀不了"));
            Assert.That(result.AgentAttachments.Single().Id, Is.EqualTo(current.Id));
        }
    }

    [Test]
    public void Resolve_AgentTargetWithoutASource_Throws()
    {
        // Act and assert
        Assert.ThrowsAsync<ArgumentException>(
            async () => await Replay().ResolveAsync([User(Ref("圖.png"))], AttachmentTarget.Agent, ReplayOptions.Default, CancellationToken.None));
    }
}
