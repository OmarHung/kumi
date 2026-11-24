using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 變更集主線上反覆用到的請求，與從專案目前的藍圖改出想要的藍圖。
/// 想要的藍圖從專案詳情的 blueprint 改出來，和網頁的設定頁一樣：送出的是全文，差異由伺服器算。
/// 每一個修改都回傳新的物件，不動傳進來的藍圖，同一份藍圖可以改出好幾份變更集。
/// </summary>
internal static class ChangeSetSteps
{
    public static string RouteOf(string projectKey) => $"/v1/projects/{projectKey}/changesets";

    public static string RouteOf(string projectKey, long number) =>
        $"{RouteOf(projectKey)}/{number.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>專案目前的藍圖（一份複本）。</summary>
    public static async Task<JsonObject> BlueprintOfAsync(HttpClient client, string projectKey)
    {
        var detail = await RealStackSteps.GetAsync(client, $"/v1/projects/{projectKey}");
        return detail["blueprint"]?.DeepClone().AsObject() ?? throw new AssertionException("專案詳情沒有 blueprint");
    }

    /// <summary>JSON 的變更集文件。沒有搬移指示時不寫 migrations。</summary>
    public static HttpContent Json(long baseVersion, string summary, JsonObject blueprint, JsonObject? migrations = null)
    {
        var document = new JsonObject
        {
            ["baseVersion"] = baseVersion,
            ["summary"] = summary,
            ["blueprint"] = blueprint.DeepClone(),
        };
        if (migrations is not null)
        {
            document["migrations"] = migrations.DeepClone();
        }

        return ApiRequests.Document(document.ToJsonString());
    }

    /// <summary>
    /// 網頁設定頁送出的變更集：摘要沒填就不送；migrations 一定帶 moveStates 與 dropFields 兩個陣列，沒有要搬的就是空陣列。
    /// 和 <see cref="Json"/> 分開，才看得出伺服器收的是網頁真的會送的形狀。
    /// </summary>
    public static HttpContent SettingsJson(
        long baseVersion, string? summary, JsonObject blueprint, IEnumerable<JsonObject>? moveStates = null)
    {
        var document = new JsonObject { ["baseVersion"] = baseVersion };
        if (summary is not null)
        {
            document["summary"] = summary;
        }

        document["blueprint"] = blueprint.DeepClone();
        document["migrations"] = new JsonObject
        {
            ["moveStates"] = new JsonArray([.. (moveStates ?? []).Select(move => move.DeepClone())]),
            ["dropFields"] = new JsonArray(),
        };
        return ApiRequests.Document(document.ToJsonString());
    }

    public static JsonObject MoveState(string workflow, string from, string to) =>
        new() { ["workflow"] = workflow, ["from"] = from, ["to"] = to };

    /// <summary>網頁送出的轉換：allowedRoles 與 requireFields 一定寫出來，沒有限制就是空陣列。</summary>
    public static JsonObject Transition(string from, string to, params string[] allowedRoles) => new()
    {
        ["from"] = from,
        ["to"] = to,
        ["allowedRoles"] = Strings(allowedRoles),
        ["requireFields"] = new JsonArray(),
    };

    /// <summary>送出，回應交給呼叫端檢查：被擋下來也是主線的一部分。</summary>
    public static Task<HttpResponseMessage> ProposeAsync(HttpClient client, string projectKey, HttpContent document) =>
        client.PostAsync(RouteOf(projectKey), document);

