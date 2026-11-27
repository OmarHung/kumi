using Microsoft.EntityFrameworkCore;
using PmTool.Ai.ItemRevisions;
using PmTool.Ai.Items;
using PmTool.Work;

namespace PmTool.Ai.Persistence;

/// <summary>週期與里程碑、改專案、留言、復原項目四種建議的表（internal/HANDOFF-ai-coverage.md）。</summary>
internal sealed partial class AiDbContext
{
    private static void ConfigureProjectWriteProposals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlanningProposalRow>(proposal =>
        {
            proposal.ToTable("planning_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Changes).HasColumnType(Jsonb);
            proposal.Property(p => p.Reason).HasMaxLength(ItemRevisionLimits.MaxReasonLength);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId });
        });

        modelBuilder.Entity<ProjectChangeProposalRow>(proposal =>
        {
            proposal.ToTable("project_change_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Name).HasMaxLength(WorkRules.MaxProjectNameLength);
            proposal.Property(p => p.Before).HasColumnType(Jsonb);
            proposal.Property(p => p.Reason).HasMaxLength(ItemRevisionLimits.MaxReasonLength);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId });
        });

        modelBuilder.Entity<CommentProposalRow>(proposal =>
        {
            proposal.ToTable("comment_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Changes).HasColumnType(Jsonb);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId });
        });

        modelBuilder.Entity<ActionProposalRow>(proposal =>
        {
            proposal.ToTable("action_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.Kind).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Title).HasMaxLength(AiRules.MaxErrorLength);
            proposal.Property(p => p.Steps).HasColumnType(Jsonb);
            proposal.Property(p => p.State).HasColumnType(Jsonb);
            proposal.Property(p => p.Inputs).HasColumnType(Jsonb).HasDefaultValueSql("'[]'::jsonb");
            proposal.Property(p => p.Reason).HasMaxLength(ItemRevisionLimits.MaxReasonLength);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId });
        });

        modelBuilder.Entity<ItemRestoreProposalRow>(proposal =>
        {
            proposal.ToTable("item_restore_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Items).HasColumnType(Jsonb);
            proposal.Property(p => p.Reason).HasMaxLength(ItemRevisionLimits.MaxReasonLength);
            proposal.Property(p => p.RestoredKeys).HasColumnType(Jsonb).HasDefaultValueSql("'[]'::jsonb");
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId });
        });
    }
}
