using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>刪除、復原與已刪除的清單。一個編號走單一項目的端點，多個走批次的端點。</summary>
internal static class ItemRecoveryCommands
{
    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("item", "delete", "刪除項目（可以一次多個，要是同一個專案）", "kumi item delete 編號...", [], 1, int.MaxValue, DeleteAsync),
        new("item", "restore", "復原已刪除的項目（可以一次多個，要是同一個專案）", "kumi item restore 編號...", [], 1, int.MaxValue, RestoreAsync),
        new("item", "deleted", "列出專案已刪除的項目，最近刪除的在前",
            "kumi item deleted --project 代號 [--limit 數量] [--cursor 值] [--all]",
            [new("project", "代號", "專案代號", 'p'), new("limit", "數量", "一頁最多幾個（預設 50，最多 200）"),
             new("cursor", "值", "從這個位置接著列（用 --json 輸出的 nextCursor）"), new("all", "", "自動翻頁，一次列完")],
            0, 0, ListDeletedAsync),
    ];

    private static async Task<int> DeleteAsync(CommandCall call)
    {
        var keys = call.Arguments.Positionals;
        if (keys.Count > 1)
        {
            return await ItemBulkCommands.RunAsync(call, keys, "bulk-delete", [], "deleted", needsNames: false,
                (item, _) => $"已刪除 {item.Display("key")}。");
        }

        var deleted = await call.Api.DeleteAsync($"items/{Uri.EscapeDataString(keys[0])}", call.CancellationToken);
        return await ReportAsync(call, deleted, $"已刪除 {keys[0]}。");
    }

    private static async Task<int> RestoreAsync(CommandCall call)
    {
        var keys = call.Arguments.Positionals;
        if (keys.Count > 1)
        {
            return await ItemBulkCommands.RunAsync(call, keys, "bulk-restore", [], "restored", needsNames: false,
                (item, _) => Restored(item));
        }

        var item = await call.Api.PostAsync($"items/{Uri.EscapeDataString(keys[0])}/restore", null, call.CancellationToken);
        return await ReportAsync(call, item, Restored(item));
    }

    private static string Restored(JsonElement item) => $"已復原 {item.Display("key")}：{item.Display("title")}";

    private static async Task<int> ReportAsync(CommandCall call, JsonElement data, string message)
    {
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, data);
        }
        else
        {
            await call.Out.WriteLineAsync(message);
        }

        return ExitCodes.Success;
    }

    private static async Task<int> ListDeletedAsync(CommandCall call)
    {
        var projectKey = call.Arguments.Required("project");
        var path = $"projects/{Uri.EscapeDataString(projectKey)}/items/deleted";
        var query = new List<KeyValuePair<string, string>>();
        ItemCommands.AddAll(query, "limit", call.Arguments.Values("limit"));
        var all = call.Arguments.Has("all");
        if (all && !call.Arguments.Has("limit"))
        {
            query.Add(new("limit", $"{ItemCommands.MaxPageSize}"));
        }

        var (items, nextCursor) = await ItemCommands.ReadPagesAsync(call, path, query, call.Arguments.Value("cursor"), all);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["items"] = new JsonArray([.. items.Select(item => JsonNode.Parse(item.GetRawText()))]),
                ["nextCursor"] = nextCursor,
            });
            return ExitCodes.Success;
        }

        var names = await ProjectLookup.LoadNamesAsync(call, projectKey);
        var rows = items
            .Select(item => (IReadOnlyList<string>)
            [
                item.Display("key"), names.TypeName(item.Text("type")), item.Display("title"),
                names.StateName(item.Text("type"), item.Text("state")),
                item.Get("deletedBy")?.Display("displayName") ?? "-", JsonAccess.Moment(item.Text("deletedAt")),
            ])
            .ToArray();
        await TextTable.WriteAsync(call.Out, ["編號", "類型", "標題", "狀態", "刪除的人", "刪除時間"], rows);
        if (nextCursor is not null)
        {
            await call.Error.WriteLineAsync(
                $"還有更多已刪除的項目沒有列出。用 --cursor {nextCursor} 看下一頁（其他選項和這次一樣），或加 --all 一次列完。");
        }

        return ExitCodes.Success;
    }
}
