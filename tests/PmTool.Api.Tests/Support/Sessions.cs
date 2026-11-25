using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PmTool.Identity;

namespace PmTool.Api.Tests.Support;

/// <summary>登入相關的共用步驟。</summary>
internal static class Sessions
{
    public const string Route = "/v1/session";

    public const string CookieName = "pmtool_session";

    /// <summary>一律走 HTTPS（<c>Security:RequireHttps</c>）時工作階段 Cookie 的名稱。</summary>
    public const string HostCookieName = "__Host-pmtool_session";

    public static HttpContent Credentials(string email, string password) => ApiRequests.Json(new { email, password });

    /// <summary>登入並確認成功。用戶端是 factory 建的，會自己記住 Cookie。</summary>
    public static async Task SignInAsync(HttpClient client, AuthenticatedUser user)
    {
        using var response = await client.PostAsync(Route, Credentials(user.Email, TestUsers.Password));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    /// <summary>登入，回傳工作階段的 Cookie（名稱=值），給不自動記 Cookie 的用戶端用。</summary>
    public static async Task<string> SignInForCookieAsync(HttpClient client, AuthenticatedUser user)
    {
        using var response = await client.PostAsync(Route, Credentials(user.Email, TestUsers.Password));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return SessionCookieOf(response) ?? throw new AssertionException("登入成功卻沒有發工作階段的 Cookie");
    }

    /// <summary>回應裡設定工作階段 Cookie 的那一行 Set-Cookie；沒有就是 null。</summary>
    public static string? SetCookieLineOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var lines)
            ? lines.FirstOrDefault(line =>
                line.StartsWith(CookieName + "=", StringComparison.Ordinal) || line.StartsWith(HostCookieName + "=", StringComparison.Ordinal))
            : null;

    public static string? SessionCookieOf(HttpResponseMessage response) => SetCookieLineOf(response)?.Split(';')[0];

    /// <summary>
    /// 不記 Cookie 的用戶端，每個請求自己帶 Cookie。用來模擬留著一份舊 Cookie、不收伺服器新發的那一份的用戶端。
    /// </summary>
    public static HttpClient CreateReplayClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add(ApiRequests.CsrfHeader, "1");
        return client;
    }

    /// <summary>帶著指定的 Cookie 送一個請求，回傳狀態碼。</summary>
    public static async Task<HttpStatusCode> SendWithCookieAsync(HttpClient client, string route, string cookie)
    {
        using var request = WithCookie(HttpMethod.Get, route, cookie);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    public static HttpRequestMessage WithCookie(HttpMethod method, string route, string cookie)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add(ApiRequests.CsrfHeader, "1");
        return request;
    }
}
