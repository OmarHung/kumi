using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>留言的列表、編輯、刪除與復原。誰能做、能不能復原由伺服器判斷，這裡只轉換格式。</summary>
internal static class CommentCommands
{
    private const int MaxPages = 50;
    private const int PageSize = 200;

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("item", "comments", "列出項目的留言（含編號、已編輯與已刪除的標記）", "kumi item comments 編號", [], 1, 1, ListAsync),
        new("item", "comment-edit", "編輯自己的留言（舊的版本伺服器會留著）", "kumi item comment-edit 編號 留言編號 -m 內容",
            [new("message", "內容", "新的內容", 'm')], 2, 2, EditAsync),
        new("item", "comment-delete", "刪除留言（可以用 comment-restore 復原）", "kumi item comment-delete 編號 留言編號", [], 2, 2, DeleteAsync),
        new("item", "comment-restore", "復原已刪除的留言", "kumi item comment-restore 編號 留言編號", [], 2, 2, RestoreAsync),
    ];

    private static async Task<int> ListAsync(CommandCall call)
    {
        var key = call.Arguments.Positionals[0];
        var (items, reachedLimit) = await ReadAllAsync(call, Uri.EscapeDataString(key));
        if (reachedLimit)
        {
            await call.Error.WriteLineAsync("留言太多，只讀了前一萬筆。");
        }

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["items"] = new JsonArray([.. items.Select(item => JsonNode.Parse(item.GetRawText()))]),
            });
            return ExitCodes.Success;
        }

        await WriteThreadAsync(call.Out, items);
        return ExitCodes.Success;
    }

    private static async Task<(IReadOnlyList<JsonElement> Items, bool ReachedLimit)> ReadAllAsync(CommandCall call, string escapedKey)
    {
        var items = new List<JsonElement>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var query = new List<KeyValuePair<string, string>> { new("kind", "commented"), new("limit", $"{PageSize}") };
            if (cursor is not null)
            {
                query.Add(new("cursor", cursor));
            }

            var response = await call.Api.GetAsync($"items/{escapedKey}/activity", query, call.CancellationToken);
            items.AddRange(response.List("items").Select(item => item.Clone()));
            cursor = response.Text("nextCursor");
            if (cursor is null)
            {
                return (items, false);
            }
        }

        return (items, true);
    }

    /// <summary>依時間列出，回覆縮排在它回覆的那一則底下；找不到上層的回覆（上層不在這批資料裡）照時間放在原位。</summary>
    private static async Task WriteThreadAsync(TextWriter output, IReadOnlyList<JsonElement> items)
    {
        var ids = items.Select(item => item.Text("id")).OfType<string>().ToHashSet();
        var replies = items
            .Where(item => item.Text("replyToId") is { } parent && ids.Contains(parent))
            .ToLookup(item => item.Text("replyToId")!);
        foreach (var item in items.Where(item => item.Text("replyToId") is not { } parent || !ids.Contains(parent)))
        {
            await output.WriteLineAsync(Line(item, indent: false));
            foreach (var reply in replies[item.Text("id") ?? string.Empty])
            {
                await output.WriteLineAsync(Line(reply, indent: true));
            }
        }
    }

    private static string Line(JsonElement item, bool indent)
    {
        var prefix = indent ? "  " : string.Empty;
        var head = $"{prefix}{item.Display("id")}  {JsonAccess.Moment(item.Text("at"))}  {item.Get("actor")?.Display("displayName") ?? "-"}";
        if (item.Text("deletedAt") is not null)
        {
            var by = item.Get("deletedBy")?.Text("displayName");
            return $"{head}  {(by is null ? "（已刪除）" : $"（已由 {by} 刪除）")}";
        }

        var edited = item.Text("editedAt") is null ? string.Empty : "（已編輯）";
        var first = (item.Text("comment") ?? string.Empty).Split('\n')[0].TrimEnd('\r');
        return $"{head}  {edited}{first}";
    }

    private static Task<int> EditAsync(CommandCall call) =>
        SendAsync(call, "已更新 {0} 的留言。", (path, token) =>
            call.Api.PatchAsync(path, new JsonObject { ["body"] = call.Arguments.Required("message") }, token));

    private static Task<int> DeleteAsync(CommandCall call) =>
        SendAsync(call, "已刪除 {0} 的一則留言（可以用 kumi item comment-restore 復原）。", call.Api.DeleteAsync);

    private static Task<int> RestoreAsync(CommandCall call) =>
        SendAsync(call, "已復原 {0} 的一則留言。", (path, token) => call.Api.PostAsync($"{path}/restore", null, token));

    private static async Task<int> SendAsync(
        CommandCall call, string message, Func<string, CancellationToken, Task<JsonElement>> send)
    {
        var key = call.Arguments.Positionals[0];
        var path = $"items/{Uri.EscapeDataString(key)}/comments/{Uri.EscapeDataString(call.Arguments.Positionals[1])}";
        var activity = await send(path, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, activity);
        }
        else
        {
            await call.Out.WriteLineAsync(string.Format(message, key));
        }

        return ExitCodes.Success;
    }
}
