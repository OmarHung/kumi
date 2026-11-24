using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>網頁的靜態檔與單頁應用的後備路由。web root 用每個測試自己的暫存目錄。</summary>
[TestFixture]
public sealed class WebHostingTests
{
    private const string IndexHtml = "<!doctype html><title>pm-tool</title><div id=\"root\"></div>";
    internal const string AssetPath = "assets/index-3f9a2c1b.js";
    private const string AssetContent = "console.log('pm-tool');";

    [TestCase("/")]
    [TestCase("/projects/WEB")]
    [TestCase("/my-work")]
    [TestCase("/login?next=%2Fprojects")]
    public async Task ClientSideRoute_Get_ReturnsIndexHtmlThatIsNotCached(string route)
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(IndexHtml));
            Assert.That(response.Headers.CacheControl?.NoCache, Is.True, "改版之後要拿到新的 index.html");
        }
    }

    [Test]
    public async Task IndexHtml_RequestedDirectly_IsNotCachedEither()
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/index.html");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.CacheControl?.NoCache, Is.True);
    }

    [Test]
    public async Task HashedAsset_Get_IsServedWithLongCaching()
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/" + AssetPath);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo(AssetContent));
            Assert.That(response.Headers.CacheControl?.MaxAge, Is.EqualTo(TimeSpan.FromDays(365)));
            Assert.That(response.Headers.CacheControl?.Extensions.Select(e => e.Name), Does.Contain("immutable"));
        }
    }

    [Test]
    public async Task StaticFiles_MoreRequestsThanTheRateLimit_AreNeverRejected()
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow + 10; i++)
        {
            using var response = await client.GetAsync("/" + AssetPath);
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ClientSideRoute_MoreRequestsThanTheRateLimit_AreNeverRejected()
    {
        // Arrange：本機用 Docker 時所有瀏覽器共用同一個來源 IP，網頁本身不能吃匿名的額度。
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow + 10; i++)
        {
            using var response = await client.GetAsync("/");
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignInPage_AfterTheAnonymousBudgetIsUsedUp_IsStillServed()
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        }

        // Act
        using var rejected = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        using var signInPage = await client.GetAsync("/login");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(signInPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await signInPage.Content.ReadAsStringAsync(), Is.EqualTo(IndexHtml));
        }
    }

    [Test]
    public async Task UnknownApiRoute_AfterTheAnonymousBudgetIsUsedUp_IsStillRateLimited()
    {
        // Arrange：後備路由移到限流之前，/v1/ 底下的路徑仍然要算進額度，也不能拿到網頁。
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await client.GetAsync("/v1/no-such-route");
        }

        // Act
        using var response = await client.GetAsync("/v1/no-such-route");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    [TestCase("/v1/no-such-route")]
    [TestCase("/v1")]
    [TestCase("/health/no-such-route")]
    [TestCase("/openapi/v1.json")]
    [TestCase("/assets/missing-0000.js")]
    [TestCase("/favicon.ico")]
    public async Task ReservedOrFileLikePath_NotFound_Returns404EnvelopeInsteadOfIndexHtml(string route)
    {
        // Arrange：缺的 JS 回 HTML 的話，瀏覽器只會報看不懂的 MIME 錯誤。
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task ClientSideRoute_Post_Returns404Envelope()
    {
        // Arrange
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync("/projects/WEB", ApiRequests.Json(new { }));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    [Test]
    public async Task ApiRouteWithWrongMethod_WithWebFiles_StillReturns405()
    {
        // Arrange：後備路由不能把 /v1/ 底下的錯誤吃掉。
        using var factory = CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(ApiRequests.BlueprintRoute);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.MethodNotAllowed, ApiErrorCodes.MethodNotAllowed);
    }

    [Test]
    public async Task ClientSideRoute_WithoutIndexHtml_Returns404Envelope()
    {
        // Arrange：網頁還沒建置（例如只跑 API 的開發環境）。不能丟例外。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/projects/WEB");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.NotFound, ApiErrorCodes.NotFound);
    }

    internal static ApiFactory CreateFactoryWithWebFiles()
    {
        var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        File.WriteAllText(Path.Combine(factory.WebRoot, "index.html"), IndexHtml);
        Directory.CreateDirectory(Path.Combine(factory.WebRoot, "assets"));
        File.WriteAllText(Path.Combine(factory.WebRoot, AssetPath), AssetContent);
        return factory;
    }
}
