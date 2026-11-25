using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Persistence;

namespace PmTool.Ai.Maintenance;

/// <summary>
/// 本機代理工作內容的全組織清理（AG-16）。工作列的內容含整段對話：領走時就清成 <c>{}</c>、伺服器自己結束時也清，
/// 這裡掃的是漏網的（程序在中途當掉、那個人又不再登記時沒人順手清）。
/// </summary>
internal static class LocalJobRetention
{
    /// <summary>建立超過這麼久、內容還沒清的工作，內容一律清掉（等待領走的上限遠短於此）。</summary>
    public static readonly TimeSpan PayloadRetention = TimeSpan.FromHours(1);

    public const string EmptyPayload = "{}";

    /// <returns>清掉內容的工作數。</returns>
    public static async Task<int> SweepAsync(AiDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - PayloadRetention;
        return await db.LocalJobs
            .Where(j => j.CreatedAt < cutoff && j.Payload != EmptyPayload)
            .ExecuteUpdateAsync(set => set.SetProperty(j => j.Payload, EmptyPayload), cancellationToken);
    }
}
