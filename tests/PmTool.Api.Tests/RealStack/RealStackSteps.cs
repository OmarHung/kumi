using System.Net;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>主線上反覆用到的請求。每一步都確認狀態碼，失敗時訊息裡帶著回應的內容。</summary>
internal static class RealStackSteps
{
    // 測試用的假密碼，只存在於這個測試的資料庫裡。
    public const string MemberPassword = "real-stack-member-0000";

    public static async Task<JsonNode> SignInAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        return data?["user"] ?? throw new AssertionException("登入的回應沒有 user");
    }

    /// <summary>登入，回傳工作階段的 Cookie（名稱=值），給不自動記 Cookie 的用戶端用。</summary>
    public static async Task<string> SignInForCookieAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));
        await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        return Sessions.SessionCookieOf(response) ?? throw new AssertionException("登入成功卻沒有發工作階段的 Cookie");
    }

    /// <summary>登入的狀態碼，給預期會失敗的登入用。會用掉一次登入的額度。</summary>
    public static async Task<HttpStatusCode> TrySignInAsync(HttpClient client, string email, string password)
    {
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));
        return response.StatusCode;
    }

    public static async Task<HttpStatusCode> StatusOfAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        return response.StatusCode;
    }

    /// <summary>每次都不一樣的 Email：同一個資料庫裡的測試才不會撞到彼此的帳號。</summary>
    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.com";

    public static async Task<JsonNode> GetAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK)
            ?? throw new AssertionException($"GET {route} 的 data 是 null");
    }

    public static async Task<JsonNode> CreateProjectAsync(HttpClient client, string key, string template)
    {
        using var response = await client.PostAsync(
            "/v1/projects", ApiRequests.Json(new { key, name = $"Example {key}", template }));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        Assert.That(response.Headers.Location?.ToString(), Is.EqualTo($"/v1/projects/{key}"));
        return data!;
    }

    public static Task<JsonNode> CreateItemAsync(HttpClient client, string projectKey, object body) =>
        CreateItemAsync(client, projectKey, ApiRequests.Json(body));

    public static async Task<JsonNode> CreateItemAsync(HttpClient client, string projectKey, HttpContent body)
    {
        using var response = await client.PostAsync($"/v1/projects/{projectKey}/items", body);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created)
            ?? throw new AssertionException("新增項目的 data 是 null");
    }

    public static Task<JsonNode> PatchItemAsync(HttpClient client, string itemKey, object body) =>
        PatchItemAsync(client, itemKey, ApiRequests.Json(body));

    public static async Task<JsonNode> PatchItemAsync(HttpClient client, string itemKey, HttpContent body)
    {
        using var response = await client.PatchAsync($"/v1/items/{itemKey}", body);
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK)
            ?? throw new AssertionException("修改項目的 data 是 null");
    }

    /// <summary>改狀態，回應交給呼叫端檢查：被擋下來也是主線的一部分。</summary>
    public static Task<HttpResponseMessage> TransitionAsync(HttpClient client, string itemKey, string to) =>
        client.PostAsync($"/v1/items/{itemKey}/transitions", ApiRequests.Json(new { to }));

    /// <summary>依序轉過每一個狀態，每一步都要成功；回傳最後的項目。</summary>
    public static async Task<JsonNode> TransitionThroughAsync(HttpClient client, string itemKey, params string[] states)
    {
        JsonNode? item = null;
        foreach (var state in states)
        {
            using var response = await TransitionAsync(client, itemKey, state);
            item = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
            Assert.That(item?["state"]?.GetValue<string>(), Is.EqualTo(state));
        }

        return item ?? throw new AssertionException("沒有指定要轉去的狀態");
    }

    public static Task<HttpResponseMessage> PutMemberAsync(HttpClient client, string projectKey, Guid personId, string role) =>
        client.PutAsync($"/v1/projects/{projectKey}/members/{personId}", ApiRequests.Json(new { role }));

    public static async Task<JsonNode> SetMemberAsync(HttpClient client, string projectKey, Guid personId, string role)
    {
        using var response = await PutMemberAsync(client, projectKey, personId, role);
        var member = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        Assert.That(member?["role"]?.GetValue<string>(), Is.EqualTo(role));
        return member!;
    }

    /// <summary>組織管理員新增一般成員；回傳新的人員。</summary>
    public static async Task<JsonNode> CreateMemberAsync(HttpClient client, string displayName, string email)
    {
        using var response = await client.PostAsync(
            "/v1/people", ApiRequests.Json(new { displayName, email, password = MemberPassword, role = "member" }));
        var person = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created);
        Assert.That(person?["email"]?.GetValue<string>(), Is.EqualTo(email));
        return person!;
    }

    public static Task<JsonNode> ActivityAsync(HttpClient client, string itemKey, string query = "") =>
        GetAsync(client, $"/v1/items/{itemKey}/activity{query}");

    /// <summary>留言，回應交給呼叫端檢查：被擋下來也是主線的一部分。</summary>
    public static Task<HttpResponseMessage> PostCommentAsync(HttpClient client, string itemKey, string body) =>
        client.PostAsync($"/v1/items/{itemKey}/comments", ApiRequests.Json(new { body }));

    /// <summary>留言要成功；回傳那一筆活動。</summary>
    public static async Task<JsonNode> CommentAsync(HttpClient client, string itemKey, string body)
    {
        using var response = await PostCommentAsync(client, itemKey, body);
        var activity = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created)
            ?? throw new AssertionException("留言的 data 是 null");
        Assert.That(activity["kind"]?.GetValue<string>(), Is.EqualTo("commented"));
        return activity;
    }

    public static string[] KeysOf(JsonNode? items) =>
        [.. (items?.AsArray() ?? []).Select(item => item?["key"]?.GetValue<string>() ?? string.Empty)];

    public static string[] KindsOf(JsonNode? activities) =>
        [.. (activities?.AsArray() ?? []).Select(activity => activity?["kind"]?.GetValue<string>() ?? string.Empty)];

    /// <summary>屬性要寫出來而且是 null：合約說用不到的屬性寫成 null，不是省略。</summary>
    public static bool IsWrittenAsNull(JsonNode? node, string property) =>
        node is JsonObject obj && obj.ContainsKey(property) && obj[property] is null;
}
