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
            page.HasOne<PageRow>().WithMany().HasForeignKey(p => p.ParentId);
            page.HasIndex(p => new { p.SpaceId, p.Number }).IsUnique();
            page.HasIndex(p => new { p.SpaceId, p.ParentId, p.Position });
            page.HasIndex(p => new { p.SpaceId, p.DeletionId });
        });

        modelBuilder.Entity<PageRestrictionRow>(restriction =>
        {
            restriction.ToTable("page_restrictions");
            restriction.HasKey(r => new { r.PageId, r.PersonId });
            restriction.HasOne<PageRow>().WithMany().HasForeignKey(r => r.PageId);
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
        });
    }
}
