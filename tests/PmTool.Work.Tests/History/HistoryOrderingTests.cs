using PmTool.Abstractions;
using PmTool.Work.History;
using PmTool.Work.Persistence;
using static PmTool.Work.Tests.History.HistorySupport;

namespace PmTool.Work.Tests.History;

/// <summary>來源的時間只到秒：同一個時間的活動在時間軸上的順序是穩定的，照匯入的順序。</summary>
[TestFixture]
public sealed class HistoryOrderingTests
{
    private const int SameTimeCount = 40;

    private WorkTestHost _host = null!;

    [OneTimeSetUp]
    public async Task StartAsync() => _host = await WorkTestHost.StartAsync();

    [OneTimeTearDown]
    public async Task StopAsync() => await _host.DisposeAsync();

    [Test]
    public void Assign_ManyActivitiesAtTheSameTime_GetStrictlyIncreasingIds()
    {
        // Arrange
        var ids = new HistoryActivityIds();
        var at = Past();
        var activities = Enumerable.Range(0, 5000).Select(_ => Activity("commented", at)).ToArray();

        // Act
        var assigned = activities.Select(ids.Assign).Select(a => a.Id.ToByteArray(bigEndian: true)).ToArray();

        // Assert
        var ordered = assigned.Zip(assigned.Skip(1), (before, after) => before.AsSpan().SequenceCompareTo(after) < 0);
        Assert.That(ordered, Has.All.True);
    }

    [Test]
    public void Assign_CreatedActivity_SortsBeforeCommentsOfTheSameTimeFromALaterRequest()
    {
        // Arrange
        var at = Past();
        var created = new HistoryActivityIds().Assign(Activity("created", at)).Id.ToByteArray(bigEndian: true);

        // Act
        var comment = new HistoryActivityIds().Assign(Activity("commented", at)).Id.ToByteArray(bigEndian: true);

        // Assert
        Assert.That(created.AsSpan().SequenceCompareTo(comment), Is.LessThan(0));
    }

    [Test]
    public async Task ImportCommentsAsync_CommentsAndCreationAtTheSameSecond_AreListedInImportOrder()
    {
        // Arrange
        var (_, manager, member) = await ProjectWithMembersAsync(_host);
        var at = Past();
        await _host.History.ImportItemsAsync(manager, "WEB", Batch(Row("m-1", member.PersonId, createdAt: at)), CancellationToken.None);
        var comments = Enumerable.Range(0, SameTimeCount).Select(n => Comment($"u-{n}", "m-1", member.PersonId, body: $"第 {n} 則", at: at)).ToArray();

        // Act
        await _host.History.ImportCommentsAsync(manager, "WEB", Comments(comments), CancellationToken.None);
        var timeline = await ActivityLog.AllAsync(_host, manager, "WEB-1");

        // Assert
        var expected = new[] { ActivityKind.Created }.Concat(Enumerable.Repeat(ActivityKind.Commented, SameTimeCount));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timeline.Select(a => a.Kind), Is.EqualTo(expected));
            Assert.That(timeline.Skip(1).Select(a => a.Comment), Is.EqualTo(Enumerable.Range(0, SameTimeCount).Select(n => $"第 {n} 則")));
        }
    }

    private static ItemActivityEntity Activity(string kind, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        ProjectId = Guid.NewGuid(),
        ItemId = Guid.NewGuid(),
        ActorId = Guid.NewGuid(),
        Kind = kind,
        At = at,
        Content = "{}",
    };
}
