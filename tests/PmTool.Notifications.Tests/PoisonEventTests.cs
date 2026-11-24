using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PmTool.Notifications.Events;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Tests;

/// <summary>
/// 一筆一直失敗的事件（毒藥事件）不能讓所有人的通知都停住：每筆事件隔離、失敗記下來、
/// 失敗到上限就略過、其他事件照常處理。
/// </summary>
[TestFixture]
public sealed class PoisonEventTests : ConsumerTestBase
{
    private const int MaxAttempts = NotificationsDbContext.MaxEventAttempts;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ManualClock _clock = new();

    protected override TimeProvider? Clock => _clock;

    /// <summary>每一輪之間時鐘走過重試間隔，所以每一輪的失敗都算進次數。</summary>
    private async Task HandleRepeatedlyAsync(int times, params WorkEvent[] events)
    {
        for (var attempt = 0; attempt < times; attempt++)
        {
            await HandleOnceAsync(events);
            _clock.Advance(NotificationsDbContext.MinEventRetryInterval + TimeSpan.FromSeconds(1));
        }
    }

    private Task HandleOnceAsync(WorkEvent[] events) =>
        Host.Consumer.HandleAsync(events, CancellationToken.None).WaitAsync(Patience);

    private async Task<IReadOnlyList<FailedEventRow>> FailuresOfAsync(params Guid[] eventIds)
    {
        await using var db = await Host.CreateDbContextAsync();
        return await db.FailedEvents.AsNoTracking().Where(f => eventIds.Contains(f.EventId)).ToListAsync();
    }

