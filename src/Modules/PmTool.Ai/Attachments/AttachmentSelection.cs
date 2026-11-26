using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Ai.Assistant;
using PmTool.Ai.Assistant.Transcript;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 送出訊息時對附件的檢查與綁定（<c>StartAsync</c> 用）。檢查在預留用量之前，什麼都不存；
/// 綁定在存下使用者的訊息的同一個交易裡，用條件式的 UPDATE，同時被刪掉或被另一則訊息搶走的整個交易回滾。
/// </summary>
internal static class AttachmentSelection
{
    /// <summary>
    /// 數量、重複、每個都是自己在這個專案待送的（沒過期）、合計大小、選的來源讀得到（<paramref name="agent"/> 是本機或主機的引擎時看它的版本與種類，API 的模型都讀得到）。回傳依 <paramref name="ids"/> 的順序。
    /// </summary>
    public static async Task<Result<IReadOnlyList<AiAttachmentRow>>> CheckAsync(
        AiDbContext db,
        Actor actor,
        string? projectKey,
        IReadOnlyList<Guid> ids,
        AgentSource? agent,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return Array.Empty<AiAttachmentRow>();
        }

        if (ids.Count > AssistantAttachmentRules.MaxPerMessage)
        {
            return AttachmentFailures.TooManyInMessage();
        }

        if (ids.Distinct().Count() != ids.Count)
        {
            return AssistantFailures.MessageInvalid("同一個附件不能重複附上。");
        }

        var oldest = now - AssistantAttachmentRules.PendingLifetime;
        var found = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.Id)
                && a.OrganizationId == actor.OrganizationId
                && a.PersonId == actor.PersonId
                && a.ProjectKey == projectKey
                && a.BoundAt == null
                && a.UploadedAt >= oldest)
            .ToListAsync(cancellationToken);
        if (found.Count != ids.Count)
        {
            return AttachmentFailures.NotPending();
        }

        var rows = ids.Select(id => found.Single(row => row.Id == id)).ToList();
        if (rows.Sum(row => row.SizeBytes) > AssistantAttachmentRules.MaxBytesPerMessage)
        {
            return AttachmentFailures.TooLargeInTotal();
        }

        if (agent?.Check([.. rows.Select(row => AttachmentKinds.Parse(row.Kind))]) is { } unreadable)
        {
            return unreadable;
        }

        return rows;
    }

    /// <summary>
    /// 綁到剛存下的使用者訊息上，依順序。綁到的數量不對（有一個剛好被刪掉、過期或被別的訊息搶走）回失敗，呼叫端整個交易不提交。
    /// </summary>
    public static async Task<Failure?> BindAsync(
        AiDbContext db,
        Actor actor,
        string? projectKey,
        Guid conversationId,
        Guid messageId,
        IReadOnlyList<AiAttachmentRow> rows,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var oldest = now - AssistantAttachmentRules.PendingLifetime;
        for (var position = 0; position < rows.Count; position++)
        {
            var id = rows[position].Id;
            var slot = position;
            var bound = await db.Attachments
                .Where(a => a.Id == id
                    && a.OrganizationId == actor.OrganizationId
                    && a.PersonId == actor.PersonId
                    && a.ProjectKey == projectKey
                    && a.BoundAt == null
                    && a.UploadedAt >= oldest)
                .ExecuteUpdateAsync(
                    set => set
                        .SetProperty(a => a.ConversationId, conversationId)
                        .SetProperty(a => a.MessageId, messageId)
                        .SetProperty(a => a.Position, slot)
                        .SetProperty(a => a.BoundAt, now),
                    cancellationToken);
            if (bound == 0)
            {
                return AttachmentFailures.NotPending();
            }
        }

        return null;
    }

    public static IReadOnlyList<TranscriptAttachment> ToTranscript(IReadOnlyList<AiAttachmentRow> rows) =>
        [.. rows.Select(row => new TranscriptAttachment(row.Id, row.FileName, row.ContentType, row.Kind, row.SizeBytes))];

    public static IReadOnlyList<AttachmentReference> ToReferences(IReadOnlyList<AiAttachmentRow> rows) =>
        [.. rows.Select(row => new AttachmentReference(row.Id, row.FileName, row.ContentType, AttachmentKinds.Parse(row.Kind), row.SizeBytes))];

    /// <summary>綁好之後的合約型別（資料列是綁之前讀的，上傳的時間與其他欄位不會變）。</summary>
    public static IReadOnlyList<AssistantAttachment> ToContracts(IReadOnlyList<AiAttachmentRow> rows) =>
        [.. rows.Select(AttachmentMapper.ToContract)];
}
