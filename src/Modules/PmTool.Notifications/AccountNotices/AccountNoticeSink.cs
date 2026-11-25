using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PmTool.Identity;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.AccountNotices;

/// <summary>
/// 身分模組的帳號事件（建立權杖、命令列登入、授權 AI 用戶端、登入碼被重複使用）變成本人收件匣的一則通知。
/// 事件是身分模組在交易提交之後才送來的盡力通知：這裡一律不丟例外，寫不進去、推送失敗都只記 Warning，不影響原本的操作。
/// 事件的編號就是稽核紀錄的編號，（事件、收件人、種類）唯一，同一件事送兩次只有一則。
/// </summary>
internal sealed partial class AccountNoticeSink(
    IDbContextFactory<NotificationsDbContext> contexts,
    IEnumerable<INotificationObserver> observers,
    ILogger<AccountNoticeSink> logger) : IAccountEventSink
{
    private const string UniqueViolation = "23505";

    public async Task PublishAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
    {
        try
        {
            if (!await TryStoreAsync(accountEvent, cancellationToken))
            {
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogStoreFailed(logger, accountEvent.EventId, exception);
            return;
        }

        await NotifyObserversAsync(accountEvent, cancellationToken);
    }

    /// <summary>存起來；回 false 是這個事件已經通知過了。</summary>
    private async Task<bool> TryStoreAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
    {
        var row = new NotificationRow
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = accountEvent.OrganizationId,
            RecipientId = accountEvent.PersonId,
            Kind = NotificationKind.AccountSecurity,
            ActorPersonId = accountEvent.PersonId,
            AccountNotice = AccountNoticeMapper.From(accountEvent),
            EventId = accountEvent.EventId,
            CreatedAt = accountEvent.OccurredAt,
        };

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        db.Notifications.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation } violation
            && violation.ConstraintName == NotificationsDbContext.NotificationEventIndex)
        {
            return false;
        }
    }

    /// <summary>通知已經存好才告訴觀察者（即時推送）；每個觀察者各自接住例外，一個壞掉不影響後面的。</summary>
    private async Task NotifyObserversAsync(AccountEvent accountEvent, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<NotifiedRecipient> recipients = [new NotifiedRecipient(accountEvent.OrganizationId, accountEvent.PersonId)];
        foreach (var observer in observers)
        {
            try
            {
                await observer.NotifiedAsync(recipients, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogObserverFailed(logger, observer.GetType().Name, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "帳號事件 {EventId} 沒能變成通知，略過。")]
    private static partial void LogStoreFailed(ILogger logger, Guid eventId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "帳號通知存好了，但通知觀察者 {Observer} 失敗，畫面晚一點才會更新。")]
    private static partial void LogObserverFailed(ILogger logger, string observer, Exception exception);
}
