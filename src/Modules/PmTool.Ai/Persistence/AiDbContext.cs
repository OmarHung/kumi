using Microsoft.EntityFrameworkCore;
using PmTool.Ai.Items;
using PmTool.Ai.ProjectProposals;
using PmTool.Ai.LocalAgents;

namespace PmTool.Ai.Persistence;

internal sealed partial class AiDbContext(DbContextOptions<AiDbContext> options) : DbContext(options)
{
    public const string Schema = "ai";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    private const int MaxEnumLength = 32;

    private const string Jsonb = "jsonb";

    public DbSet<AiSettingsRow> Settings => Set<AiSettingsRow>();

    public DbSet<AiConnectionRow> Connections => Set<AiConnectionRow>();

    public DbSet<AiConversationRow> Conversations => Set<AiConversationRow>();

    public DbSet<AiMessageRow> Messages => Set<AiMessageRow>();

    public DbSet<ItemProposalRow> ItemProposals => Set<ItemProposalRow>();

    public DbSet<ProjectProposalRow> ProjectProposals => Set<ProjectProposalRow>();

    public DbSet<LocalAgentRow> LocalAgents => Set<LocalAgentRow>();

    public DbSet<LocalEngineRow> LocalEngines => Set<LocalEngineRow>();

    public DbSet<LocalJobRow> LocalJobs => Set<LocalJobRow>();

    public DbSet<LocalEventRow> LocalEvents => Set<LocalEventRow>();

    public DbSet<HostAgentRow> HostAgents => Set<HostAgentRow>();

    public DbSet<HostSourceRow> HostSources => Set<HostSourceRow>();

    public DbSet<HostLoginRow> HostLogins => Set<HostLoginRow>();

    public DbSet<HostJobRow> HostJobs => Set<HostJobRow>();

    public DbSet<HostJobEventRow> HostJobEvents => Set<HostJobEventRow>();

    public DbSet<HostUsageRow> HostUsage => Set<HostUsageRow>();

    public DbSet<HostToolCallRow> HostToolCalls => Set<HostToolCallRow>();

    public DbSet<AiConnectionUsageRow> ConnectionUsage => Set<AiConnectionUsageRow>();

    public DbSet<AiAttachmentRow> Attachments => Set<AiAttachmentRow>();