    [Test]
    public async Task Handle_OneEventAlwaysFails_TheOthersStillNotifyAndThePositionEventuallyReachesTheEnd()
    {
        // Arrange：中間夾一筆會失敗的事件。
        var (good, creator, assignee, _, _) = NewItem();
        var (poisoned, poisonCreator, poisonAssignee, _, _) = NewItem();
        var (after, afterCreator, afterAssignee, _, _) = NewItem();
        Host.Access.Poison(poisoned.ProjectId);
        var first = good.Created(creator, assignee);
        var middle = poisoned.Created(poisonCreator, poisonAssignee);
        var last = after.Created(afterCreator, afterAssignee);

        // Act：背景工作每一輪都從記下的位置重讀同一批。
        await HandleRepeatedlyAsync(MaxAttempts, first, middle, last);

        // Assert
        var failure = (await FailuresOfAsync(middle.EventId)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(afterAssignee), Is.EqualTo(new[] { NotificationKind.Assigned }), "失敗的事件後面的事件沒有被重複處理");
            Assert.That(await Host.KindsForAsync(poisonAssignee), Is.Empty);
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(last.Position));
            Assert.That(failure.Attempts, Is.EqualTo(MaxAttempts));
            Assert.That(failure.OrganizationId, Is.EqualTo(poisoned.OrganizationId));
            Assert.That((failure.TransactionId, failure.Sequence), Is.EqualTo((middle.Position.TransactionId, middle.Position.Sequence)));
            Assert.That(failure.ErrorType, Does.Contain(nameof(InvalidOperationException)));
            Assert.That(failure.ErrorMessage, Is.Not.Empty);
        }
    }

    [Test]
    public async Task Handle_EventFailsManyTimesWithinTheRetryInterval_IsNeverSkipped()
    {
        // Arrange：背景工作每 2 秒一輪，故障只持續了半分鐘（十五輪）。
        var (item, creator, assignee, _, _) = NewItem();
        Host.Access.Poison(item.ProjectId);
        var created = item.Created(creator, assignee);
        var before = await Host.Consumer.GetPositionAsync(CancellationToken.None);

        // Act
        foreach (var _ in Enumerable.Range(0, 15))
        {
            await HandleOnceAsync([created]);
            _clock.Advance(TimeSpan.FromSeconds(2));
        }

        Host.Access.Heal(item.ProjectId);
        await HandleOnceAsync([created]);

        // Assert：次數沒有累積到上限，故障排除後照常處理。
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await FailuresOfAsync(created.EventId)).Single().Attempts, Is.EqualTo(1));
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.GreaterThan(before));
        }
    }

    [Test]
    public async Task Handle_FailureWithinTheInterval_OnlyUpdatesTheMessageAndNotTheCount()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        Host.Access.Poison(item.ProjectId);
        var created = item.Created(creator, assignee);
        await HandleOnceAsync([created]);
        Host.Access.FailProject(item.ProjectId, new InvalidOperationException("第二種錯誤"));

        // Act
        _clock.Advance(TimeSpan.FromSeconds(10));
        await HandleOnceAsync([created]);

        // Assert
        var failure = (await FailuresOfAsync(created.EventId)).Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure.Attempts, Is.EqualTo(1));
            Assert.That(failure.ErrorMessage, Is.EqualTo("第二種錯誤"));
        }
    }

    private static IEnumerable<TestCaseData> TransientErrors()
    {
        yield return new TestCaseData(new NpgsqlException("連線中斷", new System.IO.IOException("斷線"))).SetName("TransientNpgsqlException");
        yield return new TestCaseData(new TimeoutException("逾時")).SetName("TimeoutException");
        yield return new TestCaseData(new InvalidOperationException("EF 包的", new System.Net.Sockets.SocketException())).SetName("WrappedSocketException");
    }

    [TestCaseSource(nameof(TransientErrors))]
    public async Task Handle_TransientError_AbortsTheWholeRoundWithoutRecordingAFailure(Exception transient)
    {
        // Arrange：第一筆正常，第二筆碰到暫時性的錯誤。
        var (good, creator, assignee, _, _) = NewItem();
        var (flaky, flakyCreator, flakyAssignee, _, _) = NewItem();
        Host.Access.FailProject(flaky.ProjectId, transient);
        var first = good.Created(creator, assignee);
        var second = flaky.Created(flakyCreator, flakyAssignee);
        var before = await Host.Consumer.GetPositionAsync(CancellationToken.None);

        // Act
        var thrown = Assert.ThrowsAsync(transient.GetType(), () => HandleOnceAsync([first, second]));

        // Assert：整個交易退回，位置不動、第一筆的通知也沒寫進去、沒有失敗紀錄。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.SameAs(transient));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(before));
            Assert.That(await Host.KindsForAsync(assignee), Is.Empty);
            Assert.That(await Host.KindsForAsync(flakyAssignee), Is.Empty);
            Assert.That(await FailuresOfAsync(first.EventId, second.EventId), Is.Empty);
        }
    }

    [Test]
    public async Task Handle_TransientErrorClears_TheNextRoundHandlesEverything()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        Host.Access.FailProject(item.ProjectId, new TimeoutException("逾時"));
        var created = item.Created(creator, assignee);
        Assert.ThrowsAsync<TimeoutException>(() => HandleOnceAsync([created]));
        Host.Access.Heal(item.ProjectId);

        // Act
        await HandleOnceAsync([created]);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(created.Position));
            Assert.That(await FailuresOfAsync(created.EventId), Is.Empty);
        }
    }

    [Test]
    public async Task Handle_OneEventFailedOnce_TheOthersAreHandledRightAwayAndThePositionStopsBeforeIt()
    {
        // Arrange
        var (good, creator, assignee, _, _) = NewItem();
        var (poisoned, poisonCreator, poisonAssignee, _, _) = NewItem();
        var (after, afterCreator, afterAssignee, _, _) = NewItem();
        Host.Access.Poison(poisoned.ProjectId);
        var first = good.Created(creator, assignee);
        var middle = poisoned.Created(poisonCreator, poisonAssignee);
        var last = after.Created(afterCreator, afterAssignee);

        // Act
        await HandleRepeatedlyAsync(1, first, middle, last);

        // Assert：不用等到失敗上限，其他人的通知已經出來；位置不能越過還要重試的事件。
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(afterAssignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(first.Position));
            Assert.That((await FailuresOfAsync(middle.EventId)).Single().Attempts, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Handle_FailingEventIsFirstInTheBatch_PositionDoesNotMoveUntilItIsSkipped()
    {
        // Arrange
        var (poisoned, creator, assignee, _, _) = NewItem();
        var (good, goodCreator, goodAssignee, _, _) = NewItem();
        Host.Access.Poison(poisoned.ProjectId);
        var bad = poisoned.Created(creator, assignee);
        var fine = good.Created(goodCreator, goodAssignee);
        var before = await Host.Consumer.GetPositionAsync(CancellationToken.None);

        // Act
        await HandleRepeatedlyAsync(MaxAttempts - 1, bad, fine);
        var whileRetrying = await Host.Consumer.GetPositionAsync(CancellationToken.None);
        await HandleRepeatedlyAsync(1, bad, fine);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(whileRetrying, Is.EqualTo(before));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(fine.Position));
        }
    }

    [Test]
    public async Task Handle_EventFailsThenRecovers_IsHandledAndTheFailureStaysOnRecord()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        Host.Access.Poison(item.ProjectId);
        var created = item.Created(creator, assignee);
        await HandleRepeatedlyAsync(1, created);
        Host.Access.Heal(item.ProjectId);

        // Act
        await HandleRepeatedlyAsync(1, created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(created.Position));
            Assert.That((await FailuresOfAsync(created.EventId)).Single().Attempts, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Handle_EachFailure_IsLoggedAsErrorWithTheEventId()
    {
        // Arrange
        var (item, creator, assignee, _, _) = NewItem();
        Host.Access.Poison(item.ProjectId);
        var created = item.Created(creator, assignee);

        // Act
        await HandleRepeatedlyAsync(MaxAttempts, created);

        // Assert：每次失敗一筆 Error，帶事件編號與例外。
        var errors = Host.Logs.Entries
            .Where(e => e.Level == LogLevel.Error && Equals(e.Properties.GetValueOrDefault("EventId"), created.EventId))
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(errors, Has.Count.EqualTo(MaxAttempts));
            Assert.That(errors.Select(e => e.Exception), Has.All.InstanceOf<InvalidOperationException>());
        }
    }

    [Test]
    public async Task Handle_DatabaseErrorInOneEvent_RollsBackOnlyThatEventAndTheRestOfTheBatchCommits()
    {
        // Arrange：專案代號超過欄位長度，資料庫會拒絕這一筆（交易進入中止狀態，不退回 savepoint 就什麼都不能做）。
        var (good, creator, assignee, _, _) = NewItem();
        var (broken, brokenCreator, brokenAssignee, _, _) = NewItem();
        var rejected = broken.Created(brokenCreator, brokenAssignee);
        rejected = rejected with { Item = rejected.Item with { ProjectKey = new string('K', 100) } };
        var before = good.Created(creator, assignee);
        var (late, lateCreator, lateAssignee, _, _) = NewItem();
        var after = late.Created(lateCreator, lateAssignee);

        // Act
        await HandleRepeatedlyAsync(MaxAttempts, before, rejected, after);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(lateAssignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
            Assert.That(await Host.KindsForAsync(brokenAssignee), Is.Empty);
            Assert.That((await FailuresOfAsync(rejected.EventId)).Single().Attempts, Is.EqualTo(MaxAttempts));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(after.Position));
        }
    }

    [Test]
    public async Task Handle_UnknownKind_IsSkippedWithAWarningAndThePositionMovesOn()
    {
        // Arrange：新版寫的事件種類。
        var (item, creator, assignee, _, _) = NewItem();
        var unknown = item.Created(creator, assignee) with { Kind = WorkEventKind.Unknown };
        var known = item.Created(creator, assignee);

        // Act
        await HandleRepeatedlyAsync(1, unknown, known);

        // Assert
        var warning = Host.Logs.Entries.Single(e => e.Level == LogLevel.Warning && Equals(e.Properties.GetValueOrDefault("EventId"), unknown.EventId));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(warning.Message, Does.Contain(unknown.EventId.ToString()));
            Assert.That(await Host.Consumer.GetPositionAsync(CancellationToken.None), Is.EqualTo(known.Position));
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }), "只有認得的那一筆產生通知");
            Assert.That(await FailuresOfAsync(unknown.EventId), Is.Empty);
        }
    }

    [Test]
    public async Task Handle_ManyEventsOfOneProject_AsksTheWorkEngineOncePerOwnerPairNotPerEvent()
    {
        // Arrange：同一個專案的十個項目、各有建立與留言。看得到項目要看負責人與建立者（只看自己的角色），
        // 所以每一組（專案、建立者、負責人）問一次：這裡建立事件是一組、留言事件是另一組，各十筆，一共兩次，不是二十次。
        var organization = Guid.CreateVersion7();
        var project = Guid.CreateVersion7();
        Guid[] people = [.. Enumerable.Range(0, 4).Select(_ => Guid.CreateVersion7())];
        Host.Access.Allow(project, people);
        List<WorkEvent> events = [];
        foreach (var _ in Enumerable.Range(0, 10))
        {
            var item = new EventBuilder(organization, project, Guid.CreateVersion7());
            events.Add(item.Created(people[0], people[1]));
            events.Add(item.Commented(people[2], "留言", people[3]));
        }

        var callsBefore = Host.Access.FilterCalls;

        // Act
        await Host.Consumer.HandleAsync(events, CancellationToken.None);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Host.Access.FilterCalls - callsBefore, Is.EqualTo(2));
            Assert.That((await Host.KindsForAsync(people[1])).Count(kind => kind == NotificationKind.Assigned), Is.EqualTo(10));
            Assert.That((await Host.KindsForAsync(people[3])).Count(kind => kind == NotificationKind.Mentioned), Is.EqualTo(10));
        }
    }

    [Test]
    public async Task Handle_AnotherProcessHoldsTheLock_SkipsTheRoundWithoutWaitingAndHandlesItNextTime()
    {
        // Arrange：另一個連線拿著同一把 advisory lock（模擬別的容器正在處理）。
        var (item, creator, assignee, _, _) = NewItem();
        var created = item.Created(creator, assignee);
        var before = await Host.Consumer.GetPositionAsync(CancellationToken.None);
        await using var other = new NpgsqlConnection(Host.ConnectionString);
        await other.OpenAsync();
        await using var holder = await other.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({NotificationEventConsumer.LockKey})", other, holder))
        {
            await hold.ExecuteNonQueryAsync();
        }

        // Act：拿不到鎖就直接回來，不卡住。
        await HandleRepeatedlyAsync(1, created);
        var whileHeld = (await Host.KindsForAsync(assignee), await Host.Consumer.GetPositionAsync(CancellationToken.None));
        await holder.CommitAsync();
        await HandleRepeatedlyAsync(1, created);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(whileHeld.Item1, Is.Empty);
            Assert.That(whileHeld.Item2, Is.EqualTo(before));
            Assert.That(await Host.KindsForAsync(assignee), Is.EqualTo(new[] { NotificationKind.Assigned }));
        }
    }

    [Test]
    public async Task Handle_DeletedEventOfAnotherOrganization_DoesNotRemoveThisOrganizationsSubscriptions()
    {
        // Arrange：兩個組織剛好有同一個項目編號（正常不會發生，這是隔離的最後一道防線）。
        var (item, creator, _, _, _) = NewItem();
        await HandleAsync(item.Created(creator));
        var foreign = new EventBuilder(Guid.CreateVersion7(), item.ProjectId, item.ItemId);

        // Act
        await HandleAsync(foreign.Deleted(creator));

        // Assert
        Assert.That((await Host.AllSubscriptionsAsync()).Any(s => s.ItemId == item.ItemId && s.PersonId == creator), Is.True);
    }
}
