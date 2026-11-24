using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Npgsql;
using PmTool.Notifications.Persistence;
using PmTool.Work;

namespace PmTool.Notifications.Events;

/// <summary>
/// 通知是工作引擎事件流的讀者。一批事件的處理（建立通知、自動關注）與「讀到哪裡」的位置在同一個交易裡，
/// 好幾個程序同時叫到時，交易層級的 advisory lock 讓同一時間只有一個在做（拿不到鎖就跳過這一輪，下一輪再來）；
/// 拿到鎖之後重新讀位置，別的程序剛處理過的事件略過。
/// 每筆事件各用一個 savepoint 隔離：一筆壞掉的事件只退回它自己，記進 <c>notifications.failed_events</c>，其他事件照常處理。
/// 確定的失敗（不是連線、逾時這類暫時性的）累積到 <see cref="NotificationsDbContext.MaxEventAttempts"/> 次、
/// 而且每次至少隔 <see cref="NotificationsDbContext.MinEventRetryInterval"/> 才略過、位置往前走；沒到之前位置停在它前面，下一輪重試
/// （它後面已經處理的事件到時會再看到一次，靠通知與關注的去重不會重複）。
/// 暫時性的錯誤不記失敗也不算次數：整輪往外丟、交易退回、位置不動，下一輪整批重試，免得資料庫或工作引擎短暫連不上就把事件永久略過。
/// </summary>
/// <summary>通知觀察者最多讓一批通知的處理等多久。測試換成很短的數字。</summary>
internal sealed record ObserverLimits(TimeSpan Timeout)
{
    public static ObserverLimits Default { get; } = new(TimeSpan.FromSeconds(10));
}

