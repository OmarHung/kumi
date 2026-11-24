using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// Security:RequireHttps：Cookie 一律 Secure、每個回應帶 HSTS、不是 HTTPS 的請求 308 轉到 https（/health 開頭的不轉）。
/// 預設關著，本機的 http://localhost 照舊。
/// </summary>
[TestFixture]
public sealed class HttpsRequirementTests
{
    private const string Hsts = "max-age=31536000";

    private static readonly Dictionary<string, string?> RequireHttps = new() { ["Security:RequireHttps"] = "true" };

    [TestCase("GET", "/v1/session?from=example")]
    [TestCase("POST", "/v1/session?from=example")]
    [TestCase("GET", "/projects/WEB")]
    [TestCase("POST", "/v1/host-agent/check-in")]
    [TestCase("POST", "/v1/host-agent/pair")]
    public async Task Request_OverHttp_IsRedirectedTo308WithTheSameAddressOverHttps(string method, string route)
    {
        // Arrange：308 保留方法與內文，POST 轉過去還是 POST。
        using var factory = CreateFactory(RequireHttps);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), route));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PermanentRedirect));
            Assert.That(response.Headers.Location, Is.EqualTo(new Uri($"https://localhost{route}")));
            Assert.That(response.Headers.GetValues("Strict-Transport-Security"), Is.EqualTo(new[] { Hsts }));
            Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
        }
    }

    [Test]
    public async Task Request_OverHttpOnAnotherPort_IsRedirectedToTheDefaultHttpsPort()
    {
        // Arrange：代理後面的 http 連接埠不是 https 的連接埠。
        using var factory = CreateFactory(RequireHttps);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("http://example.com:8080"),
        });

        // Act
        using var response = await client.GetAsync("/v1/session");

        // Assert
        Assert.That(response.Headers.Location, Is.EqualTo(new Uri("https://example.com/v1/session")));
    }

    [TestCase("pm.example.com", "https://pm.example.com/v1/session?from=example")]
    [TestCase("pm.example.com:8443", "https://pm.example.com:8443/v1/session?from=example")]
    public async Task Request_WithAPublicHost_IsRedirectedToItWhateverTheHostHeaderSays(string publicHost, string expected)
    {
        // Arrange：假的 Host 標頭換來的 308 若被前面的快取存起來，別人會被轉去別的網站；設了 PublicHost 就不看它。
        var settings = new Dictionary<string, string?>(RequireHttps) { ["Security:PublicHost"] = publicHost };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/session?from=example");
        request.Headers.Host = "evil.example";

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PermanentRedirect));
            Assert.That(response.Headers.Location, Is.EqualTo(new Uri(expected)));
        }
    }

    [Test]
    public async Task Request_WithoutAPublicHost_IsRedirectedToTheRequestHost()
    {
        // Arrange：沒設時照舊用請求的 Host。
        using var factory = CreateFactory(RequireHttps);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/session");
        request.Headers.Host = "pm.example.org";

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.Headers.Location, Is.EqualTo(new Uri("https://pm.example.org/v1/session")));
    }

    [TestCase("/health")]
    [TestCase("/health/ready")]
    public async Task HealthCheck_OverHttp_IsNotRedirected(string route)
    {
        // Arrange：平台的健康檢查常走內部的 http。
        using var factory = CreateFactory(RequireHttps);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        using var response = await client.GetAsync(route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Request_OverHttps_IsServedWithHsts()
    {
        // Arrange
        using var factory = CreateFactory(RequireHttps);
        using var client = CreateHttpsClient(factory);

        // Act
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("Strict-Transport-Security"), Is.EqualTo(new[] { Hsts }));
            Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Contain("; secure"));
        }
    }

    [Test]
    public void Cookie_RequireHttps_IsAlwaysSecure()
    {
        // Arrange：不跟著請求走：就算有請求沒被轉走（例如代理忘了帶 X-Forwarded-Proto），Cookie 也不會以明文送出。
        using var factory = CreateFactory(RequireHttps);

        // Act
        var cookie = CookieOptionsOf(factory);

        // Assert
        Assert.That(cookie.Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.Always));
    }

    [Test]
    public async Task Default_NoHstsAndNoRedirectAndCookieFollowsTheRequest()
    {
        // Arrange：本機的 http://localhost:8080 要能照舊登入。
        using var factory = CreateFactory(new Dictionary<string, string?>());
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(Sessions.Route, Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.False);
            Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Not.Contain("secure"));
            Assert.That(CookieOptionsOf(factory).Cookie.SecurePolicy, Is.EqualTo(CookieSecurePolicy.SameAsRequest));
        }
    }

    [Test]
    public async Task Request_BehindAPlatformFrontEndThatTerminatesHttps_IsServedNotRedirected()
    {
        // Arrange：Cloud Run 的設定。平台的前端用 http 連進來，帶著 X-Forwarded-Proto: https。
        var settings = new Dictionary<string, string?>(RequireHttps)
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAllProxies"] = "true",
        };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateApiClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Sessions.Route)
        {
            Content = Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password),
        };
        request.Headers.Add(ApiFactory.RemoteIpHeader, "10.0.0.5");
        request.Headers.Add("X-Forwarded-For", "198.51.100.10");
        request.Headers.Add("X-Forwarded-Proto", "https");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Headers.GetValues("Strict-Transport-Security"), Is.EqualTo(new[] { Hsts }));
            Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Contain("; secure"));
        }
    }

    [Test]
    public async Task Request_BehindAProxyWithoutForwardedProto_IsRedirected()
    {
        // Arrange：代理沒帶 X-Forwarded-Proto 的話，看起來就是 http；轉走，而不是在 http 上發 Cookie。
        var settings = new Dictionary<string, string?>(RequireHttps)
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAllProxies"] = "true",
        };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, Sessions.Route);
        request.Headers.Add(ApiFactory.RemoteIpHeader, "10.0.0.5");
        request.Headers.Add("X-Forwarded-For", "198.51.100.10");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PermanentRedirect));
    }

    private static ApiFactory CreateFactory(IReadOnlyDictionary<string, string?> settings) =>
        new(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true, Settings = settings });

    private static HttpClient CreateHttpsClient(ApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Add(ApiRequests.CsrfHeader, "1");
        return client;
    }

    private static CookieAuthenticationOptions CookieOptionsOf(ApiFactory factory) =>
        factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
}
