using PmTool.Abstractions;

namespace PmTool.Storage;

public enum StorageArea { ItemAttachments, KnowledgeAttachments }

/// <param name="OrganizationBytes">組織的總量上限；0 是不限制。</param>
/// <param name="PersonBytesPerDay">每人過去 24 小時上傳的上限；0 是不限制。</param>
public sealed record StorageQuotaSettings(long OrganizationBytes, long PersonBytesPerDay)
{
    public const string OrganizationGiBSetting = "Attachments:Quota:OrganizationGiB";

    public const string PersonDailyGiBSetting = "Attachments:Quota:PersonDailyGiB";

    public const long GiB = 1024L * 1024 * 1024;

    public static readonly StorageQuotaSettings Default = new(20 * GiB, 2 * GiB);
}

public static class StorageQuotaRules
{
    /// <summary>「每人每日」是往回 24 小時的視窗，不依時區切日，不會在午夜一次放行。</summary>
    public static readonly TimeSpan PersonWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// advisory lock 的名稱：呼叫端用 pg_advisory_xact_lock(hashtextextended(這個字串, 0))。
    /// 項目與知識庫拿同一把，才排得進同一個隊伍。
    /// </summary>
    public static string LockNameOf(Guid organizationId) => "storage_quota:" + organizationId.ToString("D");
}

/// <summary>一個模組自己的用量。模組實作、自己 TryAddEnumerable 註冊；用自己的連線、只讀已經提交的資料。</summary>
public interface IStorageUsageSource
{
    StorageArea Area { get; }

    Task<StorageAreaUsage> UsageAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<long> UploadedBytesSinceAsync(Guid organizationId, Guid personId, DateTimeOffset since, CancellationToken cancellationToken);
}

public sealed record StorageAreaUsage(StorageArea Area, long Bytes, long Files);

public enum StorageQuotaVerdict { Allowed, OrganizationFull, PersonDailyFull }

public sealed record StorageQuotaCheck(
    StorageQuotaVerdict Verdict,
    long OrganizationUsedBytes,
    long OrganizationLimitBytes,
    long PersonUsedBytes,
    long PersonLimitBytes)
{
    public bool IsAllowed => Verdict == StorageQuotaVerdict.Allowed;
}

/// <param name="OrganizationLimitBytes">null 是不限制。</param>
/// <param name="PersonDailyLimitBytes">null 是不限制。</param>
public sealed record StorageUsageReport(
    long? OrganizationLimitBytes,
    long? PersonDailyLimitBytes,
    IReadOnlyList<StorageAreaUsage> Areas)
{
    public long TotalBytes => Areas.Sum(a => a.Bytes);
}

public interface IStorageQuota
{
    /// <param name="organizationId">組織。</param>
    /// <param name="personId">null：不算每人每日（歷史匯入）。</param>
    /// <param name="incomingBytes">
    /// 0 是寫檔前的預檢：已經到上限（used &gt;= limit）就不必讀內容；
    /// 大於 0 是交易裡的確認：used + incoming &gt; limit 才擋。
    /// </param>
    /// <remarks>防並行的責任在呼叫端：先在自己的交易裡拿 <see cref="StorageQuotaRules.LockNameOf"/> 的鎖再呼叫。</remarks>
    Task<StorageQuotaCheck> CheckAsync(Guid organizationId, Guid? personId, long incomingBytes, CancellationToken cancellationToken);

    /// <summary>只有組織管理員（actor.IsOrganizationAdmin）；其他人 Forbidden "storage.forbidden"。</summary>
    Task<Result<StorageUsageReport>> ReportAsync(Actor actor, CancellationToken cancellationToken);
}

public static class StorageErrorCodes
{
    public const string Forbidden = "storage.forbidden";
}

public sealed class StorageQuota(IEnumerable<IStorageUsageSource> sources, StorageQuotaSettings settings, TimeProvider time) : IStorageQuota
{
    private readonly IReadOnlyList<IStorageUsageSource> _sources = sources.ToList();

    public async Task<StorageQuotaCheck> CheckAsync(
        Guid organizationId, Guid? personId, long incomingBytes, CancellationToken cancellationToken)
    {
        var organizationUsed = settings.OrganizationBytes > 0 ? await OrganizationUsedAsync(organizationId, cancellationToken) : 0;
        var personUsed = 0L;
        if (personId is { } person && settings.PersonBytesPerDay > 0)
        {
            personUsed = await PersonUsedAsync(organizationId, person, cancellationToken);
        }

        var verdict = StorageQuotaVerdict.Allowed;
        if (IsOver(organizationUsed, incomingBytes, settings.OrganizationBytes))
        {
            verdict = StorageQuotaVerdict.OrganizationFull;
        }
        else if (personId is not null && IsOver(personUsed, incomingBytes, settings.PersonBytesPerDay))
        {
            verdict = StorageQuotaVerdict.PersonDailyFull;
        }

        return new StorageQuotaCheck(verdict, organizationUsed, settings.OrganizationBytes, personUsed, settings.PersonBytesPerDay);
    }

    public async Task<Result<StorageUsageReport>> ReportAsync(Actor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsOrganizationAdmin)
        {
            return new Failure(FailureKind.Forbidden, StorageErrorCodes.Forbidden, "只有組織管理員能看儲存空間的用量。");
        }

        var areas = new List<StorageAreaUsage>();
        foreach (var area in Enum.GetValues<StorageArea>())
        {
            var bytes = 0L;
            var files = 0L;
            foreach (var source in _sources.Where(s => s.Area == area))
            {
                var usage = await source.UsageAsync(actor.OrganizationId, cancellationToken);
                bytes += usage.Bytes;
                files += usage.Files;
            }

            areas.Add(new StorageAreaUsage(area, bytes, files));
        }

        return new StorageUsageReport(
            settings.OrganizationBytes > 0 ? settings.OrganizationBytes : null,
            settings.PersonBytesPerDay > 0 ? settings.PersonBytesPerDay : null,
            areas);
    }

    /// <summary>limit 為 0 是不限制。預檢（incoming 為 0）在剛好用滿時就擋；確認時加上這一個檔案之後超過才擋。</summary>
    private static bool IsOver(long used, long incoming, long limit)
    {
        if (limit <= 0)
        {
            return false;
        }

        return incoming <= 0 ? used >= limit : used + incoming > limit;
    }

    private async Task<long> OrganizationUsedAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var total = 0L;
        foreach (var source in _sources)
        {
            total += (await source.UsageAsync(organizationId, cancellationToken)).Bytes;
        }

        return total;
    }

    private async Task<long> PersonUsedAsync(Guid organizationId, Guid personId, CancellationToken cancellationToken)
    {
        var since = time.GetUtcNow() - StorageQuotaRules.PersonWindow;
        var total = 0L;
        foreach (var source in _sources)
        {
            total += await source.UploadedBytesSinceAsync(organizationId, personId, since, cancellationToken);
        }

        return total;
    }
}
