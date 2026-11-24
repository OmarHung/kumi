using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

internal sealed partial class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public const string DataProtectionKeysTable = "data_protection_keys";

    /// <summary>Email 的唯一限制。新增人員靠它判斷 Email 已被使用，所以名稱固定下來。</summary>
    public const string AccountEmailIndex = "ux_accounts_email";

    public const int MaxOrganizationNameLength = 100;

    /// <summary>角色存成小寫的英文字，和對外的 snake_case 一致，直接查資料表也看得懂。</summary>
    private const int MaxRoleLength = 16;

    /// <summary>SHA-256 的長度。資料庫也限制長度，寫錯的雜湊（例如存成明文）直接失敗，不會默默存進去。</summary>
    private const int TokenHashBytes = 32;

    private const int MaxRevokedReasonLength = 32;

    private const int MaxDisplayPrefixLength = 16;

    /// <summary>Data Protection 給的金鑰名稱是「key-」加上 GUID，留一點餘裕。</summary>
    private const int MaxKeyNameLength = 100;

    public DbSet<OrganizationRow> Organizations => Set<OrganizationRow>();

    public DbSet<PersonRow> People => Set<PersonRow>();

    public DbSet<AccountRow> Accounts => Set<AccountRow>();

    public DbSet<SessionRow> Sessions => Set<SessionRow>();

    public DbSet<AccessTokenRow> AccessTokens => Set<AccessTokenRow>();

    public DbSet<CliLoginCodeRow> CliLoginCodes => Set<CliLoginCodeRow>();

    public DbSet<OAuthClientRow> OAuthClients => Set<OAuthClientRow>();

    public DbSet<OAuthGrantRow> OAuthGrants => Set<OAuthGrantRow>();

    public DbSet<OAuthCodeRow> OAuthCodes => Set<OAuthCodeRow>();

    public DbSet<OAuthRefreshTokenRow> OAuthRefreshTokens => Set<OAuthRefreshTokenRow>();

    public DbSet<DataProtectionKeyRow> DataProtectionKeys => Set<DataProtectionKeyRow>();

    /// <summary>執行時與設計時（產生遷移）共用同一套設定，兩邊才不會產生不一樣的模型。</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<OrganizationRow>(organization =>
        {
            organization.ToTable("organizations");
            organization.Property(o => o.Id).ValueGeneratedNever();
            organization.Property(o => o.Name).HasMaxLength(MaxOrganizationNameLength);
            organization.Property(o => o.TimeZone).HasDefaultValue(OrganizationTime.DefaultTimeZoneId);
        });

        modelBuilder.Entity<PersonRow>(person =>
        {
            person.ToTable("people");
            person.Property(p => p.Id).ValueGeneratedNever();
            person.Property(p => p.DisplayName).HasMaxLength(IdentityRules.MaxDisplayNameLength);
            person.Property(p => p.Role)
                .HasMaxLength(MaxRoleLength)
                .HasConversion(role => role.ToString().ToLowerInvariant(), name => Enum.Parse<OrganizationRole>(name, true));
            person.HasOne<OrganizationRow>().WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            person.HasIndex(p => new { p.OrganizationId, p.DisplayName });
        });

        modelBuilder.Entity<AccountRow>(account =>
        {
            account.ToTable("accounts");
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.Email).HasMaxLength(IdentityRules.MaxEmailLength);
            account.HasIndex(a => a.Email).IsUnique().HasDatabaseName(AccountEmailIndex);
            account.HasIndex(a => a.PersonId).IsUnique();
            account.HasOne<PersonRow>().WithMany().HasForeignKey(a => a.PersonId).OnDelete(DeleteBehavior.Restrict);
            account.HasOne<OrganizationRow>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SessionRow>(session =>
        {
            session.ToTable("sessions", table => table.HasCheckConstraint(
                "ck_sessions_token_hash_length", $"octet_length(token_hash) = {TokenHashBytes}"));
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.RevokedReason).HasMaxLength(MaxRevokedReasonLength);
            session.HasIndex(s => s.TokenHash).IsUnique();
            session.HasOne<AccountRow>().WithMany().HasForeignKey(s => s.AccountId).OnDelete(DeleteBehavior.Restrict);
            session.HasOne<OrganizationRow>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AccessTokenRow>(token =>
        {
            token.ToTable("access_tokens", table =>
            {
                table.HasCheckConstraint("ck_access_tokens_token_hash_length", $"octet_length(token_hash) = {TokenHashBytes}");
                table.HasCheckConstraint("ck_access_tokens_scopes_not_empty", "cardinality(scopes) >= 1");
            });
            token.Property(t => t.Id).ValueGeneratedNever();
            token.Property(t => t.Name).HasMaxLength(AccessTokenRules.MaxNameLength);
            token.Property(t => t.DisplayPrefix).HasMaxLength(MaxDisplayPrefixLength);
            token.Property(t => t.MachineName).HasMaxLength(CliLoginRules.MaxMachineNameLength);
            token.Property(t => t.RevokedReason).HasMaxLength(MaxRevokedReasonLength);
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => new { t.OrganizationId, t.AccountId });
            token.HasOne<AccountRow>().WithMany().HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Restrict);
            token.HasOne<OrganizationRow>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            token.HasOne<OAuthGrantRow>().WithMany().HasForeignKey(t => t.GrantId).OnDelete(DeleteBehavior.Restrict);
            token.HasIndex(t => t.GrantId).HasFilter("grant_id IS NOT NULL");
        });

        ConfigureOAuth(modelBuilder);
        ConfigureCliLogin(modelBuilder);

        modelBuilder.Entity<DataProtectionKeyRow>(key =>
        {
            key.ToTable(DataProtectionKeysTable);
            key.Property(k => k.FriendlyName).HasMaxLength(MaxKeyNameLength);
        });
    }
}
