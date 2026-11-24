using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Identity.Persistence;

namespace PmTool.Identity;

/// <summary>
/// 讀組織的時區。每次都問資料庫太浪費（建立專案、每次提醒都會用），所以同一個程序快取 5 分鐘；
/// 時區很少改，晚幾分鐘生效可以接受。找不到的時區用預設，不讓設定壞了拖垮建立專案。
/// </summary>
internal sealed partial class OrganizationClock(
    IDbContextFactory<IdentityDbContext> contexts,
    TimeProvider time,
    ILogger<OrganizationClock> logger) : IOrganizationClock
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<Guid, (TimeZoneInfo Zone, DateTimeOffset ExpiresAt)> _cache = new();

    public async Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (_cache.TryGetValue(organizationId, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Zone;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var stored = await db.Organizations
            .AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => o.TimeZone)
            .FirstOrDefaultAsync(cancellationToken);
        var zone = Resolve(organizationId, stored);
        _cache[organizationId] = (zone, now + CacheLifetime);
        return zone;
    }

    private TimeZoneInfo Resolve(Guid organizationId, string? stored)
    {
        // 組織不存在時沒有可以警告的設定，直接用預設。
        if (stored is not null && Find(stored) is null)
        {
            LogUnknownTimeZone(organizationId, stored);
        }

        if (Find(OrganizationTime.DefaultTimeZoneId) is null)
        {
            LogDefaultTimeZoneMissing(OrganizationTime.DefaultTimeZoneId);
        }

        return Pick(stored, Find);
    }

    private static TimeZoneInfo? Find(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;

    /// <summary>
    /// 存的時區、預設的時區、UTC，依序用第一個找得到的。精簡的映像檔可能沒有時區資料，
    /// 連預設的都找不到時也要算得出「今天」，不讓建立專案因此失敗（IOrganizationClock 的承諾）。
    /// </summary>
    internal static TimeZoneInfo Pick(string? stored, Func<string, TimeZoneInfo?> find) =>
        (stored is null ? null : find(stored)) ?? find(OrganizationTime.DefaultTimeZoneId) ?? TimeZoneInfo.Utc;

    [LoggerMessage(Level = LogLevel.Warning, Message = "組織 {OrganizationId} 的時區「{TimeZone}」在這台機器上找不到，改用預設的時區。")]
    private partial void LogUnknownTimeZone(Guid organizationId, string timeZone);

    [LoggerMessage(Level = LogLevel.Warning, Message = "預設的時區「{TimeZone}」在這台機器上找不到（沒有時區資料？），改用 UTC。")]
    private partial void LogDefaultTimeZoneMissing(string timeZone);
}
