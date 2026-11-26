using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PmTool.Cli.Tests;

/// <summary>
/// 假的 Kumi 伺服器的歷史匯入部分：人員、專案、週期、成員、四個 history 端點。它記得匯入過什麼，
/// 所以重跑時 lookup 與略過的行為和真的一樣。規則只做到測試需要的：成員、外部編號重複、回覆的順序。
/// </summary>
internal sealed partial class FakePmBackend
{
    public static readonly Guid Pm = Guid.Parse("0190a000-0000-7000-8000-000000000001");
    public static readonly Guid Dev = Guid.Parse("0190a000-0000-7000-8000-000000000002");
    public static readonly Guid Former = Guid.Parse("0190a000-0000-7000-8000-000000000003");

    private int _cycleCounter;

    public Dictionary<string, HashSet<Guid>> Members { get; } = new() { ["CASE"] = [Pm, Dev, Former], ["MNT"] = [Pm, Dev, Former] };

    public Dictionary<string, List<(Guid Id, string Name)>> Cycles { get; } = new() { ["CASE"] = [], ["MNT"] = [] };

    public HashSet<string> ArchivedProjects { get; } = [];

    public bool CanManage { get; set; } = true;

    /// <summary>設了的話，上傳附件一律回這個失敗（HTTP 狀態、錯誤代碼）。</summary>
    public (HttpStatusCode Status, string Code)? AttachmentFailure { get; set; }

    /// <summary>加成員時送的角色。</summary>
    public List<string> MemberRoles { get; } = [];

    /// <summary>外部編號 → 項目所在的專案與編號。</summary>
    public Dictionary<string, (string Project, string ItemKey)> Items { get; } = [];

    public Dictionary<string, (string Project, string ItemKey, Guid Id, JsonNode Json)> Comments { get; } = [];

    public Dictionary<string, (string Project, string ItemKey, Guid Id)> Attachments { get; } = [];

    /// <summary>每次上傳附件時，欄位出現的順序。</summary>
    public List<string[]> UploadFieldOrders { get; } = [];

    /// <summary>設成 true，留言端點回 500（模擬匯到一半失敗）。</summary>
    public bool FailComments { get; set; }

    /// <summary>伺服器逐列檢查的額外規則：回傳問題就是 422 的一筆細項（路徑用 snake_case 的欄位）。</summary>
    public Func<JsonNode, string?>? RowProblem { get; set; }

    public void Attach(FakeServer server)
    {
        server.On("GET", "/v1/people", _ => FakeServer.Envelope(HttpStatusCode.OK, new object[]
        {
            Person(Pm, "Example PM", "pm@example.com"),
            Person(Dev, "Example Dev", "dev@example.com"),
            Person(Former, "Former Staff", "former@example.com"),
        }));
        foreach (var key in Members.Keys.ToArray())
        {
            server.On("GET", $"/v1/projects/{key}", _ => FakeServer.Envelope(HttpStatusCode.OK, ProjectDetail(key)));
            server.On("GET", $"/v1/projects/{key}/cycles", _ => FakeServer.Envelope(HttpStatusCode.OK, Cycles[key]
                .Select(cycle => new { id = cycle.Id, name = cycle.Name, startDate = "2026-11-01", endDate = "2026-11-30" })));
            server.On("POST", $"/v1/projects/{key}/cycles", request => CreateCycle(key, request));
            server.On("POST", $"/v1/projects/{key}/history/items", request => ImportItems(key, request));
            server.On("POST", $"/v1/projects/{key}/history/comments", request => ImportComments(key, request));
            foreach (var person in new[] { Pm, Dev, Former })
            {
                server.On("PUT", $"/v1/projects/{key}/members/{person}", request =>
                {
                    MemberRoles.Add(request.Json!["role"]!.GetValue<string>());
                    Members[key].Add(person);
                    return FakeServer.Envelope(HttpStatusCode.OK, new { personId = person, displayName = "x", role = "member" });
                });
            }
        }

        server.On("POST", "/v1/history/lookup", Lookup);
        server.OnPattern("POST", "/v1/items/[A-Z]+-\\d+/history/attachments", UploadAttachment);
    }

    private static object Person(Guid id, string name, string email) =>
        new { personId = id, displayName = name, email, role = "member", disabled = false };

