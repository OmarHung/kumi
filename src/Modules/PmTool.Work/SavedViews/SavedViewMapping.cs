using System.Text.Json;
using Microsoft.Extensions.Logging;
using PmTool.Work.Persistence;

namespace PmTool.Work.SavedViews;

/// <summary>資料列與合約型別互轉。條件存成 jsonb，之後加條件不必改資料表。</summary>
internal static class SavedViewMapping
{
    private const string TableName = "table";
    private const string BoardName = "board";
    private const string CalendarName = "calendar";
    private const string TimelineName = "timeline";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string LayoutName(SavedViewLayout layout) => layout switch
    {
        SavedViewLayout.Table => TableName,
        SavedViewLayout.Board => BoardName,
        SavedViewLayout.Calendar => CalendarName,
        SavedViewLayout.Timeline => TimelineName,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "版面在檢查欄位時就確認過了。"),
    };

    public static string QueryJson(SavedViewQuery query) => JsonSerializer.Serialize(query, Json);

    /// <summary>
    /// 一筆壞掉的條件（手動改過資料庫、或舊版寫的）不能讓整份清單失敗：那一筆當成沒設任何條件，並記一筆警告。
    /// 記錄只帶檢視編號，不帶條件的內容。
    /// </summary>
    public static SavedView ToContract(SavedViewEntity row, ILogger logger) => new(
        row.Id,
        row.Name,
        LayoutOf(row.Layout),
        ReadQuery(row, logger),
        row.CreatedAt,
        row.UpdatedAt);

    // 不認得的名字（舊版或手動改過）當成表格，和壞掉的條件一樣不讓整份清單失敗。
    private static SavedViewLayout LayoutOf(string name) => name switch
    {
        BoardName => SavedViewLayout.Board,
        CalendarName => SavedViewLayout.Calendar,
        TimelineName => SavedViewLayout.Timeline,
        _ => SavedViewLayout.Table,
    };

    private static SavedViewQuery ReadQuery(SavedViewEntity row, ILogger logger)
    {
        try
        {
            if (JsonSerializer.Deserialize<SavedViewQuery>(row.Query, Json) is { } query)
            {
                return query;
            }
        }
        catch (JsonException)
        {
            // 落到下面和 null 一樣處理。
        }

        logger.LogWarning("檢視 {ViewId} 的條件讀不回來，當成沒有設任何條件。", row.Id);
        return new SavedViewQuery(null, null, null, null, null, null, null);
    }
}
