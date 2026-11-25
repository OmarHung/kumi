using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>本機代理工作內容的全組織清理（AG-16）。工作包 C 實作。</summary>
internal static class LocalJobRetention
{
    public static Task<int> SweepAsync(AiDbContext db, DateTimeOffset now, CancellationToken cancellationToken) => Task.FromResult(0);
}
