using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>跨專案搜尋：找得到哪些專案、看不看得到由伺服器判斷，這裡只轉換格式。</summary>
internal static class SearchCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("search", string.Empty, "用標題或編號找任何看得到的專案裡的項目", "kumi search 文字 [--limit 數量]",
            [new("limit", "數量", "最多列幾個（1 到 20，預設 8）")], 1, 1, SearchAsync),
    ];

    private static async Task<int> SearchAsync(CommandCall call)
    {
        var query = new List<KeyValuePair<string, string>> { new("q", call.Arguments.Positionals[0]) };
        if (call.Arguments.Value("limit") is { } limit)
        {
            query.Add(new("limit", limit));
        }

        var data = await call.Api.GetAsync("search/items", query, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, data);
            return ExitCodes.Success;
        }

        var items = data.List("items");
        if (items.Count == 0)
        {
            await call.Out.WriteLineAsync("沒有找到符合的項目。");
            return ExitCodes.Success;
        }

        var projectNames = await LoadProjectNamesAsync(call);
        var blueprints = new Dictionary<string, BlueprintNames>();
        foreach (var projectKey in items.Select(item => item.Text("projectKey") ?? string.Empty).Distinct())
        {
            blueprints[projectKey] = await ProjectLookup.LoadNamesAsync(call, projectKey);
        }

        var rows = items
            .Select(item => (IReadOnlyList<string>)
            [
                item.Display("key"), item.Display("title"),
                blueprints[item.Text("projectKey") ?? string.Empty].StateName(item.Text("type"), item.Text("state")),
                projectNames.GetValueOrDefault(item.Text("projectKey") ?? string.Empty) ?? item.Display("projectKey"),
            ])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["編號", "標題", "狀態", "專案"], rows);
        return ExitCodes.Success;
    }

    /// <summary>專案名稱只是讓輸出好讀；查不到就退回專案代號，不讓搜尋因此失敗。</summary>
    private static async Task<Dictionary<string, string>> LoadProjectNamesAsync(CommandCall call)
    {
        try
        {
            var projects = await call.Api.GetAsync("projects", null, call.CancellationToken);
            return projects.Items()
                .Where(project => project.Text("key") is not null && project.Text("name") is not null)
                .ToDictionary(project => project.Text("key")!, project => project.Text("name")!);
        }
        catch (CliException)
        {
            return [];
        }
    }
}
