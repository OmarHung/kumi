using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Attachments;

/// <summary>附件、它所在的項目與呼叫者在專案裡的權限。</summary>
internal sealed record AttachmentTarget(AttachmentEntity Attachment, ItemTarget Item);

internal static class AttachmentLookup
{
    /// <summary>
    /// 依附件編號找，並確認呼叫者對它所在的項目有 <paramref name="permission"/>。
    /// 看不到的項目、刪除的項目、別的組織的附件一律是 attachment.not_found：問的是附件，不能讓人從代碼分辨它存不存在；
    /// 看得到但權限不夠（唯讀的角色、封存的專案）照項目的規則回 Forbidden 或 project.archived。
    /// <paramref name="forUpdate"/> 時鎖住項目那一列，和上傳、其他修改排隊；鎖到之後再讀一次附件：
    /// 等鎖的時候它可能已經被別人刪掉了。
    /// </summary>
    public static async Task<Result<AttachmentTarget>> FindAsync(
        WorkDbContext db,
        BlueprintCache cache,
        Actor actor,
        Guid attachmentId,
        string permission,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var located = await db.Attachments
            .AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId && a.Id == attachmentId)
            .Join(db.Items, a => a.ItemId, i => i.Id, (a, i) => new { Attachment = a, i.Number })
            .Join(db.Projects, x => x.Attachment.ProjectId, p => p.Id, (x, p) => new { x.Attachment, x.Number, ProjectKey = p.Key })
            .SingleOrDefaultAsync(cancellationToken);
        if (located is null)
        {
            return WorkFailures.AttachmentNotFound();
        }

        var itemKey = ItemKeys.Format(located.ProjectKey, located.Number);
        var item = await ItemStore.FindAsync(db, cache, actor, itemKey, permission, forUpdate, cancellationToken);
        if (item.Failure is { } failure)
        {
            return failure.Kind == FailureKind.NotFound ? WorkFailures.AttachmentNotFound() : failure;
        }

        if (!forUpdate)
        {
            return new AttachmentTarget(located.Attachment, item.Value);
        }

        var current = await db.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.OrganizationId == actor.OrganizationId && a.Id == attachmentId, cancellationToken);
        return current is null ? WorkFailures.AttachmentNotFound() : new AttachmentTarget(current, item.Value);
    }

    public static Attachment ToContract(AttachmentEntity entity, string itemKey) => new(
        entity.Id, itemKey, entity.FileName, entity.ContentType, entity.SizeBytes, entity.Sha256, entity.UploadedBy, entity.UploadedAt);
}
