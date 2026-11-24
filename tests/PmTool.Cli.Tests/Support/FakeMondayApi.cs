using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace PmTool.Cli.Tests;

/// <summary>
/// 假的 monday：接在 <see cref="FakeServer"/> 上，依查詢的形狀回事先放好的看板、項目、留言。
/// 資料全是假名（Example 公司、example.com）；檔案的簽章網址故意帶著「SECRET_SIG」，用來確認它不會進 dump。
/// </summary>
internal sealed class FakeMondayApi
{
    public const string Token = "mon_example_token_value";
    public const string Url = "https://monday.example.test/v2";
    public const string SecretSignature = "SECRET_SIG";
    public const string BoardId = "123";
    public const string SubitemBoardId = "900";

    private readonly Dictionary<string, byte[]> _files = [];

    public List<JsonObject> Items { get; } = [];

    public Dictionary<string, List<JsonObject>> ExtraUpdates { get; } = [];

    public int ItemsPerPage { get; set; } = 2;

    public List<JsonNode> Variables { get; } = [];

    public string? FailOnCursor { get; set; }

    public static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>登記一個可以下載的檔案，回傳 asset 的 JSON（含簽章網址，跟 monday 一樣）。</summary>
    public JsonObject Asset(string id, string name, string content = "fake-content", long? declaredSize = null, bool downloadable = true)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        if (downloadable)
        {
            _files[$"/f/{id}"] = bytes;
        }

