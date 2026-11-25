using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>兩種清理各自一份規則（<see cref="ConversationRetention"/>、<see cref="LocalJobRetention"/>），這裡只是排在一起跑。</summary>
internal sealed class AiMaintenance(IDbContextFactory<AiDbContext> contexts, TimeProvider time) : IAiMaintenance
{
    public async Task<AiMaintenanceResult> SweepAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var conversations = await ConversationRetention.SweepAsync(db, now, cancellationToken);
        var payloads = await LocalJobRetention.SweepAsync(db, now, cancellationToken);
        return new AiMaintenanceResult(conversations, payloads);
    }
}
