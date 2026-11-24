using System.Net;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

[TestFixture]
public sealed class HealthEndpointTests
{
    [Test]
    public async Task Health_Get_Returns200WithStatusOk()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(body, Is.EqualTo("""{"status":"ok"}"""));
        }
    }

    [Test]
    public async Task Health_MoreRequestsThanRateLimit_IsNeverRejected()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow + 10; i++)
        {
            using var response = await client.GetAsync("/health");
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Health_AfterRateLimitIsExhausted_StillReturns200()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        for (var i = 0; i <= ApiLimits.RateLimitPermitsPerWindow; i++)
        {
            using var _ = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        }

        // Act
        using var rejected = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        using var health = await client.GetAsync("/health");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
