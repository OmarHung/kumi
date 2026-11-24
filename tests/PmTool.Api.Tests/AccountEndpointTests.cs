using System.Net;
using System.Text.Json.Nodes;
using PmTool.Abstractions;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>「我的帳號」：改自己的密碼、登出其他裝置。</summary>
[TestFixture]
public sealed class AccountEndpointTests
{
    private const string PasswordRoute = "/v1/account/password";
    private const string OtherSessionsRoute = "/v1/account/other-sessions";
    private const string NewPassword = "brand-new-password";

    [Test]
    public async Task ChangePassword_Accepted_ReturnsHowManyOtherSessionsWereSignedOut()
    {
        // Arrange：同一個人在兩個瀏覽器登入。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var current = factory.CreateApiClient();
        using var other = factory.CreateApiClient();
        await Sessions.SignInAsync(current, TestUsers.Member);
        await Sessions.SignInAsync(other, TestUsers.Member);
        var token = factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId)[0];

        // Act
        using var response = await current.PutAsync(PasswordRoute, ChangeBody(TestUsers.Password, NewPassword));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        using var currentAfterwards = await current.GetAsync(Sessions.Route);
        using var otherAfterwards = await other.GetAsync(Sessions.Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonNode.DeepEquals(data, new JsonObject { ["signedOutSessions"] = 1 }), Is.True, data?.ToJsonString());
            Assert.That(factory.Modules.Accounts.Calls, Is.EqualTo(new[] { new ChangePasswordCall(token, TestUsers.Password, NewPassword) }));
            Assert.That(currentAfterwards.StatusCode, Is.EqualTo(HttpStatusCode.OK), "改密碼的這一個留著");
            Assert.That(otherAfterwards.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
    }

    [TestCase(IdentityErrorCodes.PasswordIncorrect, "currentPassword")]
    [TestCase(IdentityErrorCodes.PasswordTooShort, "newPassword")]
    [TestCase(IdentityErrorCodes.PasswordTooLong, "newPassword")]
    public async Task ChangePassword_ModuleRejects_ReturnsTheModuleCodeAndTheFieldPath(string code, string path)
    {
        // Arrange：模組的 Invalid 和其他端點一樣回 422，細項照原樣交給網頁標在欄位上。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Accounts.OnChange = _ => new Failure(FailureKind.Invalid, code, "身分模組說的原因。")
        {
            Details = [new FailureDetail(code, path, "身分模組說的原因。")],
        };
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync(PasswordRoute, ChangeBody("whatever-0000", "short"));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.UnprocessableEntity, code);

