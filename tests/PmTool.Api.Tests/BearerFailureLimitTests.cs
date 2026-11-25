using System.Net;
using PmTool.Api.RateLimiting;
using PmTool.Api.Tests.Support;
using PmTool.Identity;
using static PmTool.Api.Tests.AccessTokenAuthenticationTests;

namespace PmTool.Api.Tests;

/// <summary>
/// 無效的 Bearer 權杖大量重送：驗證排在限流之前，同一個來源失敗太多次之後，要在查資料庫之前就擋下。
/// </summary>
[TestFixture]
public sealed class BearerFailureLimitTests
{
    private const string SessionRoute = "/v1/session";

    [Test]
    public async Task Bearer_TooManyFailuresFromOneSource_Returns429WithoutLookingUpTheToken()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        await FailAsync(client, ApiLimits.BearerFailuresPerWindow);
        var lookupsBefore = factory.Modules.AccessTokens.FindCalls.Count;

        // Act
        using var blocked = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, RandomSecret()));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(blocked, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(blocked.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero).And.LessThanOrEqualTo(TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds)));
        Assert.That(factory.Modules.AccessTokens.FindCalls, Has.Count.EqualTo(lookupsBefore), "超過次數之後不再查");
    }

    [Test]
    public async Task Bearer_FailuresUpToTheLimit_StillAnswer401()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        var statuses = await FailAsync(client, ApiLimits.BearerFailuresPerWindow);

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Bearer_ValidTokens_DoNotUseUpTheFailureAllowance()
    {
        // Arrange：失敗到差一次就滿，之後再送很多次有效的，最後一次失敗仍是 401。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        await FailAsync(client, ApiLimits.BearerFailuresPerWindow - 1);

        // Act
        var valid = new List<HttpStatusCode>();
        for (var i = 0; i < 100; i++)
        {
            using var ok = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
            valid.Add(ok.StatusCode);
        }

        using var lastFailure = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, RandomSecret()));

        // Assert
        Assert.That(valid, Is.All.EqualTo(HttpStatusCode.OK));
        Assert.That(lastFailure.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Bearer_AfterTheWindow_TheSourceCanTryAgain()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        await FailAsync(client, ApiLimits.BearerFailuresPerWindow);

        // Act
        factory.Modules.Time.Advance(TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds));
        using var response = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Bearer_BlockedSource_DoesNotAffectAnotherSourceOrTheCookie()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Owner);
        await FailAsync(client, ApiLimits.BearerFailuresPerWindow, "203.0.113.7");

        // Act
        using var blocked = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), "203.0.113.7"));
        using var otherSource = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, secret, "203.0.113.8"));
        using var cookie = await client.SendAsync(WithIp(new HttpRequestMessage(HttpMethod.Get, SessionRoute), "203.0.113.7"));

        // Assert
        Assert.That(blocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(otherSource.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(cookie.StatusCode, Is.EqualTo(HttpStatusCode.OK), "沒帶 Bearer 的請求不受影響");
    }

    [Test]
    public async Task Bearer_SameIpv6Prefix_CountsAsOneSource()
    {
        // Arrange：同一個 /64 換位址送，額度是共用的。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var failed = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), $"2001:db8:1:2::{i + 1:x}"));
        }

        // Act
        using var response = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), "2001:db8:1:2:aaaa::1"));
        using var otherPrefix = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), "2001:db8:1:3::1"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(otherPrefix.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Bearer_BlockedSourceWithATokenInTheCache_IsLetThrough()
    {
        // Arrange：權杖剛用過、在快取裡；之後同一個來源因為送了一堆假權杖被擋。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        using var warmUp = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
        await FailAsync(client, ApiLimits.BearerFailuresPerWindow);
        var lookupsBefore = factory.Modules.AccessTokens.FindCalls.Count;

        // Act
        using var cached = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));
        using var fake = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, RandomSecret()));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cached.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(fake.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(factory.Modules.AccessTokens.FindCalls, Has.Count.EqualTo(lookupsBefore), "被擋的來源不查資料庫");
        }
    }

    [Test]
    public async Task Bearer_TrackerIsFull_ANewSourceIsNeitherRecordedNorBlocked()
    {
        // Arrange：記到上限，每個來源失敗一次。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailureMaxTrackedSources; i++)
        {
            using var _ = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), $"198.18.{i / 250}.{i % 250 + 1}"));
        }

        // Act：沒記過的來源失敗再多次也不被擋，也不會拖累別的新來源。
        var statuses = await FailAsync(client, ApiLimits.BearerFailuresPerWindow + 5, "203.0.113.50");
        using var other = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, secret, "203.0.113.51"));

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void RecordFailure_TrackerIsFull_ScansForExpiredEntriesAtMostOncePerSecond()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var limiter = new BearerFailureLimiter(time);
        for (var i = 0; i < ApiLimits.BearerFailureMaxTrackedSources; i++)
        {
            limiter.RecordFailure($"source-{i}");
        }

        var scansWhileFilling = limiter.CleanupScanCount;

        // Act
        for (var i = 0; i < 1_000; i++)
        {
            limiter.RecordFailure($"extra-{i}");
        }

        // Assert
        Assert.That(limiter.CleanupScanCount - scansWhileFilling, Is.EqualTo(1));
    }

    [Test]
    public void RecordFailure_TrackerIsFull_ScansAgainAfterASecondAndReusesTheSpaceOfExpiredEntries()
    {
        // Arrange
        var time = new ManualTimeProvider();
        var limiter = new BearerFailureLimiter(time);
        for (var i = 0; i < ApiLimits.BearerFailureMaxTrackedSources; i++)
        {
            limiter.RecordFailure($"source-{i}");
        }

        limiter.RecordFailure("rejected-while-full");
        time.Advance(TimeSpan.FromSeconds(ApiLimits.RateLimitWindowSeconds + 1));

        // Act
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            limiter.RecordFailure("new-source");
        }

        // Assert
        Assert.That(limiter.IsBlocked("new-source", out _), Is.True);
    }

    [Test]
    public async Task Bearer_HostAgentCredentialFailures_DoNotBlockPersonalTokensFromTheSameAddress()
    {
        // Arrange：worker 的機器憑證失敗到上限。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var secret = factory.Modules.AccessTokens.Issue(TestUsers.Member, AccessTokenScope.ItemsRead);
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.BearerFailuresPerWindow; i++)
        {
            using var _ = await client.SendAsync(Bearer(HttpMethod.Post, "/v1/host-agent/check-in", $"kha_{Guid.NewGuid():N}"));
        }

        // Act
        using var agentBlocked = await client.SendAsync(Bearer(HttpMethod.Post, "/v1/host-agent/check-in", $"kha_{Guid.NewGuid():N}"));
        using var personal = await client.SendAsync(Bearer(HttpMethod.Get, SessionRoute, secret));

        // Assert
        Assert.That(agentBlocked.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        Assert.That(personal.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static string RandomSecret() => $"{AccessTokenRules.Prefix}{Guid.NewGuid():N}";

    private static async Task<List<HttpStatusCode>> FailAsync(HttpClient client, int times, string? ip = null)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < times; i++)
        {
            using var response = await client.SendAsync(BearerFrom(HttpMethod.Get, SessionRoute, RandomSecret(), ip));
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static HttpRequestMessage BearerFrom(HttpMethod method, string route, string secret, string? ip) =>
        WithIp(Bearer(method, route, secret), ip);

    private static HttpRequestMessage WithIp(HttpRequestMessage request, string? ip)
    {
        if (ip is not null)
        {
            request.Headers.Add(ApiFactory.RemoteIpHeader, ip);
        }

        return request;
    }
}
