using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class SecurityHeaderTests
{
    private static readonly string[] Routes = ["/", "/assets/index-3f9a2c1b.js", "/health", "/v1/session", "/v1/no-such-route"];

    [TestCaseSource(nameof(Routes))]
    public async Task Response_AnyKind_CarriesTheSecurityHeaders(string route)
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        AssertSecurityHeaders(response);
    }

    [Test]
    public async Task Response_ServerError_StillCarriesTheSecurityHeaders()
    {
        // Arrange：例外處理會清掉回應的標頭，安全標頭要在那之後還在。
        using var factory = new ApiFactory(FakeBlueprintValidator.Throwing(new InvalidOperationException("boom")));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        AssertSecurityHeaders(response);
    }

    [Test]
    public async Task ContentSecurityPolicy_AllowsOnlyOwnScriptsAndForbidsFraming()
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/");
        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        var directives = policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(directives, Does.Contain("default-src 'self'"));
            Assert.That(directives, Does.Contain("frame-ancestors 'none'"));
            Assert.That(directives, Does.Contain("base-uri 'self'"));
            Assert.That(directives, Does.Contain("form-action 'self'"));
            Assert.That(policy, Does.Not.Contain("unsafe-inline"));
            Assert.That(policy, Does.Not.Contain("unsafe-eval"));
        }
    }

    [Test]
    public async Task ContentSecurityPolicy_AllowsDataImages_ForTheUnreadTabIcon()
    {
        // Arrange：有未讀時分頁的圖示是網頁現組的 SVG（data: 網址，web/src/features/inbox/faviconBadge.ts），拿掉 data: 就不會換。
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/");
        var policy = string.Join(";", response.Headers.GetValues("Content-Security-Policy"));
        var directives = policy.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        // Assert
        Assert.That(directives, Does.Contain("img-src 'self' data:"));
    }

    private static void AssertSecurityHeaders(HttpResponseMessage response)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
            Assert.That(response.Headers.GetValues("Referrer-Policy"), Is.EqualTo(new[] { "no-referrer" }));
            Assert.That(response.Headers.GetValues("X-Frame-Options"), Is.EqualTo(new[] { "DENY" }));
            Assert.That(response.Headers.Contains("Content-Security-Policy"), Is.True);
        }
    }
}
