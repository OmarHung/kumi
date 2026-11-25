using Microsoft.EntityFrameworkCore;

namespace PmTool.Identity.Persistence;

internal sealed partial class IdentityDbContext
{
    private static void ConfigureCliLogin(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CliLoginCodeRow>(code =>
        {
            code.ToTable("cli_login_codes", table =>
            {
                table.HasCheckConstraint("ck_cli_login_codes_hash_length", $"octet_length(code_hash) = {TokenHashBytes}");
                table.HasCheckConstraint("ck_cli_login_codes_has_token", "cardinality(cli_scopes) >= 1 OR include_agent");
            });
            code.Property(c => c.Id).ValueGeneratedNever();
            code.Property(c => c.CodeChallenge).HasMaxLength(MaxCodeChallengeLength);
            code.Property(c => c.MachineName).HasMaxLength(CliLoginRules.MaxMachineNameLength);
            code.Property(c => c.OriginKey).HasMaxLength(CliLoginRules.MaxOriginLength).HasDefaultValue(string.Empty);
            code.Property(c => c.IssuedTokenIds).HasDefaultValueSql("'{}'");
            code.HasIndex(c => c.CodeHash).IsUnique();
            code.HasIndex(c => new { c.OrganizationId, c.ExpiresAt });
            code.HasOne<AccountRow>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.Restrict);
            code.HasOne<OrganizationRow>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
