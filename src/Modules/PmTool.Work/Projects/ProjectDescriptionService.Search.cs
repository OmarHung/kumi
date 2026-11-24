using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Projects;

internal sealed partial class ProjectDescriptionService
{
    // 排序的層級：代號完全符合、代號或名稱以它開頭、代號或名稱包含、只有說明包含。
    private const int ExactKeyRank = 0;
    private const int PrefixRank = 1;
    private const int ContainsRank = 2;
    private const int DescriptionOnlyRank = 3;

    public async Task<Result<IReadOnlyList<ProjectSearchHit>>> SearchProjectsAsync(
        Actor actor, string text, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // 文字與數量的規則和項目的搜尋同一份（同樣的上限、同樣的路徑 q 與 limit）。
        var checkedText = ItemSearch.Validate(text, limit);
        if (checkedText.Failure is { } invalid)
        {
            return invalid;
        }

        var query = checkedText.Value;
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = await FindAsync(db, actor, query, limit, cancellationToken);
        var hits = rows
            .Select(row => new ProjectSearchHit(row.Summary, row.Rank == DescriptionOnlyRank ? ProjectSnippet.From(row.Text, query) : null))
            .ToArray();
        return hits;
    }

    private sealed record SearchRow(ProjectSummary Summary, int Rank, string? Text);

    // 比對、排序、limit 都在資料庫裡做；說明只在「只有說明比對到」時才讀回來，不為名稱就找到的專案搬幾十 KB 的文字。
    private static async Task<List<SearchRow>> FindAsync(
        WorkDbContext db, Actor actor, string query, int limit, CancellationToken cancellationToken)
    {
        var escaped = ItemQueryPlan.EscapeLike(query);
        var contains = "%" + escaped + "%";
        var prefix = escaped + "%";
        var escape = ItemQueryPlan.LikeEscape;

        var found = from visible in VisibleProjects.Of(db, actor)
                    join description in db.ProjectDescriptions.Where(d => d.OrganizationId == actor.OrganizationId)
                        on visible.Project.Id equals description.ProjectId into descriptions
                    from description in descriptions.DefaultIfEmpty()
                    let project = visible.Project
                    let rank = EF.Functions.ILike(project.Key, escaped, escape) ? ExactKeyRank
                        : EF.Functions.ILike(project.Key, prefix, escape) || EF.Functions.ILike(project.Name, prefix, escape) ? PrefixRank
                        : EF.Functions.ILike(project.Key, contains, escape) || EF.Functions.ILike(project.Name, contains, escape) ? ContainsRank
                        : DescriptionOnlyRank
                    where rank != DescriptionOnlyRank || (description.Text != null && EF.Functions.ILike(description.Text, contains, escape))
                    orderby rank, project.ArchivedAt != null, project.Key
                    select new { Visible = visible, Rank = rank, Text = rank == DescriptionOnlyRank ? description.Text : null };

        var rows = await found.AsNoTracking().Take(limit).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SearchRow(
            new ProjectSummary(
                row.Visible.Project.Id, row.Visible.Project.Key, row.Visible.Project.Name,
                row.Visible.Project.CurrentBlueprintVersion, row.Visible.Role, row.Visible.Project.CreatedAt)
            {
                ArchivedAt = row.Visible.Project.ArchivedAt,
            },
            row.Rank,
            row.Text))];
    }
}
