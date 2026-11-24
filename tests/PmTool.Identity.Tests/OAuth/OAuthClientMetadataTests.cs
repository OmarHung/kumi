using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PmTool.Identity.OAuth;
using static PmTool.Identity.Tests.OAuth.OAuthTestSupport;

namespace PmTool.Identity.Tests.OAuth;

/// <summary>Client ID Metadata Document 的抓取、驗證與快取（<c>internal/HANDOFF-mcp.md</c> 4.4）。用假的 HTTP handler，不連網路。</summary>
[TestFixture]
public sealed class OAuthClientMetadataTests
{
    private const string ClientUrl = "https://app.example.com/oauth/client.json";
    private const string RedirectUri = "https://app.example.com/callback";

    private readonly ManualTimeProvider _time = new();
    private readonly string _challenge = NewPkce().Challenge;

    private static string Document(string? clientId = ClientUrl, string? name = "範例應用", string redirects = $"[\"{RedirectUri}\"]", string? method = null) =>
        "{" + string.Join(
            ",",
            new[]
            {
                clientId is null ? null : $"\"client_id\":\"{clientId}\"",
                name is null ? null : $"\"client_name\":\"{name}\"",
                $"\"redirect_uris\":{redirects}",
                method is null ? null : $"\"token_endpoint_auth_method\":\"{method}\"",
            }.OfType<string>()) + "}";

    private async Task<OAuthResult<OAuthAuthorizationRequest>> CheckAsync(IdentityTestHost host, string clientId = ClientUrl, string redirect = RedirectUri) =>
        await host.OAuth.CheckAuthorizationAsync(Server, Parameters(clientId, _challenge, redirect), CancellationToken.None);

    [Test]
    public async Task Check_ValidDocument_NamesTheClientAndItsDomain()
    {
        // Arrange
        var handler = FakeMetadataHandler.Json(Document());
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host);

