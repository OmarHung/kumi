using PmTool.Abstractions;
using PmTool.Knowledge;
using PmTool.Work;

namespace PmTool.Ai.Tests.Actions.Attachments;

internal sealed record AddedFile(string Target, string FileName, string? ContentType, byte[] Content);

/// <summary>項目的附件：記下每一次附加（含內容）與刪除；清單依項目編號。</summary>
internal sealed class RecordingItemAttachments : IAttachmentService
{
    public Dictionary<string, List<Attachment>> ByItem { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<AddedFile> Added { get; } = [];

    public List<Guid> Deleted { get; } = [];

    public Failure? RejectNext { get; set; }

    public Attachment Seed(string itemKey, string fileName)
    {
        var attachment = new Attachment(Guid.CreateVersion7(), itemKey, fileName, "application/pdf", 10, "abc", Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        if (!ByItem.TryGetValue(itemKey, out var list))
        {
            ByItem[itemKey] = list = [];
        }

        list.Add(attachment);
        return attachment;
    }

    public Task<Result<IReadOnlyList<Attachment>>> ListAsync(Actor actor, string itemKey, CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<Attachment>>>(ByItem.TryGetValue(itemKey, out var list) ? list.ToList() : []);

    public async Task<Result<Attachment>> AddAsync(Actor actor, string itemKey, NewAttachment upload, CancellationToken cancellationToken)
    {
        if (Take() is { } failure)
        {
            return failure;
        }

        using var memory = new MemoryStream();
        await upload.Content.CopyToAsync(memory, cancellationToken);
        Added.Add(new AddedFile(itemKey, upload.FileName, upload.ContentType, memory.ToArray()));
        return Seed(itemKey, upload.FileName);
    }

    public Task<Result<AttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<Attachment>> DeleteAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (Take() is { } failure)
        {
            return Task.FromResult<Result<Attachment>>(failure);
        }

        Deleted.Add(attachmentId);
        var found = ByItem.Values.SelectMany(l => l).Single(a => a.Id == attachmentId);
        ByItem[found.ItemKey].Remove(found);
        return Task.FromResult<Result<Attachment>>(found);
    }

    private Failure? Take()
    {
        var failure = RejectNext;
        RejectNext = null;
        return failure;
    }
}

/// <summary>知識庫：只有讀一頁（附件建議看頁面在不在、能不能編輯）。頁面由測試放進來。</summary>
internal sealed class FakePages : IKnowledgePageService
{
    public Dictionary<(string Space, int Number), PageDetail> Pages { get; } = [];

    public PageDetail Add(string space, int number, string title, bool canEdit = true)
    {
        var summary = new SpaceSummary(
            Guid.NewGuid(), space, "空間", null, null, DateTimeOffset.UnixEpoch, Guid.NewGuid(), null, new SpacePermissions(canEdit, canEdit, false));
        var page = new PageDetail(
            Guid.NewGuid(), summary, number, title, "", 1, [], false, [], DateTimeOffset.UnixEpoch, Guid.NewGuid(), DateTimeOffset.UnixEpoch, Guid.NewGuid(),
            1, new PagePermissions(canEdit, canEdit, canEdit));
        Pages[(space, number)] = page;
        return page;
    }

    public Task<Result<PageDetail>> GetAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        Task.FromResult<Result<PageDetail>>(Pages.TryGetValue((spaceKey, pageNumber), out var page)
            ? page
            : new Failure(FailureKind.NotFound, KnowledgeErrorCodes.PageNotFound, "找不到頁面。"));

    public Task<Result<PageTree>> GetTreeAsync(Actor actor, string spaceKey, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<PageDetail>> CreateAsync(Actor actor, string spaceKey, NewPage page, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<Result<PageDetail>> UpdateAsync(Actor actor, string spaceKey, int pageNumber, PageChanges changes, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageTree>> MoveAsync(Actor actor, string spaceKey, int pageNumber, PageMove move, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageDeletion>> DeleteAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageDetail>> RestoreAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<TrashPage>> ListTrashAsync(Actor actor, string spaceKey, TrashQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<TrashPurge>> PurgeTrashAsync(Actor actor, string spaceKey, TrashPurgeRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageRestriction>> GetRestrictionAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageRestriction>> SetRestrictionAsync(
        Actor actor, string spaceKey, int pageNumber, IReadOnlyList<Guid>? people, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

/// <summary>知識庫頁面的附件：記下每一次附加（含內容）。</summary>
internal sealed class RecordingPageAttachments : IKnowledgeAttachmentService
{
    public List<AddedFile> Added { get; } = [];

    public async Task<Result<PageAttachment>> AddAsync(
        Actor actor, string spaceKey, int pageNumber, NewPageAttachment upload, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await upload.Content.CopyToAsync(memory, cancellationToken);
        Added.Add(new AddedFile($"{spaceKey}/{pageNumber}", upload.FileName, upload.ContentType, memory.ToArray()));
        return new PageAttachment(Guid.CreateVersion7(), Guid.NewGuid(), upload.FileName, "application/pdf", memory.Length, "abc", actor.PersonId, DateTimeOffset.UnixEpoch);
    }

    public Task<Result<IReadOnlyList<PageAttachment>>> ListAsync(Actor actor, string spaceKey, int pageNumber, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<Result<PageAttachmentContent>> OpenAsync(Actor actor, Guid attachmentId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
