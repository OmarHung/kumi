using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>一次處理多個項目：改狀態、改屬性、刪除、復原。成功與失敗分開回報，失敗的不影響成功的。</summary>
internal static class ItemBulkCommands
{
    private static readonly HashSet<string> BulkUpdatable = ["priority", "startDate", "dueDate"];

    private const string BulkUpdateLimit = "一次改多個項目時只能改負責人、優先度、開始日、到期日。";

    public static async Task<int> MoveAsync(CommandCall call, IReadOnlyList<string> keys)
    {
        var to = call.Arguments.Required("to");
        return await RunAsync(call, keys, "bulk-update", new JsonObject { ["state"] = to }, "updated", needsNames: true,
            (item, names) => $"{item.Display("key")} 已移到「{names.StateName(item.Text("type"), item.Text("state"))}」。");
    }

    public static async Task<int> UpdateAsync(CommandCall call, IReadOnlyList<string> keys)
    {
        // 先在本機擋下不允許的屬性：不送出、不先去查人員。
        if (call.Arguments.Has("title"))
        {
            throw CliException.Usage(BulkUpdateLimit);
        }

        var body = new JsonObject();
        ItemInput.ApplySettings(body, call.Arguments.Values("set"), BlueprintNames.Empty);
        if (body.Any(property => !BulkUpdatable.Contains(property.Key)))
        {
            throw CliException.Usage(BulkUpdateLimit);
        }

        if (call.Arguments.Value("assignee") is { } assignee)
        {
            body["assigneeId"] = await ItemInput.ResolveAssigneeAsync(call, assignee, allowNone: true);
        }

        if (body.Count == 0)
        {
            throw CliException.Usage("沒有要改的東西。請至少給 --assignee 或 --set。");
        }

        return await RunAsync(call, keys, "bulk-update", body, "updated", needsNames: false,
            (item, _) => $"已更新 {item.Display("key")}。");
    }

    /// <summary>
    /// 多個編號要是同一個專案：端點是以專案為單位的。回傳專案代號（大寫）。
    /// 比對不分大小寫，和伺服器一樣（WEB-1 與 web-2 是同一個專案）。
    /// </summary>
    public static string ProjectOf(IReadOnlyList<string> keys)
    {
        var projects = new List<string>();
        foreach (var key in keys)
        {
            var project = ProjectLookup.ProjectKeyOfItem(key)?.ToUpperInvariant()
                ?? throw CliException.Usage($"項目編號的格式不對：{key}。");
            if (!projects.Contains(project))
            {
                projects.Add(project);
            }
        }

        return projects.Count == 1
            ? projects[0]
            : throw CliException.Usage($"一次只能處理同一個專案的項目，收到：{string.Join("、", projects)}。");
    }

    /// <summary>送出批次請求。成功的照單一操作的格式一行一筆；失敗的逐筆印到標準錯誤，並以「規則擋下」的結束代碼結束。</summary>
    public static async Task<int> RunAsync(
        CommandCall call, IReadOnlyList<string> keys, string action, JsonObject extra, string succeededProperty, bool needsNames,
        Func<JsonElement, BlueprintNames, string> describe)
    {
        var project = ProjectOf(keys);
        var body = new JsonObject { ["keys"] = new JsonArray([.. keys.Select(key => (JsonNode?)JsonValue.Create(key))]) };
        foreach (var (name, value) in extra)
        {
            body[name] = value?.DeepClone();
        }

        var data = await call.Api.PostAsync($"projects/{Uri.EscapeDataString(project)}/items/{action}", body, call.CancellationToken);
        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, data);
        }
        else
        {
            var names = needsNames ? await ProjectLookup.LoadNamesAsync(call, project) : BlueprintNames.Empty;
            foreach (var item in data.List(succeededProperty))
            {
                await call.Out.WriteLineAsync(describe(item, names));
            }
        }

        var failures = data.List("failed");
        foreach (var failure in failures)
        {
            await call.Error.WriteLineAsync($"{failure.Display("key")}：{failure.Get("error")?.Display("message") ?? "失敗"}");
        }

        return failures.Count == 0 ? ExitCodes.Success : ExitCodes.Invalid;
    }
}
