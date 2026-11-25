using Microsoft.Extensions.DependencyInjection;
using PmTool.Identity.Audit;

namespace PmTool.Identity.Tests.Audit;

internal static class AuditTestSupport
{
    /// <summary>經由模組內部的寫入工具新增一筆稽核紀錄（公開的合約只有讀取），回傳它的編號。</summary>
    public static async Task<Guid> AddEventAsync(
        this IdentityTestHost host,
        Guid organizationId,
        string action,
        Guid? actorPersonId = null,
        Guid? targetPersonId = null,
        string outcome = AuditOutcomes.Succeeded,
        IReadOnlyDictionary<string, string>? details = null)
    {
        await using var db = await host.CreateDbContextAsync();
        return await host.Services.GetRequiredService<AuditTrail>().AddAsync(
            db,
            new AuditRecord(organizationId, action, outcome) { ActorPersonId = actorPersonId, TargetPersonId = targetPersonId, Details = details },
            CancellationToken.None);
    }

    /// <summary>
    /// 把登入稽核的佇列處理完（測試的主機不跑背景的消費者，這裡在呼叫端的執行緒上同步寫）。
    /// </summary>
    public static async Task DrainSignInAuditAsync(this IdentityTestHost host)
    {
        var queue = host.Services.GetRequiredService<SignInAuditQueue>();
        var writer = host.Services.GetRequiredService<SignInAuditWriter>();
        while (queue.Reader.TryRead(out var attempt))
        {
            await writer.WriteAsync(attempt, CancellationToken.None);
        }
    }
}
