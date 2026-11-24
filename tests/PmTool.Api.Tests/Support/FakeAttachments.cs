using System.Collections.Concurrent;
using PmTool.Abstractions;
using PmTool.Work;

namespace PmTool.Api.Tests.Support;

/// <summary>一次上傳呼叫：伺服器交給附件服務的檔名、類型與讀到的內容。</summary>
internal sealed record UploadCall(Actor Actor, string ItemKey, string FileName, string? ContentType, byte[] Content);

/// <summary>測試用的附件服務：記住每次呼叫的參數，回傳值由測試設定。上傳時把內容讀完，像真的服務一樣。</summary>
internal sealed class FakeAttachmentService : IAttachmentService
{
    public static readonly Guid SampleId = Guid.Parse("0190a000-0000-7000-8000-0000000000a1");

    private readonly ConcurrentQueue<UploadCall> _addCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, string ItemKey)> _listCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _openCalls = new();
    private readonly ConcurrentQueue<(Actor Actor, Guid Id)> _deleteCalls = new();

    public static Attachment Sample(
        Guid? id = null, string itemKey = "WEB-7", string fileName = "規格.pdf", string contentType = "application/pdf", long size = 11, Guid? uploadedBy = null) =>
        new(
            id ?? SampleId,
            itemKey,
            fileName,
            contentType,
            size,
            "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9",
            uploadedBy ?? TestUsers.Member.PersonId,
            new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero));

    /// <summary>回傳的附件；預設把上傳的檔名、類型與大小帶回去。</summary>
    public Func<UploadCall, Result<Attachment>> OnAdd { get; set; } =
        call => Sample(itemKey: call.ItemKey, fileName: call.FileName, contentType: call.ContentType ?? "application/octet-stream", size: call.Content.Length);

    public Func<Actor, string, Result<IReadOnlyList<Attachment>>> OnList { get; set; } =
        (_, _) => new List<Attachment> { Sample() };

    /// <summary>下載的內容；預設是 11 個位元組的 hello world。</summary>
    public Func<Actor, Guid, Result<(Attachment Attachment, byte[] Content)>> OnOpen { get; set; } =
        (_, id) => (Sample(id), "hello world"u8.ToArray());

    public Func<Actor, Guid, Result<Attachment>> OnDelete { get; set; } = (_, id) => Sample(id);

    public IReadOnlyList<UploadCall> AddCalls => _addCalls.ToArray();

    public IReadOnlyList<(Actor Actor, string ItemKey)> ListCalls => _listCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> OpenCalls => _openCalls.ToArray();

    public IReadOnlyList<(Actor Actor, Guid Id)> DeleteCalls => _deleteCalls.ToArray();

    public async Task<Result<Attachment>> AddAsync(Actor actor, string itemKey, NewAttachment upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer, cancellationToken);
        var call = new UploadCall(actor, itemKey, upload.FileName, upload.ContentType, buffer.ToArray());
        _addCalls.Enqueue(call);
        return OnAdd(call);
    }

    public Task<Result<IReadOnlyList<Attachment>>> ListAsync(Actor actor, string itemKey, CancellationToken cancellationToken)
    {
        _listCalls.Enqueue((actor, itemKey));
        return Task.FromResult(OnList(actor, itemKey));
    }

    public Task<Result<AttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        _openCalls.Enqueue((actor, attachmentId));
        var opened = OnOpen(actor, attachmentId);
        return Task.FromResult<Result<AttachmentContent>>(
            opened.Failure is { } failure ? failure : new AttachmentContent(opened.Value.Attachment, new MemoryStream(opened.Value.Content)));
    }

    public Task<Result<Attachment>> DeleteAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        _deleteCalls.Enqueue((actor, attachmentId));
        return Task.FromResult(OnDelete(actor, attachmentId));
    }
}
