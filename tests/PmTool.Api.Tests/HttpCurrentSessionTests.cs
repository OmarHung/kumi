using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Abstractions;
using PmTool.Api.Security;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>身分模組問「發出這個要求的工作階段是哪一個」：Cookie 的票證有值，用存取權杖或沒有請求時是 null。</summary>
[TestFixture]
public sealed class HttpCurrentSessionTests
{
    private const string SessionTokenClaim = "pmtool:session_token";
    private const string AccessTokenIdClaim = "pmtool:access_token_id";

    [Test]
    public void Token_NoRequest_IsNull()
    {
        // Arrange
        var session = new HttpCurrentSession(new HttpContextAccessor());

        // Act & Assert
        Assert.That(session.Token, Is.Null);
    }

    [Test]
    public void Token_CookieTicketWithASessionToken_IsThatToken()
    {
        // Arrange
        var session = SessionFor(new ClaimsPrincipal(new ClaimsIdentity([new Claim(SessionTokenClaim, "example-session-token")], "Cookies")));

        // Act & Assert
        Assert.That(session.Token, Is.EqualTo("example-session-token"));
    }

    [Test]
    public void Token_CalledWithAnAccessToken_IsNull()
    {
        // Arrange：存取權杖驗證的身分沒有工作階段的權杖
        var session = SessionFor(new ClaimsPrincipal(new ClaimsIdentity([new Claim(AccessTokenIdClaim, Guid.NewGuid().ToString())], "AccessToken")));

        // Act & Assert
        Assert.That(session.Token, Is.Null);
    }

    [Test]
    public void Token_NotSignedIn_IsNull()
    {
        // Arrange
        var session = SessionFor(new ClaimsPrincipal(new ClaimsIdentity()));

        // Act & Assert
        Assert.That(session.Token, Is.Null);
    }

    [Test]
    public void Services_CurrentSessionIsTheHttpOne()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());

        // Act
        var resolved = factory.Services.GetRequiredService<ICurrentSession>();

        // Assert
        Assert.That(resolved, Is.TypeOf<HttpCurrentSession>());
    }

    private static HttpCurrentSession SessionFor(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } });
}
