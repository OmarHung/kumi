namespace PmTool.Abstractions;

/// <summary>
/// 組織的時區。「今天」「每天早上九點」「這個月」都依組織的時區算，不依伺服器（容器是 UTC），
/// 也不依瀏覽器（背景工作沒有瀏覽器）。身分模組實作，其他模組只透過這個介面讀。
/// </summary>
public interface IOrganizationClock
{
    /// <summary>
    /// 組織的時區。組織不存在、或存的時區在這台機器上找不到時，用 <see cref="OrganizationTime.DefaultTimeZoneId"/>：
    /// 「今天」一定算得出來，不讓建立專案或提醒因為設定壞了而失敗。
    /// </summary>
    Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken);
}

public static class OrganizationTime
{
    /// <summary>新組織與找不到設定時的時區。IANA 的名稱：容器（Linux）與開發機（macOS）都認得。</summary>
    public const string DefaultTimeZoneId = "Asia/Taipei";

    /// <summary>某個時刻在這個時區是哪一天。</summary>
    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
}
