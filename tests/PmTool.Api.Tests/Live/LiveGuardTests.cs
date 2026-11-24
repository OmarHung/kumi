using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;
using PmTool.Identity;

namespace PmTool.Api.Tests.Live;

/// <summary>即時連線端點在進 hub 之前的檢查：登入、存取權杖、防偽標頭、來源、名額。</summary>
[TestFixture]
public sealed class LiveGuardTests
{
    private const string Negotiate = LiveRegistration.Route + "/negotiate?negotiateVersion=1";

    internal static ApiFactory NewFactory() => new(
        FakeBlueprintValidator.Passing(),
        new ApiFactoryOptions { Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true" } });

    [Test]
    public async Task Negotiate_NotSignedIn_Returns401()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = factory.CreateApiClient();

        // Act
        using var response = await client.PostAsync(Negotiate, content: null);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Unauthorized, ApiErrorCodes.AuthRequired);
    }

    [Test]
    public async Task Negotiate_SignedInWithoutTheCsrfHeader_Returns403()
    {
        // Arrange
        using var factory = NewFactory();
        using var signedIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signedIn, TestUsers.Member);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Cookie", cookie);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.CsrfRequired);
    }

    [Test]
    public async Task Negotiate_SignedInWithTheCookie_Succeeds()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await SignedInAsync(factory);

        // Act
        using var response = await client.PostAsync(Negotiate, content: null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Negotiate_WithAnAccessToken_Returns403BecauseTheEndpointDeclaresNoScope()
    {
        // Arrange：權杖有所有的範圍也一樣，這個端點只給網頁（Cookie）。
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(
            TestUsers.Owner, AccessTokenScope.ItemsRead, AccessTokenScope.ItemsWrite, AccessTokenScope.BlueprintRead, AccessTokenScope.BlueprintWrite);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [Test]
    public async Task Connect_WithAnAccessToken_IsRefusedToo()
    {
        // Arrange
        using var factory = NewFactory();
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Owner, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, LiveRegistration.Route + "?id=whatever");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.Forbidden, ApiErrorCodes.InsufficientScope);
    }

    [TestCase("http://localhost", HttpStatusCode.OK, TestName = "Negotiate_OriginIsTheSameHost_Succeeds")]
    [TestCase("http://LOCALHOST", HttpStatusCode.OK, TestName = "Negotiate_OriginHostDiffersOnlyInCase_Succeeds")]
    [TestCase("https://evil.example.com", HttpStatusCode.Forbidden, TestName = "Negotiate_OriginIsAnotherSite_Returns403")]
    [TestCase("http://localhost.evil.example.com", HttpStatusCode.Forbidden, TestName = "Negotiate_OriginIsASubdomainLookalike_Returns403")]
    [TestCase("null", HttpStatusCode.Forbidden, TestName = "Negotiate_OriginIsNull_Returns403")]
    public async Task Negotiate_Origin(string origin, HttpStatusCode expected)
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await SignedInAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Origin", origin);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Negotiate_OriginWithAnotherPortWhileHostNamesOne_Returns403()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await SignedInAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Host = "pm.example.com:8080";
        request.Headers.Add("Origin", "http://pm.example.com:9090");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Negotiate_OriginWithThePortOfTheHost_Succeeds()
    {
        // Arrange
        using var factory = NewFactory();
        using var client = await SignedInAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Host = "pm.example.com:8080";
        request.Headers.Add("Origin", "http://pm.example.com:8080");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Negotiate_OriginWithoutAPortWhileTheHostHidesIt_Succeeds()
    {
        // Arrange：反向代理常把埠拿掉再轉進來，不能因此把正常的網頁擋掉。
        using var factory = NewFactory();
        using var client = await SignedInAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Host = "pm.example.com";
        request.Headers.Add("Origin", "https://pm.example.com");

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
    }

    [TestCase("pm.example.com", "https://pm.example.com", HttpStatusCode.OK, TestName = "Negotiate_PublicHostSet_OriginIsThePublicHost_Succeeds")]
    [TestCase("pm.example.com", "https://PM.example.com", HttpStatusCode.OK, TestName = "Negotiate_PublicHostSet_OriginDiffersOnlyInCase_Succeeds")]
    [TestCase("pm.example.com", "http://localhost", HttpStatusCode.Forbidden, TestName = "Negotiate_PublicHostSet_OriginIsTheRequestHostInstead_Returns403")]
    [TestCase("pm.example.com", "https://pm.example.com:8443", HttpStatusCode.Forbidden, TestName = "Negotiate_PublicHostWithoutAPort_OriginWithAnotherPort_Returns403")]
    [TestCase("pm.example.com:8443", "https://pm.example.com:8443", HttpStatusCode.OK, TestName = "Negotiate_PublicHostWithAPort_OriginWithThatPort_Succeeds")]
    [TestCase("pm.example.com:8443", "https://pm.example.com", HttpStatusCode.Forbidden, TestName = "Negotiate_PublicHostWithAPort_OriginWithoutIt_Returns403")]
    public async Task Negotiate_PublicHostSet_OnlyThatHostIsTrusted(string publicHost, string origin, HttpStatusCode expected)
    {
        // Arrange：Host 標頭是用戶端自己填的，設了 PublicHost 就不拿它比。
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true", ["Security:PublicHost"] = publicHost },
            });
        using var client = await SignedInAsync(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Add("Origin", origin);

        // Act
        using var response = await client.SendAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expected), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Negotiate_PublicHostSet_NoOrigin_Succeeds()
    {
        // Arrange
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions
            {
                Settings = new Dictionary<string, string?> { [LiveRegistration.EnabledSetting] = "true", ["Security:PublicHost"] = "pm.example.com" },
            });
        using var client = await SignedInAsync(factory);

        // Act
        using var response = await client.PostAsync(Negotiate, content: null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Connect_MoreThanTheLimitPerPerson_RefusesTheNextOneAtNegotiate()
    {
        // Arrange
        using var factory = NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var clients = new List<LiveHubClient>();
        try
        {
            for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
            {
                var client = LiveHubClient.Create(factory.Server, cookie);
                clients.Add(client);
                await client.StartAsync();
            }

            using var http = factory.CreateApiClient();
            using var request = Sessions.WithCookie(HttpMethod.Post, Negotiate, cookie);

            // Act
            using var response = await http.SendAsync(request);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
            Assert.That(response.Headers.RetryAfter?.Delta, Is.EqualTo(TimeSpan.FromSeconds(1)), "名額滿的 429 要帶 Retry-After");
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task Connect_AnotherPersonIsNotAffectedByTheLimitOfThisOne()
    {
        // Arrange
        using var factory = NewFactory();
        using var memberSignIn = factory.CreateApiClient();
        using var ownerSignIn = factory.CreateApiClient();
        var memberCookie = await Sessions.SignInForCookieAsync(memberSignIn, TestUsers.Member);
        var ownerCookie = await Sessions.SignInForCookieAsync(ownerSignIn, TestUsers.Owner);
        var clients = new List<LiveHubClient>();
        try
        {
            for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
            {
                var client = LiveHubClient.Create(factory.Server, memberCookie);
                clients.Add(client);
                await client.StartAsync();
            }

            // Act
            var other = LiveHubClient.Create(factory.Server, ownerCookie);
            clients.Add(other);
            await other.StartAsync();

            // Assert
            Assert.That(factory.Services.GetRequiredService<LiveConnections>().CountOf(TestUsers.Owner.PersonId), Is.EqualTo(1));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task OpenConnections_DoNotUseUpThePersonsConcurrentRequests()
    {
        // Arrange：每人同時最多 16 個請求；開滿 20 條長輪詢（每條都有一個請求一直在進行中）之後，一般請求要照常回應。
        using var factory = NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var clients = new List<LiveHubClient>();
        try
        {
            for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
            {
                var client = LiveHubClient.Create(factory.Server, cookie);
                clients.Add(client);
                await client.StartAsync();
            }

            // Act：同時 12 個（在一般請求的上限之內）一般請求，加上長輪詢占著的 20 個。
            using var http = factory.CreateApiClient();
            var statuses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Sessions.SendWithCookieAsync(http, "/v1/session", cookie)));

            // Assert
            Assert.That(statuses, Has.All.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task OpenWebSocketConnections_DoNotUseUpThePersonsConcurrentRequests()
    {
        // Arrange
        using var factory = NewFactory();
        using var signIn = factory.CreateApiClient();
        var cookie = await Sessions.SignInForCookieAsync(signIn, TestUsers.Member);
        var clients = new List<LiveHubClient>();
        try
        {
            for (var i = 0; i < LiveConnections.MaxPerPerson; i++)
            {
                var client = LiveHubClient.Create(factory.Server, cookie, HttpTransportType.WebSockets);
                clients.Add(client);
                await client.StartAsync();
            }

            // Act
            using var http = factory.CreateApiClient();
            var statuses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Sessions.SendWithCookieAsync(http, "/v1/session", cookie)));

            // Assert
            Assert.That(statuses, Has.All.EqualTo(HttpStatusCode.OK));
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.DisposeAsync();
            }
        }
    }

    private static async Task<HttpClient> SignedInAsync(ApiFactory factory)
    {
        var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        return client;
    }
}