internal sealed partial class NotificationEventConsumer(
    IDbContextFactory<NotificationsDbContext> contexts,
    IWorkAccessQueries access,
    IWorkEventStream stream,
    TimeProvider clock,
    IEnumerable<INotificationObserver> observers,
    ObserverLimits observerLimits,
    ILogger<NotificationEventConsumer> logger)
    : IWorkEventConsumer
{
    public const string ConsumerName = "notifications";

    /// <summary>advisory lock 的鍵（固定值，內容是 "PMTLNOTF" 的位元組）。和授權用的鎖是不同的值。</summary>
    internal const long LockKey = 0x504D544C_4E4F5446;

    private const string SavepointName = "notification_event";

    public string Name => ConsumerName;

    public async Task<WorkEventPosition> GetPositionAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await ReadPositionAsync(db, cancellationToken);
    }

    /// <summary>
    /// 讀自己存的位置，先請事件流換算成現在這個資料庫的位置（還原到新的資料庫之後，存的是舊的編號，排在所有新事件後面）；
    /// 換算後不同，就把換算後的存回去。
    /// </summary>
    private async Task<WorkEventPosition> ReadPositionAsync(NotificationsDbContext db, CancellationToken cancellationToken)
    {
        var stored = await ReadStoredPositionAsync(db, cancellationToken);
        var resolved = await stream.ResolvePositionAsync(stored, cancellationToken);
        if (resolved != stored)
        {
            await SavePositionAsync(db, resolved, cancellationToken);
        }

        return resolved;
    }

    public async Task HandleAsync(IReadOnlyList<WorkEvent> events, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
        {
            return;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // 拿不到鎖就是別的程序正在做：跳過這一輪，不要卡在這裡等（等鎖沒有逾時）。
        var locked = await db.Database
            .SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock({LockKey}) AS \"Value\"")
            .ToListAsync(cancellationToken);
        if (!locked.Single())
        {
            return;
        }

        var position = await ReadPositionAsync(db, cancellationToken);
        WorkEvent[] fresh = [.. events.Where(e => e.Position.CompareTo(position) > 0).OrderBy(e => e.Position)];
        if (fresh.Length == 0)
        {
            return;
        }

        var readers = new ReaderCache(access);
        await SeedReadersAsync(db, readers, fresh, cancellationToken);
        var applier = new EventApplier(db, readers, await LoadExistingAsync(db, fresh, cancellationToken));

        var advanceTo = await ApplyAllAsync(db, transaction, applier, fresh, cancellationToken);
        if (advanceTo is not null)
        {
            await SavePositionAsync(db, advanceTo, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        await NotifyObserversAsync(applier.NotifiedRecipients, cancellationToken);
    }

    /// <summary>
    /// 通知已經提交之後才告訴觀察者（即時推送）；它們出錯只是畫面晚一點更新，不能讓這一批變成失敗而重來。
    /// 每一個觀察者各自接住例外，一個壞掉不影響後面的。
    /// </summary>
    private async Task NotifyObserversAsync(IReadOnlyCollection<NotifiedRecipient> recipients, CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var observer in observers)
        {
            // 每個觀察者有自己的期限：它卡住（例如推送給一條不讀資料的連線）不能拖住整個通知的處理，後面的批次還在等。
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(observerLimits.Timeout);
            try
            {
                await observer.NotifiedAsync(recipients, deadline.Token).WaitAsync(deadline.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LogObserverTimedOut(observer.GetType().Name, observerLimits.Timeout.TotalSeconds);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                LogObserverFailed(exception, observer.GetType().Name);
            }
        }
    }

    /// <summary>處理整批，回傳位置可以記到哪裡；一筆都不能往前（第一筆就要重試）時回 null。</summary>
    private async Task<WorkEventPosition?> ApplyAllAsync(
        NotificationsDbContext db, IDbContextTransaction transaction, EventApplier applier, WorkEvent[] fresh, CancellationToken cancellationToken)
    {
        WorkEventPosition? advanceTo = null;
        var blocked = false;
        foreach (var workEvent in fresh)
        {
            var settled = await TryApplyAsync(db, transaction, applier, workEvent, cancellationToken);

            // 還要重試的事件擋在位置前面；它後面的事件照常處理，但位置不能越過它。
            blocked |= !settled;
            if (!blocked)
            {
                advanceTo = workEvent.Position;
            }
        }

        return advanceTo;
    }

    private static bool IsCommentEdit(WorkEventKind kind) =>
        kind is WorkEventKind.ItemCommentEdited or WorkEventKind.ItemCommentDeleted or WorkEventKind.ItemCommentRestored;

    /// <summary>true：處理好了，或已經失敗太多次、決定略過；false：這一輪失敗，下一輪重試。</summary>
    private async Task<bool> TryApplyAsync(
        NotificationsDbContext db, IDbContextTransaction transaction, EventApplier applier, WorkEvent workEvent, CancellationToken cancellationToken)
    {
        if (workEvent.Kind == WorkEventKind.Unknown)
        {
            // 新版寫的、這一版不認得：略過，位置照樣往前（合約的規矩）。
            LogUnknownKind(logger, workEvent.EventId, workEvent.Position.TransactionId, workEvent.Position.Sequence);
            return true;
        }

        if (IsCommentEdit(workEvent.Kind) && workEvent.Item.ActivityId is null)
        {
            // 不應該發生（工作引擎每個編輯都帶留言的編號）；沒有編號就不知道是哪一則，略過、位置照樣往前。
            LogCommentEventWithoutActivity(logger, workEvent.Kind.ToString(), workEvent.EventId);
            return true;
        }

        await transaction.CreateSavepointAsync(SavepointName, cancellationToken);
        try
        {
            await applier.ApplyAsync(workEvent, cancellationToken);
            await transaction.ReleaseSavepointAsync(SavepointName, cancellationToken);
            return true;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && !IsTransient(exception))
        {
            // 退回這一筆做到一半的寫入（資料庫出錯時交易已經是中止狀態，不退回就什麼都不能做），再記下失敗。
            await transaction.RollbackToSavepointAsync(SavepointName, cancellationToken);
            db.ChangeTracker.Clear();
            var attempts = await RecordFailureAsync(db, workEvent, exception, clock.GetUtcNow(), cancellationToken);
            if (attempts < NotificationsDbContext.MaxEventAttempts)
            {
                LogEventFailed(logger, exception, workEvent.EventId, attempts, NotificationsDbContext.MaxEventAttempts);
                return false;
            }

            LogEventSkipped(logger, exception, workEvent.EventId, attempts);
            return true;
        }
    }

    /// <summary>
    /// 連線、逾時這類過一會兒就會好的錯誤。它們不是事件本身的問題，記成失敗會讓一段短暫的故障
    /// 把一整批事件耗到上限而永久略過（通知就漏掉了），所以交給呼叫端整輪重試。
    /// EF 常把底層的例外包一層，所以內層每一層都看。
    /// </summary>
    internal static bool IsTransient(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true } or TimeoutException or System.Net.Sockets.SocketException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 記下失敗並回傳目前的次數。<c>last_failed_at</c> 是「上一次算進次數的失敗」的時間：距離它不到間隔的失敗只更新錯誤訊息，
    /// 次數與時間都不動，所以短時間的連續失敗不會把事件耗到上限。
    /// </summary>
    private static async Task<int> RecordFailureAsync(
        NotificationsDbContext db, WorkEvent workEvent, Exception exception, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var type = Shorten(exception.GetType().FullName ?? exception.GetType().Name, NotificationsDbContext.MaxErrorTypeLength);
        var message = Shorten(exception.Message, NotificationsDbContext.MaxErrorMessageLength);
        var counts = now - NotificationsDbContext.MinEventRetryInterval;
        var attempts = await db.Database.SqlQuery<int>($"""
            INSERT INTO notifications.failed_events
                (event_id, organization_id, transaction_id, sequence, error_type, error_message, attempts, last_failed_at)
            VALUES ({workEvent.EventId}, {workEvent.OrganizationId}, {workEvent.Position.TransactionId}, {workEvent.Position.Sequence},
                    {type}, {message}, 1, {now})
            ON CONFLICT (event_id) DO UPDATE SET
                attempts = CASE WHEN failed_events.last_failed_at <= {counts}
                                THEN failed_events.attempts + 1 ELSE failed_events.attempts END,
                last_failed_at = CASE WHEN failed_events.last_failed_at <= {counts}
                                      THEN EXCLUDED.last_failed_at ELSE failed_events.last_failed_at END,
                error_type = EXCLUDED.error_type,
                error_message = EXCLUDED.error_message
            RETURNING attempts AS "Value"
            """).ToListAsync(cancellationToken);
        return attempts.Single();
    }

    /// <summary>去掉 NUL（PostgreSQL 的文字欄位不收），截到上限而且不從代理字元對的中間切開。</summary>
    private static string Shorten(string text, int maxLength)
    {
        var clean = text.Replace("\0", string.Empty, StringComparison.Ordinal);
        if (clean.Length <= maxLength)
        {
            return clean;
        }

        return clean[..(char.IsHighSurrogate(clean[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }

    /// <summary>
    /// 每個專案（只看自己的角色再加上項目的負責人與建立者）先用整批會碰到的人問一次（事件裡提到的人，加上這些項目現有的關注者）。
    /// 問不到（工作引擎出錯）不在這裡處理：之後每個事件自己會再問、各自失敗，不會因為一個專案拖累整批。
    /// </summary>
    private async Task SeedReadersAsync(
        NotificationsDbContext db, ReaderCache readers, WorkEvent[] fresh, CancellationToken cancellationToken)
    {
        var itemIds = fresh.Select(e => e.Item.ItemId).Distinct().ToArray();
        var organizationIds = fresh.Select(e => e.OrganizationId).Distinct().ToArray();
        var followers = await db.Subscriptions.AsNoTracking()
            .Where(s => itemIds.Contains(s.ItemId) && organizationIds.Contains(s.OrganizationId))
            .Select(s => new { s.ItemId, s.PersonId })
            .ToListAsync(cancellationToken);

        foreach (var project in fresh.Where(e => e.Kind != WorkEventKind.Unknown).GroupBy(e => (e.OrganizationId, e.Item.ProjectId)))
        {
            var items = project.Select(e => e.Item.ItemId).ToHashSet();
            var people = project
                .SelectMany(e => PeopleOf(e))
                .Concat(followers.Where(f => items.Contains(f.ItemId)).Select(f => f.PersonId))
                .ToHashSet();

            // 看得到項目看負責人與建立者，每一組各問一次；問的是整個專案會碰到的人，事件之後才補上的關注者就不必再問。
            foreach (var ownership in project.Select(e => new ItemOwnership(e.Item.CreatedBy, e.Item.AssigneeId)).Distinct())
            {
                try
                {
                    await readers.SeedAsync(project.Key.OrganizationId, project.Key.ProjectId, ownership, people, cancellationToken);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    LogSeedFailed(exception);
                }
            }
        }
    }

    private static IEnumerable<Guid> PeopleOf(WorkEvent workEvent)
    {
        var item = workEvent.Item;
        yield return workEvent.ActorPersonId;
        yield return item.CreatedBy;
        if (item.AssigneeId is { } assignee)
        {
            yield return assignee;
        }

        foreach (var mentioned in item.MentionedPersonIds)
        {
            yield return mentioned;
        }

        foreach (var follower in item.Followers)
        {
            yield return follower;
        }
    }

    private static async Task<HashSet<(Guid, Guid, NotificationKind)>> LoadExistingAsync(
        NotificationsDbContext db, WorkEvent[] events, CancellationToken cancellationToken)
    {
        Guid[] ids = [.. events.Select(e => e.EventId)];
        var rows = await db.Notifications.AsNoTracking()
            .Where(n => ids.Contains(n.EventId))
            .Select(n => new { n.EventId, n.RecipientId, n.Kind })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => (row.EventId, row.RecipientId, row.Kind))];
    }

    private static async Task<WorkEventPosition> ReadStoredPositionAsync(NotificationsDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.ReaderPositions.AsNoTracking().SingleOrDefaultAsync(p => p.Name == ConsumerName, cancellationToken);
        return row is null ? WorkEventPosition.Start : new WorkEventPosition(row.TransactionId, row.Sequence);
    }

    private static async Task SavePositionAsync(NotificationsDbContext db, WorkEventPosition position, CancellationToken cancellationToken)
    {
        var updated = await db.ReaderPositions
            .Where(p => p.Name == ConsumerName)
            .ExecuteUpdateAsync(set => set
                .SetProperty(p => p.TransactionId, position.TransactionId)
                .SetProperty(p => p.Sequence, position.Sequence), cancellationToken);
        if (updated == 0)
        {
            db.ReaderPositions.Add(new ReaderPositionRow
            {
                Name = ConsumerName,
                TransactionId = position.TransactionId,
                Sequence = position.Sequence,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "通知處理事件 {EventId} 失敗（第 {Attempt} 次，最多 {MaxAttempts} 次），下一輪重試；其他事件照常處理。")]
    private static partial void LogEventFailed(ILogger logger, Exception exception, Guid eventId, int attempt, int maxAttempts);

    [LoggerMessage(Level = LogLevel.Error, Message = "通知處理事件 {EventId} 已經失敗 {Attempt} 次，略過它；失敗紀錄在 notifications.failed_events。")]
    private static partial void LogEventSkipped(ILogger logger, Exception exception, Guid eventId, int attempt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "略過不認得的事件種類（事件 {EventId}，位置 {TransactionId}/{Sequence}）：可能是新版寫的，升級之後才會處理。")]
    private static partial void LogUnknownKind(ILogger logger, Guid eventId, long transactionId, long sequence);

    [LoggerMessage(Level = LogLevel.Warning, Message = "留言事件 {Kind}（事件 {EventId}）沒有留言的編號，略過它。")]
    private static partial void LogCommentEventWithoutActivity(ILogger logger, string kind, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "通知已經存好，但通知觀察者「{Observer}」超過 {Seconds} 秒還沒做完，不等它了。")]
    private partial void LogObserverTimedOut(string observer, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "通知已經存好，但通知觀察者「{Observer}」失敗；收件匣要等下一次重抓才會更新。")]
    private partial void LogObserverFailed(Exception exception, string observer);

    [LoggerMessage(Level = LogLevel.Debug, Message = "預先查專案成員失敗，改由各事件自己再查。")]
    private partial void LogSeedFailed(Exception exception);
}
