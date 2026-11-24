using PmTool.Api.Live;
using PmTool.Work;

namespace PmTool.Api.Tests.Live;

/// <summary>一輪讀到的事件怎麼分給群組：全部看得到的專案群組、只看自己的人各自的群組。</summary>
[TestFixture]
public sealed class LiveEventRouterTests
{
    private static readonly Guid ProjectId = Guid.CreateVersion7();
    private static readonly Guid Creator = Guid.CreateVersion7();
    private static readonly Guid Assignee = Guid.CreateVersion7();
    private static readonly Guid PreviousAssignee = Guid.CreateVersion7();

    [Test]
    public void Route_OneEvent_GoesToTheProjectGroupAndToEachPersonWhoOwnsTheItem()
    {
        // Arrange
        var workEvent = Event("MNT-1", assignee: Assignee, previousAssignee: PreviousAssignee);

        // Act
        var pushes = LiveEventRouter.Route([workEvent]);

        // Assert
        Assert.That(pushes.Select(p => p.Group), Is.EquivalentTo(new[]
        {
            LiveGroups.Project(ProjectId),
            LiveGroups.Own(ProjectId, Creator),
            LiveGroups.Own(ProjectId, Assignee),
            LiveGroups.Own(ProjectId, PreviousAssignee),
        }));
        Assert.That(pushes.All(p => p.ProjectKey == "MNT" && p.ItemKeys!.SequenceEqual(["MNT-1"])), Is.True);
    }

    [Test]
    public void Route_CreatorIsAlsoTheAssignee_SendsOnlyOneMessageToThatPerson()
    {
        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1", assignee: Creator)]);

        // Assert
        Assert.That(pushes.Count(p => p.Group == LiveGroups.Own(ProjectId, Creator)), Is.EqualTo(1));
    }

    [Test]
    public void Route_SeveralEventsOfOneProject_AreMergedIntoOneMessagePerGroupWithoutRepeatedKeys()
    {
        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1"), Event("MNT-7"), Event("MNT-1")]);

        // Assert
        var project = pushes.Single(p => p.Group == LiveGroups.Project(ProjectId));
        Assert.That(project.ItemKeys, Is.EqualTo(new[] { "MNT-1", "MNT-7" }));
    }

    [Test]
    public void Route_OnlyThePersonWhoOwnsAnItemGetsItsOwnGroupMessage()
    {
        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1", assignee: Assignee), Event("MNT-2", creator: Creator)]);

        // Assert
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Own(ProjectId, Assignee)).ItemKeys, Is.EqualTo(new[] { "MNT-1" }));
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Own(ProjectId, Creator)).ItemKeys, Is.EqualTo(new[] { "MNT-1", "MNT-2" }));
    }

    [Test]
    public void Route_MoreThanTheLimitOfKeys_SendsNullMeaningTheWholeProject()
    {
        // Arrange
        var events = Enumerable.Range(1, LiveEventRouter.MaxItemKeys + 1).Select(n => Event($"MNT-{n}")).ToArray();

        // Act
        var pushes = LiveEventRouter.Route(events);

        // Assert
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Project(ProjectId)).ItemKeys, Is.Null);
    }

    [Test]
    public void Route_ExactlyTheLimitOfKeys_ListsThem()
    {
        // Arrange
        var events = Enumerable.Range(1, LiveEventRouter.MaxItemKeys).Select(n => Event($"MNT-{n}")).ToArray();

        // Act
        var pushes = LiveEventRouter.Route(events);

        // Assert
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Project(ProjectId)).ItemKeys, Has.Count.EqualTo(LiveEventRouter.MaxItemKeys));
    }

    [Test]
    public void Route_UnknownEvents_AreSkipped()
    {
        // Arrange
        var unknown = Event("MNT-1") with { Kind = WorkEventKind.Unknown };

        // Act & Assert
        Assert.That(LiveEventRouter.Route([unknown]), Is.Empty);
    }

    [Test]
    public void Route_TwoProjects_AreSeparateMessages()
    {
        // Arrange
        var other = Guid.CreateVersion7();
        var otherEvent = Event("WEB-1") with { Item = Event("WEB-1").Item with { ProjectId = other, ProjectKey = "WEB" } };

        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1"), otherEvent]);

        // Assert
        Assert.That(pushes.Where(p => p.Group.StartsWith("project:", StringComparison.Ordinal)).Select(p => p.ProjectKey), Is.EquivalentTo(new[] { "MNT", "WEB" }));
    }

    [Test]
    public void Route_AllEventsByOnePerson_CarryThatPersonAsTheActor()
    {
        // Arrange
        var actor = Guid.CreateVersion7();

        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1", actor: actor), Event("MNT-2", actor: actor)]);

        // Assert
        Assert.That(pushes.Select(p => p.ActorId), Is.All.EqualTo(actor));
    }

    [Test]
    public void Route_EventsByDifferentPeople_HaveNoActor()
    {
        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1", actor: Guid.CreateVersion7()), Event("MNT-2", actor: Guid.CreateVersion7())]);

        // Assert
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Project(ProjectId)).ActorId, Is.Null);
    }

    [Test]
    public void Route_OwnGroupOnlyCountsTheEventsSentToIt()
    {
        // Arrange：A 做的 1 號是 C 的，B 做的 2 號不是 C 的；C 只會收到 1 號，所以對他來說全部是 A 做的。
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        // Act
        var pushes = LiveEventRouter.Route([Event("MNT-1", actor: a, assignee: Assignee), Event("MNT-2", actor: b)]);

        // Assert
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Own(ProjectId, Assignee)).ActorId, Is.EqualTo(a));
        Assert.That(pushes.Single(p => p.Group == LiveGroups.Project(ProjectId)).ActorId, Is.Null);
    }

    private static WorkEvent Event(string itemKey, Guid? creator = null, Guid? assignee = null, Guid? previousAssignee = null, Guid? actor = null) => new(
        Guid.CreateVersion7(),
        new WorkEventPosition(10, 1),
        WorkEventKind.ItemUpdated,
        Guid.CreateVersion7(),
        actor ?? Creator,
        DateTimeOffset.UnixEpoch,
        new ItemEventData(
            ProjectId, "MNT", Guid.CreateVersion7(), itemKey, "標題", creator ?? Creator, assignee, AssigneeChanged: false, previousAssignee,
            FromState: null, ToState: null, CommentExcerpt: null, MentionedPersonIds: [], ChangedFields: []));
}
