using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Web;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 真的身分模組與資料庫，從註冊用戶端走到撤銷：同意、換權杖、用權杖通過驗證（只能用在 /mcp）、更新、重複使用、撤銷。
/// 瀏覽器的部分（同意頁）用 HTTP 直接送。
/// </summary>
[TestFixture]
public sealed class OAuthFlowTests : RealStackTestBase
{
    private const string Redirect = "http://127.0.0.1:53682/callback";

    [Test]
    public async Task FullFlow_ConsentExchangeUseRefreshReplayAndRevoke()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = Factory.CreateClient();
        var clientId = await RegisterAsync(cli);
        var (verifier, challenge) = NewPkce();
        var parameters = Parameters(clientId, challenge, "items:read items:write");

        // Act：同意頁
        var check = await PostDataAsync(owner.Client, "/v1/oauth/authorization/check", parameters, HttpStatusCode.OK);
        var approve = await PostDataAsync(owner.Client, "/v1/oauth/authorization/approve", With(parameters, "scopes", new[] { "items:read" }), HttpStatusCode.OK);
        var callback = new Uri(approve["redirectTo"]!.GetValue<string>());
        var query = HttpUtility.ParseQueryString(callback.Query);

        // Act：換權杖
        var tokens = await TokenAsync(cli, ("grant_type", "authorization_code"), ("code", query["code"]!), ("redirect_uri", Redirect),
            ("client_id", clientId), ("code_verifier", verifier), ("resource", "http://localhost/mcp"));
        var access = tokens["access_token"]!.GetValue<string>();

        // Act：用權杖
        using var onApi = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/projects", access));
        using var onMcp = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", access));
        var listed = await RealStackSteps.GetAsync(owner.Client, "/v1/account/oauth-grants");
        var personalTokens = await RealStackSteps.GetAsync(owner.Client, "/v1/account/tokens");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(check["clientName"]!.GetValue<string>(), Is.EqualTo("範例代理"));
            Assert.That(check["clientHost"], Is.Null);
            Assert.That(check["redirectHost"]!.GetValue<string>(), Is.EqualTo("127.0.0.1:53682"));
            Assert.That(check["account"]!["email"]!.GetValue<string>(), Is.EqualTo(RealStackFactory.OwnerEmail));
            Assert.That(query["state"], Is.EqualTo("s-1"));
            Assert.That(query["iss"], Is.EqualTo("http://localhost"));
            Assert.That(tokens["scope"]!.GetValue<string>(), Is.EqualTo("items:read"));
            Assert.That(tokens["expires_in"]!.GetValue<int>(), Is.EqualTo(3600));
            Assert.That(onApi.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "OAuth 發的權杖只能打 /mcp");
            Assert.That(onMcp.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(listed.AsArray().Single()!["clientName"]!.GetValue<string>(), Is.EqualTo("範例代理"));
            Assert.That(personalTokens.AsArray(), Is.Empty, "個人存取權杖的清單不列 OAuth 發的");
        }

        // Act：更新（輪替）與重複使用
        var refreshed = await TokenAsync(cli, ("grant_type", "refresh_token"), ("refresh_token", tokens["refresh_token"]!.GetValue<string>()), ("client_id", clientId));
        using var replay = await cli.PostAsync("/oauth/token", Form(("grant_type", "refresh_token"), ("refresh_token", tokens["refresh_token"]!.GetValue<string>()), ("client_id", clientId)));
        using var afterReplay = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", refreshed["access_token"]!.GetValue<string>()));

