using Microsoft.EntityFrameworkCore;

namespace PmTool.Notifications.Persistence;

internal sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>事件的唯一限制。重複處理同一個事件時靠它擋下重複的通知，所以名稱固定下來。</summary>
    public const string NotificationEventIndex = "ux_notifications_event_recipient_kind";

    /// <summary>同一筆事件「確定的失敗」到第幾次就略過它（暫時性的錯誤不算）。</summary>
    public const int MaxEventAttempts = 3;

    /// <summary>
    /// 同一筆事件的兩次失敗至少隔這麼久，後一次才算進次數。背景工作每 2 秒一輪，
    /// 不隔開的話，一段短暫的故障就會把事件在幾秒內耗到上限而被永久略過。
    /// </summary>
    public static readonly TimeSpan MinEventRetryInterval = TimeSpan.FromMinutes(1);

    public const int MaxErrorTypeLength = 200;

    public const int MaxErrorMessageLength = 500;

    private const int MaxReaderNameLength = 100;

    private const int MaxKeyLength = 64;

    private const int MaxKindLength = 32;

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    public DbSet<NotificationRow> Notifications => Set<NotificationRow>();

    public DbSet<ReaderPositionRow> ReaderPositions => Set<ReaderPositionRow>();

    public DbSet<FailedEventRow> FailedEvents => Set<FailedEventRow>();

    public DbSet<EmailSettingsRow> EmailSettings => Set<EmailSettingsRow>();

    public DbSet<EmailPreferenceRow> EmailPreferences => Set<EmailPreferenceRow>();

    /// <summary>寄送失敗的原因最多存這麼長（我們的說明加上對方主機回的一小段話）。</summary>
    public const int MaxEmailErrorLength = 500;

    /// <summary>執行時與設計時（產生遷移）共用同一套設定，兩邊才不會產生不一樣的模型。</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SubscriptionRow>(subscription =>
        {
            subscription.ToTable("subscriptions");
            subscription.HasKey(s => new { s.ItemId, s.PersonId });
            subscription.Property(s => s.Reason)
                .HasMaxLength(MaxKindLength)
                .HasConversion(reason => reason.ToString().ToLowerInvariant(), name => Enum.Parse<SubscriptionReason>(name, true));
        });

        modelBuilder.Entity<NotificationRow>(notification =>
        {
            notification.ToTable("notifications");
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.Kind)
                .HasMaxLength(MaxKindLength)
                .HasConversion(kind => NotificationKinds.NameOf(kind), name => NotificationKinds.Parse(name));
            notification.Property(n => n.ProjectKey).HasMaxLength(MaxKeyLength);
            notification.Property(n => n.ItemKey).HasMaxLength(MaxKeyLength);
            notification.HasIndex(n => new { n.EventId, n.RecipientId, n.Kind }).IsUnique().HasDatabaseName(NotificationEventIndex);

            // 收件匣：某個人的通知，新的在前。
            notification.HasIndex(n => new { n.OrganizationId, n.RecipientId, n.CreatedAt, n.Id })
                .IsDescending(false, false, true, true);

            // 未讀數與「只看未讀」只需要掃還沒讀的。
            notification.HasIndex(n => new { n.OrganizationId, n.RecipientId })
                .HasFilter("read_at IS NULL")
                .HasDatabaseName("ix_notifications_unread");

            // 寄 Email 的背景工作只掃還在等的。
            notification.HasIndex(n => new { n.OrganizationId, n.CreatedAt })
                .HasFilter("email_handled_at IS NULL")
                .HasDatabaseName("ix_notifications_email_pending");
        });

        modelBuilder.Entity<EmailSettingsRow>(settings =>
        {
            settings.ToTable("email_settings");
            settings.HasKey(s => s.OrganizationId);
            settings.Property(s => s.OrganizationId).ValueGeneratedNever();
            settings.Property(s => s.Provider)
                .HasMaxLength(MaxKindLength)
                .HasConversion(provider => EmailEnumNames.NameOf(provider), name => EmailEnumNames.ParseProvider(name));
            settings.Property(s => s.Security)
                .HasMaxLength(MaxKindLength)
                .HasConversion(
                    security => security == null ? null : EmailEnumNames.NameOf(security.Value),
                    name => name == null ? null : EmailEnumNames.ParseSecurity(name));
            settings.Property(s => s.Host).HasMaxLength(EmailRules.MaxHostLength);
            settings.Property(s => s.Username).HasMaxLength(EmailRules.MaxUsernameLength);
            settings.Property(s => s.FromAddress).HasMaxLength(EmailRules.MaxUsernameLength);
            settings.Property(s => s.FromName).HasMaxLength(EmailRules.MaxFromNameLength);
            settings.Property(s => s.SiteUrl).HasMaxLength(EmailRules.MaxSiteUrlLength);
            settings.Property(s => s.LastError).HasMaxLength(MaxEmailErrorLength);
        });

        modelBuilder.Entity<EmailPreferenceRow>(preference =>
        {
            preference.ToTable("email_preferences");
            preference.HasKey(p => new { p.OrganizationId, p.PersonId });
        });

        modelBuilder.Entity<FailedEventRow>(failed =>
        {
            failed.ToTable("failed_events");
            failed.HasKey(f => f.EventId);
            failed.Property(f => f.EventId).ValueGeneratedNever();
            failed.Property(f => f.ErrorType).HasMaxLength(MaxErrorTypeLength);
            failed.Property(f => f.ErrorMessage).HasMaxLength(MaxErrorMessageLength);
            failed.HasIndex(f => new { f.OrganizationId, f.LastFailedAt });
        });

        modelBuilder.Entity<ReaderPositionRow>(position =>
        {
            position.ToTable("reader_positions");
            position.HasKey(p => p.Name);
            position.Property(p => p.Name).HasMaxLength(MaxReaderNameLength);
        });
    }
}

