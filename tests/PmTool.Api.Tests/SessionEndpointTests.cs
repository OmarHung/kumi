using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class SessionEndpointTests
{
    [Test]
    public async Task SignIn_ValidCredentials_ReturnsTheUser()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        var owner = TestUsers.Owner;

        // Act
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(owner.Email, TestUsers.Password));
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["user"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(owner.PersonId));
            Assert.That(data?["user"]?["displayName"]?.GetValue<string>(), Is.EqualTo(owner.DisplayName));
            Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(owner.Email));
            Assert.That(data?["user"]?["organizationId"]?.GetValue<Guid>(), Is.EqualTo(owner.OrganizationId));
            Assert.That(data?["user"]?["organizationName"]?.GetValue<string>(), Is.EqualTo(owner.OrganizationName));
            Assert.That(data?["user"]?["role"]?.GetValue<string>(), Is.EqualTo("owner"));
            Assert.That(data?["user"]?.AsObject().Count, Is.EqualTo(6), "帳號編號這類內部資料不回給用戶端");
            Assert.That(factory.Modules.Authentication.SignInCalls, Is.EqualTo(new[] { (owner.Email, TestUsers.Password) }));
            Assert.That(factory.Modules.Sessions.StartCalls, Is.EqualTo(new[] { owner }), "伺服器端開了一筆工作階段");
        }
    }

    [Test]
    public async Task SignIn_AgainInTheSameBrowser_EndsThePreviousSession()
    {
        // Arrange：舊的 Cookie 會被新的蓋掉，再也用不到，不必留到閒置期限。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        var first = factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Single();

        // Act
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Assert
        var tokens = factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId);
        Assert.That(tokens, Has.Count.EqualTo(2));
        Assert.That(factory.Modules.Sessions.IsActive(first), Is.False);
        Assert.That(factory.Modules.Sessions.IsActive(tokens[1]), Is.True);
    }

    [Test]
    public async Task SignIn_ValidCredentials_SetsAStrictHttpOnlySessionCookie()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(
            Sessions.Route, Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));
        var cookie = Sessions.SetCookieLineOf(response)?.ToLowerInvariant();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cookie, Is.Not.Null);
            Assert.That(cookie, Does.Contain("httponly"));
            Assert.That(cookie, Does.Contain("samesite=strict"));
            Assert.That(cookie, Does.Contain("path=/"));
            Assert.That(cookie, Does.Not.Contain("secure"), "用 http 連進來時不能標 Secure，否則瀏覽器不會送回來");
        }
    }

    [Test]
    public async Task SignIn_OverHttps_MarksTheCookieSecure()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(ApiRequests.CsrfHeader, "1");

        // Act
        using var response = await client.PostAsync(
            Sessions.Route, Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));

        // Assert
        Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Contain("secure"));
    }

    [TestCase("owner@example.com", "wrong-password-0000", TestName = "SignIn_WrongPassword_Returns401InvalidCredentials")]
    [TestCase("nobody@example.com", TestUsers.Password, TestName = "SignIn_UnknownEmail_Returns401InvalidCredentials")]
    public async Task SignIn_Rejected(string email, string password)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(email, password));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.InvalidCredentials);
        Assert.That(Sessions.SetCookieLineOf(response), Is.Null);
    }

    [Test]
    public async Task SignIn_UnknownEmailAndWrongPassword_ReturnTheSameBody()
    {
        // Arrange：訊息分得出是哪一種的話，就能拿來探測哪些 Email 有帳號。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var unknown = await client.PostAsync(Sessions.Route, Sessions.Credentials("nobody@example.com", TestUsers.Password));
        using var wrong = await client.PostAsync(Sessions.Route, Sessions.Credentials(TestUsers.Owner.Email, "wrong-password-0000"));

        // Assert
        Assert.That(await unknown.Content.ReadAsStringAsync(), Is.EqualTo(await wrong.Content.ReadAsStringAsync()));
    }

    [Test]
    public async Task GetSession_WithoutSignIn_Returns401AuthRequired()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.GetAsync(Sessions.Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(response.Headers.Location, Is.Null, "不導向登入頁");
    }

    [Test]
    public async Task GetSession_AfterSignIn_ReturnsTheSameUser()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        using var response = await client.GetAsync(Sessions.Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["user"]?["personId"]?.GetValue<Guid>(), Is.EqualTo(TestUsers.Member.PersonId));
            Assert.That(data?["user"]?["role"]?.GetValue<string>(), Is.EqualTo("member"));
            Assert.That(data?["user"]?["organizationName"]?.GetValue<string>(), Is.EqualTo(TestUsers.OrganizationName));
        }
    }

    [Test]
    public async Task SignOut_AfterSignIn_ClearsTheSession()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        using var signOut = await client.DeleteAsync(Sessions.Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(signOut, HttpStatusCode.OK);
        using var afterwards = await client.GetAsync(Sessions.Route);

        // Assert
        Assert.That(data, Is.Null);
        Assert.That(Sessions.SetCookieLineOf(signOut), Does.Contain("expires=Thu, 01 Jan 1970"));
        await ApiRequests.AssertErrorEnvelopeAsync(afterwards, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task SignOut_EndsTheSessionOnTheServer_SoACopyOfTheCookieNoLongerWorks()
    {
        // Arrange：有人事先複製了這份 Cookie。登出之後它也要失效，不能等到閒置或絕對期限。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = Sessions.CreateReplayClient(factory);
        var cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);
        var token = factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Single();

        // Act
        using var signOut = Sessions.WithCookie(HttpMethod.Delete, Sessions.Route, cookie);
        using var signOutResponse = await client.SendAsync(signOut);
        var afterwards = await Sessions.SendWithCookieAsync(client, Sessions.Route, cookie);

        // Assert
        Assert.That(signOutResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Sessions.EndCalls, Is.EqualTo(new[] { token }));
        Assert.That(afterwards, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SignOut_WithoutSession_StillSucceeds()
    {
        // Arrange：工作階段已經過期的網頁按登出，結果一樣是登出了，不必當成錯誤。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.DeleteAsync(Sessions.Route);

        // Assert
        Assert.That(await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK), Is.Null);
        Assert.That(factory.Modules.Sessions.EndCalls, Is.Empty, "沒有工作階段就沒有東西要作廢");
    }
}