        // Assert
        var request = result.Value;
        Assert.That(request.ClientId, Is.EqualTo(ClientUrl));
        Assert.That(request.ClientName, Is.EqualTo("範例應用"));
        Assert.That(request.ClientHost, Is.EqualTo("app.example.com"));
        Assert.That(request.RedirectHost, Is.EqualTo("app.example.com"));
        Assert.That(request.RedirectIsLoopback, Is.False);
    }

    [Test]
    public async Task Check_LookalikeUnicodeDomain_IsShownAsPunycodeOnTheConsentAndInTheGrantList()
    {
        // Arrange：app 的第一個字母是西里爾字母的 а（U+0430），看起來和 app.example.com 一模一樣。
        const string lookalikeClient = "https://\u0430pp.example.com/oauth/client.json";
        const string lookalikeRedirect = "https://\u0430pp.example.com/callback";
        var handler = FakeMetadataHandler.Json(Document(lookalikeClient, redirects: $"[\"{lookalikeRedirect}\"]"));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        var owner = await host.StartWithOwnerAsync();

        // Act
        var request = (await CheckAsync(host, lookalikeClient, lookalikeRedirect)).Value;
        var (code, _) = await host.ApproveAsync(owner, lookalikeClient, lookalikeRedirect);
        var grant = (await host.OAuth.ListGrantsAsync(owner.ToActor(), CancellationToken.None)).Single();

        // Assert
        Assert.That(request.ClientHost, Is.EqualTo("xn--pp-6kc.example.com"));
        Assert.That(request.RedirectHost, Is.EqualTo("xn--pp-6kc.example.com"));
        Assert.That(grant.ClientHost, Is.EqualTo("xn--pp-6kc.example.com"));
        Assert.That(code, Is.Not.Empty);
    }

    [Test]
    public async Task Check_SuccessfulFetch_PurgesCacheRowsExpiredForOverADayUnlessAGrantUsesThem()
    {
        // Arrange：三份文件各抓一次；其中一個有有效的授權。
        string UrlOf(string name) => $"https://{name}.example.com/client.json";
        var handler = new FakeMetadataHandler((request, _) =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            var host = request.RequestUri.Host;
            return Task.FromResult(FakeMetadataHandler.JsonResponse(Document(url, redirects: $"[\"https://{host}/cb\"]")));
        });
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        var owner = await host.StartWithOwnerAsync();
        await CheckAsync(host, UrlOf("stale"), "https://stale.example.com/cb");
        await CheckAsync(host, UrlOf("inuse"), "https://inuse.example.com/cb");
        await host.ApproveAsync(owner, UrlOf("inuse"), "https://inuse.example.com/cb");

        // Act：快取最長 1 天，再過 1 天以上才算「過期超過一天」；這時抓一份新的。
        _time.Advance(OAuthRules.MetadataCacheMax + OAuthRules.MetadataCacheMax + TimeSpan.FromMinutes(1));
        await CheckAsync(host, UrlOf("fresh"), "https://fresh.example.com/cb");

        // Assert
        await using var db = await host.CreateDbContextAsync();
        var remaining = await db.OAuthClients.Select(c => c.ClientId).ToListAsync();
        Assert.That(remaining, Does.Not.Contain(UrlOf("stale")));
        Assert.That(remaining, Does.Contain(UrlOf("inuse")).And.Contain(UrlOf("fresh")));
    }

    [Test]
    public async Task Check_AsksForJsonWithAGetAndTheClientIdUrl()
    {
        // Arrange
        HttpRequestMessage? seen = null;
        var handler = new FakeMetadataHandler((request, _) =>
        {
            seen = request;
            return Task.FromResult(FakeMetadataHandler.JsonResponse(Document()));
        });
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        await CheckAsync(host);

        // Assert
        Assert.That(seen?.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(seen?.RequestUri?.AbsoluteUri, Is.EqualTo(ClientUrl));
        Assert.That(seen?.Headers.Accept.ToString(), Does.Contain("application/json"));
    }

    [Test]
    public async Task Check_RedirectNotInTheDocument_IsInvalidRequest()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(Document()));

        // Act
        var result = await CheckAsync(host, redirect: "https://app.example.com/other");

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidRequest));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [Test]
    public async Task Approve_WithADocumentClient_RecordsTheDomainOnTheGrant()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(Document()));
        var owner = await host.StartWithOwnerAsync();

        // Act
        var (code, verifier) = await host.ApproveAsync(owner, ClientUrl, RedirectUri);
        var tokens = await host.ExchangeAsync(ClientUrl, code, verifier, RedirectUri);

        // Assert
        var grant = (await host.OAuth.ListGrantsAsync(owner.ToActor(), CancellationToken.None)).Single();
        Assert.That(tokens.Succeeded, Is.True);
        Assert.That(grant.ClientHost, Is.EqualTo("app.example.com"));
        Assert.That(grant.ClientName, Is.EqualTo("範例應用"));
    }

    [Test]
    public async Task Check_WithinTheCacheTime_DoesNotFetchAgain()
    {
        // Arrange
        var handler = FakeMetadataHandler.Json(Document(), "max-age=3600");
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        await CheckAsync(host);

        // Act
        _time.Advance(TimeSpan.FromMinutes(59));
        await CheckAsync(host);

        // Assert
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task Check_AfterTheCacheTime_FetchesAgainAndPicksUpChanges()
    {
        // Arrange
        var body = Document(name: "舊名稱");
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(FakeMetadataHandler.JsonResponse(body, "max-age=600")));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        await CheckAsync(host);
        body = Document(name: "新名稱");

        // Act
        _time.Advance(TimeSpan.FromMinutes(11));
        var result = await CheckAsync(host);

        // Assert
        Assert.That(handler.Calls, Is.EqualTo(2));
        Assert.That(result.Value.ClientName, Is.EqualTo("新名稱"));
    }

    [TestCase("max-age=1", 4, 6)]
    [TestCase("max-age=100000000", 23 * 60, 25 * 60)]
    [TestCase("no-store", 4, 6)]
    [TestCase(null, 4, 6)]
    public async Task Check_CacheTimeFollowsMaxAgeClampedBetweenFiveMinutesAndADay(string? cacheControl, int stillCachedMinutes, int refetchedMinutes)
    {
        // Arrange
        var handler = FakeMetadataHandler.Json(Document(), cacheControl);
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        await CheckAsync(host);

        // Act
        _time.Advance(TimeSpan.FromMinutes(stillCachedMinutes));
        await CheckAsync(host);
        var callsWhileCached = handler.Calls;
        _time.Advance(TimeSpan.FromMinutes(refetchedMinutes - stillCachedMinutes));
        await CheckAsync(host);

        // Assert
        Assert.That(callsWhileCached, Is.EqualTo(1));
        Assert.That(handler.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task Check_FetchFailsAfterTheCacheExpired_UsesTheOldCopyForUpToADay()
    {
        // Arrange
        var failing = false;
        var handler = new FakeMetadataHandler((_, _) => failing
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException("連不上"))
            : Task.FromResult(FakeMetadataHandler.JsonResponse(Document())));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        await CheckAsync(host);
        failing = true;

        // Act
        _time.Advance(TimeSpan.FromHours(2));
        var stale = await CheckAsync(host);
        _time.Advance(TimeSpan.FromHours(23));
        var tooOld = await CheckAsync(host);

        // Assert
        Assert.That(stale.Value.ClientName, Is.EqualTo("範例應用"));
        Assert.That(tooOld.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
    }

    [Test]
    public async Task Check_FetchRespondsWithADifferentValidDocumentThenAnInvalidOne_DoesNotFallBackToTheCache()
    {
        // Arrange
        var body = Document();
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(FakeMetadataHandler.JsonResponse(body)));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        await CheckAsync(host);
        body = Document(clientId: "https://evil.example.org/other");

        // Act
        _time.Advance(TimeSpan.FromMinutes(10));
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
    }

    [Test]
    public async Task Check_NoCacheAndTheServerIsDown_IsTemporarilyUnavailable()
    {
        // Arrange
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [Test]
    public async Task Check_ConnectionFails_IsTemporarilyUnavailable()
    {
        // Arrange
        var handler = new FakeMetadataHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("連不上")));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
    }

    [Test]
    public async Task Check_ServerNeverAnswers_TimesOutAsTemporarilyUnavailable()
    {
        // Arrange
        var handler = new FakeMetadataHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var host = await OAuthTestSupport.StartAsync(_time, handler, fetchTimeout: TimeSpan.FromMilliseconds(200));

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
    }

    [Test]
    public async Task Check_TheCallerCancels_StillCancels()
    {
        // Arrange
        var handler = new FakeMetadataHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        Assert.That(
            async () => await host.OAuth.CheckAuthorizationAsync(Server, Parameters(ClientUrl, _challenge, RedirectUri), cancel.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    private static IEnumerable<TestCaseData> BadDocuments()
    {
        yield return new TestCaseData(Document(clientId: "https://evil.example.org/c.json")).SetName("client_id 和網址不一致");
        yield return new TestCaseData(Document(clientId: null)).SetName("沒有 client_id");
        yield return new TestCaseData(Document(name: null)).SetName("沒有 client_name");
        yield return new TestCaseData(Document(name: "\\u202E\\u200B")).SetName("client_name 去掉控制字元後是空的");
        yield return new TestCaseData(Document(redirects: "[]")).SetName("redirect_uris 是空的");
        yield return new TestCaseData(Document(redirects: "\"https://app.example.com/callback\"")).SetName("redirect_uris 不是陣列");
        yield return new TestCaseData(Document(redirects: "[\"https://app.example.com/callback\",\"http://app.example.com/cb\"]")).SetName("有 http 的導回網址");
        yield return new TestCaseData(Document(redirects: "[\"myapp://cb\"]")).SetName("導回網址是自訂的 scheme");
        yield return new TestCaseData(Document(redirects: "[5]")).SetName("導回網址不是字串");
        yield return new TestCaseData(Document(method: "client_secret_basic")).SetName("不是公開用戶端");
        yield return new TestCaseData("[]").SetName("文件是陣列");
        yield return new TestCaseData("not json").SetName("文件不是 JSON");
        yield return new TestCaseData(string.Empty).SetName("文件是空的");
    }

    [TestCaseSource(nameof(BadDocuments))]
    public async Task Check_InvalidDocument_IsInvalidClient(string body)
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(body));

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
        Assert.That(result.Failure?.RedirectTo, Is.Null);
    }

    [Test]
    public async Task Check_PublicClientAuthMethodNone_IsAccepted()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(Document(method: "none")));

        // Act & Assert
        Assert.That((await CheckAsync(host)).Succeeded, Is.True);
    }

    [Test]
    public async Task Check_DocumentTooLarge_IsInvalidClient()
    {
        // Arrange
        var padding = new string('x', OAuthRules.MaxMetadataBytes);
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(Document().TrimEnd('}') + $",\"pad\":\"{padding}\"}}"));

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
    }

    [Test]
    public async Task Check_DocumentWithoutALengthHeaderThatGrowsPastTheLimit_IsInvalidClient()
    {
        // Arrange：沒有 Content-Length 的串流，要讀到上限就停，不能整份讀進來。
        var handler = new FakeMetadataHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) };
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        });
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
    }

    [Test]
    public async Task Check_NotJsonContentType_IsInvalidClient()
    {
        // Arrange
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Document(), Encoding.UTF8, "text/html"),
        }));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act & Assert
        Assert.That((await CheckAsync(host)).Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
    }

    [Test]
    public async Task Check_ProblemJsonSuffix_IsAcceptedAsJson()
    {
        // Arrange
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Document(), Encoding.UTF8, "application/client-metadata+json"),
        }));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act & Assert
        Assert.That((await CheckAsync(host)).Succeeded, Is.True);
    }

    [TestCase(HttpStatusCode.NotFound)]
    [TestCase(HttpStatusCode.MovedPermanently)]
    [TestCase(HttpStatusCode.Found)]
    [TestCase(HttpStatusCode.Forbidden)]
    public async Task Check_NonOkResponse_IsInvalidClient_AndRedirectsAreNotFollowed(HttpStatusCode status)
    {
        // Arrange
        var handler = new FakeMetadataHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
        Assert.That(handler.Calls, Is.EqualTo(1));
    }

    [TestCase("https://app.example.com")]
    [TestCase("https://app.example.com/")]
    [TestCase("https://user:secret@app.example.com/client.json")]
    [TestCase("https://app.example.com/client.json#frag")]
    [TestCase("https://app.example.com/a/../client.json")]
    [TestCase("https://app.example.com/./client.json")]
    [TestCase("https://127.0.0.1/client.json")]
    [TestCase("https://[::1]/client.json")]
    [TestCase("https://10.0.0.5/client.json")]
    [TestCase("https://169.254.169.254/latest/meta-data")]
    [TestCase("https://app.example.com/a\0b")]
    public async Task Check_BadClientIdUrl_IsInvalidClientAndNothingIsFetched(string clientId)
    {
        // Arrange
        var handler = FakeMetadataHandler.Json(Document());
        await using var host = await OAuthTestSupport.StartAsync(_time, handler);

        // Act
        var result = await CheckAsync(host, clientId);

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
        Assert.That(handler.Calls, Is.Zero);
    }

    [Test]
    public async Task Check_ClientIdTooLong_IsInvalidClient()
    {
        // Arrange
        await using var host = await OAuthTestSupport.StartAsync(_time, FakeMetadataHandler.Json(Document()));

        // Act
        var result = await CheckAsync(host, "https://app.example.com/" + new string('a', OAuthRules.MaxRedirectUriLength));

        // Assert
        Assert.That(result.Failure?.Error, Is.EqualTo(OAuthError.InvalidClient));
    }

    [Test]
    public async Task Check_HostThatResolvesToTheLoopback_LooksTheSameAsAHostThatCannotBeReached_ButTheLogTellsThemApart()
    {
        // Arrange：第一個不給假的 handler，走正式的位址檢查（連線當下檢查解析出來的位址）；第二個是連不上。
        var blockedLog = new CapturingLogger<ClientMetadataFetcher>();
        await using var blockedHost = await OAuthTestSupport.StartAsync(_time, configure: services => services.AddSingleton<ILogger<ClientMetadataFetcher>>(blockedLog));
        var failedLog = new CapturingLogger<ClientMetadataFetcher>();
        var down = new FakeMetadataHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("連不上")));
        await using var failedHost = await OAuthTestSupport.StartAsync(_time, down, configure: services => services.AddSingleton<ILogger<ClientMetadataFetcher>>(failedLog));

        // Act
        var blocked = await CheckAsync(blockedHost, "https://localhost/client.json");
        var failed = await CheckAsync(failedHost);

        // Assert
        Assert.That(blocked.Failure?.Error, Is.EqualTo(OAuthError.TemporarilyUnavailable));
        Assert.That(blocked.Failure, Is.EqualTo(failed.Failure), "錯誤代碼與說明一個字都不能不同");
        Assert.That(blocked.Failure?.Description, Does.Not.Contain("內網"));
        Assert.That(blockedLog.Entries.Select(e => e.Message), Has.Some.Contain("內網或本機"));
        Assert.That(failedLog.Entries.Select(e => e.Message), Has.None.Contain("內網或本機").And.Some.Contain("連不上"));
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)'x', offset, count);
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
