using PmTool.Cli.Api;
using PmTool.Cli.Import.Monday;
using PmTool.Cli.Import.Monday.Run;

namespace PmTool.Cli.Tests;

/// <summary>匯入測試的假資料（全是 Example）：記憶體裡的 dump 與對應、直接跑檢查與計畫的小工具。</summary>
internal static class ImportFixtures
{
    public const string Board = "b1";
    public static readonly DateTimeOffset Created = new(2026, 11, 1, 2, 3, 4, TimeSpan.Zero);

    public static readonly IReadOnlyList<MondayColumn> Columns =
    [
        new("status", "Status", "status", ["Done", "Working on it"]),
        new("priority", "Priority", "status", ["High", "Low"]),
        new("person", "Owner", "people", []),
        new("date4", "Due Date", "date", []),
        new("timeline", "Timeline", "timeline", []),
        new("numbers7", "Hrs", "numbers", []),
        new("client", "Client", "board_relation", []),
        new("text1", "Memo", "long_text", []),
        new("linkcol", "Link", "link", []),
        new("check", "Approved", "checkbox", []),
        new("drop", "Tags", "dropdown", []),
        new("reviewer", "Reviewer", "people", []),
    ];

    public static MondayItem Item(
        string id,
        string name = "Example 項目",
        string group = "g1",
        string state = "active",
        string? creator = "1001",
        string? status = "Done",
        string[]? owners = null,
        IReadOnlyList<MondayUpdate>? updates = null,
        IReadOnlyList<MondayAsset>? assets = null,
        IReadOnlyList<MondayItem>? subitems = null,
        params MondayColumnValue[] extra)
    {
        var values = new List<MondayColumnValue> { Status("status", status), People("person", owners ?? []) };
        values.AddRange(extra);
        return new MondayItem(
            id, name, group, state, Created, creator, $"https://example.monday.test/boards/1/pulses/{id}",
            values, subitems ?? [], updates ?? [], assets ?? []);
    }

    public static MondayColumnValue Status(string column, string? label) =>
        new(column, "status", label, label is null ? null : "{\"index\":1}", label, [], []);

    public static MondayColumnValue People(string column, params string[] ids) =>
        new(column, "people", string.Join(", ", ids), null, null, ids, []);

    public static MondayColumnValue Date(string column, string date) =>
        new(column, "date", date, $"{{\"date\":\"{date}\"}}", null, [], []);

    public static MondayColumnValue Timeline(string column, string from, string to) =>
        new(column, "timeline", $"{from} - {to}", $"{{\"from\":\"{from}\",\"to\":\"{to}\"}}", null, [], []);

    public static MondayColumnValue Text(string column, string type, string? text, string? json = null) =>
        new(column, type, text, json, null, [], []);

    public static MondayColumnValue Relation(string column, params string[] names) =>
        new(column, "board_relation", string.Join(", ", names), null, null, [], [.. names.Select((name, index) => new MondayLinkedItem($"r{index}", name))]);

    public static MondayUpdate Update(
        string id, string? creator = "1001", string html = "<p>留言</p>", string at = "2026-11-02T03:00:00Z",
        IReadOnlyList<MondayReply>? replies = null, IReadOnlyList<MondayAsset>? assets = null) =>
        new(id, creator, DateTimeOffset.Parse(at), html, assets ?? [], replies ?? []);

    public static MondayReply Reply(string id, string? creator = "1002", string html = "<p>回覆</p>", string at = "2026-11-02T04:00:00Z") =>
        new(id, creator, DateTimeOffset.Parse(at), html, []);

    public static MondayAsset Asset(string id, string name, string? localPath = null, string? uploader = "1002", string at = "2026-11-02T01:00:00Z") =>
        new(id, name, 10, uploader, DateTimeOffset.Parse(at), localPath);

    public static MondayDump Dump(params MondayItem[] items) => new(
        "/dump",
        Created,
        [new MondayUser("1001", "Example PM", "pm@example.com"), new MondayUser("1002", "Example Dev", "dev@example.com"), new MondayUser("1003", "Former Staff", null)],
        [
            new MondayBoard(
                Board, "Example Cases",
                [new MondayGroup("g1", "Example 官網改版"), new MondayGroup("g2", "2026年11月例行維護"), new MondayGroup("g3", "Temp")],
                Columns,
                [new MondayColumn("status", "Status", "status", ["Done"]), new MondayColumn("person", "Owner", "people", [])],
                items),
        ]);

    public static BoardMapping BoardMapping(Func<BoardMapping, BoardMapping>? change = null)
    {
        var mapping = new BoardMapping
        {
            StatusColumn = "status",
            OwnerColumn = "person",
            DueColumn = "date4",
            TimelineColumn = "timeline",
            PriorityColumn = "priority",
            Priorities = new Dictionary<string, string> { ["High"] = "high", ["Low"] = "low" },
            States = new Dictionary<string, string> { ["Done"] = "done", ["Working on it"] = "doing", [string.Empty] = "todo" },
            Groups = new Dictionary<string, GroupMapping>
            {
                ["g1"] = new() { Project = "CASE", Type = "task" },
                ["g2"] = new()
                {
                    Project = "MNT",
                    Type = "change",
                    Cycle = new CycleMapping("2026 年 11 月", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
                },
                ["g3"] = new() { Skip = true },
            },
        };
        return change?.Invoke(mapping) ?? mapping;
    }

    public static MondayMapping Mapping(Func<BoardMapping, BoardMapping>? changeBoard = null, Func<MondayMapping, MondayMapping>? change = null)
    {
        var mapping = new MondayMapping
        {
            Dump = "/dump",
            People = new Dictionary<string, string?> { ["1001"] = "pm@example.com", ["1002"] = "Dev@Example.com", ["1003"] = null },
            FallbackPerson = "former@example.com",
            Assignee = new AssigneeRule { Exclude = ["1001"] },
            Boards = new Dictionary<string, BoardMapping> { [Board] = BoardMapping(changeBoard) },
        };
        return change?.Invoke(mapping) ?? mapping;
    }

    public static FakeServer Server(out FakePmBackend backend)
    {
        var server = new FakeServer();
        backend = new FakePmBackend();
        backend.Attach(server);
        return server;
    }

    public static HistoryApi Api(FakeServer server, string source = "monday") =>
        new(new ApiClient(new HttpClient(server, disposeHandler: false), CliHarness.ServerUrl, CliHarness.Token), source, CancellationToken.None);

    /// <summary>跑完檢查與計畫，不寫任何東西。</summary>
    public static async Task<ImportPlan> PlanAsync(
        FakeServer server, MondayDump dump, MondayMapping mapping, bool addMembers = false, string memberRole = "editor")
    {
        var api = Api(server, mapping.Source);
        var people = new PersonDirectory(mapping, dump, await api.PeopleAsync());
        return await new ImportPlanner(mapping, dump, people, new RunOptions(DryRun: false, addMembers, memberRole), api, new Notes(), new Notes()).BuildAsync();
    }
}