    public static object Blueprint() => new
    {
        fields = new object[]
        {
            new { key = "monday_url", name = "monday 原連結", type = "url" },
            new { key = "actual_hours", name = "工時", type = "number" },
            new { key = "notes", name = "備註", type = "long_text" },
            new { key = "summary", name = "摘要", type = "text" },
            new { key = "ship_date", name = "出貨日", type = "date" },
            new { key = "reviewer", name = "審核", type = "person" },
            new { key = "approved", name = "核准", type = "checkbox" },
            new { key = "ref_url", name = "參考網址", type = "url" },
            new
            {
                key = "tags", name = "標籤", type = "multi_select",
                options = new[] { new { key = "t1", name = "一" }, new { key = "t2", name = "二" } },
            },
            new
            {
                key = "client", name = "客戶", type = "single_select",
                options = new[] { new { key = "client_a", name = "客戶 A" }, new { key = "client_b", name = "客戶 B" } },
            },
        },
        // 和真的伺服器一樣：藍圖只列自訂角色，內建的 admin、editor、commenter、viewer 不在這裡。
        roles = new[] { new { key = "client", name = "客戶", permissions = new[] { "item.read", "comment.create" } } },
        workflows = new[]
        {
            new
            {
                key = "flow",
                name = "流程",
                states = new[]
                {
                    new { key = "todo", category = "not_started" }, new { key = "doing", category = "in_progress" },
                    new { key = "done", category = "done" }, new { key = "dropped", category = "canceled" },
                },
            },
        },
        itemTypes = new object[]
        {
            new { key = "task", name = "任務", workflow = "flow", fields = new[] { "monday_url", "actual_hours", "client", "notes", "summary", "ship_date", "reviewer", "approved", "ref_url", "tags" } },
            new { key = "change", name = "變更", workflow = "flow", fields = new[] { "monday_url", "client" } },
            // 沒有「客戶」欄位的類型：標題規則把項目改成這個類型時，客戶的值要略過。
            new { key = "security", name = "資安", workflow = "flow", fields = new[] { "monday_url" } },
        },
    };

    private object ProjectDetail(string key) => new
    {
        project = new { key, name = key, archivedAt = ArchivedProjects.Contains(key) ? "2026-11-01T00:00:00Z" : null },
        blueprint = Blueprint(),
        members = Members[key].Select(id => new { personId = id, displayName = "x", role = "member" }),
        myPermissions = CanManage ? new[] { "item.read", "project.manage" } : new[] { "item.read" },
    };

    private HttpResponseMessage CreateCycle(string key, RecordedRequest request)
    {
        var id = Guid.Parse($"0190a000-0000-7000-8000-0000000c{++_cycleCounter:D4}");
        var name = request.Json!["name"]!.GetValue<string>();
        Cycles[key].Add((id, name));
        return FakeServer.Envelope(HttpStatusCode.Created, new { id, name, startDate = request.Json!["startDate"]!.GetValue<string>(), endDate = request.Json!["endDate"]!.GetValue<string>() });
    }

    private HttpResponseMessage ImportItems(string key, RecordedRequest request)
    {
        var body = request.Json!;
        var dryRun = body["dryRun"]!.GetValue<bool>();
        var rows = body["rows"]!.AsArray();
        var details = new List<(string Path, string Message)>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]!;
            foreach (var field in new[] { "createdBy", "assigneeId" })
            {
                if (row[field]?.GetValue<string>() is { } person && !Members[key].Contains(Guid.Parse(person)))
                {
                    details.Add(($"rows/{index}/{(field == "createdBy" ? "created_by" : "assignee_id")}", "不是這個專案的成員。"));
                }
            }

