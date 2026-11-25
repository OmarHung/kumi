using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>
/// 組織連線用量（<c>ai.connection_usage</c>）的清理（審查 L-4）。每天、每月的上限只看最近的資料，
/// 留一年多的歷史夠對帳；不清的話每一則回覆都多一列，表會一直長。對話刪掉不會動它，只有這裡依建立時間刪。
/// </summary>
internal static class ConnectionUsageRetention
{
    /// <summary>建立超過這麼久的列刪掉（上限最長只算「這個月」，遠短於此）。</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(400);

    /// <returns>刪掉的列數。</returns>
    public static async Task<int> SweepAsync(AiDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - Retention;
        return await db.ConnectionUsage.Where(u => u.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
