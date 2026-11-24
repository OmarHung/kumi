using PmTool.Identity;
using PmTool.Work;

namespace PmTool.Api.Endpoints;

/// <summary>附件在 API 上的形狀。上傳的人和負責人一樣帶顯示名稱，網頁不必另外查。</summary>
internal sealed record AttachmentData(
    Guid Id,
    string ItemKey,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    PersonReference UploadedBy,
    DateTimeOffset UploadedAt)
{
    public static AttachmentData From(Attachment attachment, PeopleNames names) => new(
        attachment.Id,
        attachment.ItemKey,
        attachment.FileName,
        attachment.ContentType,
        attachment.SizeBytes,
        attachment.Sha256,
        new PersonReference(attachment.UploadedBy, names.NameOf(attachment.UploadedBy)),
        attachment.UploadedAt);

    /// <summary>一個回應裡所有上傳的人一次查完。</summary>
    public static async Task<IReadOnlyList<AttachmentData>> FromManyAsync(
        IReadOnlyList<Attachment> attachments, IPeopleDirectory people, Guid organizationId, CancellationToken cancellationToken)
    {
        var names = await PeopleNames.LookupAsync(people, organizationId, attachments.Select(a => a.UploadedBy), cancellationToken);
        return [.. attachments.Select(a => From(a, names))];
    }
}
