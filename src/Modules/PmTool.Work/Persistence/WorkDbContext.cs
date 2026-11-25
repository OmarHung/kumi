using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;

namespace PmTool.Work.Persistence;

internal sealed class WorkDbContext(DbContextOptions<WorkDbContext> options) : DbContext(options)
{
    public const string Schema = "work";

    /// <summary>代號重複時資料庫回報的限制名稱，用它把唯一限制的錯誤轉成 project.key_taken。</summary>
    public const string ProjectKeyIndex = "ux_projects_organization_id_key";

    /// <summary>封存的值的來源剛好一個：變更集或改類型的活動。</summary>
    public const string ArchiveSourceCheck = "ck_archived_field_values_one_source";

    /// <summary>
    /// 檢視名稱在（專案、人）裡不分大小寫唯一的索引名稱。EF 的模型描述不了 lower(name) 的運算式索引，
    /// 所以它只在遷移裡用 SQL 建立；用名稱把唯一限制的錯誤轉成 saved_view.name_taken。
    /// </summary>
    public const string SavedViewNameIndex = "ux_saved_views_project_owner_lower_name";

    /// <summary>
    /// 「我的工作」的檢視（沒有專案）名稱在人裡不分大小寫唯一的索引名稱。專案的索引不管 project_id 是 null 的列
    /// （null 彼此不相等），所以另外一個；理由同 <see cref="SavedViewNameIndex"/>。
    /// </summary>
    public const string MyWorkViewNameIndex = "ux_saved_views_my_work_owner_lower_name";

    /// <summary>週期、里程碑的名稱在專案裡不分大小寫唯一的索引名稱；理由同 <see cref="SavedViewNameIndex"/>。</summary>
    public const string CycleNameIndex = "ux_cycles_project_lower_name";

    public const string MilestoneNameIndex = "ux_milestones_project_lower_name";

    /// <summary>
    /// 說明格式的名稱在組織裡不分大小寫唯一、每個組織最多一份預設的索引名稱。兩個都是運算式或部分索引，
    /// 只在遷移裡用 SQL 建立；寫入先排隊檢查，這兩個是最後一道防線，撞到時轉成 name_taken 或不留兩份預設。
    /// </summary>
    public const string DescriptionTemplateNameIndex = "ux_description_templates_organization_lower_name";

    public const string DescriptionTemplateDefaultIndex = "ux_description_templates_organization_default";

    /// <summary>事件流的資料表，執行的角色只能新增與讀取（見 WorkDatabaseInitializer）。</summary>
    public const string EventsTable = "events";

    public const string MigrationsHistoryTable = "__ef_migrations_history";
    private const string Jsonb = "jsonb";
    private const string FieldsIndex = "ix_items_fields";

    public DbSet<ProjectEntity> Projects => Set<ProjectEntity>();

    public DbSet<BlueprintVersionEntity> BlueprintVersions => Set<BlueprintVersionEntity>();

    public DbSet<ProjectMemberEntity> ProjectMembers => Set<ProjectMemberEntity>();

    public DbSet<ItemEntity> Items => Set<ItemEntity>();

    public DbSet<ItemActivityEntity> ItemActivities => Set<ItemActivityEntity>();

    public DbSet<CommentRevisionEntity> CommentRevisions => Set<CommentRevisionEntity>();

    public DbSet<ChangeSetEntity> ChangeSets => Set<ChangeSetEntity>();

    public DbSet<ArchivedFieldValueEntity> ArchivedFieldValues => Set<ArchivedFieldValueEntity>();

    public DbSet<WorkEventEntity> Events => Set<WorkEventEntity>();

    public DbSet<SavedViewEntity> SavedViews => Set<SavedViewEntity>();

    public DbSet<CycleEntity> Cycles => Set<CycleEntity>();

    public DbSet<MilestoneEntity> Milestones => Set<MilestoneEntity>();

    public DbSet<AttachmentEntity> Attachments => Set<AttachmentEntity>();

    public DbSet<HistoryRefEntity> HistoryRefs => Set<HistoryRefEntity>();

