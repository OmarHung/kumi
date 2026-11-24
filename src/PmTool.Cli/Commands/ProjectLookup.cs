using PmTool.Cli.Api;

namespace PmTool.Cli.Commands;

/// <summary>查專案的藍圖，把代號換成名稱、知道欄位的型別。查不到不算失敗：輸出退回印代號，真正的錯誤由主要的請求回報。</summary>
internal static class ProjectLookup
{
    public static async Task<BlueprintNames> LoadNamesAsync(CommandCall call, string? projectKey)
    {
        if (string.IsNullOrWhiteSpace(projectKey))
        {
            return BlueprintNames.Empty;
        }

        try
        {
            var detail = await call.Api.GetAsync($"projects/{Uri.EscapeDataString(projectKey)}", null, call.CancellationToken);
            return BlueprintNames.From(detail.Get("blueprint"));
        }
        catch (CliException)
        {
            return BlueprintNames.Empty;
        }
    }

    /// <summary>項目編號是「專案代號-數字」：代號是最後一個「-」前面的部分。不是這個樣子就是 null。</summary>
    public static string? ProjectKeyOfItem(string itemKey)
    {
        var dash = itemKey.LastIndexOf('-');
        return dash > 0 && dash < itemKey.Length - 1 ? itemKey[..dash] : null;
    }
}