            if (RowProblem?.Invoke(row) is { } problem)
            {
                details.Add(($"rows/{index}/fields", problem));
            }
        }

        if (details.Count > 0)
        {
            return Failure(HttpStatusCode.UnprocessableEntity, "item.import_invalid", "匯入的項目有問題。", details);
        }

        var created = new List<object>();
        var skipped = new List<object>();
        foreach (var row in rows)
        {
            var externalId = row!["externalId"]!.GetValue<string>();
            if (Items.TryGetValue(externalId, out var existing))
            {
                skipped.Add(new { externalId, itemKey = existing.ItemKey });
            }
            else if (!dryRun)
            {
                var itemKey = $"{key}-{Items.Count(item => item.Value.Project == key) + 1}";
                Items[externalId] = (key, itemKey);
                created.Add(new { externalId, itemKey });
            }
        }

        return FakeServer.Envelope(HttpStatusCode.OK, new { dryRun, created, skipped });
    }

    private HttpResponseMessage ImportComments(string key, RecordedRequest request)
    {
        if (FailComments)
        {
            return Failure(HttpStatusCode.InternalServerError, "server.error", "伺服器出錯了。", []);
        }

        var created = new List<object>();
        var skipped = new List<object>();
        foreach (var comment in request.Json!["comments"]!.AsArray())
        {
            var externalId = comment!["externalId"]!.GetValue<string>();
            var item = Items.GetValueOrDefault(comment["itemExternalId"]!.GetValue<string>());
            if (item.Project != key)
            {
                return Failure(HttpStatusCode.UnprocessableEntity, "history.invalid", "項目不在這個專案裡。", [("comments/0/item_external_id", "找不到項目。")]);
            }

            if (!Members[key].Contains(Guid.Parse(comment["authorId"]!.GetValue<string>())))
            {
                return Failure(HttpStatusCode.UnprocessableEntity, "item.import_invalid", "作者不是成員。", [("comments/0/author_id", "不是這個專案的成員。")]);
            }

            if (comment["replyToExternalId"]?.GetValue<string>() is { } parent && !Comments.ContainsKey(parent))
            {
                return Failure(HttpStatusCode.UnprocessableEntity, "item.import_invalid", "回覆要排在被回覆的那一則之後。", [("comments/0/reply_to_external_id", "找不到。")]);
            }

            if (Comments.TryGetValue(externalId, out var existing))
            {
                skipped.Add(new { externalId, activityId = existing.Id });
                continue;
            }

            var id = Guid.NewGuid();
            Comments[externalId] = (key, item.ItemKey, id, comment.DeepClone());
            created.Add(new { externalId, activityId = id });
        }

        return FakeServer.Envelope(HttpStatusCode.OK, new { created, skipped });
    }

    private HttpResponseMessage Lookup(RecordedRequest request)
    {
        var kind = request.Json!["kind"]!.GetValue<string>();
        var found = new List<object>();
        foreach (var node in request.Json["externalIds"]!.AsArray())
        {
            var externalId = node!.GetValue<string>();
            (string Project, string ItemKey, Guid Id)? hit = kind switch
            {
                "item" => Items.TryGetValue(externalId, out var item) ? (item.Project, item.ItemKey, Guid.Parse("0190a000-0000-7000-8000-00000000f001")) : null,
                "comment" => Comments.TryGetValue(externalId, out var comment) ? (comment.Project, comment.ItemKey, comment.Id) : null,
                _ => Attachments.TryGetValue(externalId, out var attachment) ? attachment : null,
            };
            if (hit is { } value)
            {
                found.Add(new { externalId, kind, projectKey = value.Project, itemKey = value.ItemKey, targetId = value.Id });
            }
        }

        return FakeServer.Envelope(HttpStatusCode.OK, found);
    }

    private HttpResponseMessage UploadAttachment(RecordedRequest request)
    {
        if (AttachmentFailure is { } failure)
        {
            return Failure(failure.Status, failure.Code, "伺服器不收這個檔案。", []);
        }

        var itemKey = request.Path.Split('/')[3];
        var names = FieldName().Matches(request.Body).Select(match => match.Groups[1].Value).ToArray();
        UploadFieldOrders.Add(names);
        var externalId = Regex.Match(request.Body, "name=\"?externalId\"?\r\n\r\n([^\r]*)\r\n").Groups[1].Value;
        if (Attachments.TryGetValue(externalId, out var existing))
        {
            return FakeServer.Envelope(HttpStatusCode.OK, new { id = existing.Id, itemKey = existing.ItemKey });
        }

        var id = Guid.NewGuid();
        Attachments[externalId] = (itemKey.Split('-')[0], itemKey, id);
        return FakeServer.Envelope(HttpStatusCode.Created, new { id, itemKey });
    }

    [GeneratedRegex("name=\"?(\\w+)\"?(?:;|\r)")]
    private static partial Regex FieldName();

    private static HttpResponseMessage Failure(HttpStatusCode status, string code, string message, IEnumerable<(string Path, string Message)> details) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(
                    new { success = false, data = (object?)null, error = new { code, message, details = details.Select(detail => new { code, path = detail.Path, message = detail.Message }).ToArray() } },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json"),
        };
}
