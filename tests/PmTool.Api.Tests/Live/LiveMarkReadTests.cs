using System.Net;
using Microsoft.Extensions.DependencyInjection;
using PmTool.Api.Live;
using PmTool.Api.Tests.Support;

namespace PmTool.Api.Tests.Live;

/// <summary>標成已讀的回應不等推送：推送卡住或失敗，回應照樣馬上回來。</summary>
[TestFixture]
public sealed class LiveMarkReadTests
{
    private sealed class HangingNotifier : ILiveNotifier
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task InboxChangedAsync(Guid personId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return new TaskCompletionSource().Task;
        }

        public Task InboxChangedAsync(IReadOnlyCollection<Guid> personIds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return new TaskCompletionSource().Task;
        }

        public void AccessChanged(IReadOnlyCollection<Guid>? personIds)
        {
        }
    }

    [TestCase("/v1/notifications/read", """{"notificationIds":["00000000-0000-7000-8000-000000000001"]}""", TestName = "MarkRead_PushHangs_StillRespondsRightAway")]
    [TestCase("/v1/notifications/read-all", null, TestName = "MarkAllRead_PushHangs_StillRespondsRightAway")]
    public async Task MarkRead_PushNeverFinishes_RespondsRightAway(string route, string? body)
    {
        // Arrange
        var notifier = new HangingNotifier();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<ILiveNotifier>(notifier) });
        factory.Modules.Notifications.OnMarkRead = (_, ids) => ids.Count;
        factory.Modules.Notifications.OnMarkAllRead = _ => 3;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        var request = client.PostAsync(route, body is null ? null : ApiRequests.Document(body));
        var finished = await Task.WhenAny(request, Task.Delay(TimeSpan.FromSeconds(5))) == request;

        // Assert
        Assert.That(finished, Is.True, "推送卡住時回應不能跟著卡住");
        using var response = await request;
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(notifier.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task MarkAllRead_NothingWasUnread_DoesNotPush()
    {
        // Arrange
        var notifier = new HangingNotifier();
        using var factory = new ApiFactory(
            FakeBlueprintValidator.Passing(),
            new ApiFactoryOptions { ConfigureServices = services => services.AddSingleton<ILiveNotifier>(notifier) });
        factory.Modules.Notifications.OnMarkAllRead = _ => 0;
        using var client = await ProjectEndpointTests.SignedInClientAsync(factory);

        // Act
        using var response = await client.PostAsync("/v1/notifications/read-all", content: null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(notifier.Calls, Is.Zero);
    }
}
