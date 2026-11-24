using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 放在反向代理後面時讀 X-Forwarded-For 與 X-Forwarded-Proto：只信任設定的代理、只看最後一層。
/// 連線的來源 IP 用 ApiFactory 的測試標頭指定，代表直接連進來的那一方（代理，或冒充代理的人）。
/// </summary>
[TestFixture]
public sealed class ForwardedHeadersTests
{
    private const string Proxy = "192.0.2.1";
    private const string Stranger = "192.0.2.99";
    private const string ClientA = "198.51.100.10";
    private const string ClientB = "198.51.100.20";

    private static readonly Dictionary<string, string?> TrustedProxy = new()
    {
        ["ForwardedHeaders:Enabled"] = "true",
        ["ForwardedHeaders:KnownProxies:0"] = Proxy,
    };

    [Test]
    public async Task RateLimit_ThroughATrustedProxy_CountsEachRealClientSeparately()
    {
        // Arrange：所有請求都從同一台代理進來。不讀轉送的標頭的話，全站共用一份額度。
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Proxy, ClientA);

        // Act
        using var sameClient = await SendAsync(client, Proxy, ClientA);
        using var otherClient = await SendAsync(client, Proxy, ClientB);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameClient.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(otherClient.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task RateLimit_ForwardedForFromAnUntrustedSource_IsIgnored()
    {
        // Arrange：直接連進來的人自己帶 X-Forwarded-For，每次換一個位址，想要新的額度。
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Stranger, ClientA);

        // Act
        using var response = await SendAsync(client, Stranger, ClientB);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task RateLimit_ClientPrependsAFakeAddress_OnlyTheLastHopCounts()
    {
        // Arrange：用戶端自己帶了 X-Forwarded-For，代理在後面加上真的位址；只有最後一個是代理加的。
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Proxy, ClientA);

        // Act
        using var response = await SendAsync(client, Proxy, $"{ClientB}, {ClientA}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [TestCase("127.0.0.1", TestName = "RateLimit_ForwardedForFromIpv4Loopback_IsIgnoredUnlessConfigured")]
    [TestCase("::1", TestName = "RateLimit_ForwardedForFromIpv6Loopback_IsIgnoredUnlessConfigured")]
    public async Task RateLimit_ForwardedForFromLoopback_IsIgnoredUnlessConfigured(string loopback)
    {
        // Arrange：內建的預設會信任本機位址；只信任設定的代理，同一台機器上的別的程式不能冒充。
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, loopback, ClientA);

        // Act
        using var response = await SendAsync(client, loopback, ClientB);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task RateLimit_ProxyInsideATrustedNetwork_IsTrusted()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, "10.1.2.3", ClientA);

        // Act
        using var otherClient = await SendAsync(client, "10.200.0.1", ClientB);

        // Assert
        Assert.That(otherClient.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task RateLimit_TrustAllProxies_ReadsTheForwardedForFromAnySource()
    {
        // Arrange：Cloud Run 這類環境，連得進來的只有平台的前端，而它的位址不固定。
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAllProxies"] = "true",
        };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Stranger, ClientA);

        // Act
        using var otherClient = await SendAsync(client, Stranger, ClientB);

        // Assert
        Assert.That(otherClient.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase("1", HttpStatusCode.OK, TestName = "RateLimit_ForwardLimitOne_ReadsOnlyTheLastHop")]
    [TestCase("2", HttpStatusCode.TooManyRequests, TestName = "RateLimit_ForwardLimitTwo_ReadsTheSecondToLastHop")]
    public async Task RateLimit_ForwardLimit_DecidesHowManyProxiesAreSkipped(string limit, HttpStatusCode expected)
    {
        // Arrange：用戶端 → CDN → nginx → api。nginx 把 CDN 的位址加在後面，api 看到「用戶端, CDN」；
        // 用戶端自己還可以在最前面塞一個假的。要取的是倒數第二個（用戶端）。
        var settings = new Dictionary<string, string?>
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:TrustAllProxies"] = "true",
            ["ForwardedHeaders:ForwardLimit"] = limit,
        };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Proxy, ClientA);

        // Act
        using var response = await SendAsync(client, Proxy, $"{ClientB}, {ClientA}, 203.0.113.7");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public async Task RateLimit_NotEnabled_IgnoresTheForwardedForEvenFromAConfiguredProxy()
    {
        // Arrange：預設不讀，本機的行為不變。
        var settings = new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = Proxy };
        using var factory = CreateFactory(settings);
        using var client = factory.CreateClient();
        await UseUpTheBudgetAsync(client, Proxy, ClientA);

        // Act
        using var response = await SendAsync(client, Proxy, ClientB);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task SignIn_ForwardedProtoHttpsFromATrustedProxy_SetsASecureCookie()
    {
        // Arrange：代理和 API 之間是 http，用戶端和代理之間是 https。
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateApiClient();

        // Act
        using var response = await SignInAsync(client, Proxy, proto: "https");

        // Assert
        Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Contain("; secure"));
    }

    [Test]
    public async Task SignIn_ForwardedProtoHttpsFromAnUntrustedSource_IsIgnored()
    {
        // Arrange
        using var factory = CreateFactory(TrustedProxy);
        using var client = factory.CreateApiClient();

        // Act
        using var response = await SignInAsync(client, Stranger, proto: "https");

        // Assert
        Assert.That(Sessions.SetCookieLineOf(response)?.ToLowerInvariant(), Does.Not.Contain("secure"));
    }

    private static ApiFactory CreateFactory(IReadOnlyDictionary<string, string?> settings) =>
        new(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true, Settings = settings });

    private static async Task UseUpTheBudgetAsync(HttpClient client, string remoteIp, string forwardedFor)
    {
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var response = await SendAsync(client, remoteIp, forwardedFor);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "還沒用完額度就被擋了");
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string remoteIp, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiRequests.BlueprintRoute) { Content = ApiRequests.Document("{}") };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string remoteIp, string proto)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Sessions.Route)
        {
            Content = Sessions.Credentials(TestUsers.Member.Email, TestUsers.Password),
        };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        request.Headers.Add("X-Forwarded-For", ClientA);
        request.Headers.Add("X-Forwarded-Proto", proto);
        return client.SendAsync(request);
    }
}
