using System.Text.Json;
using System.Text.Json.Nodes;
using PmTool.Cli.Api;
using PmTool.Cli.Output;

namespace PmTool.Cli.Commands;

/// <summary>項目的查詢、建立、修改、改狀態與留言。看不看得到、能不能改都由伺服器判斷，這裡只轉換格式。</summary>
internal static class ItemCommands
{
    private const int ViewChildrenLimit = 50;
    private const int ViewActivityLimit = 10;
    internal const int MaxPageSize = 200;

    private static readonly OptionSpec Project = new("project", "代號", "專案代號", 'p');
    private static readonly OptionSpec Set = new("set", "欄位=值", "設定欄位；值是 JSON 的數字、true、null 就當 JSON，否則是文字", Repeatable: true);
    private static readonly OptionSpec Assignee = new("assignee", "@me|email", "負責人");

    private static readonly string[] DateOptions = ["due-from", "due-to", "dates-from", "dates-to"];

    public static readonly IReadOnlyList<CommandSpec> Specs =
    [
        new("item", "list", "列出專案的項目", "kumi item list --project 代號 [--mine] [--state 狀態] [--category 類別] [--type 類型] [--priority 優先順序] [--due-from 日期] [--due-to 日期] [--dates-from 日期] [--dates-to 日期] [--no-due] [--field 欄位=值[,值…]]... [--limit 數量] [--cursor 值] [--all]",
            [Project, new("mine", "", "只看指派給我的"), new("state", "狀態", "狀態的 key", Repeatable: true),
             new("category", "類別", "狀態類別（backlog、not_started、in_progress、done、canceled）", Repeatable: true),
             new("type", "類型", "項目類型的 key", Repeatable: true),
             new("priority", "優先順序", "優先順序（none、low、medium、high、urgent）", Repeatable: true),
             new("due-from", "日期", "到期日在這一天或之後（YYYY-MM-DD）"), new("due-to", "日期", "到期日在這一天或之前（YYYY-MM-DD）"),
             new("dates-from", "日期", "期間（開始日到到期日）和這一天之後有重疊的（YYYY-MM-DD）"),
             new("dates-to", "日期", "期間和這一天之前有重疊的（YYYY-MM-DD）"),
             new("no-due", "", "只看沒有到期日的（不能和 --due-from、--due-to、--dates-from、--dates-to 一起用）"), new("field", "欄位=值[,值…]", "依自訂欄位篩選：值是選項的 key、人員編號、me、true／false，_none 是沒有填；同一欄位的多個值用逗號（或），不同欄位可以重複給（而且）", Repeatable: true),
             new("limit", "數量", "一頁最多幾個（預設 50，最多 200）"),
             new("cursor", "值", "從這個位置接著列（用 --json 輸出的 nextCursor）"), new("all", "", "自動翻頁，一次列完")],
            0, 0, ListAsync),
        new("item", "view", "看一個項目：欄位、下層與最近的活動", "kumi item view 編號 [--json]", [], 1, 1, ViewAsync),
        new("item", "create", "建立項目", "kumi item create --project 代號 --type 類型 --title 標題 [--assignee @me|email] [--set 欄位=值]...",
            [Project, new("type", "類型", "項目類型的 key"), new("title", "標題", "標題", 't'), Assignee, Set], 0, 0, CreateAsync),
        new("item", "update", "修改項目（多個編號時只能改負責人、優先度、開始日、到期日）",
            "kumi item update 編號... [--title 標題] [--assignee @me|email|none] [--set 欄位=值]...",
            [new("title", "標題", "新的標題", 't'), new("assignee", "@me|email|none", "負責人；none 是清掉"), Set], 1, int.MaxValue, UpdateAsync),
        new("item", "move", "改項目的狀態（可以一次多個，要是同一個專案）", "kumi item move 編號... --to 狀態",
            [new("to", "狀態", "狀態的 key")], 1, int.MaxValue, MoveAsync),
        new("item", "comment", "留言", "kumi item comment 編號 -m 內容", [new("message", "內容", "留言的內容", 'm')], 1, 1, CommentAsync),
    ];