        return Parse($$"""
            { "id": "{{id}}", "name": "{{name}}", "file_extension": ".txt", "file_size": {{declaredSize ?? bytes.Length}},
              "created_at": "2026-11-02T03:04:05Z", "uploaded_by": { "id": "1002" },
              "public_url": "https://files.example.test/f/{{id}}?sig={{SecretSignature}}" }
            """);
    }

    public static JsonObject Reply(string id, string at, string creator = "1002") => Parse($$"""
        { "id": "{{id}}", "creator_id": "{{creator}}", "created_at": "{{at}}", "body": "<p>reply {{id}}</p>", "assets": [] }
        """);

    public static JsonObject Update(string id, string at, JsonArray? replies = null, JsonArray? assets = null, string creator = "1001")
    {
        var update = Parse($$"""
            { "id": "{{id}}", "creator_id": "{{creator}}", "created_at": "{{at}}", "body": "<p>update {{id}}</p>" }
            """);
        update["assets"] = assets ?? [];
        update["replies"] = replies ?? [];
        return update;
    }

    public static JsonObject Item(string id, string name, string group, JsonArray? updates = null, JsonArray? subitems = null, JsonArray? assets = null, string? status = "Done")
    {
        var item = Parse($$"""
            { "id": "{{id}}", "name": "{{name}}", "state": "active", "created_at": "2026-11-01T00:00:00Z", "creator_id": "1001",
              "url": "https://example.monday.test/boards/123/pulses/{{id}}", "group": { "id": "{{group}}" },
              "column_values": [
                { "id": "status", "type": "status", "text": "Done", "value": "{\"index\":1}", "label": {{(status is null ? "null" : $"\"{status}\"")}}, "index": 1 },
                { "id": "person", "type": "people", "text": "Dev", "value": null,
                  "persons_and_teams": [ { "id": "1002", "kind": "person" }, { "id": "77", "kind": "team" } ] },
                { "id": "client", "type": "board_relation", "text": "Example A, Example B", "value": null,
                  "linked_items": [ { "id": "5", "name": "Example, A" }, { "id": "6", "name": "Example B" } ] },
                { "id": "date4", "type": "date", "text": "2026-11-04", "value": "{\"date\":\"2026-11-04\"}" }
              ] }
            """);
        item["assets"] = assets ?? [];
        item["subitems"] = subitems ?? [];
        item["updates"] = updates ?? [];
        return item;
    }

    public void Attach(FakeServer server)
    {
        server.On("POST", "/v2", request => Ok(Handle(request.Json!)));
        foreach (var (path, bytes) in _files)
        {
            server.On("GET", path, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private static HttpResponseMessage Ok(JsonObject data)
    {
        data["complexity"] = Parse("""{ "before": 10000000, "after": 9990000, "query": 10000, "reset_in_x_seconds": 30 }""");
        var body = new JsonObject { ["data"] = data };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    private JsonObject Handle(JsonNode request)
    {
        var query = request["query"]!.GetValue<string>();
        var variables = request["variables"];
        Variables.Add(variables?.DeepClone() ?? new JsonObject());
        if (query.Contains("users(", StringComparison.Ordinal))
        {
            return Parse("""
                { "users": [ { "id": "1001", "name": "Example PM", "email": "pm@example.com" },
                             { "id": "1002", "name": "Example Dev", "email": "Dev@Example.com" },
                             { "id": "1003", "name": "Former Staff", "email": null } ] }
                """);
        }

        if (query.Contains("groups { id title }", StringComparison.Ordinal))
        {
            return Parse(BoardJson);
        }

        if (query.Contains("boards(ids: $ids) { id columns", StringComparison.Ordinal))
        {
            return Parse($$$"""
                { "boards": [ { "id": "{{{SubitemBoardId}}}", "columns": [
                    { "id": "status", "title": "Status", "type": "status", "settings_str": "{\"labels\":{\"0\":\"Todo\",\"1\":\"Done\"}}" },
                    { "id": "person", "title": "Owner", "type": "people", "settings_str": "{}" } ] } ] }
                """);
        }

        if (query.Contains("next_items_page", StringComparison.Ordinal))
        {
            return NextPage(variables!["cursor"]!.GetValue<string>());
        }

        if (query.Contains("items_page", StringComparison.Ordinal))
        {
            return Page(0);
        }

        var page = variables!["page"]!.GetValue<int>();
        var itemId = variables["ids"]![0]!.GetValue<string>();
        var extra = ExtraUpdates.GetValueOrDefault(itemId) ?? [];
        var slice = extra.Skip((page - 2) * 100).Take(100).Select(update => update.DeepClone()).ToArray();
        return Parse($$"""{ "items": [ { "id": "{{itemId}}", "updates": [] } ] }""").Also(data => data["items"]![0]!["updates"] = new JsonArray(slice));
    }

    private JsonObject NextPage(string cursor)
    {
        if (cursor == FailOnCursor)
        {
            throw new HttpRequestException("連線中斷");
        }

        return Page(int.Parse(cursor["c".Length..], System.Globalization.CultureInfo.InvariantCulture));
    }

    private JsonObject Page(int skip)
    {
        var slice = Items.Skip(skip).Take(ItemsPerPage).Select(item => item.DeepClone()).ToArray();
        var more = skip + ItemsPerPage < Items.Count;
        var page = new JsonObject { ["cursor"] = more ? $"c{skip + ItemsPerPage}" : null, ["items"] = new JsonArray(slice) };
        return skip == 0 && Variables[^1]?["cursor"] is null
            ? new JsonObject { ["boards"] = new JsonArray(new JsonObject { ["items_page"] = page }) }
            : new JsonObject { ["next_items_page"] = page };
    }

    private static string BoardJson => $$$"""
        { "boards": [ { "id": "{{{BoardId}}}", "name": "Example Cases",
            "groups": [ { "id": "group_a", "title": "Example 官網改版" }, { "id": "group_m11", "title": "2026年11月例行維護" }, { "id": "group_old", "title": "Temp" } ],
            "columns": [
              { "id": "name", "title": "Name", "type": "name", "settings_str": "{}" },
              { "id": "status", "title": "Status", "type": "status", "settings_str": "{\"labels\":{\"1\":\"Done\",\"0\":\"Working on it\",\"2\":\"Stuck\",\"5\":\"\"}}" },
              { "id": "priority", "title": "Priority", "type": "status", "settings_str": "{\"labels\":{\"0\":\"High\",\"1\":\"Low\"}}" },
              { "id": "person", "title": "Owner", "type": "people", "settings_str": "{}" },
              { "id": "date4", "title": "Due Date", "type": "date", "settings_str": "{}" },
              { "id": "timeline", "title": "Timeline", "type": "timeline", "settings_str": "{}" },
              { "id": "numbers7", "title": "Hrs", "type": "numbers", "settings_str": "{}" },
              { "id": "client", "title": "Client", "type": "board_relation", "settings_str": "{}" },
              { "id": "files", "title": "Files", "type": "file", "settings_str": "{}" },
              { "id": "subitems", "title": "Subitems", "type": "subtasks", "settings_str": "{\"boardIds\":[{{{SubitemBoardId}}}]}" } ] } ] }
        """;
}

internal static class JsonObjectExtensions
{
    public static JsonObject Also(this JsonObject value, Action<JsonObject> action)
    {
        action(value);
        return value;
    }
}
