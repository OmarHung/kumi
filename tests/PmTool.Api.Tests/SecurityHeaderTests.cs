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

    private const string ExpectedPermissionsPolicy =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), serial=(), hid=(), bluetooth=(), browsing-topics=()";

    [TestCaseSource(nameof(Routes))]
    public async Task Response_AnyKind_RestrictsBrowserFeaturesAndIsolatesTheWindow(string route)
    {
        // Arrange
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert：不限制 clipboard，網頁的「複製指令」要用。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Headers.GetValues("Permissions-Policy"), Is.EqualTo(new[] { ExpectedPermissionsPolicy }));
            Assert.That(response.Headers.GetValues("Cross-Origin-Opener-Policy"), Is.EqualTo(new[] { "same-origin" }));
        }
    }

    [Test]
    public async Task Response_ServerError_StillCarriesPermissionsPolicyAndOpenerPolicy()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Throwing(new InvalidOperationException("boom")));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(response.Headers.Contains("Permissions-Policy"), Is.True);
            Assert.That(response.Headers.Contains("Cross-Origin-Opener-Policy"), Is.True);
        }
    }

    [TestCase("/v1/session")]
    [TestCase("/v1/no-such-route")]
    [TestCase("/mcp")]
    public async Task Response_UnderV1OrMcp_WithoutItsOwnCacheControl_IsNoStore(string route)
    {
        // Arrange：API 的回應帶著個人的資料，中間的快取不該存。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task Response_SignedInApiCall_IsNoStore()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync("/v1/projects");

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("no-store"));
    }

    [Test]
    public async Task Response_ServerErrorUnderV1_IsNoStore()
    {
        // Arrange：錯誤回應是例外處理重寫的，也要帶。
        using var factory = new ApiFactory(FakeBlueprintValidator.Throwing(new InvalidOperationException("boom")));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert：例外處理自己寫的是 no-cache, no-store，只要有 no-store 就好。
        Assert.That(NonValidated(response, "Cache-Control"), Does.Contain("no-store"));
    }

    [Test]
    public async Task Response_EndpointThatWritesItsOwnCacheControl_KeepsIt()
    {
        // Arrange：中繼資料端點自己決定快取（沒設 PublicHost 時是 no-store，設了是公開五分鐘）。
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Security:PublicHost"] = "pm.example.com" } });
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/.well-known/oauth-authorization-server");

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("public, max-age=300"));
    }

    [Test]
    public async Task Response_AttachmentDownload_KeepsItsPrivateCacheControl()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Attachments.OnOpen = (_, _) => (FakeAttachmentService.Sample(contentType: "text/html", size: 11), "hello world"u8.ToArray());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.GetAsync($"/v1/attachments/{FakeAttachmentService.SampleId}/content");

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("private, max-age=0"));
    }

    [Test]
    public async Task Response_KumiDownload_KeepsItsPrivateCacheControl()
    {
        // Arrange
        var packages = ApiFactory.CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(packages, "kumi-0.3.0-macos-arm64"), "binary");
            using var factory = new ApiFactory(
                FakeBlueprintValidator.Passing(),
                new ApiFactoryOptions { Settings = new Dictionary<string, string?> { ["Cli:PackagesPath"] = packages } });
            using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

            // Act
            using var response = await client.GetAsync("/v1/cli/download/macos-arm64");

            // Assert
            Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("private, max-age=0"));
        }
        finally
        {
            Directory.Delete(packages, recursive: true);
        }
    }

    [Test]
    public async Task Response_EventStream_KeepsNoCache()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory, TestUsers.Member);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{AssistantEndpointTests.Item}/messages")
        {
            Content = ApiRequests.Json(new
            {
                text = "你好",
                selection = new { connectionId = FakeAssistantService.ConnectionId, model = "claude-opus-5-5", effort = "high" },
            }),
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Act
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("no-cache"));
    }

    [Test]
    public async Task Response_WebPage_IsNotMadeNoStore()
    {
        // Arrange：只管 /v1 與 /mcp：網頁的靜態檔照舊自己決定怎麼快取。
        using var factory = WebHostingTests.CreateFactoryWithWebFiles();
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/");

        // Assert
        Assert.That(NonValidated(response, "Cache-Control"), Is.EqualTo("no-cache"));
    }

    private static string NonValidated(HttpResponseMessage response, string name) =>
        // 不經過 HttpClient 的解析：它會把 Cache-Control 的內容重排，測試要看伺服器實際送的字。
        response.Headers.NonValidated.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;

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