        // Assert
        Assert.That(ApiRequests.Describe(details), Is.EqualTo(new[] { $"{code}|{path}" }));
    }

    [TestCase("""{"currentPassword":"correct-horse-battery"}""", "", TestName = "ChangePassword_NewPasswordMissing_Returns400")]
    [TestCase("""{"currentPassword":"correct-horse-battery","newPassword":12}""", "newPassword", TestName = "ChangePassword_NewPasswordNotAString_Returns400")]
    [TestCase("""{"currentPassword":"a","newPassword":"b","extra":1}""", "extra", TestName = "ChangePassword_UnknownProperty_Returns400")]
    public async Task ChangePassword_BadBody(string body, string path)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PutAsync(PasswordRoute, ApiRequests.Document(body));
        var details = await ApiRequests.AssertFailureEnvelopeAsync(response, HttpStatusCode.BadRequest, ApiErrorCodes.BadRequest);

        // Assert
        Assert.That(details.Select(d => d?["path"]?.GetValue<string>()), Is.EqualTo(new[] { path }));
        Assert.That(factory.Modules.Accounts.Calls, Is.Empty);
    }

    [Test]
    public async Task ChangePassword_HasABudgetPerPerson_ThatChangingTheAddressDoesNotReset()
    {
        // Arrange：拿著別人工作階段的人不能用它無限次猜目前的密碼；額度跟著人走，換一個來源 IP 也不會多出來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateApiClient();
        using var signIn = await SendFromAsync(client, HttpMethod.Post, Sessions.Route, "192.0.2.60", Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));
        Assert.That(signIn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var guesses = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.PasswordChangePermitsPerWindow; i++)
        {
            using var guess = await SendFromAsync(client, HttpMethod.Put, PasswordRoute, $"192.0.2.{70 + i}", ChangeBody($"guess-{i:0000}-0000", NewPassword));
            guesses.Add(guess.StatusCode);
        }

        // Act
        using var change = await SendFromAsync(client, HttpMethod.Put, PasswordRoute, "192.0.2.99", ChangeBody(TestUsers.Password, NewPassword));

        // Assert
        Assert.That(guesses, Is.All.EqualTo(HttpStatusCode.UnprocessableEntity));
        await ApiRequests.AssertErrorEnvelopeAsync(change, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    [Test]
    public async Task ChangePassword_UsingUpThePersonsBudget_DoesNotUseUpSignInsFromTheSameAddress()
    {
        // Arrange：本機經 Docker 轉發時大家共用一個來源 IP。已登入的人故意送錯的目前密碼，
        // 不能因此把同一個 IP 上所有人的登入額度耗光，所以改密碼不和登入共用依 IP 的額度。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var member = factory.CreateApiClient();
        using var signIn = await SendFromAsync(member, HttpMethod.Post, Sessions.Route, "192.0.2.60", Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));
        Assert.That(signIn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        for (var i = 0; i <= ApiLimits.PasswordChangePermitsPerWindow; i++)
        {
            using var guess = await SendFromAsync(member, HttpMethod.Put, PasswordRoute, "192.0.2.60", ChangeBody($"guess-{i:0000}-0000", NewPassword));
        }

        // Act
        using var owner = factory.CreateApiClient();
        using var ownerSignIn = await SendFromAsync(owner, HttpMethod.Post, Sessions.Route, "192.0.2.60", Sessions.Credentials(TestUsers.Owner.Email, TestUsers.Password));
        using var ownerChange = await SendFromAsync(owner, HttpMethod.Put, PasswordRoute, "192.0.2.60", ChangeBody(TestUsers.Password, NewPassword));

        // Assert
        Assert.That(ownerSignIn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(ownerChange.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task EndOtherSessions_SignsOutTheOtherBrowsersAndKeepsThisOne()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var current = factory.CreateApiClient();
        using var second = factory.CreateApiClient();
        using var third = factory.CreateApiClient();
        foreach (var client in new[] { current, second, third })
        {
            await Sessions.SignInAsync(client, TestUsers.Guest);
        }

        // Act
        using var response = await current.DeleteAsync(OtherSessionsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);
        var statuses = new List<HttpStatusCode>();
        foreach (var client in new[] { current, second, third })
        {
            using var afterwards = await client.GetAsync(Sessions.Route);
            statuses.Add(afterwards.StatusCode);
        }

        // Assert：訪客也可以登出自己的其他裝置。
        Assert.That(JsonNode.DeepEquals(data, new JsonObject { ["signedOutSessions"] = 2 }), Is.True, data?.ToJsonString());
        Assert.That(statuses, Is.EqualTo(new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized }));
    }

    [Test]
    public async Task EndOtherSessions_NoOtherSessions_ReturnsZero()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.DeleteAsync(OtherSessionsRoute);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["signedOutSessions"]?.GetValue<int>(), Is.Zero);
        Assert.That(factory.Modules.Sessions.EndOthersCalls, Has.Count.EqualTo(1));
    }

    private static HttpContent ChangeBody(string currentPassword, string newPassword) =>
        ApiRequests.Json(new { currentPassword, newPassword });

    private static Task<HttpResponseMessage> SendFromAsync(HttpClient client, HttpMethod method, string route, string remoteIp, HttpContent body)
    {
        var request = new HttpRequestMessage(method, route) { Content = body };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        return client.SendAsync(request);
    }
}