        // Assert
        Assert.That(refreshed["refresh_token"]!.GetValue<string>(), Is.Not.EqualTo(tokens["refresh_token"]!.GetValue<string>()));
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(JsonNode.Parse(await replay.Content.ReadAsStringAsync())!["error"]!.GetValue<string>(), Is.EqualTo("invalid_grant"));
        Assert.That(afterReplay.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "用過的更新權杖再出現，整筆授權撤銷");
    }

    [Test]
    public async Task RevokingTheGrantOnTheWeb_StopsTheTokenAtOnce()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = Factory.CreateClient();
        var (access, refresh, clientId) = await ConnectAsync(owner.Client, cli);
        using var before = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", access));
        var grantId = (await RealStackSteps.GetAsync(owner.Client, "/v1/account/oauth-grants")).AsArray().Single()!["grantId"]!.GetValue<Guid>();

        // Act
        using var revoked = await owner.Client.DeleteAsync($"/v1/account/oauth-grants/{grantId}");
        using var after = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", access));
        using var refreshed = await cli.PostAsync("/oauth/token", Form(("grant_type", "refresh_token"), ("refresh_token", refresh), ("client_id", clientId)));
        using var again = await owner.Client.DeleteAsync($"/v1/account/oauth-grants/{grantId}");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(before.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(refreshed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        }
    }

    [Test]
    public async Task RevokeEndpoint_WithTheRefreshToken_EndsTheGrant()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = Factory.CreateClient();
        var (access, refresh, _) = await ConnectAsync(owner.Client, cli);

        // Act
        using var revoke = await cli.PostAsync("/oauth/revoke", Form(("token", refresh)));
        using var after = await cli.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Post, "/mcp", access));

        // Assert
        Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That((await RealStackSteps.GetAsync(owner.Client, "/v1/account/oauth-grants")).AsArray(), Is.Empty);
    }

    [Test]
    public async Task Check_WithAnUnregisteredRedirect_IsRejectedWithoutARedirect()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        using var cli = Factory.CreateClient();
        var clientId = await RegisterAsync(cli);
        var parameters = Parameters(clientId, NewPkce().Challenge, "items:read");

        // Act
        using var response = await owner.Client.PostAsync(
            "/v1/oauth/authorization/check", ApiRequests.Json(With(parameters, "redirectUri", "https://evil.example.org/cb")));
        var error = (await ApiRequests.ReadEnvelopeAsync(response))["error"]!;

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo((HttpStatusCode)422));
        Assert.That(error["code"]!.GetValue<string>(), Is.EqualTo("oauth.request_invalid"));
        Assert.That(error["data"]!["error"]!.GetValue<string>(), Is.EqualTo("invalid_request"));
        Assert.That(error["data"]!["redirectTo"], Is.Null);
    }

    internal static async Task<(string Access, string Refresh, string ClientId)> ConnectAsync(HttpClient web, HttpClient cli)
    {
        var clientId = await RegisterAsync(cli);
        var (verifier, challenge) = NewPkce();
        var parameters = Parameters(clientId, challenge, "items:read items:write");
        var approve = await PostDataAsync(web, "/v1/oauth/authorization/approve", With(parameters, "scopes", new[] { "items:read" }), HttpStatusCode.OK);
        var code = HttpUtility.ParseQueryString(new Uri(approve["redirectTo"]!.GetValue<string>()).Query)["code"]!;
        var tokens = await TokenAsync(cli, ("grant_type", "authorization_code"), ("code", code), ("redirect_uri", Redirect),
            ("client_id", clientId), ("code_verifier", verifier));
        return (tokens["access_token"]!.GetValue<string>(), tokens["refresh_token"]!.GetValue<string>(), clientId);
    }

    private static async Task<string> RegisterAsync(HttpClient cli)
    {
        using var response = await cli.PostAsync(
            "/oauth/register", ApiRequests.Json(new { client_name = "範例代理", redirect_uris = new[] { Redirect } }));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["client_id"]!.GetValue<string>();
    }

    private static async Task<JsonNode> TokenAsync(HttpClient cli, params (string Name, string Value)[] fields)
    {
        using var response = await cli.PostAsync("/oauth/token", Form(fields));
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        return JsonNode.Parse(text)!;
    }

    private static async Task<JsonNode> PostDataAsync(HttpClient client, string route, object body, HttpStatusCode expected)
    {
        using var response = await client.PostAsync(route, ApiRequests.Json(body));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, expected) ?? throw new AssertionException("沒有 data");
    }

    private static HttpContent Form(params (string Name, string Value)[] fields) =>
        new FormUrlEncodedContent(fields.Select(f => KeyValuePair.Create(f.Name, f.Value)));

    private static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static Dictionary<string, object> Parameters(string clientId, string challenge, string scope) => new()
    {
        ["responseType"] = "code",
        ["clientId"] = clientId,
        ["redirectUri"] = Redirect,
        ["scope"] = scope,
        ["state"] = "s-1",
        ["codeChallenge"] = challenge,
        ["codeChallengeMethod"] = "S256",
        ["resource"] = "http://localhost/mcp",
    };

    private static Dictionary<string, object> With(Dictionary<string, object> source, string key, object value) =>
        new(source) { [key] = value };
}
