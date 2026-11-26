using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Attachments;

/// <summary>
/// 本機代理與主機的 worker 下載工作的附件（internal/HANDOFF-ai-attachments.md 第 8 節）。工作的權限（誰領的、還在不在執行、租約）
/// 由呼叫的服務先檢查；這裡只認「這件工作的清單」：不在清單裡的附件一律找不到，不管它是不是同一個人、同一個組織的。
/// </summary>
internal sealed partial class AgentAttachmentReader(
    IDbContextFactory<AiDbContext> contexts, IFileStore store, ILogger<AgentAttachmentReader> logger)
{
    public async Task<Result<AssistantAttachmentContent>> OpenAsync(
        Guid organizationId, IReadOnlyList<Guid> listed, Guid attachmentId, CancellationToken cancellationToken)
    {
        if (!listed.Contains(attachmentId))
        {
            return AttachmentFailures.NotFound();
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.Attachments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.OrganizationId == organizationId, cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Error, Message = "助理附件 {AttachmentId} 要交給引擎，儲存空間裡卻找不到檔案；請從備份還原。")]
    private static partial void LogMissingFile(ILogger logger, Guid attachmentId);
}
