using System.Collections.Frozen;
using PmTool.Api.Responses;

namespace PmTool.Api.Endpoints;

/// <summary>
/// 項目的新增與修改被 API 擋下（400）時，細項的路徑換成工作引擎在 422 裡用的名字（文件裡的名字），
/// 同一個欄位在兩種錯誤裡寫法一樣，網頁只要認一種。只換整個屬性名稱；
/// 名稱本來就一樣的（type、title、priority、estimate）與 fields 底下的路徑照原樣。
/// </summary>
internal static class ItemRequestPaths
{
    private static readonly FrozenDictionary<string, string> ModuleNames = new Dictionary<string, string>
    {
        ["assigneeId"] = "assignee",
        ["startDate"] = "start_date",
        ["dueDate"] = "due_date",
        ["parentKey"] = "parent",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static ApiFailure UseModuleNames(ApiFailure failure) => failure with
    {
        Details = [.. failure.Details.Select(detail => detail with { Path = ModuleNames.GetValueOrDefault(detail.Path, detail.Path) })],
    };
}