    private static async Task<int> ListAsync(CommandCall call)
    {
        var projectKey = call.Arguments.Required("project");
        var path = $"projects/{Uri.EscapeDataString(projectKey)}/items";
        var query = new List<KeyValuePair<string, string>>();
        if (call.Arguments.Has("mine"))
        {
            query.Add(new("assignee", "me"));
        }

        AddAll(query, "state", call.Arguments.Values("state"));
        AddAll(query, "stateCategory", call.Arguments.Values("category"));
        AddAll(query, "type", call.Arguments.Values("type"));
        AddDueAndPriority(call, query);
        query.AddRange(FieldFilterOptions.ToQuery(call.Arguments.Values("field")));
        AddAll(query, "limit", call.Arguments.Values("limit"));
        var all = call.Arguments.Has("all");
        if (all && !call.Arguments.Has("limit"))
        {
            query.Add(new("limit", $"{MaxPageSize}"));
        }

        var (items, nextCursor) = await ReadPagesAsync(call, path, query, call.Arguments.Value("cursor"), all);
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
        await ItemPrinter.WriteTableAsync(call.Out, items, names);
        if (nextCursor is not null)
        {
            await call.Error.WriteLineAsync(
                $"還有更多項目沒有列出。用 --cursor {nextCursor} 看下一頁（其他選項和這次一樣），或加 --all 一次列完。");
        }

        return ExitCodes.Success;
    }

    private static void AddDueAndPriority(CommandCall call, List<KeyValuePair<string, string>> query)
    {
        if (call.Arguments.Has("no-due") && DateOptions.Any(call.Arguments.Has))
        {
            throw CliException.Usage("--no-due 不能和 --due-from、--due-to、--dates-from、--dates-to 一起用。");
        }

        AddAll(query, "priority", call.Arguments.Values("priority"));
        AddAll(query, "dueFrom", call.Arguments.Values("due-from"));
        AddAll(query, "dueTo", call.Arguments.Values("due-to"));
        AddAll(query, "datesFrom", call.Arguments.Values("dates-from"));
        AddAll(query, "datesTo", call.Arguments.Values("dates-to"));
        if (call.Arguments.Has("no-due"))
        {
            query.Add(new("noDue", "true"));
        }
    }

    /// <summary>讀一頁，或加了 --all 就一直翻到沒有下一頁。伺服器重複給同一個位置時停下來，不要無限地翻。</summary>
    internal static async Task<(IReadOnlyList<JsonElement> Items, string? NextCursor)> ReadPagesAsync(
        CommandCall call, string path, List<KeyValuePair<string, string>> query, string? cursor, bool all)
    {
        var items = new List<JsonElement>();
        var seen = new HashSet<string>();
        while (true)
        {
            var pageQuery = cursor is null ? query : [.. query, new KeyValuePair<string, string>("cursor", cursor)];
            var page = await call.Api.GetAsync(path, pageQuery, call.CancellationToken);
            items.AddRange(page.List("items"));
            cursor = page.Text("nextCursor");
            if (!all || cursor is null)
            {
                return (items, cursor);
            }

            if (!seen.Add(cursor))
            {
                throw new CliException(ExitCodes.Error, "伺服器重複回了同一個位置，沒辦法繼續翻頁。");
            }
        }
    }

    private static async Task<int> ViewAsync(CommandCall call)
    {
        var key = Uri.EscapeDataString(call.Arguments.Positionals[0]);
        var item = await call.Api.GetAsync($"items/{key}", null, call.CancellationToken);
        var projectKey = Uri.EscapeDataString(item.Text("projectKey") ?? string.Empty);
        var children = await call.Api.GetAsync(
            $"projects/{projectKey}/items", [new("parent", item.Text("key") ?? string.Empty), new("limit", $"{ViewChildrenLimit}")], call.CancellationToken);
        var activity = await RecentActivity.LoadAsync(call, key, ViewActivityLimit);
        if (activity.ReachedPageLimit)
        {
            await call.Error.WriteLineAsync("活動太多，只讀了前一萬筆：下面列的不一定是最新的。");
        }

        if (call.Json)
        {
            await JsonOutput.WriteAsync(call.Out, new JsonObject
            {
                ["item"] = JsonNode.Parse(item.GetRawText()),
                ["children"] = JsonNode.Parse(children.Get("items")?.GetRawText() ?? "[]"),
                ["activity"] = new JsonArray([.. activity.Newest.Select(entry => JsonNode.Parse(entry.GetRawText()))]),
            });
            return ExitCodes.Success;
        }

        var names = await ProjectLookup.LoadNamesAsync(call, item.Text("projectKey"));
        var more = new ItemPrinter.Truncation(
            children.Text("nextCursor") is not null, ViewChildrenLimit, activity.HasEarlier, ViewActivityLimit);
        await ItemPrinter.WriteDetailAsync(call.Out, item, children.List("items"), activity.Newest, names, more);
        return ExitCodes.Success;
    }

