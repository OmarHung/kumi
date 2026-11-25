using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>
/// 把該寄的通知寄出去（規格見 HANDOFF-email-notifications 第 4 節）。好幾個程序同時跑時，靠一條專用連線上的
/// <c>pg_try_advisory_lock</c> 讓同一時間只有一個在寄：寄信要好幾秒，不用長交易包住整輪，
/// 否則交易裡改過的通知列會擋住收件匣的「標成已讀」。每個步驟各自短短地寫資料庫。
/// 寄出去了、標記卻失敗（資料庫斷了）時，下一輪會再寄一次：寧可重複也不要漏。
/// </summary>
internal sealed partial class EmailDispatcher(
    IDbContextFactory<NotificationsDbContext> contexts,
    NotificationVisibility visibility,
    IEmailTransport transport,
    EmailPasswordProtector protector,
    PmTool.Identity.IPeopleDirectory people,
    IOrganizationClock organizationClock,
    TimeProvider clock,
    ILogger<EmailDispatcher> logger) : IEmailDispatcher
{
    /// <summary>advisory lock 的鍵（固定值，內容是 "PMTLEMAL" 的位元組）。和通知讀者的鎖、授權用的鎖是不同的值。</summary>
    internal const long LockKey = 0x504D544C_454D414C;

    internal const string InternalErrorMessage = "寄送時發生內部錯誤，下一輪再試。";

    internal const string UndecryptablePasswordMessage = "存的密碼解不開，請重新輸入";

    /// <summary>連續失敗之後等多久再試：1、2、5、10、30 分鐘，之後一直是 30 分鐘。</summary>
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

    public async Task<int> DispatchAsync(CancellationToken cancellationToken)
    {
        // 鎖綁在連線上：這條連線只拿來持有鎖，其他的讀寫各自用別的連線。
        await using var lockDb = await contexts.CreateDbContextAsync(cancellationToken);
        await lockDb.Database.OpenConnectionAsync(cancellationToken);
        var locked = await lockDb.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({LockKey}) AS \"Value\"").ToListAsync(cancellationToken);
        if (!locked.Single())
        {
            return 0;
        }

        try
        {
            return await RunAsync(cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync(lockDb);
        }
    }

    private async Task ReleaseLockAsync(NotificationsDbContext lockDb)
    {
        try
        {
            await lockDb.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({LockKey})", CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 連線斷了的話鎖會跟著釋放，不必再處理；記下來方便查。
            LogUnlockFailed(logger, exception);
        }
    }

    private async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await MarkUnneededHandledAsync(cancellationToken);

        var now = clock.GetUtcNow();
        List<EmailSettingsRow> organizations;
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            organizations = await db.EmailSettings.AsNoTracking()
                .Where(s => s.Enabled && (s.RetryAfter == null || s.RetryAfter <= now))
                .ToListAsync(cancellationToken);
        }

        var sent = 0;
        foreach (var settings in organizations)
        {
            sent += await DispatchOrganizationSafelyAsync(settings, cancellationToken);
        }

        return sent;
    }

    /// <summary>已經讀了的、組織沒開寄信的、太舊的：不必寄，標成處理完了。帳號通知讀了照寄（讀的人可能不是本人），其餘照舊。</summary>
    private async Task MarkUnneededHandledAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var oldest = now - EmailRules.MaxAge;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.Notifications
            .Where(n => n.EmailHandledAt == null
                && ((n.ReadAt != null && n.Kind != NotificationKind.AccountSecurity)
                    || n.CreatedAt < oldest
                    || !db.EmailSettings.Any(s => s.OrganizationId == n.OrganizationId && s.Enabled)))
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.EmailHandledAt, now), cancellationToken);
    }

    /// <summary>一個組織出了意外（不是寄信失敗）：記下來、走和連線失敗一樣的退避，不讓它擋住後面的組織。</summary>
    private async Task<int> DispatchOrganizationSafelyAsync(EmailSettingsRow settings, CancellationToken cancellationToken)
    {
        try
        {
            return await DispatchOrganizationAsync(settings, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogOrganizationCrashed(logger, settings.OrganizationId, exception);
            await RecordFailureAsync(settings, InternalErrorMessage, cancellationToken);
            return 0;
        }
    }

    private async Task<int> DispatchOrganizationAsync(EmailSettingsRow settings, CancellationToken cancellationToken)
    {
        var recipients = await DueRecipientsAsync(settings.OrganizationId, cancellationToken);
        if (recipients.Count == 0)
        {
            return 0;
        }

        string? password = null;
        if (settings.ProtectedPassword is { } stored && !protector.TryUnprotect(stored, out password))
        {
            await RecordFailureAsync(settings, UndecryptablePasswordMessage, cancellationToken);
            return 0;
        }

        var connection = EmailSenderIdentity.ConnectionOf(
            settings.Provider, settings.Host, settings.Port, settings.Security, settings.Username, password);
        var zone = await organizationClock.TimeZoneAsync(settings.OrganizationId, cancellationToken);
        var sender = (Address: EmailSenderIdentity.FromAddressOf(settings.Provider, settings.Username, settings.FromAddress), settings.FromName);

        IEmailSession? session = null;
        var sent = 0;
        try
        {
            foreach (var recipientId in recipients)
            {
                foreach (var prepared in await PrepareAsync(settings, recipientId, sender, zone, cancellationToken))
                {
                    session ??= await transport.OpenAsync(connection, cancellationToken);
                    if (await TrySendAsync(session, settings, prepared, cancellationToken))
                    {
                        sent++;
                    }
                }
            }
        }
        catch (EmailDeliveryException exception)
        {
            LogOrganizationFailed(logger, settings.OrganizationId, exception.Kind, connection.ToString());
            await RecordFailureAsync(settings, exception.Describe(NotificationsDbContext.MaxEmailErrorLength), cancellationToken);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
        }

        return sent;
    }

    /// <summary>
    /// 寄一封。只和這個收件人、這封信有關的失敗只略過他：永久的（拒收、地址不對、DATA 被拒）標成處理完了，
    /// 暫時的（4xx）通知照舊在等、下一輪再試；其他的失敗往外丟，由組織層級處理。
    /// </summary>
    private async Task<bool> TrySendAsync(
        IEmailSession session, EmailSettingsRow settings, PreparedEmail prepared, CancellationToken cancellationToken)
    {
        try
        {
            await session.SendAsync(prepared.Email, cancellationToken);
        }
        catch (EmailDeliveryException exception)
            when (exception.Kind is EmailFailureKind.RecipientRejected or EmailFailureKind.InvalidRecipient or EmailFailureKind.MessageRejected)
        {
            LogRecipientRejected(logger, settings.OrganizationId, prepared.RecipientId, exception.Kind);
            await MarkHandledAsync(prepared.NotificationIds, cancellationToken);
            return false;
        }
        catch (EmailDeliveryException exception) when (exception.Kind == EmailFailureKind.RecipientTemporarilyFailed)
        {
            LogRecipientDeferred(logger, settings.OrganizationId, prepared.RecipientId);
            return false;
        }

        await MarkHandledAsync(prepared.NotificationIds, cancellationToken);
        await RecordSuccessAsync(settings.OrganizationId, cancellationToken);
        return true;
    }

    private async Task RecordSuccessAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.EmailSettings.Where(s => s.OrganizationId == organizationId).ExecuteUpdateAsync(
            set => set
                .SetProperty(s => s.LastSentAt, now)
                .SetProperty(s => s.ConsecutiveFailures, 0)
                .SetProperty(s => s.RetryAfter, (DateTimeOffset?)null),
            cancellationToken);
    }

    private async Task RecordFailureAsync(EmailSettingsRow settings, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var failures = settings.ConsecutiveFailures + 1;
        var retryAfter = now + Backoff[Math.Min(failures, Backoff.Length) - 1];
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await db.EmailSettings.Where(s => s.OrganizationId == settings.OrganizationId).ExecuteUpdateAsync(
            set => set
                .SetProperty(s => s.LastFailedAt, now)
                .SetProperty(s => s.LastError, reason)
                .SetProperty(s => s.ConsecutiveFailures, failures)
                .SetProperty(s => s.RetryAfter, (DateTimeOffset?)retryAfter),
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "組織 {OrganizationId} 的 Email 寄不出去（{Kind}，{Endpoint}），稍後再試。")]
    private static partial void LogOrganizationFailed(ILogger logger, Guid organizationId, EmailFailureKind kind, string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "組織 {OrganizationId} 的收件人 {PersonId} 寄不了（{Kind}），略過他的通知。")]
    private static partial void LogRecipientRejected(ILogger logger, Guid organizationId, Guid personId, EmailFailureKind kind);

    [LoggerMessage(Level = LogLevel.Warning, Message = "組織 {OrganizationId} 的收件人 {PersonId} 暫時收不了，這一輪略過，通知照舊在等。")]
    private static partial void LogRecipientDeferred(ILogger logger, Guid organizationId, Guid personId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "組織 {OrganizationId} 的收件人 {PersonId} 的 Email 地址寄不出去，略過他的通知。")]
    private static partial void LogInvalidAddress(ILogger logger, Guid organizationId, Guid personId);

    [LoggerMessage(Level = LogLevel.Error, Message = "組織 {OrganizationId} 的 Email 寄送發生內部錯誤，這個組織稍後再試。")]
    private static partial void LogOrganizationCrashed(ILogger logger, Guid organizationId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "放掉寄信的鎖失敗，連線斷了的話鎖會自己釋放。")]
    private static partial void LogUnlockFailed(ILogger logger, Exception exception);
}
