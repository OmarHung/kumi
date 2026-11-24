using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests;

/// <summary>
/// <c>Authorization</c> 標頭的各種寫法。把目前的行為鎖住：只有「恰好一個標頭、以 <c>Bearer </c>（不分大小寫）開頭」才算權杖請求，
/// 其他寫法一律當作沒帶權杖，不會被誤認成別人的身分。
/// </summary>
[TestFixture]
public sealed class AuthorizationHeaderTests
{
    private const string SessionRoute = "/v1/session";

    [TestCase("bearer {0}")]
    [TestCase("BEARER {0}")]
    [TestCase("Bearer  {0}")]
    [TestCase("Bearer {0} ")]
    public async Task Header_BearerWithCaseOrExtraSpaces_IsAValidToken(string template)
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        request.Headers.TryAddWithoutValidation("Authorization", string.Format(template, secret));

        // Act
        using var response = await client.SendAsync(request);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Member.Email));
    }

    // HttpClient 會把 Authorization 標頭重新整理成「配置 空白 值」，tab 這類寫法送不出去，
    // 所以測試用另一個標頭帶原文，由伺服器端的測試掛勾在進入應用程式之前換進去。
    [TestCase("Bearer\t{0}")]
    [TestCase("Bearer{0}")]
    [TestCase("{0}")]
    [TestCase("Token {0}")]
    public async Task Header_NotABearerHeader_IsIgnoredSoTheRequestIsAnonymous(string template)
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(), new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<IStartupFilter>(new RawAuthorizationFilter()) });
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        request.Headers.Add(RawAuthorizationFilter.Header, string.Format(template, secret));

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(factory.Modules.AccessTokens.FindCalls, Is.Empty, "不是 Bearer 的標頭不拿去查權杖");
    }

    [Test]
    public async Task Header_TwoAuthorizationHeaders_AreNotTreatedAsAToken()
    {
        // Arrange：兩個標頭的語意不明（不同的元件可能挑不同的那一個），所以兩個都不採用。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        request.Headers.TryAddWithoutValidation("Authorization", [$"Bearer {secret}", $"Bearer {secret}"]);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
        Assert.That(factory.Modules.AccessTokens.FindCalls, Is.Empty);
    }

    [Test]
    public async Task Header_TwoAuthorizationHeadersWithACookie_FallBackToTheCookieNotTheToken()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var request = new HttpRequestMessage(HttpMethod.Get, SessionRoute);
        request.Headers.TryAddWithoutValidation("Authorization", [$"Bearer {secret}", $"Bearer {secret}"]);

        // Act
        using var response = await client.SendAsync(request);
        var data = await ApiRequests.AssertSuccessEnvelopeAsync(response, HttpStatusCode.OK);

        // Assert
        Assert.That(data?["user"]?["email"]?.GetValue<string>(), Is.EqualTo(TestUsers.Owner.Email));
    }

    /// <summary>把 <see cref="Header"/> 的值原封不動換成 Authorization 標頭。</summary>
    private sealed class RawAuthorizationFilter : IStartupFilter
    {
        public const string Header = "X-Test-Raw-Authorization";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(Header, out var raw))
                {
                    context.Request.Headers.Authorization = raw;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
