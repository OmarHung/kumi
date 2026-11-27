using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Ai;

namespace PmTool.Api.Tests.Support;

/// <summary>一次上傳呼叫：伺服器交給附件服務的檔名、類型與讀到的內容。</summary>
internal sealed record AssistantUploadCall(Actor Actor, string? ProjectKey, string FileName, string? ContentType, byte[] Content);

/// <summary>測試用的助理附件服務：記住每次呼叫的參數，回傳值由測試設定。上傳時把內容讀完，像真的服務一樣。</summary>
internal sealed class FakeAssistantAttachmentService : IAssistantAttachmentService
{
    public static readonly Guid SampleId = Guid.Parse("0190a000-0000-7000-8000-0000000000b1");

    private readonly ConcurrentQueue<AssistantUploadCall> _uploadCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string? ProjectKey, Guid Id)> _deleteCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string? ProjectKey, Guid Id)> _openCalls = new();

    public static AssistantAttachment Sample(
        Guid? id = null,
        string fileName = "截圖.png",
        string contentType = "image/png",
        long size = 11,
        AssistantAttachmentKind kind = AssistantAttachmentKind.Image) =>
        new(id ?? SampleId, fileName, contentType, size, kind, new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));

    public Func<AssistantUploadCall, Result<AssistantAttachment>> OnUpload { get; set; } =
        call => Sample(fileName: call.FileName, size: call.Content.Length);

    public Func<Actor, string?, Guid, Result<Unit>> OnDelete { get; set; } = (_, _, _) => Unit.Value;

    /// <summary>下載的內容；預設是 11 個位元組的 hello world、類型照 <see cref="Sample"/>。</summary>
    public Func<Actor, string?, Guid, Result<(AssistantAttachment Attachment, byte[] Content)>> OnOpen { get; set; } =
        (_, _, id) => (Sample(id), "hello world"u8.ToArray());

    public IReadOnlyList<AssistantUploadCall> UploadCalls => _uploadCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string? ProjectKey, Guid Id)> DeleteCalls => _deleteCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string? ProjectKey, Guid Id)> OpenCalls => _openCalls.ToArray();

    public async Task<Result<AssistantAttachment>> UploadAsync(
        Actor actor, string? projectKey, NewAssistantAttachment upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        var call = new AssistantUploadCall(actor, projectKey, upload.FileName, upload.ContentType, buffer.ToArray());
        _uploadCalls.Enqueue(call);
        return OnUpload(call);
    }

    public Task<Result<Unit>> DeletePendingAsync(Actor actor, string? projectKey, Guid attachmentId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, projectKey, attachmentId));
        return Task.FromResult(OnDelete(actor, projectKey, attachmentId));
    }

    public Task<Result<AssistantAttachmentContent>> OpenAsync(
        Actor actor, string? projectKey, Guid attachmentId, CancellationToken cancellationToken)
    {
        _openCalls.Enqueue((actor, projectKey, attachmentId));
        var opened = OnOpen(actor, projectKey, attachmentId);
        return Task.FromResult<Result<AssistantAttachmentContent>>(
            opened.Failure is { } failure
                ? failure
                : new AssistantAttachmentContent(
                    opened.Value.Attachment,
                    "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
                    new MemoryStream(opened.Value.Content)));
    }
}