    private static async Task<int> CreateAsync(CommandCall call)
    {
        var projectKey = call.Arguments.Required("project");
        var body = new JsonObject { ["type"] = call.Arguments.Required("type"), ["title"] = call.Arguments.Required("title") };
        if (call.Arguments.Value("assignee") is { } assignee)
        {
            body["assigneeId"] = await ItemInput.ResolveAssigneeAsync(call, assignee, allowNone: false);
        }

        await ApplySettingsAsync(call, body, projectKey);
        var item = await call.Api.PostAsync($"projects/{Uri.EscapeDataString(projectKey)}/items", body, call.CancellationToken);
        return await ReportAsync(call, item, $"已建立 {item.Display("key")}：{item.Display("title")}");
    }

    private static async Task<int> UpdateAsync(CommandCall call)
    {
        if (call.Arguments.Positionals.Count > 1)
        {
            return await ItemBulkCommands.UpdateAsync(call, call.Arguments.Positionals);
        }

        var body = new JsonObject();
        if (call.Arguments.Value("title") is { } title)
        {
            body["title"] = title;
        }

        if (call.Arguments.Value("assignee") is { } assignee)
        {
            body["assigneeId"] = await ItemInput.ResolveAssigneeAsync(call, assignee, allowNone: true);
        }

        var key = call.Arguments.Positionals[0];
        await ApplySettingsAsync(call, body, ProjectLookup.ProjectKeyOfItem(key));
        if (body.Count == 0)
        {
            throw CliException.Usage("沒有要改的東西。請至少給 --title、--assignee 或 --set。");
        }

        var item = await call.Api.PatchAsync($"items/{Uri.EscapeDataString(key)}", body, call.CancellationToken);
        return await ReportAsync(call, item, $"已更新 {item.Display("key")}。");
    }

    private static async Task<int> MoveAsync(CommandCall call)
    {
        if (call.Arguments.Positionals.Count > 1)
        {
            return await ItemBulkCommands.MoveAsync(call, call.Arguments.Positionals);
        }

        var key = call.Arguments.Positionals[0];
        var item = await call.Api.PostAsync(
            $"items/{Uri.EscapeDataString(key)}/transitions", new JsonObject { ["to"] = call.Arguments.Required("to") }, call.CancellationToken);
        var names = call.Json ? BlueprintNames.Empty : await ProjectLookup.LoadNamesAsync(call, item.Text("projectKey") ?? ProjectLookup.ProjectKeyOfItem(key));
        return await ReportAsync(call, item, $"{item.Display("key")} 已移到「{names.StateName(item.Text("type"), item.Text("state"))}」。");
    }

    private static async Task<int> CommentAsync(CommandCall call)
    {
        var key = call.Arguments.Positionals[0];
        var comment = await call.Api.PostAsync(
            $"items/{Uri.EscapeDataString(key)}/comments", new JsonObject { ["body"] = call.Arguments.Required("message") }, call.CancellationToken);
        return await ReportAsync(call, comment, $"已在 {key} 留言。");
    }

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

    /// <summary>--set 的值要依欄位的型別轉，所以有自訂欄位時先查藍圖；查不到就退回猜法，不擋住指令。</summary>
    private static async Task ApplySettingsAsync(CommandCall call, JsonObject body, string? projectKey)
    {
        var settings = call.Arguments.Values("set");
        var names = ItemInput.HasCustomFields(settings) ? await ProjectLookup.LoadNamesAsync(call, projectKey) : BlueprintNames.Empty;
        ItemInput.ApplySettings(body, settings, names);
    }

    internal static void AddAll(List<KeyValuePair<string, string>> query, string name, IEnumerable<string> values) =>
        query.AddRange(values.Select(value => new KeyValuePair<string, string>(name, value)));
}
