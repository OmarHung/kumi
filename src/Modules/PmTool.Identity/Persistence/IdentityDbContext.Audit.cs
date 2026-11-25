using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PmTool.Abstractions;

namespace PmTool.Identity.Persistence;

internal sealed partial class IdentityDbContext
{
    public const string AuditEventsTable = "audit_events";

    private const int MaxAuditActionLength = 48;

    private const int MaxAuditOutcomeLength = 16;

    public DbSet<AuditEventRow> AuditEvents => Set<AuditEventRow>();

    private static void ConfigureAudit(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEventRow>(audit =>
        {
            audit.ToTable(AuditEventsTable, table => table.HasCheckConstraint(
                "ck_audit_events_outcome", $"outcome IN ('{AuditOutcomes.Succeeded}', '{AuditOutcomes.Failed}')"));
            audit.Property(e => e.Id).ValueGeneratedNever();
            audit.Property(e => e.Action).HasMaxLength(MaxAuditActionLength);
            audit.Property(e => e.Outcome).HasMaxLength(MaxAuditOutcomeLength);
            audit.Property(e => e.SourceAddress).HasMaxLength(RequestSourceText.MaxAddressLength);
            audit.Property(e => e.UserAgent).HasMaxLength(RequestSourceText.MaxUserAgentLength);

            // 細項是字串對字串的對照；用轉換器存成 JSON 文字，不必打開 Npgsql 的動態 JSON 序列化。
            audit.Property(e => e.Details)
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'{}'::jsonb")
                .HasConversion(
                    details => JsonSerializer.Serialize(details, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, (JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
                    new ValueComparer<Dictionary<string, string>>(
                        (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
                        details => details.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                        details => new Dictionary<string, string>(details)));

            // 清單依時間新的在前；依人篩選時各看「做的」與「被做的」，所以兩邊各一個。
            audit.HasIndex(e => new { e.OrganizationId, e.OccurredAt, e.Id }).IsDescending(false, true, true);
            audit.HasIndex(e => new { e.OrganizationId, e.TargetPersonId, e.OccurredAt })
                .IsDescending(false, false, true)
                .HasFilter("target_person_id IS NOT NULL");
            audit.HasIndex(e => new { e.OrganizationId, e.ActorPersonId, e.OccurredAt })
                .IsDescending(false, false, true)
                .HasFilter("actor_person_id IS NOT NULL");

            audit.HasOne<OrganizationRow>().WithMany().HasForeignKey(e => e.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
