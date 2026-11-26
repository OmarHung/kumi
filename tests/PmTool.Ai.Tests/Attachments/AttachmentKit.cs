using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Ai.Tests.Assistant.Fakes;

namespace PmTool.Ai.Tests.Attachments;

/// <summary>附件測試共用的小工具：造各種內容的位元組、上傳、送出帶附件的訊息。</summary>
internal static class AttachmentKit
{
    /// <summary>PNG 的檔頭加上補到指定大小的零（內容不必是合法的圖片，我們只看開頭）。</summary>
    public static byte[] Image(int size = 64) => Pad(AttachmentSnifferTests.Png, size);

    public static byte[] Pdf(int size = 64) => Pad(AttachmentSnifferTests.Pdf, size);

    public static byte[] Text(string content) => Encoding.UTF8.GetBytes(content);

    private static byte[] Pad(byte[] header, int size)
    {
        var bytes = new byte[Math.Max(size, header.Length)];
        header.CopyTo(bytes, 0);
        return bytes;
    }

    public static IAssistantAttachmentService Attachments(this AssistantHarness h) => h.Host.Get<IAssistantAttachmentService>();

    public static MemoryFileStore Store(this AssistantHarness h) => (MemoryFileStore)h.Host.Get<PmTool.Storage.IFileStore>();

    public static async Task<Result<AssistantAttachment>> TryUploadAsync(
        this AssistantHarness h, string fileName, byte[] content, Actor? actor = null, string? declaredType = null)
    {
        await using var stream = new MemoryStream(content);
        return await h.Attachments().UploadAsync(
            actor ?? h.Actor,
            AssistantHarness.ProjectKey,
            new NewAssistantAttachment { FileName = fileName, ContentType = declaredType, Content = stream },
            CancellationToken.None);
    }

    public static async Task<AssistantAttachment> UploadAsync(this AssistantHarness h, string fileName, byte[] content, Actor? actor = null)
    {
        var result = await h.TryUploadAsync(fileName, content, actor);
        Assert.That(result.Succeeded, Is.True, () => result.Failure?.Message ?? string.Empty);
        return result.Value;
    }

    public static Task<Result<AssistantRun>> TryStartAsync(
        this AssistantHarness h, Guid conversationId, string text, IReadOnlyList<Guid> attachmentIds, Actor? actor = null, AiSelection? selection = null) =>
        h.Service.StartAsync(
            actor ?? h.Actor,
            AssistantHarness.ProjectKey,
            conversationId,
            new AssistantMessageInput(text, selection ?? h.Selection) { AttachmentIds = attachmentIds },
            CancellationToken.None);

    /// <summary>送出帶附件的訊息並讀到結束（腳本要先排好模型的回覆）。</summary>
    public static async Task<ReplyResult> SendWithAsync(
        this AssistantHarness h, Guid conversationId, string text, params AssistantAttachment[] attachments)
    {
        var started = await h.TryStartAsync(conversationId, text, [.. attachments.Select(a => a.Id)]);
        Assert.That(started.Succeeded, Is.True, () => started.Failure?.Message ?? string.Empty);
        return new ReplyResult(started.Value, await AssistantHarness.ReadAllAsync(started.Value));
    }

    public static void AssertFailure(Failure? failure, FailureKind kind, string code)
    {
        Assert.That(failure, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Kind, Is.EqualTo(kind));
            Assert.That(failure.Code, Is.EqualTo(code));
        }
    }
}