    /// <summary>執行時與設計時（產生遷移）共用同一套設定，兩邊才不會產生不一樣的模型。</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AiSettingsRow>(settings =>
        {
            settings.ToTable("settings");
            settings.HasKey(s => s.OrganizationId);
            settings.Property(s => s.OrganizationId).ValueGeneratedNever();
            settings.Property(s => s.DefaultModel).HasMaxLength(AiRules.MaxModelIdLength);
            settings.Property(s => s.DefaultEffort).HasMaxLength(MaxEnumLength);
        });

        modelBuilder.Entity<AiConnectionRow>(connection =>
        {
            connection.ToTable("connections");
            connection.HasKey(c => c.Id);
            connection.Property(c => c.Id).ValueGeneratedNever();
            connection.Property(c => c.Name).HasMaxLength(AiRules.MaxNameLength);
            connection.Property(c => c.NameKey).HasMaxLength(AiRules.MaxNameLength);
            connection.Property(c => c.Kind).HasMaxLength(MaxEnumLength);
            connection.Property(c => c.BaseUrl).HasMaxLength(AiRules.MaxBaseUrlLength);
            connection.Property(c => c.ApiKeyHint).HasMaxLength(AiRules.ApiKeyHintLength);
            connection.Property(c => c.Models).HasColumnType(Jsonb);

            // 擁有者是 null 的（組織的連線）算同一組：NULLS NOT DISTINCT，組織的連線之間名稱照舊不能重複。
            connection.HasIndex(c => new { c.OrganizationId, c.OwnerPersonId, c.NameKey }).IsUnique().AreNullsDistinct(false);
        });

        modelBuilder.Entity<AiConversationRow>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.HasKey(c => c.Id);
            conversation.Property(c => c.Id).ValueGeneratedNever();
            conversation.Property(c => c.Title).HasMaxLength(AiRules.MaxTitleLength);

            // 對話清單：某個人在某個專案的對話，最近更新的在前。
            conversation.HasIndex(c => new { c.OrganizationId, c.ProjectId, c.PersonId, c.UpdatedAt })
                .IsDescending(false, false, false, true);

            // 一個人同時在回覆的對話數。
            conversation.HasIndex(c => new { c.OrganizationId, c.PersonId, c.RunningUntil });

            // 保存期限的清理：依最後更新的時間找過期的對話。
            conversation.HasIndex(c => c.UpdatedAt);
        });

        modelBuilder.Entity<AiMessageRow>(message =>
        {
            message.ToTable("messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.Role).HasMaxLength(MaxEnumLength);
            message.Property(m => m.Parts).HasColumnType(Jsonb);
            message.Property(m => m.Transcript).HasColumnType(Jsonb);
            message.Property(m => m.Status).HasMaxLength(MaxEnumLength);
            message.Property(m => m.Error).HasMaxLength(AiRules.MaxErrorLength);
            message.Property(m => m.ConnectionName).HasMaxLength(AiRules.MaxNameLength);
            message.Property(m => m.Model).HasMaxLength(AiRules.MaxModelIdLength);
            message.Property(m => m.Effort).HasMaxLength(MaxEnumLength);
            message.Property(m => m.ItemKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            message.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();

            // 設定頁的「這個月的用量」：依組織、連線、時間加總。
            message.HasIndex(m => new { m.OrganizationId, m.ConnectionId, m.CreatedAt });

            // 同一個模組裡的表可以有外鍵：刪對話時訊息一起刪。
            message.HasOne<AiConversationRow>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ItemProposalRow>(proposal =>
        {
            proposal.ToTable("item_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.ItemKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.ItemTitle).HasMaxLength(ItemProposalLimits.MaxItemTitleLength);
            proposal.Property(p => p.Changes).HasColumnType(Jsonb);
            proposal.Property(p => p.DescriptionRevision).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.Error).HasMaxLength(AiRules.MaxErrorLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId, p.CreatedAt });
        });

        modelBuilder.Entity<ProjectProposalRow>(proposal =>
        {
            proposal.ToTable("project_proposals");
            proposal.HasKey(p => p.Id);
            proposal.Property(p => p.Id).ValueGeneratedNever();
            proposal.Property(p => p.Key).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.Property(p => p.Name).HasMaxLength(ProjectProposalLimits.MaxNameLength);
            proposal.Property(p => p.Template).HasMaxLength(ProjectProposalLimits.MaxTemplateLength);
            proposal.Property(p => p.TemplateName).HasMaxLength(ProjectProposalLimits.MaxTemplateLength);
            proposal.Property(p => p.Status).HasMaxLength(MaxEnumLength);
            proposal.Property(p => p.CreatedProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            proposal.HasIndex(p => new { p.OrganizationId, p.PersonId, p.CreatedAt });
        });

        modelBuilder.Entity<AiConnectionUsageRow>(usage =>
        {
            usage.ToTable("connection_usage");
            usage.HasKey(u => u.Id);
            usage.Property(u => u.Id).ValueGeneratedNever();
            usage.Property(u => u.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            usage.Property(u => u.State).HasMaxLength(MaxEnumLength);
            usage.Property(u => u.FinishStatus).HasMaxLength(MaxEnumLength);

            // 每人每天的則數、全組織每月的則數與 token：都依組織與預留的時間找。
            usage.HasIndex(u => new { u.OrganizationId, u.PersonId, u.CreatedAt });
            usage.HasIndex(u => new { u.OrganizationId, u.CreatedAt });
        });

        modelBuilder.Entity<AiAttachmentRow>(attachment =>
        {
            attachment.ToTable("attachments");
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.ProjectKey).HasMaxLength(ItemProposalLimits.MaxItemKeyLength);
            attachment.Property(a => a.FileName).HasMaxLength(AssistantAttachmentRules.MaxFileNameLength);
            attachment.Property(a => a.ContentType).HasMaxLength(MaxEnumLength * 2);
            attachment.Property(a => a.Kind).HasMaxLength(MaxEnumLength);
            attachment.Property(a => a.Sha256).HasMaxLength(64);

            // 待送的數量與過期清理：依組織、人、上傳時間找。
            attachment.HasIndex(a => new { a.OrganizationId, a.PersonId, a.UploadedAt });
            attachment.HasIndex(a => a.UploadedAt);

            // 訊息、對話刪掉時資料列留著（檔案還在儲存空間裡），外鍵設成 null 之後由維護先刪檔再刪列。
            attachment.HasIndex(a => a.MessageId);
            attachment.HasIndex(a => a.ConversationId);
            attachment.HasOne<AiMessageRow>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.SetNull);
            attachment.HasOne<AiConversationRow>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.SetNull);
        });

        ConfigureLocalAgents(modelBuilder);
        ConfigureHostAgents(modelBuilder);
    }

    private static void ConfigureLocalAgents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LocalAgentRow>(agent =>
        {
            agent.ToTable("local_agents");
            agent.HasKey(a => a.Id);
            agent.Property(a => a.Id).ValueGeneratedNever();
            agent.Property(a => a.MachineKey).HasMaxLength(LocalAgentLimits.MaxMachineKeyLength);
            agent.Property(a => a.Name).HasMaxLength(AiRules.MaxMachineNameLength);
            agent.Property(a => a.AgentVersion).HasMaxLength(LocalAgentLimits.MaxVersionLength);
            agent.HasIndex(a => new { a.OrganizationId, a.PersonId, a.MachineKey }).IsUnique();
        });

        modelBuilder.Entity<LocalEngineRow>(engine =>
        {
            engine.ToTable("local_engines");
            engine.HasKey(e => e.Id);
            engine.Property(e => e.Id).ValueGeneratedNever();
            engine.Property(e => e.Kind).HasMaxLength(MaxEnumLength);
            engine.Property(e => e.Version).HasMaxLength(LocalAgentLimits.MaxVersionLength);
            engine.Property(e => e.Models).HasColumnType(Jsonb);
            engine.HasIndex(e => new { e.AgentId, e.Kind }).IsUnique();
            engine.HasOne<LocalAgentRow>().WithMany().HasForeignKey(e => e.AgentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LocalJobRow>(job =>
        {
            job.ToTable("local_agent_jobs");
            job.HasKey(j => j.Id);
            job.Property(j => j.Id).ValueGeneratedNever();
            job.Property(j => j.Payload).HasColumnType(Jsonb);
            job.Property(j => j.State).HasMaxLength(MaxEnumLength);
            job.Property(j => j.FinishStatus).HasMaxLength(MaxEnumLength);
            job.Property(j => j.FinishError).HasMaxLength(AiRules.MaxErrorLength);

            // 領工作：這個人、這台電腦的引擎、還在等的最舊的一件。
            job.HasIndex(j => new { j.OrganizationId, j.PersonId, j.State, j.CreatedAt });
        });

        modelBuilder.Entity<LocalEventRow>(progress =>
        {
            progress.ToTable("local_agent_events");
            progress.HasKey(e => new { e.JobId, e.Seq });
            progress.Property(e => e.Content).HasColumnType(Jsonb);
        });
    }
}