    public DbSet<ProjectDescriptionEntity> ProjectDescriptions => Set<ProjectDescriptionEntity>();

    public DbSet<DescriptionTemplateEntity> DescriptionTemplates => Set<DescriptionTemplateEntity>();

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        RegisterFieldFunctions(modelBuilder);
        ConfigureProjects(modelBuilder.Entity<ProjectEntity>());
        ConfigureBlueprintVersions(modelBuilder.Entity<BlueprintVersionEntity>());
        ConfigureMembers(modelBuilder.Entity<ProjectMemberEntity>());
        ConfigureItems(modelBuilder.Entity<ItemEntity>());
        ConfigureActivities(modelBuilder.Entity<ItemActivityEntity>());
        ConfigureCommentRevisions(modelBuilder.Entity<CommentRevisionEntity>());
        ConfigureChangeSets(modelBuilder.Entity<ChangeSetEntity>());
        ConfigureArchivedFieldValues(modelBuilder.Entity<ArchivedFieldValueEntity>());
        ConfigureEvents(modelBuilder.Entity<WorkEventEntity>());
        ConfigureSavedViews(modelBuilder.Entity<SavedViewEntity>());
        ConfigureCycles(modelBuilder.Entity<CycleEntity>());
        ConfigureMilestones(modelBuilder.Entity<MilestoneEntity>());
        ConfigureAttachments(modelBuilder.Entity<AttachmentEntity>());
        ConfigureHistoryRefs(modelBuilder.Entity<HistoryRefEntity>());
        ConfigureProjectDescriptions(modelBuilder.Entity<ProjectDescriptionEntity>());
        ConfigureDescriptionTemplates(modelBuilder.Entity<DescriptionTemplateEntity>());
    }

    private static void ConfigureDescriptionTemplates(EntityTypeBuilder<DescriptionTemplateEntity> template)
    {
        template.ToTable("description_templates");
        template.HasKey(t => t.Id);
        template.Property(t => t.Id).ValueGeneratedNever();
        template.Property(t => t.Name).HasMaxLength(ProjectDescriptionRules.MaxTemplateNameLength);
        template.HasIndex(t => t.OrganizationId);
    }

    private static void ConfigureProjectDescriptions(EntityTypeBuilder<ProjectDescriptionEntity> description)
    {
        description.ToTable("project_descriptions");

        // 一個專案一列，主鍵就是專案編號：同時寫兩次靠「有就更新」，不會撞主鍵。
        description.HasKey(d => d.ProjectId);
        description.Property(d => d.ProjectId).ValueGeneratedNever();
        description.HasOne<ProjectEntity>().WithMany().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureHistoryRefs(EntityTypeBuilder<HistoryRefEntity> reference)
    {
        reference.ToTable("history_refs");

        // 外部編號在組織裡唯一（同一個來源、同一種東西）：重跑時照它找，同時匯入同一筆時資料庫擋得住重複。
        reference.HasKey(r => new { r.OrganizationId, r.Source, r.Kind, r.ExternalId });
        reference.Property(r => r.Source).HasMaxLength(30);
        reference.Property(r => r.Kind).HasMaxLength(20);
        reference.Property(r => r.ExternalId).HasMaxLength(HistorySource.MaxExternalIdLength);
        reference.HasOne<ItemEntity>().WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAttachments(EntityTypeBuilder<AttachmentEntity> attachment)
    {
        attachment.ToTable("attachments");
        attachment.HasKey(a => a.Id);
        attachment.Property(a => a.Id).ValueGeneratedNever();
        attachment.Property(a => a.FileName).HasMaxLength(AttachmentRules.MaxFileNameLength);
        attachment.Property(a => a.ContentType).HasMaxLength(AttachmentRules.MaxContentTypeLength);
        attachment.Property(a => a.Sha256).HasMaxLength(64);

        // 項目頁依上傳的先後列出附件，數量上限也從這裡數。
        attachment.HasIndex(a => new { a.OrganizationId, a.ItemId, a.UploadedAt });

        // 儲存空間的用量：組織的總量與每人過去 24 小時的量，都能只讀索引。
        attachment.HasIndex(a => new { a.OrganizationId, a.UploadedBy, a.UploadedAt })
            .IncludeProperties(a => a.SizeBytes)
            .HasDatabaseName("ix_attachments_quota");
        attachment.HasOne<ItemEntity>().WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCycles(EntityTypeBuilder<CycleEntity> cycle)
    {
        cycle.ToTable("cycles");
        cycle.HasKey(c => c.Id);
        cycle.Property(c => c.Id).ValueGeneratedNever();
        cycle.Property(c => c.Name).HasMaxLength(PlanningRules.MaxCycleNameLength);

        // 列出專案的週期、數有幾個、找日期重疊的，都從這裡找。
        cycle.HasIndex(c => new { c.OrganizationId, c.ProjectId, c.StartDate });
        cycle.HasOne<ProjectEntity>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMilestones(EntityTypeBuilder<MilestoneEntity> milestone)
    {
        milestone.ToTable("milestones");
        milestone.HasKey(m => m.Id);
        milestone.Property(m => m.Id).ValueGeneratedNever();
        milestone.Property(m => m.Name).HasMaxLength(PlanningRules.MaxMilestoneNameLength);
        milestone.HasIndex(m => new { m.OrganizationId, m.ProjectId, m.DueDate });
        milestone.HasOne<ProjectEntity>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureSavedViews(EntityTypeBuilder<SavedViewEntity> view)
    {
        view.ToTable("saved_views");
        view.HasKey(v => v.Id);
        view.Property(v => v.Id).ValueGeneratedNever();
        view.Property(v => v.Name).HasMaxLength(SavedViewRules.MaxNameLength);
        view.Property(v => v.Layout).HasMaxLength(10);
        view.Property(v => v.Query).HasColumnType(Jsonb);

        // 列出自己在某個專案的檢視、數有幾個，都從這裡找。
        view.HasIndex(v => new { v.OrganizationId, v.ProjectId, v.OwnerId });
        view.HasOne<ProjectEntity>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureEvents(EntityTypeBuilder<WorkEventEntity> workEvent)
    {
        workEvent.ToTable(EventsTable);
        workEvent.HasKey(e => e.Sequence);
        workEvent.Property(e => e.Sequence).UseIdentityByDefaultColumn();

        // 交易編號由資料庫在新增時填：和資料同一個交易，讀的人才能分辨哪些交易已經確定。
        workEvent.Property(e => e.TransactionId).HasColumnType("xid8").HasDefaultValueSql("pg_current_xact_id()").ValueGeneratedOnAdd();
        workEvent.Property(e => e.Kind).HasMaxLength(30);
        workEvent.Property(e => e.Content).HasColumnType(Jsonb);

        // 讀的人依（交易編號、流水號）往後讀，每一批都從索引上接著讀。
        workEvent.HasIndex(e => new { e.TransactionId, e.Sequence });
        workEvent.HasIndex(e => e.EventId).IsUnique();
    }

    private static void ConfigureProjects(EntityTypeBuilder<ProjectEntity> project)
    {
        project.ToTable("projects");
        project.HasKey(p => p.Id);
        project.Property(p => p.Id).ValueGeneratedNever();
        project.Property(p => p.Key).HasMaxLength(10);
        project.Property(p => p.Name).HasMaxLength(WorkRules.MaxProjectNameLength);
        project.Property(p => p.Template).HasMaxLength(40);

        // 代號在組織裡唯一。重複靠這個限制把關，同時開兩個同代號的專案也只會成功一個。
        project.HasIndex(p => new { p.OrganizationId, p.Key }).IsUnique().HasDatabaseName(ProjectKeyIndex);
    }

    private static void ConfigureBlueprintVersions(EntityTypeBuilder<BlueprintVersionEntity> version)
    {
        version.ToTable("blueprint_versions");
        version.HasKey(v => v.Id);
        version.Property(v => v.Id).ValueGeneratedNever();
        version.Property(v => v.Document).HasColumnType(Jsonb);
        version.HasIndex(v => new { v.ProjectId, v.Version }).IsUnique();
        version.HasOne<ProjectEntity>().WithMany().HasForeignKey(v => v.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureMembers(EntityTypeBuilder<ProjectMemberEntity> member)
    {
        member.ToTable("project_members");
        member.HasKey(m => m.Id);
        member.Property(m => m.Id).ValueGeneratedNever();
        member.Property(m => m.Role).HasMaxLength(40);

        // 一個人在一個專案只有一個角色；改角色靠這個限制做「有就更新」。
        member.HasIndex(m => new { m.ProjectId, m.PersonId }).IsUnique();

        // 「我參與的專案」從人查回去。
        member.HasIndex(m => new { m.OrganizationId, m.PersonId });
        member.HasOne<ProjectEntity>().WithMany().HasForeignKey(m => m.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void RegisterFieldFunctions(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(FieldSql).GetMethod(nameof(FieldSql.Json))!)
            .HasTranslation(args => new SqlFunctionExpression(
                "jsonb_extract_path", args, nullable: true, argumentsPropagateNullability: [true, true], typeof(string), typeMapping: null));
        modelBuilder.HasDbFunction(typeof(FieldSql).GetMethod(nameof(FieldSql.Text))!)
            .HasTranslation(args => new SqlFunctionExpression(
                "jsonb_extract_path_text", args, nullable: true, argumentsPropagateNullability: [true, true], typeof(string), typeMapping: null));
    }

    private static void ConfigureItems(EntityTypeBuilder<ItemEntity> item)
    {
        item.ToTable("items");
        item.HasKey(i => i.Id);
        item.Property(i => i.Id).ValueGeneratedNever();
        item.Property(i => i.Type).HasMaxLength(40);
        item.Property(i => i.Title).HasMaxLength(WorkRules.MaxTitleLength);
        item.Property(i => i.State).HasMaxLength(40);
        item.Property(i => i.StateCategory).HasMaxLength(20);
        item.Property(i => i.Fields).HasColumnType(Jsonb);

        // 流水號包含刪除的項目：編號不再發給別的項目，所以這個唯一限制不能只看沒刪除的。
        item.HasIndex(i => new { i.ProjectId, i.Number }).IsUnique();

        // 「我的工作」：跨專案找某人負責、還沒結束的項目。用它的查詢一定排除刪除的項目，
        // 只收沒刪除的，索引不會被刪除的項目撐大。
        item.HasIndex(i => new { i.OrganizationId, i.AssigneeId, i.StateCategory })
            .HasFilter("deleted_at IS NULL");

        // 下層的清單與「還有沒有下層」的檢查都用它；刪除的下層少，留著也讓上層的外鍵檢查用得到。
        item.HasIndex(i => i.ParentId);

        // 依自訂欄位篩選用 jsonb 的包含（@>）；jsonb_path_ops 的索引比預設的小、包含查詢也比較快，
        // 這裡不需要它不支援的「有沒有這個鍵」。只收沒刪除的，理由同上。
        item.HasIndex(i => i.Fields, FieldsIndex)
            .HasMethod("gin")
            .HasOperators("jsonb_path_ops")
            .HasFilter("deleted_at IS NULL");

        // 週期與里程碑的進度、篩選都從這裡找；沒刪除的才收，理由同上。刪除週期時資料庫把項目的欄位清成 null，
        // 這裡不寫活動與事件（已知限制，見 IPlanningService.DeleteCycleAsync）。
        item.HasIndex(i => new { i.OrganizationId, i.ProjectId, i.CycleId }).HasFilter("deleted_at IS NULL AND cycle_id IS NOT NULL");
        item.HasIndex(i => new { i.OrganizationId, i.ProjectId, i.MilestoneId }).HasFilter("deleted_at IS NULL AND milestone_id IS NOT NULL");
        item.HasOne<CycleEntity>().WithMany().HasForeignKey(i => i.CycleId).OnDelete(DeleteBehavior.SetNull);
        item.HasOne<MilestoneEntity>().WithMany().HasForeignKey(i => i.MilestoneId).OnDelete(DeleteBehavior.SetNull);
        item.HasOne<ProjectEntity>().WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        item.HasOne<ItemEntity>().WithMany().HasForeignKey(i => i.ParentId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureActivities(EntityTypeBuilder<ItemActivityEntity> activity)
    {
        activity.ToTable("item_activities");
        activity.HasKey(a => a.Id);
        activity.Property(a => a.Id).ValueGeneratedNever();
        activity.Property(a => a.Kind).HasMaxLength(20);
        activity.Property(a => a.ImportedFrom).HasMaxLength(30);
        activity.Property(a => a.Content).HasColumnType(Jsonb);

        // 項目頁依時間由舊到新讀，同一時間依編號；分頁的游標就是這三個值，每一頁都從索引上接著讀。
        activity.HasIndex(a => new { a.ItemId, a.At, a.Id });

        // 只有回覆才有值；依項目找一則留言底下的回覆時用。
        activity.HasIndex(a => new { a.ItemId, a.ReplyToId }).HasFilter("reply_to_id IS NOT NULL");
        activity.HasOne<ItemActivityEntity>().WithMany().HasForeignKey(a => a.ReplyToId).OnDelete(DeleteBehavior.Restrict);
        activity.HasOne<ProjectEntity>().WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        activity.HasOne<ItemEntity>().WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCommentRevisions(EntityTypeBuilder<CommentRevisionEntity> revision)
    {
        revision.ToTable("comment_revisions");
        revision.HasKey(r => r.Id);
        revision.Property(r => r.Id).ValueGeneratedNever();

        // 一則留言的版本號唯一；讀版本時依它排。
        revision.HasIndex(r => new { r.CommentId, r.Number }).IsUnique();
        revision.HasOne<ItemActivityEntity>().WithMany().HasForeignKey(r => r.CommentId).OnDelete(DeleteBehavior.Restrict);
        revision.HasOne<ItemEntity>().WithMany().HasForeignKey(r => r.ItemId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureChangeSets(EntityTypeBuilder<ChangeSetEntity> changeSet)
    {
        changeSet.ToTable("changesets");
        changeSet.HasKey(c => c.Id);
        changeSet.Property(c => c.Id).ValueGeneratedNever();
        changeSet.Property(c => c.Status).HasMaxLength(20);
        changeSet.Property(c => c.Document).HasColumnType(Jsonb);
        changeSet.Property(c => c.ProposedVia).HasMaxLength(20);

        // 流水號在專案裡唯一。號碼由專案那一列發，這個限制是最後一道防線；清單也依它由新到舊讀。
        changeSet.HasIndex(c => new { c.ProjectId, c.Number }).IsUnique();
        changeSet.HasOne<ProjectEntity>().WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureArchivedFieldValues(EntityTypeBuilder<ArchivedFieldValueEntity> archived)
    {
        archived.ToTable("archived_field_values");
        archived.HasKey(a => a.Id);
        archived.Property(a => a.Id).ValueGeneratedNever();
        archived.Property(a => a.Field).HasMaxLength(40);
        archived.Property(a => a.Value).HasColumnType(Jsonb);

        // 之後的還原會依項目找回它被拿掉的值。
        archived.HasIndex(a => new { a.ItemId, a.Field });
        archived.HasOne<ProjectEntity>().WithMany().HasForeignKey(a => a.ProjectId).OnDelete(DeleteBehavior.Restrict);
        archived.HasOne<ItemEntity>().WithMany().HasForeignKey(a => a.ItemId).OnDelete(DeleteBehavior.Restrict);
        archived.HasOne<ChangeSetEntity>().WithMany().HasForeignKey(a => a.ChangeSetId).OnDelete(DeleteBehavior.Restrict);
        archived.HasOne<ItemActivityEntity>().WithMany().HasForeignKey(a => a.ItemActivityId).OnDelete(DeleteBehavior.Restrict);
        archived.ToTable(table => table.HasCheckConstraint(
            ArchiveSourceCheck, "(change_set_id IS NULL) <> (item_activity_id IS NULL)"));
    }
}
