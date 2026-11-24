using System.Net;
using PmTool.Api.Tests.Support;
using PmTool.Work;

namespace PmTool.Api.Tests;

/// <summary>已登入的請求依人計算，額度比匿名的大；登入端點依來源 IP 另外有更嚴的限制。</summary>
[TestFixture]
public sealed class SessionRateLimitingTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [Test]
    public async Task SignedIn_MoreRequestsThanTheAnonymousLimit_AreStillServed()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);

        // Act
        var statuses = await GetSessionManyTimesAsync(client, ApiLimits.RateLimitPermitsPerWindow * 2);

        // Assert
        Assert.That(statuses, Is.All.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignedIn_RequestsBeyondThePersonalLimit_Return429()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var client = factory.CreateApiClient();
        await Sessions.SignInAsync(client, TestUsers.Member);
        var allowed = await GetSessionManyTimesAsync(client, ApiLimits.SignedInPermitsPerWindow);

        // Act
        using var response = await client.GetAsync(Sessions.Route);

        // Assert
        Assert.That(allowed, Is.All.EqualTo(HttpStatusCode.OK));
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(response.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
    }

    [Test]
    public async Task SignedIn_TwoPeopleFromTheSameAddress_HaveSeparateBudgets()
    {
        // Arrange：TestServer 沒有來源 IP，兩個人等於從同一個位址連進來。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var first = factory.CreateApiClient();
        using var second = factory.CreateApiClient();
        await Sessions.SignInAsync(first, TestUsers.Member);
        await Sessions.SignInAsync(second, TestUsers.Owner);
        _ = await GetSessionManyTimesAsync(first, ApiLimits.SignedInPermitsPerWindow);

        // Act
        using var exhausted = await first.GetAsync(Sessions.Route);
        using var other = await second.GetAsync(Sessions.Route);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exhausted.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task SignedIn_RequestsDoNotUseTheAnonymousBudgetOfTheirAddress()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        using var signedIn = factory.CreateApiClient();
        using var anonymous = factory.CreateClient();
        await Sessions.SignInAsync(signedIn, TestUsers.Member);
        _ = await GetSessionManyTimesAsync(signedIn, ApiLimits.RateLimitPermitsPerWindow);

        // Act
        using var response = await anonymous.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignIn_MoreAttemptsThanTheLimitFromOneAddress_Returns429ForThatAddressOnly()
    {
        // Arrange
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), new ApiFactoryOptions { UseTestRemoteIpHeader = true });
        using var client = factory.CreateApiClient();
        var attempts = new List<HttpStatusCode>();
        for (var i = 0; i < ApiLimits.SignInPermitsPerWindow; i++)
        {
            using var attempt = await SignInFromAsync(client, "192.0.2.50", "wrong-password-0000");
            attempts.Add(attempt.StatusCode);
        }

        // Act：密碼對了也一樣擋，否則猜到的那一次就會成功。
        using var sameAddress = await SignInFromAsync(client, "192.0.2.50", TestUsers.Password);
        using var otherAddress = await SignInFromAsync(client, "192.0.2.51", TestUsers.Password);

        // Assert
        Assert.That(attempts, Is.All.EqualTo(HttpStatusCode.Unauthorized));
        await ApiRequests.AssertErrorEnvelopeAsync(sameAddress, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        Assert.That(otherAddress.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task SignedIn_OnePersonHasItsShareOfConcurrentRequests_Returns429ForThatPersonOnly()
    {
        // Arrange：同一個人已經有 MaxConcurrentRequestsPerPerson 個請求卡在服務裡。
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing());
        var release = new TaskCompletionSource<IReadOnlyList<ProjectSummary>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new SemaphoreSlim(0);
        factory.Modules.Projects.OnList = actor =>
        {
            if (actor.PersonId != TestUsers.Member.PersonId)
            {
                return Task.FromResult<IReadOnlyList<ProjectSummary>>([]);
            }

            entered.Release();
            return release.Task;
        };
        using var greedy = factory.CreateApiClient();
        using var other = factory.CreateApiClient();
        await Sessions.SignInAsync(greedy, TestUsers.Member);
        await Sessions.SignInAsync(other, TestUsers.Owner);
        var occupying = Enumerable.Range(0, ApiLimits.MaxConcurrentRequestsPerPerson)
            .Select(_ => greedy.GetAsync("/v1/projects"))
            .ToArray();
        for (var i = 0; i < occupying.Length; i++)
        {
            Assert.That(await entered.WaitAsync(Patience), Is.True, "請求沒有進到服務");
        }

        // Act
        using var samePerson = await greedy.GetAsync("/v1/projects").WaitAsync(Patience);
        using var otherPerson = await other.GetAsync("/v1/projects").WaitAsync(Patience);
        release.SetResult([]);
        var finished = await Task.WhenAll(occupying).WaitAsync(Patience);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(samePerson, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(otherPerson.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(finished.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
        }

        foreach (var response in finished)
        {
            response.Dispose();
        }
    }

    private static async Task<List<HttpStatusCode>> GetSessionManyTimesAsync(HttpClient client, int count)
    {
        var statuses = new List<HttpStatusCode>(count);
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(Sessions.Route);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }

    private static Task<HttpResponseMessage> SignInFromAsync(HttpClient client, string remoteIp, string password)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Sessions.Route)
        {
            Content = Sessions.Credentials(TestUsers.Owner.Email, password),
        };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        return client.SendAsync(request);
    }
}
