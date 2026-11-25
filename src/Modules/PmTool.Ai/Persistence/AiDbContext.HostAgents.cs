using Microsoft.EntityFrameworkCore;
using PmTool.Ai.HostAgents;

namespace PmTool.Ai.Persistence;

internal sealed partial class AiDbContext
{
    private const int MaxHashLength = 64;

    private static void ConfigureHostAgents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<HostAgentRow>(agent =>
        {
            agent.ToTable("host_agents");
            agent.HasKey(a => a.Id);
            agent.Property(a => a.Id).ValueGeneratedNever();
            agent.Property(a => a.Name).HasMaxLength(AiRules.MaxNameLength);
            agent.Property(a => a.NameKey).HasMaxLength(AiRules.MaxNameLength);
            agent.Property(a => a.State).HasMaxLength(MaxEnumLength);
            agent.Property(a => a.MachineKey).HasMaxLength(HostLimits.MaxMachineKeyLength);
            agent.Property(a => a.MachineName).HasMaxLength(AiRules.MaxMachineNameLength);
            agent.Property(a => a.CredentialHash).HasMaxLength(MaxHashLength);
            agent.Property(a => a.PairingCodeHash).HasMaxLength(MaxHashLength);
            agent.Property(a => a.NextCredentialHash).HasMaxLength(MaxHashLength);
            agent.Property(a => a.SuspectMachineKey).HasMaxLength(HostLimits.MaxMachineKeyLength);
            agent.Property(a => a.SuspectMachineName).HasMaxLength(AiRules.MaxMachineNameLength);
            agent.Property(a => a.AgentVersion).HasMaxLength(HostLimits.MaxVersionLength);
            agent.Property(a => a.ServerUrl).HasMaxLength(AiRules.MaxBaseUrlLength);
            agent.Property(a => a.Engines).HasColumnType(Jsonb);
            agent.Property(a => a.LogoutRequested).HasColumnType(Jsonb);
            agent.HasIndex(a => new { a.OrganizationId, a.NameKey }).IsUnique();
            agent.HasIndex(a => a.CredentialHash).IsUnique();
            agent.HasIndex(a => a.PairingCodeHash).IsUnique();
            agent.HasIndex(a => a.NextCredentialHash).IsUnique();
        });

        modelBuilder.Entity<HostSourceRow>(source =>
        {
            source.ToTable("host_sources");
            source.HasKey(s => s.Id);
            source.Property(s => s.Id).ValueGeneratedNever();
            source.Property(s => s.Engine).HasMaxLength(MaxEnumLength);
            source.Property(s => s.Name).HasMaxLength(AiRules.MaxNameLength);
            source.Property(s => s.NameKey).HasMaxLength(AiRules.MaxNameLength);
            source.Property(s => s.EnabledModels).HasColumnType(Jsonb);
            source.Property(s => s.DefaultModel).HasMaxLength(AiRules.MaxModelIdLength);
            source.Property(s => s.DefaultEffort).HasMaxLength(MaxEnumLength);
            source.HasIndex(s => new { s.AgentId, s.Engine }).IsUnique();
            source.HasIndex(s => new { s.OrganizationId, s.NameKey }).IsUnique();
        });

        modelBuilder.Entity<HostLoginRow>(login =>
        {
            login.ToTable("host_logins");
            login.HasKey(l => l.Id);
            login.Property(l => l.Id).ValueGeneratedNever();
            login.Property(l => l.Engine).HasMaxLength(MaxEnumLength);
            login.Property(l => l.CodeHash).HasMaxLength(MaxHashLength);
            login.Property(l => l.State).HasMaxLength(MaxEnumLength);
            login.Property(l => l.Billing).HasMaxLength(MaxEnumLength);
            login.Property(l => l.Error).HasMaxLength(AiRules.MaxErrorLength);
            login.HasIndex(l => l.CodeHash).IsUnique();
            login.HasIndex(l => new { l.AgentId, l.Engine, l.State });
        });

        modelBuilder.Entity<HostJobRow>(job =>
        {
            job.ToTable("host_jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Id).ValueGeneratedNever();
            job.Property(j => j.Seq).UseIdentityByDefaultColumn();
            job.Property(j => j.ProjectKey).HasMaxLength(HostLimits.MaxProjectKeyLength);
            job.Property(j => j.Payload).HasColumnType(Jsonb);
            job.Property(j => j.State).HasMaxLength(MaxEnumLength);
            job.Property(j => j.CancelMessage).HasMaxLength(AiRules.MaxErrorLength);
            job.Property(j => j.FinishStatus).HasMaxLength(MaxEnumLength);
            job.Property(j => j.FinishError).HasMaxLength(AiRules.MaxErrorLength);
            job.Property(j => j.ToolTokenHash).HasMaxLength(MaxHashLength);
            job.HasIndex(j => j.ToolTokenHash).IsUnique();
            job.HasIndex(j => j.Seq).IsUnique();

            // 領工作、算排隊的位置：某個來源、某個狀態的工作依預留的先後。
            job.HasIndex(j => new { j.SourceId, j.State, j.Seq });
        });

        modelBuilder.Entity<HostJobEventRow>(progress =>
        {
            progress.ToTable("host_job_events");
            progress.HasKey(e => new { e.JobId, e.Seq });
            progress.Property(e => e.Content).HasColumnType(Jsonb);
        });

        modelBuilder.Entity<HostUsageRow>(usage =>
        {
            usage.ToTable("host_usage");
            usage.HasKey(u => u.Id);
            usage.Property(u => u.Id).ValueGeneratedNever();
            usage.Property(u => u.ProjectKey).HasMaxLength(HostLimits.MaxProjectKeyLength);
            usage.Property(u => u.State).HasMaxLength(MaxEnumLength);
            usage.Property(u => u.FinishStatus).HasMaxLength(MaxEnumLength);
            usage.HasIndex(u => new { u.OrganizationId, u.SourceId, u.CreatedAt });
            usage.HasIndex(u => new { u.OrganizationId, u.SourceId, u.PersonId, u.CreatedAt });
        });

        modelBuilder.Entity<HostToolCallRow>(call =>
        {
            call.ToTable("host_tool_calls");
            call.HasKey(c => new { c.JobId, c.Tool, c.ArgsHash });
            call.Property(c => c.Tool).HasMaxLength(HostLimits.MaxToolNameLength);
            call.Property(c => c.ArgsHash).HasMaxLength(MaxHashLength);
            call.Property(c => c.State).HasMaxLength(MaxEnumLength);
            call.Property(c => c.Result).HasColumnType(Jsonb);
        });
    }
}
