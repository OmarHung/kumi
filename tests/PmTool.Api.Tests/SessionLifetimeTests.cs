using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// 工作階段的期限與 Cookie 的格式。閒置與絕對期限改由伺服器端的工作階段判斷（身分模組的測試逐條測），
/// 這裡看 API 這一側：票證的期限跟著伺服器端的絕對期限、舊格式的票證不收、金鑰換了讀不懂。時間用假的時鐘推進。
/// </summary>
[TestFixture]
public sealed class SessionLifetimeTests
{
    private static readonly TimeSpan AbsoluteLimit = TimeSpan.FromDays(IdentityRules.SessionAbsoluteDays);
    private static readonly TimeSpan Moment = TimeSpan.FromSeconds(1);

    [Test]
    public async Task Session_JustBeforeTheAbsoluteLimit_IsStillValid()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        factory.Modules.Time.Advance(AbsoluteLimit - Moment);
        using var response = await client.GetAsync(Sessions.Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Session_PastTheAbsoluteLimit_IsRejectedByTheTicketItself()
    {
        // Arrange：票證的期限就是伺服器端的絕對期限，過了之後解開 Cookie 時就擋下，不必問身分模組。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        factory.Modules.Time.Advance(AbsoluteLimit + Moment);
        using var response = await client.GetAsync(Sessions.Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(factory.Modules.Sessions.FindCalls, Is.Empty);
    }

    [Test]
    public async Task Session_SignIn_IsNotAPersistentCookie()
    {
        // Arrange：關掉瀏覽器就沒了；期限寫在加密的票證裡，不寫在 Cookie 的 Expires。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));

        // Assert
        Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Not.Contain("expires="));
    }

    [Test]
    public async Task Session_TicketWithoutAToken_IsRejectedWithoutAskingTheSessionService()
    {
        // Arrange：改成伺服器端工作階段之前發的 Cookie 沒有權杖，部署之後每個人要重新登入一次。
        // 用伺服器自己的金鑰把一份真的票證解開、拿掉權杖、再包回去，模擬舊格式的 Cookie。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = Sessions.CreateReplayClient(factory);
        var cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);
        var format = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var ticket = format.Unprotect(cookie[(Sessions.CookieName.Length + 1)..])
            ?? throw new AssertionException("解不開剛發的 Cookie");
        var identity = new ClaimsIdentity(
            ticket.Principal.Claims.Where(claim => claim.Type != "pmtool:session_token"), ticket.AuthenticationScheme);
        var legacy = $"{Sessions.CookieName}={format.Protect(new AuthenticationTicket(new ClaimsPrincipal(identity), ticket.Properties, ticket.AuthenticationScheme))}";

        // Act
        using var request = Sessions.WithCookie(HttpMethod.Get, Sessions.Route, legacy);
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(Sessions.SetCookieLineOf(response), Is.Null, "只拒絕，不刪 Cookie");
        Assert.That(factory.Modules.Sessions.FindCalls, Is.Empty, "不必問身分模組就知道要拒絕");
    }

    [Test]
    public async Task Session_WithKeysOnlyInMemory_IsNotReadableByAnotherInstance()
    {
        // Arrange：伺服器端的工作階段還在，但沒有共用的金鑰，另一個實體讀不懂這個 Cookie，就當作沒登入。
        // 金鑰存在資料庫、換容器還解得開的情況見 RealStack 的 KeyStorageTests。
        var modules = new FakeModules();
        string cookie;
        using (var before = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { SharedModules = modules }))
        {
            using var client = before.CreateApiClient();
            cookie = await Sessions.SignInForCookieAsync(client, TestUsers.Member);
        }

        using var after = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { SharedModules = modules });
        using var other = after.CreateClient();

        // Act
        using var request = Sessions.WithCookie(HttpMethod.Get, Sessions.Route, cookie);
        using var response = await other.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }
}
