using System.Net;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>GET /health/ready：所有模組都說準備好了才是 200。/health 只看程序活著，不看這個。</summary>
[TestFixture]
public sealed class ReadinessTests
{
    private const string Route = "/health/ready";

    [Test]
    public async Task Ready_EveryProbeReady_Returns200()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Probes.Add(FakeReadinessProbe.Ready());
        factory.Modules.Probes.Add(FakeReadinessProbe.Ready());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("""{"status":"ok"}"""));
    }

    [Test]
    public async Task Ready_NoProbes_Returns200()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Ready_OneProbeNotReady_Returns503NotReady()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Probes.Add(FakeReadinessProbe.Ready());
        factory.Modules.Probes.Add(FakeReadinessProbe.NotReady());
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.ServiceUnavailable, ApiErrorCodes.NotReady);
    }

    [Test]
    public async Task Ready_ProbeThrows_Returns503InsteadOf500()
    {
        // Arrange：資料庫連不上時，模組的檢查可能直接丟例外。那是「還沒準備好」，不是伺服器壞了。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        factory.Modules.Probes.Add(new FakeReadinessProbe(() => throw new InvalidOperationException("connection refused")));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.ServiceUnavailable, ApiErrorCodes.NotReady);
    }

    [Test]
    public async Task Ready_ProbeTakesLongerThanTheTimeout_Returns503()
    {
        // Arrange
        var never = new TaskCompletionSource<bool>();
        var options = new ApiFactoryOptions
        {
            ReadinessLimits = new ReadinessLimits { Timeout = TimeSpan.FromMilliseconds(100) },
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        factory.Modules.Probes.Add(new FakeReadinessProbe(() => never.Task));
        using var client = factory.CreateClient();

        // Act
        using var response = await client.GetAsync(Route);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.ServiceUnavailable, ApiErrorCodes.NotReady);
    }

    [Test]
    public async Task Ready_RepeatedWithinTheCacheWindow_AsksTheProbesOnceAndAgainAfterwards()
    {
        // Arrange：不限流又不用登入，每次都問資料庫的話，任何人都能替資料庫加壓。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var probe = FakeReadinessProbe.Ready();
        factory.Modules.Probes.Add(probe);
        using var client = factory.CreateClient();

        // Act
        for (var i = 0; i < 5; i++)
        {
            using var _ = await client.GetAsync(Route);
        }

        var withinWindow = probe.Calls;
        factory.Modules.Time.Advance(TimeSpan.FromSeconds(ApiLimits.ReadinessCacheSeconds));
        using var afterWindow = await client.GetAsync(Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(withinWindow, Is.EqualTo(1));
            Assert.That(probe.Calls, Is.EqualTo(2));
            Assert.That(afterWindow.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Ready_MoreRequestsThanTheRateLimit_IsNeverRejected()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateClient();
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var i = 0; i < ApiLimits.RateLimitPermitsPerWindow + 10; i++)
        {
            using var response = await client.GetAsync(Route);
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }
}