    /// <summary>送出要成功：201，Location 指向回來的那一份。</summary>
    public static async Task<JsonNode> ProposedAsync(HttpClient client, string projectKey, HttpContent document)
    {
        using var response = await ProposeAsync(client, projectKey, document);
        var detail = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created)
            ?? throw new AssertionException("送出變更集的 data 是 null");
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo(RouteOf(projectKey, NumberOf(detail))));
        return detail;
    }

    public static Task<HttpResponseMessage> ApplyAsync(HttpClient client, string projectKey, long number) =>
        client.PostAsync($"{RouteOf(projectKey, number)}/apply", null);

    public static Task<HttpResponseMessage> RejectAsync(HttpClient client, string projectKey, long number) =>
        client.PostAsync($"{RouteOf(projectKey, number)}/reject", null);

    /// <summary>套用要成功，回來的狀態是 applied。</summary>
    public static async Task<JsonNode> AppliedAsync(HttpClient client, string projectKey, long number)
    {
        using var response = await ApplyAsync(client, projectKey, number);
        var detail = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK)
            ?? throw new AssertionException("套用變更集的 data 是 null");
        Assert.That(StatusOf(detail), Is.EqualTo("applied"));
        return detail;
    }

    public static Task<JsonNode> GetAsync(HttpClient client, string projectKey, long number) =>
        RealStackSteps.GetAsync(client, RouteOf(projectKey, number));

    public static long NumberOf(JsonNode? detail) => detail?["changeSet"]?["number"]?.GetValue<long>() ?? 0;

    public static string? StatusOf(JsonNode? detail) => detail?["changeSet"]?["status"]?.GetValue<string>();

    /// <summary>一份變更集的身分與決定，方便一次比對：「#編號 狀態 base=基準 applied=套用的版本 by=提出的人 decidedBy=決定的人」。</summary>
    public static string Describe(JsonNode? changeSet) =>
        $"#{changeSet?["number"]} {changeSet?["status"]} base={changeSet?["baseVersion"]} "
        + $"applied={changeSet?["appliedVersion"]?.ToJsonString() ?? "null"} "
        + $"by={changeSet?["proposedBy"]?["displayName"]} decidedBy={changeSet?["decidedBy"]?["displayName"]?.ToString() ?? "null"}";

    /// <summary>差異逐項寫成「kind|resource|key|parentKey|changedProperties」。</summary>
    public static string[] DescribeChanges(JsonNode? changes) =>
        [.. (changes?.AsArray() ?? []).Select(change =>
            $"{change?["kind"]}|{change?["resource"]}|{change?["key"]}|{change?["parentKey"]}|"
            + string.Join(",", (change?["changedProperties"]?.AsArray() ?? []).Select(p => p?.GetValue<string>())))];

    public static string[] StateKeysOf(JsonNode? blueprint, string workflow) =>
        RealStackSteps.KeysOf(ByKey(blueprint?["workflows"], workflow)["states"]);

    public static string[] FieldsOfType(JsonNode? blueprint, string itemType) =>
        [.. (ByKey(blueprint?["itemTypes"], itemType)["fields"]?.AsArray() ?? []).Select(f => f?.GetValue<string>() ?? string.Empty)];

    public static JsonObject WithState(JsonObject blueprint, string workflow, string key, string name, string category) =>
        Edited(blueprint, copy => StatesOf(copy, workflow).Insert(
            StatesOf(copy, workflow).Count - 1,
            new JsonObject { ["key"] = key, ["name"] = name, ["category"] = category }));

    public static JsonObject WithoutState(JsonObject blueprint, string workflow, string state) =>
        Edited(blueprint, copy => StatesOf(copy, workflow).RemoveAll(s => KeyOf(s) == state));

    public static JsonObject WithStateCategory(JsonObject blueprint, string workflow, string state, string category) =>
        Edited(blueprint, copy => StatesOf(copy, workflow).Single(s => KeyOf(s) == state)!["category"] = category);

    /// <summary>新增一個自訂欄位，加在藍圖的欄位清單與指定類型的欄位清單最後。</summary>
    public static JsonObject WithField(JsonObject blueprint, string itemType, string key, string name, string type) =>
        Edited(blueprint, copy =>
        {
            copy["fields"]!.AsArray().Add(new JsonObject { ["key"] = key, ["name"] = name, ["type"] = type });
            ByKey(copy["itemTypes"], itemType)["fields"]!.AsArray().Add(key);
        });

    /// <summary>從藍圖的欄位清單與每個類型的欄位清單拿掉一個欄位。</summary>
    public static JsonObject WithoutField(JsonObject blueprint, string field) =>
        Edited(blueprint, copy =>
        {
            copy["fields"]!.AsArray().RemoveAll(f => KeyOf(f) == field);
            foreach (var type in copy["itemTypes"]!.AsArray())
            {
                type?["fields"]?.AsArray().RemoveAll(f => f?.GetValue<string>() == field);
            }
        });

    /// <summary>拿掉一個類型，連同別的類型的上層清單與檢視的類型篩選裡提到它的地方。</summary>
    public static JsonObject WithoutItemType(JsonObject blueprint, string itemType) =>
        Edited(blueprint, copy =>
        {
            copy["itemTypes"]!.AsArray().RemoveAll(t => KeyOf(t) == itemType);
            foreach (var type in copy["itemTypes"]!.AsArray())
            {
                type?["parents"]?.AsArray().RemoveAll(p => p?.GetValue<string>() == itemType);
            }

            foreach (var view in copy["views"]?.AsArray() ?? [])
            {
                view?["filter"]?["itemTypes"]?.AsArray().RemoveAll(t => t?.GetValue<string>() == itemType);
            }
        });

    public static JsonObject WithRole(JsonObject blueprint, string key, string name, params string[] permissions) =>
        Edited(blueprint, copy =>
        {
            if (copy["roles"] is not JsonArray)
            {
                copy["roles"] = new JsonArray();
            }

            copy["roles"]!.AsArray().Add(new JsonObject
            {
                ["key"] = key,
                ["name"] = name,
                ["permissions"] = Strings(permissions),
            });
        });

    /// <summary>拿掉一個自訂角色，連同轉換的允許角色與檢視的可見角色裡提到它的地方，和網頁的設定頁一樣。</summary>
    public static JsonObject WithoutRole(JsonObject blueprint, string role) =>
        Edited(blueprint, copy =>
        {
            copy["roles"]?.AsArray().RemoveAll(r => KeyOf(r) == role);
            foreach (var workflow in copy["workflows"]!.AsArray())
            {
                foreach (var transition in workflow?["transitions"]?.AsArray() ?? [])
                {
                    transition?["allowedRoles"]?.AsArray().RemoveAll(r => r?.GetValue<string>() == role);
                }
            }

            foreach (var view in copy["views"]?.AsArray() ?? [])
            {
                view?["visibleTo"]?.AsArray().RemoveAll(r => r?.GetValue<string>() == role);
            }
        });

    /// <summary>在某個狀態後面插入一個新狀態，和網頁在指定的位置新增狀態一樣。</summary>
    public static JsonObject WithStateAfter(JsonObject blueprint, string workflow, string after, string key, string name, string category) =>
        Edited(blueprint, copy =>
        {
            var states = StatesOf(copy, workflow);
            states.Insert(
                states.IndexOf(states.Single(s => KeyOf(s) == after)) + 1,
                new JsonObject { ["key"] = key, ["name"] = name, ["category"] = category });
        });

    /// <summary>把一條轉換換成另外幾條，放在原本的位置；其他轉換原樣帶回。</summary>
    public static JsonObject WithTransitionReplaced(
        JsonObject blueprint, string workflow, string from, string to, params JsonObject[] replacements) =>
        Edited(blueprint, copy =>
        {
            var transitions = ByKey(copy["workflows"], workflow)["transitions"]!.AsArray();
            var index = transitions.IndexOf(transitions.Single(t => t?["from"]?.GetValue<string>() == from && t?["to"]?.GetValue<string>() == to));
            transitions.RemoveAt(index);
            for (var offset = 0; offset < replacements.Length; offset++)
            {
                transitions.Insert(index + offset, replacements[offset].DeepClone());
            }
        });

    public static JsonObject WithItemTypeWorkflow(JsonObject blueprint, string itemType, string workflow) =>
        Edited(blueprint, copy => ByKey(copy["itemTypes"], itemType)["workflow"] = workflow);

    /// <summary>流程的轉換逐條寫成「起點->終點|允許的角色」。沒有列轉換的流程是空的。</summary>
    public static string[] DescribeTransitions(JsonNode? blueprint, string workflow) =>
        [.. (ByKey(blueprint?["workflows"], workflow)["transitions"]?.AsArray() ?? []).Select(t =>
            $"{t?["from"]}->{t?["to"]}|" + string.Join(",", (t?["allowedRoles"]?.AsArray() ?? []).Select(r => r?.GetValue<string>())))];

    private static JsonArray Strings(IEnumerable<string> values) => new([.. values.Select(v => JsonValue.Create(v))]);

    private static JsonObject Edited(JsonObject blueprint, Action<JsonObject> edit)
    {
        var copy = blueprint.DeepClone().AsObject();
        edit(copy);
        return copy;
    }

    private static JsonArray StatesOf(JsonObject blueprint, string workflow) =>
        ByKey(blueprint["workflows"], workflow)["states"]!.AsArray();

    private static JsonNode ByKey(JsonNode? list, string key) =>
        (list?.AsArray() ?? []).SingleOrDefault(node => KeyOf(node) == key)
            ?? throw new AssertionException($"藍圖裡沒有 {key}");

    private static string? KeyOf(JsonNode? node) => node?["key"]?.GetValue<string>();
}
