using Microsoft.EntityFrameworkCore;

namespace PmTool.Knowledge.Persistence;

internal sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options)
{
    public const string Schema = "knowledge";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>一個專案最多一個空間：專案空間的唯一索引，建立空間時靠它擋下同時的寫入。</summary>
    public const string SpaceProjectIndex = "ux_spaces_organization_project";

    private const int MaxFileNameLength = 255;

    private const int MaxContentTypeLength = 255;

    private const int Sha256Length = 64;

    public DbSet<SpaceRow> Spaces => Set<SpaceRow>();

    public DbSet<PageRow> Pages => Set<PageRow>();

    public DbSet<PageRestrictionRow> PageRestrictions => Set<PageRestrictionRow>();

    public DbSet<PageVersionRow> PageVersions => Set<PageVersionRow>();

    public DbSet<PageAttachmentRow> PageAttachments => Set<PageAttachmentRow>();

    /// <summary>執行時與設計時（產生遷移）共用同一套設定，兩邊才不會產生不一樣的模型。</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, Schema))
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SpaceRow>(space =>
        {
            space.ToTable("spaces");
            space.HasKey(s => s.Id);
            space.Property(s => s.Id).ValueGeneratedNever();
            space.Property(s => s.Key).HasMaxLength(10);
            space.Property(s => s.Name).HasMaxLength(KnowledgeRules.MaxSpaceNameLength);
            space.Property(s => s.Description).HasMaxLength(KnowledgeRules.MaxSpaceDescriptionLength);
            space.HasIndex(s => new { s.OrganizationId, s.Key }).IsUnique();
            space.HasIndex(s => new { s.OrganizationId, s.ProjectId })
                .IsUnique()
                .HasFilter("project_id IS NOT NULL")
                .HasDatabaseName(SpaceProjectIndex);
        });

        modelBuilder.Entity<PageRow>(page =>
        {
            page.ToTable("pages");
            page.HasKey(p => p.Id);
            page.Property(p => p.Id).ValueGeneratedNever();
            page.Property(p => p.Title).HasMaxLength(KnowledgeRules.MaxPageTitleLength);
            page.Property(p => p.Body).HasDefaultValue(string.Empty);
            page.HasOne<SpaceRow>().WithMany().HasForeignKey(p => p.SpaceId);

            // 上層一定在同一個空間：複合外鍵讓資料庫擋住跨空間的上層（parent_id 是 null 時不檢查）。
            // 擋不住成環，成環在程式裡擋。
            page.HasAlternateKey(p => new { p.SpaceId, p.Id });
            page.HasOne<PageRow>().WithMany().HasForeignKey(p => new { p.SpaceId, p.ParentId }).HasPrincipalKey(p => new { p.SpaceId, p.Id });
            page.HasIndex(p => new { p.SpaceId, p.Number }).IsUnique();
            page.HasIndex(p => new { p.SpaceId, p.ParentId, p.Position });
            page.HasIndex(p => new { p.SpaceId, p.DeletionId });

            // 垃圾桶的清單：只看每一批的最上面那一頁，依刪除的時間由新到舊。
            page.HasIndex(p => new { p.SpaceId, p.DeletedAt })
                .IsDescending(false, true)
                .HasFilter("deletion_id IS NOT NULL")
                .HasDatabaseName("ix_pages_trash");
        });

        modelBuilder.Entity<PageRestrictionRow>(restriction =>
        {
            restriction.ToTable("page_restrictions");
            restriction.HasKey(r => new { r.PageId, r.PersonId });
            restriction.HasOne<PageRow>().WithMany().HasForeignKey(r => r.PageId);

            // 「這個人在哪些頁面的名單裡」：每次算看得到哪些頁面都用到。
            restriction.HasIndex(r => new { r.OrganizationId, r.PersonId, r.PageId }).HasDatabaseName("ix_page_restrictions_person");
        });

        modelBuilder.Entity<PageVersionRow>(version =>
        {
            version.ToTable("page_versions");
            version.HasKey(v => v.Id);
            version.Property(v => v.Id).ValueGeneratedNever();
            version.Property(v => v.Title).HasMaxLength(KnowledgeRules.MaxPageTitleLength);
            version.HasOne<PageRow>().WithMany().HasForeignKey(v => v.PageId);
            version.HasIndex(v => new { v.PageId, v.Number }).IsUnique();
        });

        modelBuilder.Entity<PageAttachmentRow>(attachment =>
        {
            attachment.ToTable("page_attachments");
            attachment.HasKey(a => a.Id);
            attachment.Property(a => a.Id).ValueGeneratedNever();
            attachment.Property(a => a.FileName).HasMaxLength(MaxFileNameLength);
            attachment.Property(a => a.ContentType).HasMaxLength(MaxContentTypeLength);
            attachment.Property(a => a.Sha256).HasMaxLength(Sha256Length);
            attachment.HasOne<PageRow>().WithMany().HasForeignKey(a => a.PageId);
            attachment.HasIndex(a => new { a.PageId, a.UploadedAt });

            // 儲存空間的用量：組織的總量與每人過去 24 小時的量，都能只讀索引。
            attachment.HasIndex(a => new { a.OrganizationId, a.UploadedBy, a.UploadedAt })
                .IncludeProperties(a => a.SizeBytes)
                .HasDatabaseName("ix_page_attachments_quota");
        });
    }
}
