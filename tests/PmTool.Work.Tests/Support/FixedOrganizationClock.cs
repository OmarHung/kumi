using PmTool.Abstractions;

namespace PmTool.Work.Tests;

/// <summary>每個組織都在同一個時區的時鐘，不查身分模組。預設是 UTC+8，和真實的預設（Asia/Taipei）同一天。</summary>
internal sealed class FixedOrganizationClock(TimeZoneInfo zone) : IOrganizationClock
{
    public FixedOrganizationClock()
        : this(TimeZoneInfo.CreateCustomTimeZone("test-utc+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8"))
    {
    }

    public Task<TimeZoneInfo> TimeZoneAsync(Guid organizationId, CancellationToken cancellationToken) => Task.FromResult(zone);
}
