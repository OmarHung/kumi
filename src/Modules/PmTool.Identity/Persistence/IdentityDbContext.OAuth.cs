using Microsoft.EntityFrameworkCore;

namespace PmTool.Identity.Persistence;

internal sealed partial class IdentityDbContext
{
    /// <summary>同一個帳號、用戶端、resource 只能有一筆還沒撤銷的授權。同時授權的請求靠它排隊，不會多出重複的。</summary>
    public const string ActiveGrantIndex = "ux_oauth_grants_active";

    private const int MaxOAuthTextLength = 500;

    private const int MaxClientKindLength = 16;

    /// <summary>PKCE 的 code_challenge 是 SHA-256 的 base64url，固定 43 個字；留餘裕給之後的方法。</summary>
    private const int MaxCodeChallengeLength = 128;

    private static void ConfigureOAuth(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthClientRow>(client =>
        {
            client.ToTable("oauth_clients");
            client.Property(c => c.Id).ValueGeneratedNever();
            client.Property(c => c.Kind).HasMaxLength(MaxClientKindLength);
            client.Property(c => c.ClientId).HasMaxLength(MaxOAuthTextLength);
            client.Property(c => c.Name).HasMaxLength(OAuthRules.MaxClientNameLength);
            client.HasIndex(c => c.ClientId).IsUnique();

            // 數過去一段時間註冊了幾個、清掉沒人用的，都依種類加時間查。
            client.HasIndex(c => new { c.Kind, c.CreatedAt });
        });

        modelBuilder.Entity<OAuthGrantRow>(grant =>
        {
            grant.ToTable("oauth_grants", table => table.HasCheckConstraint("ck_oauth_grants_scopes_not_empty", "cardinality(scopes) >= 1"));
            grant.Property(g => g.Id).ValueGeneratedNever();
            grant.Property(g => g.ClientId).HasMaxLength(MaxOAuthTextLength);
            grant.Property(g => g.ClientName).HasMaxLength(OAuthRules.MaxClientNameLength);
            grant.Property(g => g.ClientHost).HasMaxLength(MaxOAuthTextLength);
            grant.Property(g => g.Resource).HasMaxLength(MaxOAuthTextLength);
            grant.Property(g => g.RevokedReason).HasMaxLength(MaxRevokedReasonLength);
            grant.HasIndex(g => new { g.OrganizationId, g.AccountId, g.ClientId, g.Resource })
                .IsUnique()
                .HasFilter("revoked_at IS NULL")
                .HasDatabaseName(ActiveGrantIndex);
            grant.HasOne<AccountRow>().WithMany().HasForeignKey(g => g.AccountId).OnDelete(DeleteBehavior.Restrict);
            grant.HasOne<OrganizationRow>().WithMany().HasForeignKey(g => g.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OAuthCodeRow>(code =>
        {
            code.ToTable("oauth_codes", table => table.HasCheckConstraint("ck_oauth_codes_hash_length", $"octet_length(code_hash) = {TokenHashBytes}"));
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.RedirectUri).HasMaxLength(OAuthRules.MaxRedirectUriLength);
            code.Property(c => c.CodeChallenge).HasMaxLength(MaxCodeChallengeLength);
            code.Property(c => c.Resource).HasMaxLength(MaxOAuthTextLength);
            code.HasIndex(c => c.CodeHash).IsUnique();
            code.HasIndex(c => c.GrantId);
            code.HasOne<OAuthGrantRow>().WithMany().HasForeignKey(c => c.GrantId).OnDelete(DeleteBehavior.Restrict);
            code.HasOne<OrganizationRow>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OAuthRefreshTokenRow>(token =>
        {
            token.ToTable("oauth_refresh_tokens", table => table.HasCheckConstraint("ck_oauth_refresh_tokens_hash_length", $"octet_length(token_hash) = {TokenHashBytes}"));
            token.Property(t => t.Id).ValueGeneratedNever();
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.HasIndex(t => t.GrantId);
            token.HasIndex(t => t.FamilyId);
            token.HasOne<OAuthGrantRow>().WithMany().HasForeignKey(t => t.GrantId).OnDelete(DeleteBehavior.Restrict);
            token.HasOne<OrganizationRow>().WithMany().HasForeignKey(t => t.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
