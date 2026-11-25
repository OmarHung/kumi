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
}
