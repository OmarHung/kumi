using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.RealStack;

/// <summary>
/// 命令列瀏覽器登入從頭到尾（真的 HTTP、真的身分模組與資料庫）：Cookie 登入 → check → approve 拿到導回網址的碼 →
/// 不帶 Cookie 兌換並換掉舊權杖。其他測試把身分模組換成假的，碼的雜湊、PKCE、撤銷舊權杖有沒有接對只有這裡看得到。
/// </summary>
[TestFixture]
public sealed class CliLoginFlowTests : RealStackTestBase
{
    private const string TokensRoute = "/v1/account/tokens";
    private const string State = "abcdefghijklmnopqrstuv";
    private const string ClientAddress = "203.0.113.7";

    [Test]
    public async Task Login_ApprovedOnTheWebAndRedeemedWithoutACookie_GivesTheTokensAndRevokesTheOldOne()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var old = await CreateTokenAsync(owner.Client, "舊的命令列權杖");
        var oldSecret = old["secret"]!.GetValue<string>();
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() + "abc";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var body = new
        {
            port = 53682, state = State, challenge, machine = "MacBook-Pro", tokens = new[] { "cli" },
            scopes = new[] { "items:read", "blueprint:read" }, grantedScopes = new[] { "items:read", "blueprint:read" }, expiresInDays = 30,
            confirmationCode = CliLoginRules.ConfirmationCodeOf(challenge),
        };

        // Act
        using var check = await owner.Client.PostAsync("/v1/cli-login/check", ApiRequests.Json(body));
        using var approve = await owner.Client.SendAsync(CliLoginRequests.From("/v1/cli-login/approve", ClientAddress, body));
        var redirect = (await ApiRequests.AssertSuccessEnvelopeAsync(approve, HttpStatusCode.OK))!["redirectTo"]!.GetValue<string>();
        var code = System.Web.HttpUtility.ParseQueryString(new Uri(redirect).Query)["code"]!;
        using var anonymous = Factory.CreateClient();
        using var redeem = await anonymous.SendAsync(CliLoginRequests.From(
            "/v1/cli-login/token", ClientAddress, new { code, codeVerifier = verifier, replaceTokens = new[] { oldSecret } }));
        var redeemed = await ApiRequests.AssertSuccessEnvelopeAsync(redeem, HttpStatusCode.OK);
        var newSecret = redeemed!["cli"]!["secret"]!.GetValue<string>();
        using var session = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", newSecret));
        var sessionData = await ApiRequests.AssertSuccessEnvelopeAsync(session, HttpStatusCode.OK);
        using var oldAfter = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", oldSecret));
        var listed = await RealStackSteps.GetAsync(owner.Client, TokensRoute);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(check.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(redeemed["agent"], Is.Null);
            Assert.That(sessionData!["scopes"]!.AsArray().Select(scope => scope!.GetValue<string>()), Is.EquivalentTo(new[] { "items:read", "blueprint:read" }));
            Assert.That(oldAfter.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "舊的權杖兌換時被撤銷");
            Assert.That(listed.AsArray().Select(token => token!["machineName"]?.GetValue<string>()), Does.Contain("MacBook-Pro"));
            Assert.That(listed.ToJsonString(), Does.Not.Contain(newSecret));
            Assert.That(listed.AsArray().Select(token => token!["createdFrom"]?.GetValue<string>()), Does.Contain(ClientAddress));
        }
    }

    [Test]
    public async Task Approve_WithoutTheConfirmationCode_Is422AndNothingCanBeRedeemed()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, challenge) = CliLoginRequests.NewPkce();
        var body = new
        {
            port = 53682, state = State, challenge, machine = "MacBook-Pro", tokens = new[] { "cli" },
            scopes = new[] { "items:read" }, grantedScopes = new[] { "items:read" }, expiresInDays = 30,
        };

        // Act
        using var approve = await owner.Client.SendAsync(CliLoginRequests.From(CliLoginRequests.Approve, ClientAddress, body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(approve, (HttpStatusCode)422, "cli_login.confirmation_mismatch");

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { "cli_login.confirmation_mismatch|confirmationCode" }));
        Assert.That(verifier, Is.Not.Empty);
    }

    [Test]
    public async Task Token_TheSameCodeRedeemedAgain_RevokesTheTokensItIssued()
    {
        // Arrange
        using var owner = await SignInAsOwnerAsync();
        var (verifier, challenge) = CliLoginRequests.NewPkce();
        using var approve = await owner.Client.SendAsync(CliLoginRequests.From(CliLoginRequests.Approve, ClientAddress, CliLoginRequests.ApproveBody(challenge)));
        var redirect = (await ApiRequests.AssertSuccessEnvelopeAsync(approve, HttpStatusCode.OK))!["redirectTo"]!.GetValue<string>();
        var code = System.Web.HttpUtility.ParseQueryString(new Uri(redirect).Query)["code"]!;
        using var anonymous = Factory.CreateClient();
        using var first = await anonymous.SendAsync(CliLoginRequests.From(CliLoginRequests.Token, ClientAddress, new { code, codeVerifier = verifier }));
        var secret = (await ApiRequests.AssertSuccessEnvelopeAsync(first, HttpStatusCode.OK))!["cli"]!["secret"]!.GetValue<string>();

        // Act
        using var second = await anonymous.SendAsync(CliLoginRequests.From(CliLoginRequests.Token, ClientAddress, new { code, codeVerifier = verifier }));
        using var afterwards = await anonymous.SendAsync(AccessTokenAuthenticationTests.Bearer(HttpMethod.Get, "/v1/session", secret));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(second.StatusCode, Is.EqualTo((HttpStatusCode)422));
            Assert.That(afterwards.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
    }

    private static async Task<JsonNode> CreateTokenAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsync(TokensRoute, ApiRequests.Json(new { name, scopes = new[] { "items:read" }, expiresInDays = 30 }));
        return await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.Created)
            ?? throw new AssertionException("建立權杖的回應沒有 data");
    }
}
