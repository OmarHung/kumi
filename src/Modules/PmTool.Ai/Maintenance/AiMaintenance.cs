using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Ai.Persistence;
using PmTool.Storage;

namespace PmTool.Ai.Maintenance;

/// <summary>各種清理各自一份規則（<see cref="ConversationRetention"/>、<see cref="LocalJobRetention"/>、<see cref="ConnectionUsageRetention"/>、<see cref="AttachmentRetention"/>），這裡只是排在一起跑。</summary>
internal sealed class AiMaintenance(
    IDbContextFactory<AiDbContext> contexts, IFileStore store, TimeProvider time, ILogger<AiMaintenance> logger) : IAiMaintenance
{
    public async Task<AiMaintenanceResult> SweepAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);

        // 對話先刪：它們的附件才會變成孤兒，在同一輪就一起清掉。
        var conversations = await ConversationRetention.SweepAsync(db, now, cancellationToken);
        var payloads = await LocalJobRetention.SweepAsync(db, now, cancellationToken);
        var usage = await ConnectionUsageRetention.SweepAsync(db, now, cancellationToken);
        var attachments = await AttachmentRetention.SweepAsync(db, store, now, logger, cancellationToken);
        return new AiMaintenanceResult(conversations, payloads, usage, attachments);
    }
}
