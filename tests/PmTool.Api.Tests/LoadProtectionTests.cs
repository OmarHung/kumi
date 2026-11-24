using System.Net;
using PmTool.Api.Endpoints;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests;

/// <summary>
/// 驗證端點是匿名的，每個請求在讀內文時最多會占住 1 MiB。同時在讀的請求數沒有上限、讀多久也沒有期限的話，
/// 用戶端只要把內文送到一半停住，開幾十條連線就能用完容器的記憶體（實際在容器上重現過）。
/// 這裡確認兩道防線：同時處理的數量有上限，讀內文有期限。
/// </summary>
[TestFixture]
public sealed class LoadProtectionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ShortReadTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan LongReadTimeout = TimeSpan.FromMinutes(5);

    [Test]
    public async Task Validate_BodyStallsPastTheReadTimeout_Returns408WithoutCallingTheValidator()
    {
        // Arrange
        var validator = FakeBlueprintValidator.Passing();
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { BodyReadTimeout = ShortReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(validator, options);
        using var client = factory.CreateClient();

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.RequestTimeout, ApiErrorCodes.RequestTimeout);
        Assert.That(validator.Calls, Is.Empty);
    }

    [Test]
    public async Task Validate_EverySlotIsBusy_Returns503ForTheNextRequestAndServesAgainOnceASlotIsFree()
    {
        // Arrange：上限設成 2，讓兩個請求卡在讀內文。
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { MaxConcurrentRequests = 2, BodyReadTimeout = LongReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();
        var first = client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        var second = client.PostAsync(ApiRequests.ChangeSetRoute, ApiRequests.Document("{}"));
        await stalled.WaitForStalledRequestsAsync(2, Patience);

        // Act：第三個進不來；健康檢查不受影響；放行之後前兩個完成，新的請求又進得來。
        using var rejected = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        using var health = await client.GetAsync("/health");
        stalled.Open();
        using var firstResponse = await first.WaitAsync(Patience);
        using var secondResponse = await second.WaitAsync(Patience);
        using var afterwards = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(rejected, HttpStatusCode.ServiceUnavailable, ApiErrorCodes.Busy);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rejected.Headers.RetryAfter, Is.Not.Null, "要告訴用戶端多久之後再試");
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(afterwards.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Validate_RequestTimesOut_GivesItsSlotBack()
    {
        // Arrange：只有一個位置。
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { MaxConcurrentRequests = 1, BodyReadTimeout = ShortReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();

        // Act
        using var timedOut = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        stalled.Open();
        using var next = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timedOut.StatusCode, Is.EqualTo(HttpStatusCode.RequestTimeout));
            Assert.That(next.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }

    [Test]
    public async Task Validate_ClientGivesUpWhileItsBodyIsStalled_GivesItsSlotBack()
    {
        // Arrange：只有一個位置，用戶端在內文卡住時自己斷線。
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { MaxConcurrentRequests = 1, BodyReadTimeout = LongReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();
        using var giveUp = new CancellationTokenSource();
        var abandoned = client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"), giveUp.Token);
        await stalled.WaitForStalledRequestsAsync(1, Patience);

        // Act
        await giveUp.CancelAsync();
        Assert.That(async () => await abandoned, Throws.InstanceOf<OperationCanceledException>());
        stalled.Open();
        var statusAfterwards = await PostUntilNotBusyAsync(client);

        // Assert
        Assert.That(statusAfterwards, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Validate_UnsupportedContentTypeWhileEverySlotIsBusy_StillReturns415()
    {
        // Arrange：只看請求本身就能拒絕的，不需要位置。
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { MaxConcurrentRequests = 1, BodyReadTimeout = LongReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();
        var occupying = client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        await stalled.WaitForStalledRequestsAsync(1, Patience);

        // Act
        using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}", "text/plain"));
        stalled.Open();
        using var occupied = await occupying.WaitAsync(Patience);

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(response, HttpStatusCode.UnsupportedMediaType, ApiErrorCodes.UnsupportedMediaType);
    }

    [Test]
    public async Task Validate_OneClientAlreadyHasItsShareOfConcurrentRequests_Returns429ForThatClientOnly()
    {
        // Arrange：位置還很多，但同一個來源已經有 MaxConcurrentRequestsPerClient 個請求卡在讀內文。
        // 限流的固定視窗允許一開始就連發 60 次；沒有這個上限的話，一個來源就能把所有位置占住大半分鐘。
        const string greedyClient = "192.0.2.40";
        var stalled = new StalledBodies();
        var options = new ApiFactoryOptions
        {
            UseTestRemoteIpHeader = true,
            LoadLimits = new ValidationLoadLimits { BodyReadTimeout = LongReadTimeout },
            WrapRequestBody = _ => stalled.CreateStream(),
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var client = factory.CreateClient();
        var occupying = Enumerable.Range(0, ApiLimits.MaxConcurrentRequestsPerClient)
            .Select(_ => SendFromAsync(client, greedyClient, "application/json"))
            .ToArray();
        await stalled.WaitForStalledRequestsAsync(occupying.Length, Patience);

        // Act：同一個來源再來一個；另一個來源送一個只看標頭就會被拒絕的請求（它有進到端點，才會回 415）。
        using var sameClient = await SendFromAsync(client, greedyClient, "application/json").WaitAsync(Patience);
        using var otherClient = await SendFromAsync(client, "192.0.2.41", "text/plain").WaitAsync(Patience);
        using var health = await client.GetAsync("/health");
        stalled.Open();
        var finished = await Task.WhenAll(occupying).WaitAsync(Patience);
        using var sameClientAfterwards = await SendFromAsync(client, greedyClient, "application/json");

        // Assert
        await ApiRequests.AssertErrorEnvelopeAsync(sameClient, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sameClient.Headers.RetryAfter, Is.Not.Null, "要告訴用戶端多久之後再試");
            Assert.That(otherClient.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
            Assert.That(health.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(finished.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(sameClientAfterwards.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        foreach (var response in finished)
        {
            response.Dispose();
        }
    }

    [Test]
    public async Task Validate_SignedInPersonAlreadyHasItsShareOfValidations_Returns429ForThatPersonOnly()
    {
        // Arrange：已登入的人一般同時可以有 MaxConcurrentRequestsPerPerson 個請求，剛好等於全部的驗證位置；
        // 驗證端點要另外把每個呼叫者限制在 MaxConcurrentValidationsPerCaller 個，一個帳號才占不滿。
        var stalled = new StalledBodies();
        var stallBodies = false;
        var options = new ApiFactoryOptions
        {
            LoadLimits = new ValidationLoadLimits { BodyReadTimeout = LongReadTimeout },
            WrapRequestBody = body => stallBodies ? stalled.CreateStream() : body,
        };
        using var factory = new ApiFactory(FakeBlueprintValidator.Passing(), options);
        using var greedy = factory.CreateApiClient();
        using var other = factory.CreateApiClient();
        await Sessions.SignInAsync(greedy, TestUsers.Member);
        await Sessions.SignInAsync(other, TestUsers.Owner);
        stallBodies = true;
        var occupying = Enumerable.Range(0, ApiLimits.MaxConcurrentValidationsPerCaller)
            .Select(_ => greedy.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}")))
            .ToArray();
        HttpResponseMessage[] finished;
        try
        {
            await stalled.WaitForStalledRequestsAsync(occupying.Length, Patience);

            // Act：同一個人再來一個；另一個人送一個只看標頭就會被拒絕的請求（它有進到端點，才會回 415）。
            using var samePerson = await greedy.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}")).WaitAsync(Patience);
            using var otherPerson = await other.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}", "text/plain")).WaitAsync(Patience);

            // Assert
            await ApiRequests.AssertErrorEnvelopeAsync(samePerson, HttpStatusCode.TooManyRequests, ApiErrorCodes.RateLimited);
            Assert.That(otherPerson.StatusCode, Is.EqualTo(HttpStatusCode.UnsupportedMediaType));
        }
        finally
        {
            stalled.Open();
            finished = await Task.WhenAll(occupying).WaitAsync(Patience);
        }

        using var samePersonAfterwards = await greedy.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(finished.Select(response => response.StatusCode), Is.All.EqualTo(HttpStatusCode.OK));
            Assert.That(samePersonAfterwards.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        Array.ForEach(finished, response => response.Dispose());
    }

    private static Task<HttpResponseMessage> SendFromAsync(HttpClient client, string remoteIp, string contentType)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ApiRequests.BlueprintRoute)
        {
            Content = ApiRequests.Document("{}", contentType),
        };
        request.Headers.Add(ApiFactory.RemoteIpHeader, remoteIp);
        return client.SendAsync(request);
    }

    // 用戶端斷線之後，伺服器那一側要一點時間才會收尾並把位置還回來。
    private static async Task<HttpStatusCode> PostUntilNotBusyAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            using var response = await client.PostAsync(ApiRequests.BlueprintRoute, ApiRequests.Document("{}"));
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable || DateTime.UtcNow > deadline)
            {
                return response.StatusCode;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
