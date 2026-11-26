using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>範圍的檢查：每個範圍允許與拒絕的端點，以及沒有宣告的端點一律不給權杖用。</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)] // 一百多個案例各自開一個 ApiFactory、類別沒有欄位：類別裡也同時跑，不然整輪要等它一個。
public sealed class AccessTokenScopeTests
{
    private const AccessTokenScope ItemsRead = AccessTokenScope.ItemsRead;
    private const AccessTokenScope ItemsWrite = AccessTokenScope.ItemsWrite;
    private const AccessTokenScope BlueprintRead = AccessTokenScope.BlueprintRead;
    private const AccessTokenScope BlueprintWrite = AccessTokenScope.BlueprintWrite;
    private const AccessTokenScope AgentRun = AccessTokenScope.AgentRun;

    /// <summary>端點與有它的範圍（任一個就可以）。</summary>
    public static IEnumerable<TestCaseData> Endpoints()
    {
        yield return Case("GET", "/v1/projects", ItemsRead);
        yield return Case("GET", "/v1/projects/WEB/items", ItemsRead);
        yield return Case("GET", "/v1/items/WEB-1", ItemsRead);
        yield return Case("GET", "/v1/items/WEB-1/activity", ItemsRead);
        yield return Case("GET", "/v1/my-work", ItemsRead);
        yield return Case("GET", "/v1/items", ItemsRead);
        yield return Case("GET", "/v1/item-types", ItemsRead);
        yield return Case("GET", "/v1/search/items?q=a", ItemsRead);
        yield return Case("GET", "/v1/templates", ItemsRead);
        yield return Case("GET", "/v1/people", ItemsRead);
        yield return Case("GET", "/v1/projects/WEB", ItemsRead, BlueprintRead);
        yield return Case("POST", "/v1/projects/WEB/items", ItemsWrite);
        yield return Case("PATCH", "/v1/items/WEB-1", ItemsWrite);
        yield return Case("DELETE", "/v1/items/WEB-1", ItemsWrite);
        yield return Case("GET", "/v1/projects/WEB/items/deleted", ItemsRead);
        yield return Case("POST", "/v1/items/WEB-1/restore", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/items/bulk-update", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/items/bulk-delete", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/items/bulk-restore", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/items/import", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/history/items", ItemsWrite);
        yield return Case("POST", "/v1/projects/WEB/history/comments", ItemsWrite);
        yield return Case("POST", "/v1/items/WEB-1/history/attachments", ItemsWrite);
        yield return Case("POST", "/v1/history/lookup", ItemsRead);
        yield return Case("PUT", "/v1/projects/WEB/members/00000000-0000-0000-0000-000000000001", BlueprintWrite);
        yield return Case("PUT", "/v1/projects/WEB/name", BlueprintWrite);
        yield return Case("GET", "/v1/projects/WEB/description", ItemsRead);
        yield return Case("PUT", "/v1/projects/WEB/description", BlueprintWrite);
        yield return Case("GET", "/v1/description-templates", ItemsRead);
        yield return Case("GET", "/v1/search/projects?q=a", ItemsRead);
        yield return Case("POST", "/v1/projects/WEB/archive", BlueprintWrite);
        yield return Case("POST", "/v1/projects/WEB/unarchive", BlueprintWrite);
        yield return Case("POST", "/v1/items/WEB-1/transitions", ItemsWrite);
        yield return Case("POST", "/v1/items/WEB-1/type", ItemsWrite);
        yield return Case("POST", "/v1/items/WEB-1/comments", ItemsWrite);
        yield return Case("PATCH", "/v1/items/WEB-1/comments/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("DELETE", "/v1/items/WEB-1/comments/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("POST", "/v1/items/WEB-1/comments/00000000-0000-0000-0000-000000000001/restore", ItemsWrite);
        yield return Case("GET", "/v1/items/WEB-1/comments/00000000-0000-0000-0000-000000000001/revisions", ItemsRead);
        yield return Case("GET", "/v1/items/WEB-1/attachments", ItemsRead);
        yield return Case("POST", "/v1/items/WEB-1/attachments", ItemsWrite);
        yield return Case("GET", "/v1/attachments/00000000-0000-0000-0000-000000000001/content", ItemsRead);
        yield return Case("DELETE", "/v1/attachments/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("GET", "/v1/notifications", ItemsRead);
        yield return Case("GET", "/v1/notifications/unread-count", ItemsRead);
        yield return Case("POST", "/v1/notifications/read", ItemsWrite);
        yield return Case("POST", "/v1/notifications/read-all", ItemsWrite);
        yield return Case("GET", "/v1/items/WEB-1/subscription", ItemsRead);
        yield return Case("PUT", "/v1/items/WEB-1/subscription", ItemsWrite);
        yield return Case("GET", "/v1/projects/WEB/changesets", BlueprintRead);
        yield return Case("GET", "/v1/projects/WEB/changesets/1", BlueprintRead);
        yield return Case("POST", "/v1/projects/WEB/changesets/preview", BlueprintRead);
        yield return Case("POST", "/v1/projects/WEB/changesets", BlueprintWrite);
        yield return Case("POST", "/v1/projects/WEB/changesets/1/apply", BlueprintWrite);
        yield return Case("POST", "/v1/projects/WEB/changesets/1/reject", BlueprintWrite);
        yield return Case("GET", "/v1/projects/WEB/cycles", ItemsRead);
        yield return Case("POST", "/v1/projects/WEB/cycles", ItemsWrite);
        yield return Case("PUT", "/v1/projects/WEB/cycles/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("DELETE", "/v1/projects/WEB/cycles/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("GET", "/v1/projects/WEB/milestones", ItemsRead);
        yield return Case("POST", "/v1/projects/WEB/milestones", ItemsWrite);
        yield return Case("PUT", "/v1/projects/WEB/milestones/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("DELETE", "/v1/projects/WEB/milestones/00000000-0000-0000-0000-000000000001", ItemsWrite);
        yield return Case("POST", "/v1/agent/check-in", AgentRun);
        yield return Case("POST", "/v1/agent/jobs/00000000-0000-0000-0000-000000000001/events", AgentRun);
        yield return Case("POST", "/v1/agent/jobs/00000000-0000-0000-0000-000000000001/finish", AgentRun);
        yield return Case("GET", "/v1/agent/jobs/00000000-0000-0000-0000-000000000001/attachments/00000000-0000-0000-0000-000000000002", AgentRun);
        yield return Case("GET", "/v1/session", ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite, AgentRun);
        yield return Case("GET", "/v1/cli", ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite, AgentRun);
        yield return Case("GET", "/v1/cli/download/macos-arm64", ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite, AgentRun);
    }

    /// <summary>沒有宣告：只能在網頁上做。</summary>
    public static IEnumerable<TestCaseData> WebOnlyEndpoints()
    {
        yield return new TestCaseData("PUT", "/v1/account/password").SetArgDisplayNames("改密碼");
        yield return new TestCaseData("POST", "/v1/account/other-sessions/sign-out").SetArgDisplayNames("登出其他裝置");
        yield return new TestCaseData("POST", "/v1/people").SetArgDisplayNames("新增人員");
        yield return new TestCaseData("POST", "/v1/people/00000000-0000-0000-0000-000000000001/disable").SetArgDisplayNames("停用人員");
        yield return new TestCaseData("POST", "/v1/people/00000000-0000-0000-0000-000000000001/enable").SetArgDisplayNames("重新啟用人員");
        yield return new TestCaseData("PUT", "/v1/people/00000000-0000-0000-0000-000000000001/password").SetArgDisplayNames("重設密碼");
        yield return new TestCaseData("PUT", "/v1/people/00000000-0000-0000-0000-000000000001/role").SetArgDisplayNames("改人員角色");
        yield return new TestCaseData("POST", "/v1/projects").SetArgDisplayNames("建立專案");
        yield return new TestCaseData("DELETE", "/v1/projects/WEB/members/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("移除成員");
        yield return new TestCaseData("POST", "/v1/description-templates").SetArgDisplayNames("新增說明格式");
        yield return new TestCaseData("PUT", "/v1/description-templates/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("修改說明格式");
        yield return new TestCaseData("DELETE", "/v1/description-templates/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("刪除說明格式");
        yield return new TestCaseData("GET", "/v1/email-settings").SetArgDisplayNames("讀寄信設定");
        yield return new TestCaseData("PUT", "/v1/email-settings").SetArgDisplayNames("改寄信設定");
        yield return new TestCaseData("POST", "/v1/email-settings/test").SetArgDisplayNames("寄測試信");
        yield return new TestCaseData("GET", "/v1/account/email-notifications").SetArgDisplayNames("讀 Email 通知的開關");
        yield return new TestCaseData("PUT", "/v1/account/email-notifications").SetArgDisplayNames("改 Email 通知的開關");
        yield return new TestCaseData("GET", "/v1/ai/settings").SetArgDisplayNames("讀 AI 設定");
        yield return new TestCaseData("PUT", "/v1/ai/settings").SetArgDisplayNames("改 AI 設定");
        yield return new TestCaseData("POST", "/v1/ai/connections").SetArgDisplayNames("新增 AI 連線");
        yield return new TestCaseData("PUT", "/v1/ai/connections/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("修改 AI 連線");
        yield return new TestCaseData("POST", "/v1/ai/connections/00000000-0000-0000-0000-000000000001/refresh").SetArgDisplayNames("重新讀取模型");
        yield return new TestCaseData("DELETE", "/v1/ai/connections/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("刪除 AI 連線");
        yield return new TestCaseData("POST", "/v1/ai/models/discover").SetArgDisplayNames("讀取模型");
        yield return new TestCaseData("GET", "/v1/ai/options").SetArgDisplayNames("讀能選的 AI");
        yield return new TestCaseData("GET", "/v1/account/ai-connections").SetArgDisplayNames("列自己的 AI 連線");
        yield return new TestCaseData("POST", "/v1/account/ai-connections").SetArgDisplayNames("新增自己的 AI 連線");
        yield return new TestCaseData("PUT", "/v1/account/ai-connections/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("修改自己的 AI 連線");
        yield return new TestCaseData("POST", "/v1/account/ai-connections/00000000-0000-0000-0000-000000000001/refresh").SetArgDisplayNames("重新讀取自己連線的模型");
        yield return new TestCaseData("DELETE", "/v1/account/ai-connections/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("刪除自己的 AI 連線");
        yield return new TestCaseData("POST", "/v1/account/ai-models/discover").SetArgDisplayNames("讀取自己連線的模型");
        yield return new TestCaseData("POST", "/v1/ai/openrouter/login").SetArgDisplayNames("用 OpenRouter 登入：開始");
        yield return new TestCaseData("POST", "/v1/ai/openrouter/complete").SetArgDisplayNames("用 OpenRouter 登入：完成");
        yield return new TestCaseData("GET", "/v1/projects/WEB/assistant/conversations").SetArgDisplayNames("列出助理的對話");
        yield return new TestCaseData("POST", "/v1/projects/WEB/assistant/conversations").SetArgDisplayNames("開新的助理對話");
        yield return new TestCaseData("GET", "/v1/projects/WEB/assistant/conversations/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("讀助理的對話");
        yield return new TestCaseData("DELETE", "/v1/projects/WEB/assistant/conversations/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("刪除助理的對話");
        yield return new TestCaseData("POST", "/v1/projects/WEB/assistant/conversations/00000000-0000-0000-0000-000000000001/messages").SetArgDisplayNames("送訊息給助理");
        yield return new TestCaseData("GET", "/v1/account/local-agents").SetArgDisplayNames("列自己的本機代理電腦");
        yield return new TestCaseData("DELETE", "/v1/account/local-agents/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("移除本機代理電腦");
        yield return new TestCaseData("POST", "/v1/client-errors").SetArgDisplayNames("回報錯誤");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces").SetArgDisplayNames("列知識庫的空間");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces").SetArgDisplayNames("建立知識庫的空間");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR").SetArgDisplayNames("讀知識庫的空間");
        yield return new TestCaseData("PUT", "/v1/knowledge/spaces/HR").SetArgDisplayNames("改知識庫空間的設定");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/archive").SetArgDisplayNames("封存知識庫的空間");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/unarchive").SetArgDisplayNames("取消封存知識庫的空間");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR/tree").SetArgDisplayNames("讀知識庫的頁面樹");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR/trash").SetArgDisplayNames("讀知識庫的垃圾桶");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/pages").SetArgDisplayNames("新增知識庫的頁面");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR/pages/1").SetArgDisplayNames("讀知識庫的頁面");
        yield return new TestCaseData("PATCH", "/v1/knowledge/spaces/HR/pages/1").SetArgDisplayNames("修改知識庫的頁面");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/pages/1/move").SetArgDisplayNames("搬移知識庫的頁面");
        yield return new TestCaseData("DELETE", "/v1/knowledge/spaces/HR/pages/1").SetArgDisplayNames("刪除知識庫的頁面");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/pages/1/restore").SetArgDisplayNames("復原知識庫的頁面");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR/pages/1/restriction").SetArgDisplayNames("讀知識庫頁面的限制");
        yield return new TestCaseData("PUT", "/v1/knowledge/spaces/HR/pages/1/restriction").SetArgDisplayNames("設定知識庫頁面的限制");
        yield return new TestCaseData("GET", "/v1/knowledge/spaces/HR/pages/1/attachments").SetArgDisplayNames("列知識庫頁面的附件");
        yield return new TestCaseData("POST", "/v1/knowledge/spaces/HR/pages/1/attachments").SetArgDisplayNames("上傳知識庫頁面的附件");
        yield return new TestCaseData("GET", "/v1/knowledge/attachments/00000000-0000-0000-0000-000000000001/content").SetArgDisplayNames("下載知識庫頁面的附件");
        yield return new TestCaseData("GET", "/v1/account/tokens").SetArgDisplayNames("列出權杖");
        yield return new TestCaseData("POST", "/v1/account/tokens").SetArgDisplayNames("建立權杖");
        yield return new TestCaseData("DELETE", "/v1/account/tokens/00000000-0000-0000-0000-000000000001").SetArgDisplayNames("撤銷權杖");
    }

    [TestCaseSource(nameof(Endpoints))]
    public async Task Endpoint_TokenWithOneOfTheScopes_IsNotRejectedForItsScope(string method, string route, AccessTokenScope[] scopes)
    {
        // Arrange：每個允許的範圍各試一次，單獨一個範圍就夠。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        foreach (var scope in scopes)
        {
            var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, scope);

            // Act
            using var response = await client.SendAsync(Request(method, route, secret));

            // Assert：模組或內文可以有別的理由失敗，但不能是範圍或登入。
            Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized), $"{method} {route} 用 {scope}");
            Assert.That(await IsInsufficientScopeAsync(response), Is.False, $"{method} {route} 用 {scope}");
        }
    }

    [TestCaseSource(nameof(Endpoints))]
    public async Task Endpoint_TokenWithOnlyOtherScopes_Returns403InsufficientScopeNamingTheScopeNeeded(
        string method, string route, AccessTokenScope[] scopes)
    {
        // Arrange：權杖有其他所有範圍，就是沒有這個端點要的。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        // agent:run 不放進「其他範圍」：帶它的權杖只能打領工作與任何權杖都可以的端點（見 AgentTokenEndpointLimitTests），
        // 放進來訊息會是本機代理的說法，不是「需要哪個範圍」。
        var others = new[] { ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite }.Except(scopes).ToArray();
        if (others.Length == 0)
        {
            Assert.Pass("任何有效的權杖都可以：沒有「其他範圍」可試。");
        }

        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, others);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request(method, route, secret));

        // Assert
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
        var message = (await ApiRequests.ReadEnvelopeAsync(response))["error"]?["message"]?.GetValue<string>();
        Assert.That(details, Is.Empty);
        foreach (var scope in scopes)
        {
            Assert.That(message, Does.Contain(AccessTokenScopes.NameOf(scope)));
        }
    }

    [TestCaseSource(nameof(WebOnlyEndpoints))]
    public async Task WebOnlyEndpoint_AnyToken_Returns403AndSaysItCanOnlyBeDoneOnTheWeb(string method, string route)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite, AgentRun);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.SendAsync(Request(method, route, secret));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [Test]
    public async Task WebOnlyEndpoint_WithTheCookie_IsNotAffected()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);

        // Act
        using var response = await client.GetAsync("/v1/account/tokens");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task WebOnlyEndpoint_RejectedBeforeTheModuleIsCalled()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, ItemsRead, ItemsWrite, BlueprintRead, BlueprintWrite);
        using var client = factory.CreateClient();
        var request = Request("POST", "/v1/projects", secret);
        request.Content = ApiRequests.Json(new { key = "WEB", name = "Example", template = "simple_tasks" });

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(factory.Modules.Projects.CreateCalls, Is.Empty);
    }

    private static TestCaseData Case(string method, string route, params AccessTokenScope[] scopes) =>
        new TestCaseData(method, route, scopes).SetArgDisplayNames($"{method} {route}");

    private static HttpRequestMessage Request(string method, string route, string secret)
    {
        var request = AccessTokenAuthenticationTests.Bearer(new HttpMethod(method), route, secret);
        if (method is "POST" or "PUT" or "PATCH")
        {
            request.Content = ApiRequests.Json(new { });
        }

        return request;
    }

    private static async Task<bool> IsInsufficientScopeAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.Forbidden
        && (await ApiRequests.ReadEnvelopeAsync(response))["error"]?["code"]?.GetValue<string>() == ApiErrorCodes.InsufficientScope;
}
