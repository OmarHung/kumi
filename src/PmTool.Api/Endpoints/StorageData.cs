using PmTool.Storage;

namespace PmTool.Api.Endpoints;

/// <param name="Area">分類的名稱，snake_case（item_attachments、knowledge_attachments）。</param>
internal sealed record StorageAreaData(string Area, long Bytes, long Files)
{
    public static StorageAreaData From(StorageAreaUsage usage) => new(NameOf(usage.Area), usage.Bytes, usage.Files);

    // 對外的名稱是合約：不用列舉的 ToString，改了列舉的名字也不會跟著變。
    private static string NameOf(StorageArea area) => area switch
    {
        StorageArea.ItemAttachments => "item_attachments",
        StorageArea.KnowledgeAttachments => "knowledge_attachments",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "沒有對應的名稱。"),
    };
}

/// <param name="OrganizationLimitBytes">null 是不限制。</param>
/// <param name="PersonDailyLimitBytes">null 是不限制。</param>
internal sealed record StorageUsageData(
    long? OrganizationLimitBytes, long? PersonDailyLimitBytes, long TotalBytes, IReadOnlyList<StorageAreaData> Areas)
{
    public static StorageUsageData From(StorageUsageReport report) => new(
        report.OrganizationLimitBytes,
        report.PersonDailyLimitBytes,
        report.TotalBytes,
        [.. report.Areas.Select(StorageAreaData.From)]);
}
