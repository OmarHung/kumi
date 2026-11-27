using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 動作建議（把檔案附到項目或知識庫頁面）讀「這則對話裡使用者上傳的檔案」。範圍只有一個：這個人、這則對話、已經送出的附件；
/// 別的對話的、別人的、還沒送出的、對話已經刪掉的一律找不到，不管編號是不是真的存在。
/// 和 <see cref="AssistantAttachmentService"/> 分開：那邊的下載是給網頁與引擎用的，要專案代號與對話可見性，這裡只認對話編號。
/// </summary>
internal sealed partial class ConversationAttachmentReader(
    IDbContextFactory<AiDbContext> contexts, IFileStore store, ILogger<ConversationAttachmentReader> logger)
{
    /// <summary>這則對話裡這個人上傳的附件，先上傳的在前。</summary>
    public async Task<IReadOnlyList<AssistantAttachment>> ListAsync(Actor actor, Guid? conversationId, CancellationToken cancellationToken)
    {
        if (conversationId is not { } id)
        {
            return [];
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await db.Attachments.AsNoTracking()
            .Where(a => a.OrganizationId == actor.OrganizationId
                && a.PersonId == actor.PersonId
                && a.ConversationId == id
                && a.MessageId != null
                && a.BoundAt != null)
            .OrderBy(a => a.UploadedAt).ThenBy(a => a.Position)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(AttachmentMapper.ToContract)];
    }

    /// <summary>
    /// 用編號或檔名（完全相同，不分大小寫）在 <paramref name="available"/> 裡找：模型在訊息裡只看得到檔名，看不到編號，所以兩種都收。
    /// 同名不只一個時要求用編號，訊息列出各個編號。
    /// </summary>
    public static Result<AssistantAttachment> Resolve(IReadOnlyList<AssistantAttachment> available, string idOrName)
    {
        var found = Guid.TryParse(idOrName, out var id)
            ? available.Where(a => a.Id == id).ToList()
            : available.Where(a => string.Equals(a.FileName, idOrName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count == 1)
        {
            return found[0];
        }

        if (found.Count > 1)
        {
            var ids = string.Join("、", found.Select(a => a.Id));
            return new Failure(FailureKind.Invalid, AiErrorCodes.AttachmentNotFound, $"有 {found.Count} 個叫「{idOrName}」的檔案，請改用編號：{ids}。");
        }

        var names = available.Count == 0 ? "這則對話裡你還沒有上傳檔案" : "這則對話裡你上傳的有：" + string.Join("、", available.Select(a => $"{a.FileName}（{a.Id}）"));
        return new Failure(FailureKind.NotFound, AiErrorCodes.AttachmentNotFound, $"找不到這個檔案。{names}。");
    }

    /// <summary>打開這則對話裡自己的附件來讀；呼叫端負責 Dispose。找不到（含儲存空間裡沒有檔案）都是 <see cref="AiErrorCodes.AttachmentNotFound"/>。</summary>
    public async Task<Result<AssistantAttachmentContent>> OpenAsync(
        Actor actor, Guid? conversationId, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (conversationId is not { } id)
        {
            return AttachmentFailures.NotFound();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(
            a => a.Id == attachmentId
                && a.OrganizationId == actor.OrganizationId
                && a.PersonId == actor.PersonId
                && a.ConversationId == id
                && a.MessageId != null
                && a.BoundAt != null,
            cancellationToken);
        if (row is null)
        {
            return AttachmentFailures.NotFound();
        }

        var content = await store.OpenReadAsync(AttachmentMapper.KeyOf(row.Id), cancellationToken);
        if (content is null)
        {
            LogMissingFile(logger, row.Id);
            return AttachmentFailures.NotFound();
        }

        return new AssistantAttachmentContent(AttachmentMapper.ToContract(row), row.Sha256, content);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "助理附件 {AttachmentId} 要附到項目或頁面，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId);
}
