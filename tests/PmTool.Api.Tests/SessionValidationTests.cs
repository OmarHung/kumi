using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 每個帶 Cookie 的請求都拿權杖問一次伺服器端的工作階段。取代原本「每五分鐘依帳號重新確認一次、結果在 API 裡快取」：
/// 現在作廢的工作階段下一個請求就失效；快取改在身分模組裡（它的測試看快取，這裡只看 API 每次都問）。
/// </summary>
[TestFixture]
public sealed class SessionValidationTests
{
    private const int Requests = 5;

    [Test]
    public async Task SignedIn_EveryRequest_AsksTheSessionServiceWithTheTokenFromSignIn()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        var token = factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Single();

        // Act
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < Requests; i++)
        {
            using var response = await client.GetAsync(Sessions.Route);
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Sessions.FindCalls, Is.EqualTo(Enumerable.Repeat(token, Requests)));
    }

    [Test]
    public async Task SessionEndedElsewhere_TheNextRequest_Returns401AndClearsTheCookie()
    {
        // Arrange：例如另一個裝置改了密碼或按了「登出其他裝置」。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        factory.Modules.Sessions.End(factory.Modules.Sessions.TokensOf(TestUsers.Member.AccountId).Single());

        // Act
        using var response = await client.GetAsync("/v1/projects");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(Sessions.SetCookieLineOf(response), Does.Contain("expires=Thu, 01 Jan 1970"));
        Assert.That(factory.Modules.Projects.ListCalls, Is.Empty);
    }

    [Test]
    public async Task AccountDisabled_TheNextRequest_Returns401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        factory.Modules.Authentication.Disable(TestUsers.Member.AccountId);

        // Act
        using var response = await client.GetAsync(Sessions.Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task UserDataUnchanged_ResponsesDoNotSetTheCookieAgain()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        using var first = await client.GetAsync(Sessions.Route);
        factory.Modules.Time.Advance(TimeSpan.FromHours(6));
        using var later = await client.GetAsync("/v1/projects");

        // Assert：Cookie 不滑動延長，帳號的資料沒變也不換發。
        Assert.That(Sessions.SetCookieLineOf(first), Is.Null);
        Assert.That(Sessions.SetCookieLineOf(later), Is.Null);
    }

    [Test]
    public async Task RoleAndNameChanged_TheNextRequestSeesThemAndGetsANewCookie()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        factory.Modules.Authentication.Replace(TestUsers.Member with { Role = OrganizationRole.Admin, DisplayName = "Renamed Example" });

        // Act
        using var changed = await client.GetAsync(Sessions.Route);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(changed, HttpStatusCode.OK);
        using var next = await client.GetAsync(Sessions.Route);

        // Assert：換發一次就好，之後票證裡的資料已經是新的。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(data?["user"]?["role"]?.GetValue<string>(), Is.EqualTo("admin"));
            Assert.That(data?["user"]?["displayName"]?.GetValue<string>(), Is.EqualTo("Renamed Example"));
            Assert.That(Sessions.SetCookieLineOf(changed), Is.Not.Null);
            Assert.That(Sessions.SetCookieLineOf(next), Is.Null);
        }
    }

    [Test]
    public async Task RenewedCookie_KeepsTheAbsoluteLimitOfTheSession()
    {
        // Arrange：換發時框架會把票證的期限往後延；票證要停在伺服器端的絕對期限，萬一沒問到也一樣擋得下。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = Sessions.CreateReplayClient(factory);
        var cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);
        factory.Modules.Time.Advance(TimeSpan.FromDays(3));
        factory.Modules.Authentication.Replace(TestUsers.Member with { DisplayName = "Renamed Example" });
        using var renewRequest = Sessions.WithCookie(HttpMethod.Get, Sessions.Route, cookie);
        using var renewed = await client.SendAsync(renewRequest);
        var renewedCookie = Sessions.SessionCookieOf(renewed) ?? throw new AssertionException("沒有換發 Cookie");
        factory.Modules.Authentication.Replace(TestUsers.Member);
        factory.Modules.Time.Advance(TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays - 3) + TimeSpan.FromSeconds(1));
        var findsBefore = factory.Modules.Sessions.FindCalls.Count;

        // Act
        var status = await Sessions.SendWithCookieAsync(client, Sessions.Route, renewedCookie);

        // Assert：票證本身就過期了，不必問身分模組。
        Assert.That(status, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(factory.Modules.Sessions.FindCalls, Has.Count.EqualTo(findsBefore));
    }

    [TestCase("/health")]
    [TestCase("/health/ready")]
    public async Task Cookie_OnHealthChecks_IsNotChecked(string route)
    {
        // Arrange：健康檢查不需要知道是誰，又不限流；讀了 Cookie 就等於讓任何人都能替資料庫加壓。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = Sessions.CreateReplayClient(factory);
        var cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);

        // Act
        var status = await Sessions.SendWithCookieAsync(client, route, cookie);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Sessions.FindCalls, Is.Empty);
    }

    [TestCase("/projects/WEB")]
    [TestCase("/")]
    [TestCase("/" + WebHostingTests.AssetPath)]
    public async Task Cookie_OnWebPages_IsNotChecked(string route)
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = Sessions.CreateReplayClient(factory);
        var cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);

        // Act
        var status = await Sessions.SendWithCookieAsync(client, route, cookie);

        // Assert
        Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(factory.Modules.Sessions.FindCalls, Is.Empty);
    }
}
