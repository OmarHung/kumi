using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class RateLimitingTests
{
    [Test]
    public async Task Validate_RequestsBeyondPermitLimit_Returns429WithEnvelopeAndRetryAfter()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var allowed = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var accepted = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
            allowed.Add(accepted.StatusCode);
        }

        // Act
        using var response = await client.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document("{}"));

        // Assert
        Assert.That(allowed, Is.All.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(response.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public async Task Validate_DifferentRemoteAddresses_HaveSeparateBudgets()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await SendFromAsync(client, "192.0.2.10");
        }

        // Act
        using var sameAddress = await SendFromAsync(client, "192.0.2.10");
        using var otherAddress = await SendFromAsync(client, "192.0.2.20");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameAddress.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(otherAddress.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [TestCase("2001:db8:1:2::1", "2001:db8:1:2:ffff:ffff:ffff:99", HttpStatusCode.TooManyRequests,
        TestName = "Validate_Ipv6AddressesInTheSameSlash64_ShareOneBudget")]
    [TestCase("2001:db8:1:2::1", "2001:db8:1:3::1", HttpStatusCode.OK,
        TestName = "Validate_Ipv6AddressesInDifferentSlash64_HaveSeparateBudgets")]
    [TestCase("::ffff:192.0.2.30", "192.0.2.30", HttpStatusCode.TooManyRequests,
        TestName = "Validate_Ipv4MappedIpv6Address_SharesTheBudgetOfTheIpv4Address")]
    [TestCase("192.0.2.30", "::ffff:192.0.2.31", HttpStatusCode.OK,
        TestName = "Validate_DifferentIpv4AddressesOneOfThemMapped_HaveSeparateBudgets")]
    public async Task Validate_AddressesAfterOneOfThemUsedUpItsBudget(
        string exhausted, string other, HttpStatusCode expectedForOther)
    {
        // Arrange：一個 IPv6 用戶通常拿到一整個 /64，位址可以隨便換；以完整位址分開計算的話，換一個位址就有新的額度。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await SendFromAsync(client, exhausted);
        }

        // Act
        using var response = await SendFromAsync(client, other);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedForOther));
    }

    [Test]
    public async Task UnknownRoute_CountsTowardsTheSameBudget()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await client.GetAsync("/no-such-route");
        }

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
    }

    private static Task<HttpResponseMessage> SendFromAsync(HttpClient client, string remoteIp)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiRequests.BlueprintRoute)
        {
            Content = ApiRequests.Document("{}"),
        };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        return client.SendAsync(request);
    }
}
