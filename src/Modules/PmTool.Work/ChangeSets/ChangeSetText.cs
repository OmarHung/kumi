using System.Globalization;
using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.ChangeSets;

/// <summary>
/// 變更集要存進 jsonb，套用之後藍圖也是。藍圖的驗證不管名稱、說明、摘要裡有沒有控制字元，
/// 但 PostgreSQL 存不下 NUL，其他控制字元只會造成顯示問題，所以存之前在這裡擋下。換行與 Tab 可以用。
/// 合約沒有這種問題的代碼，細項用 changeset.invalid 本身，路徑指向文件裡的那個字串。
/// </summary>
internal static class ChangeSetText
{
    private const string Message = "這段文字有無法儲存的特殊字元（例如控制字元），請刪掉後再送出；換行與 Tab 可以使用。";
    private const string SummaryPath = "/summary";

    /// <param name="summary">變更集的摘要。清單會列出每一份的摘要，所以另外有長度上限。</param>
    /// <param name="document">要存進去的變更集文件（JSON）。</param>
    public static IReadOnlyList<FailureDetail> Problems(string? summary, string document)
    {
        using var parsed = JsonDocument.Parse(document);
        return [.. SummaryTooLong(summary).Concat(Find(parsed.RootElement, string.Empty)).Take(ValidationReport.MaxIssues)];
    }

    private static IEnumerable<FailureDetail> SummaryTooLong(string? summary)
    {
        if (summary is { Length: > WorkRules.MaxChangeSetSummaryLength })
        {
            yield return new FailureDetail(
                WorkErrorCodes.ChangeSetInvalid,
                SummaryPath,
                $"摘要最多 {Count(WorkRules.MaxChangeSetSummaryLength)} 個字，現在有 {Count(summary.Length)} 個字；請刪短一點。");
        }
    }

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    // 文件的屬性名稱來自模型，固定是英文字母，路徑不必跳脫；深度也由模型決定，遞迴不會太深。
    private static IEnumerable<FailureDetail> Find(JsonElement element, string path) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Find(property.Value, $"{path}/{property.Name}")),
        JsonValueKind.Array => element.EnumerateArray()
            .SelectMany((item, index) => Find(item, string.Create(CultureInfo.InvariantCulture, $"{path}/{index}"))),
        JsonValueKind.String when HasForbiddenCharacters(element.GetString()!, allowLineBreaks: true) =>
            [new FailureDetail(WorkErrorCodes.ChangeSetInvalid, path, Message)],
        _ => [],
    };
}