/// <summary>通知的種類存成 snake_case 的字串，和對外的寫法一致，直接查資料表也看得懂。</summary>
internal static class NotificationKinds
{
    public static string NameOf(NotificationKind kind) => kind switch
    {
        NotificationKind.Assigned => "assigned",
        NotificationKind.Mentioned => "mentioned",
        NotificationKind.Commented => "commented",
        NotificationKind.StateChanged => "state_changed",
        NotificationKind.Replied => "replied",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不認得的通知種類。"),
    };

    public static NotificationKind Parse(string name) => name switch
    {
        "assigned" => NotificationKind.Assigned,
        "mentioned" => NotificationKind.Mentioned,
        "commented" => NotificationKind.Commented,
        "state_changed" => NotificationKind.StateChanged,
        "replied" => NotificationKind.Replied,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "不認得的通知種類。"),
    };
}

/// <summary>寄信服務與加密方式存成 snake_case 的字串，和對外的寫法一致。</summary>
internal static class EmailEnumNames
{
    public static string NameOf(EmailProvider provider) => provider switch
    {
        EmailProvider.Gmail => "gmail",
        EmailProvider.Smtp => "smtp",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "不認得的寄信服務。"),
    };

    public static EmailProvider ParseProvider(string name) => name switch
    {
        "gmail" => EmailProvider.Gmail,
        "smtp" => EmailProvider.Smtp,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "不認得的寄信服務。"),
    };

    public static string NameOf(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => "start_tls",
        SmtpSecurity.Tls => "tls",
        SmtpSecurity.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "不認得的加密方式。"),
    };

    public static SmtpSecurity ParseSecurity(string name) => name switch
    {
        "start_tls" => SmtpSecurity.StartTls,
        "tls" => SmtpSecurity.Tls,
        "none" => SmtpSecurity.None,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "不認得的加密方式。"),
    };
}
