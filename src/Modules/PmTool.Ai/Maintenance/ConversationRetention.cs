using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>對話的保存期限（AI-06）。工作包 A 實作。</summary>
internal static class ConversationRetention
{
    public static Task<int> SweepAsync(AiDbContext db, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
}
