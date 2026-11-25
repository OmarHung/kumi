using PmTool.Abstractions;
using PmTool.Api.Live;
using PmTool.Notifications;

namespace PmTool.Api.Tests.Live;

/// <summary>一次通知很多人：收件匣的推送合併成一則訊號，不是每個人一則。</summary>
[TestFixture]
public sealed class LiveNotificationObserverTests
{
    private sealed class RecordingSignals : IClusterSignals
    {
        public List<ClusterSignal> Published { get; } = [];

        public void Publish(ClusterSignal signal) => Published.Add(signal);
    }

    [Test]
    public async Task Notified_SeveralRecipients_PublishesOneSignalWithEachPersonOnce()
    {
        // Arrange
        var signals = new RecordingSignals();
        var observer = new LiveNotificationObserver(new HubLiveNotifier(signals));
        var (first, second, organization) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        // Act
        await observer.NotifiedAsync(
            [new NotifiedRecipient(organization, first), new NotifiedRecipient(organization, second), new NotifiedRecipient(Guid.CreateVersion7(), first)], CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(signals.Published, Has.Count.EqualTo(1));
            Assert.That(signals.Published[0].Kind, Is.EqualTo(ClusterSignalKind.InboxChanged));
            Assert.That(signals.Published[0].PersonIds, Is.EquivalentTo(new[] { first, second }));
        }
    }

    [Test]
    public async Task Notified_NoRecipients_PublishesNothing()
    {
        // Arrange
        var signals = new RecordingSignals();
        var observer = new LiveNotificationObserver(new HubLiveNotifier(signals));

        // Act
        await observer.NotifiedAsync([], CancellationToken.None);

        // Assert
        Assert.That(signals.Published, Is.Empty);
    }
}
