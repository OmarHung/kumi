using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PmTool.Abstractions;
using PmTool.Notifications.Persistence;

namespace PmTool.Notifications.Email;

/// <summary>
/// 組織的寄信設定。只有組織管理員能讀、能改；密碼加密後才存，也不會讀出來（只說有沒有）。
/// 存的時候用組織層級的 advisory lock 排隊：兩個管理員同時存，不會互相蓋掉一半。
/// </summary>
internal sealed partial class EmailSettingsService(
    IDbContextFactory<NotificationsDbContext> contexts,
    EmailPasswordProtector protector,
    IEmailTransport transport,
    TimeProvider clock,
    ILogger<EmailSettingsService> logger) : IEmailSettingsService
{
    public async Task<Result<EmailSettings>> GetAsync(Actor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (Forbidden(actor) is { } forbidden)
        {
            return forbidden;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        return row is null ? Empty : ToSettings(row);
    }

    public async Task<Result<EmailSettings>> SaveAsync(Actor actor, EmailSettingsInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        if (Forbidden(actor) is { } forbidden)
        {
            return forbidden;
        }

        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"email_settings:" + actor.OrganizationId}, 0))", cancellationToken);

        var existing = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        var validated = EmailSettingsValidator.Validate(input, requireFields: false, existing);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        var row = ToRow(actor, validated.Value, existing);
        if (row.Enabled && existing?.Enabled != true)
        {
            // 剛開啟：沒開寄信的期間產生的通知不補寄，在同一次寫入裡標成處理完了，不等背景工作跑到。
            var now = row.UpdatedAt;
            await db.Notifications
                .Where(n => n.OrganizationId == actor.OrganizationId && n.EmailHandledAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.EmailHandledAt, now), cancellationToken);
        }

        if (existing is null)
        {
            db.EmailSettings.Add(row);
        }
        else
        {
            db.EmailSettings.Update(row);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToSettings(row);
    }

    public async Task<Result<Unit>> SendTestAsync(
        Actor actor, EmailSettingsInput input, string recipientAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientAddress);
        if (Forbidden(actor) is { } forbidden)
        {
            return forbidden;
        }

        EmailSettingsRow? existing;
        await using (var db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            existing = await db.EmailSettings.AsNoTracking().SingleOrDefaultAsync(s => s.OrganizationId == actor.OrganizationId, cancellationToken);
        }

        var validated = EmailSettingsValidator.Validate(input, requireFields: true, existing);
        if (validated.Failure is { } invalid)
        {
            return invalid;
        }

        var settings = validated.Value;
        var password = ResolvePassword(settings, existing);
        if (password.Failure is { } undecryptable)
        {
            return undecryptable;
        }

        var connection = EmailSenderIdentity.ConnectionOf(
            settings.Provider, settings.Host, settings.Port, settings.Security, settings.Username, password.Value);
        var email = EmailComposer.Test(
            EmailSenderIdentity.FromAddressOf(settings.Provider, settings.Username, settings.FromAddress), settings.FromName, recipientAddress, settings.SiteUrl);
        try
        {
            await using var session = await transport.OpenAsync(connection, cancellationToken);
            await session.SendAsync(email, cancellationToken);
            return Unit.Value;
        }
        catch (EmailDeliveryException exception)
        {
            LogTestFailed(logger, exception.Kind, connection.ToString());
            return new Failure(
                FailureKind.Invalid, NotificationErrorCodes.EmailSendFailed, exception.Describe(NotificationsDbContext.MaxEmailErrorLength));
        }
    }

    private Result<string?> ResolvePassword(ValidatedEmailSettings settings, EmailSettingsRow? existing)
    {
        switch (settings.PasswordChange)
        {
            case PasswordChange.Replace:
                return settings.NewPassword;
            case PasswordChange.Clear:
                return (string?)null;
            default:
                if (existing?.ProtectedPassword is not { } stored)
                {
                    return (string?)null;
                }

                return protector.TryUnprotect(stored, out var plain)
                    ? plain
                    : new Failure(FailureKind.Invalid, NotificationErrorCodes.EmailSendFailed, EmailDispatcher.UndecryptablePasswordMessage);
        }
    }

    private EmailSettingsRow ToRow(Actor actor, ValidatedEmailSettings settings, EmailSettingsRow? existing)
    {
        var protectedPassword = settings.PasswordChange switch
        {
            PasswordChange.Replace => protector.Protect(settings.NewPassword!),
            PasswordChange.Clear => null,
            _ => existing?.ProtectedPassword,
        };

        return new EmailSettingsRow
        {
            OrganizationId = actor.OrganizationId,
            Enabled = settings.Enabled,
            Provider = settings.Provider,
            Host = settings.Host,
            Port = settings.Port,
            Security = settings.Security,
            Username = settings.Username,
            ProtectedPassword = protectedPassword,
            FromAddress = settings.FromAddress,
            FromName = settings.FromName,
            SiteUrl = settings.SiteUrl,
            UpdatedAt = clock.GetUtcNow(),
            UpdatedBy = actor.PersonId,
            LastSentAt = existing?.LastSentAt,
            LastFailedAt = existing?.LastFailedAt,
            LastError = existing?.LastError,

            // 剛改過設定：不必再等上一次失敗留下的退避時間，下一輪就用新的設定試。
            ConsecutiveFailures = 0,
            RetryAfter = null,
        };
    }

    private static EmailSettings ToSettings(EmailSettingsRow row) => new(
        row.Enabled,
        row.Provider,
        row.Host,
        row.Port,
        row.Security,
        row.Username,
        row.ProtectedPassword is not null,
        row.FromAddress,
        row.FromName,
        row.SiteUrl,
        new EmailDeliveryStatus(row.LastSentAt, row.LastFailedAt, row.LastError),
        row.UpdatedAt,
        row.UpdatedBy);

    private static readonly EmailSettings Empty = new(
        false, EmailProvider.Gmail, null, null, null, null, false, null, null, null, new EmailDeliveryStatus(null, null, null), null, null);

    private static Failure? Forbidden(Actor actor) => actor.IsOrganizationAdmin
        ? null
        : new Failure(FailureKind.Forbidden, NotificationErrorCodes.EmailForbidden, "只有組織的擁有者或管理員能看、能改寄信設定。");

    [LoggerMessage(Level = LogLevel.Information, Message = "測試信寄不出去（{Kind}，{Endpoint}）。")]
    private static partial void LogTestFailed(ILogger logger, EmailFailureKind kind, string endpoint);
}
