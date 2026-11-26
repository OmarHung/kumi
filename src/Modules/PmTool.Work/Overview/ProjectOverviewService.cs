using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Access;
using PmTool.Work.Blueprints;
using PmTool.Work.Common;
using PmTool.Work.Items;
using PmTool.Work.Persistence;
using PmTool.Work.Planning;

namespace PmTool.Work.Overview;

/// <summary>
/// 專案總覽。所有專案的數字用同一個查詢依（專案、狀態、類別）分組算出來，週期、里程碑與它們的進度也各一次，
/// 查詢的次數和專案的個數無關。項目的範圍和其他讀項目的路一樣用 <see cref="ItemScope"/>（只看自己的人只算自己的）。
/// </summary>
internal sealed class ProjectOverviewService(
    IDbContextFactory<WorkDbContext> contexts, BlueprintCache cache, IOrganizationClock clock, TimeProvider time) : IProjectOverviewService
{
    public async Task<IReadOnlyList<ProjectOverview>> ListAsync(Actor actor, ProjectOverviewScope scope, CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var now = WorkClock.Now(time);
        var zone = await clock.TimeZoneAsync(actor.OrganizationId, cancellationToken);
        var today = OrganizationTime.Today(zone, now);

        var candidates = await CandidatesAsync(db, actor, scope, cancellationToken);
        var candidateIds = candidates.Ids.ToArray();
        var projects = await db.Projects
            .AsNoTracking()
            .Where(p => p.OrganizationId == actor.OrganizationId && p.ArchivedAt == null && candidateIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Key, p.Name, p.CurrentBlueprintVersion })
            .ToListAsync(cancellationToken);
        if (projects.Count == 0)
        {
            return [];
        }

        var projectIds = projects.Select(p => p.Id).ToArray();
        var scoped = ItemScope.Across(
            ItemStore.Live(db).AsNoTracking().Where(i => i.OrganizationId == actor.OrganizationId),
            [.. projectIds.Where(id => !candidates.OwnOnly.Contains(id))],
            [.. projectIds.Where(candidates.OwnOnly.Contains)],
            actor.PersonId);

        var schemas = await cache.GetManyAsync(
            db, actor.OrganizationId, [.. projects.Select(p => (p.Id, p.CurrentBlueprintVersion))], cancellationToken);
        var groups = (await GroupsAsync(scoped, today, now, cancellationToken)).ToLookup(g => g.ProjectId);
        var planning = await PlanningAsync(db, scoped, projectIds, today, cancellationToken);

        return [.. projects
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => Build(p.Id, p.Key, p.Name, candidates.OwnOnly.Contains(p.Id), schemas[p.Id], [.. groups[p.Id]], planning))];
    }

    private static ProjectOverview Build(
        Guid id, string key, string name, bool ownOnly, ProjectSchema schema, IReadOnlyList<OverviewGroup> groups, PlanningPicks planning)
    {
        var open = groups.Where(g => IsOpen(g.StateCategory)).Sum(g => g.Total);
        return new ProjectOverview(
            key,
            name,
            ownOnly,
            OverviewStates.Of(schema, groups),
            open,
            groups.Sum(g => g.Overdue),
            groups.Sum(g => g.DueSoon),
            groups.Sum(g => g.Unassigned),
            groups.Sum(g => g.Stale),
            groups.Sum(g => g.DoneRecently),
            planning.Cycles.GetValueOrDefault(id),
            planning.Milestones.GetValueOrDefault(id),
            groups.Count == 0 ? null : groups.Max(g => g.LastUpdatedAt));
    }

    private static bool IsOpen(string category) => category is not (StateCategoryText.Done or StateCategoryText.Canceled);

    /// <summary>範圍裡的專案編號，以及其中呼叫者只看得到自己項目的。組織管理員看全部，沒有「只看自己」的。</summary>
    private async Task<Candidates> CandidatesAsync(
        WorkDbContext db, Actor actor, ProjectOverviewScope scope, CancellationToken cancellationToken)
    {
        if (actor.IsOrganizationAdmin)
        {
            return new Candidates(await ReadableProjects.IdsAsync(db, cache, actor, cancellationToken), new HashSet<Guid>());
        }

        // 讀得到項目的專案才算「看得到」；管理的專案再從中挑，角色有 project.manage 卻讀不到項目的不列。
        var readable = await ReadableProjects.OwnOnlyByProjectAsync(db, cache, actor, cancellationToken);
        var ids = readable.Keys.ToHashSet();
        if (scope == ProjectOverviewScope.Managed)
        {
            ids.IntersectWith(await ReadableProjects.ManageableIdsAsync(db, cache, actor, cancellationToken));
        }

        return new Candidates(ids, readable.Where(pair => pair.Value && ids.Contains(pair.Key)).Select(pair => pair.Key).ToHashSet());
    }

    // 刪除的項目已被 Live 排掉。停滯看「現在減 14 天」，到期看組織的今天。
    private static async Task<IReadOnlyList<OverviewGroup>> GroupsAsync(
        IQueryable<ItemEntity> scoped, DateOnly today, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var done = StateCategoryText.Done;
        var canceled = StateCategoryText.Canceled;
        var soonEnd = today.AddDays(ProjectOverviewRules.DueSoonDays);
        var staleBefore = now.AddDays(-ProjectOverviewRules.StaleDays);
        var doneSince = now.AddDays(-ProjectOverviewRules.DoneRecentlyDays);
        return await scoped
            .GroupBy(i => new { i.ProjectId, i.State, i.StateCategory })
            .Select(g => new OverviewGroup(
                g.Key.ProjectId,
                g.Key.State,
                g.Key.StateCategory,
                g.Count(),
                g.Count(i => i.StateCategory != done && i.StateCategory != canceled && i.DueDate < today),
                g.Count(i => i.StateCategory != done && i.StateCategory != canceled && i.DueDate >= today && i.DueDate < soonEnd),
                g.Count(i => i.StateCategory != done && i.StateCategory != canceled && i.AssigneeId == null),
                g.Count(i => i.StateCategory != done && i.StateCategory != canceled && i.UpdatedAt < staleBefore),
                g.Count(i => i.StateCategory == done && i.UpdatedAt >= doneSince),
                g.Max(i => i.UpdatedAt)))
            .ToListAsync(cancellationToken);
    }

    private static async Task<PlanningPicks> PlanningAsync(
        WorkDbContext db, IQueryable<ItemEntity> scoped, Guid[] projectIds, DateOnly today, CancellationToken cancellationToken)
    {
        var cycles = (await db.Cycles
                .AsNoTracking()
                .Where(c => projectIds.Contains(c.ProjectId) && c.StartDate <= today && c.EndDate >= today)
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.ProjectId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.StartDate).ThenBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.Id).First());

        var done = StateCategoryText.Done;
        var canceled = StateCategoryText.Canceled;
        var milestones = (await db.Milestones
                .AsNoTracking()
                .Where(m => projectIds.Contains(m.ProjectId)
                    && m.DueDate >= today
                    && scoped.Any(i => i.MilestoneId == m.Id && i.StateCategory != done && i.StateCategory != canceled))
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.ProjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.DueDate).ThenBy(m => m.Name, StringComparer.Ordinal).ThenBy(m => m.Id).First());

        var cycleProgress = cycles.Count == 0
            ? new Dictionary<Guid, ItemProgress>()
            : await PlanningProgress.OfCyclesAsync(scoped, [.. cycles.Values.Select(c => c.Id)], cancellationToken);
        var milestoneProgress = milestones.Count == 0
            ? new Dictionary<Guid, ItemProgress>()
            : await PlanningProgress.OfMilestonesAsync(scoped, [.. milestones.Values.Select(m => m.Id)], cancellationToken);

        return new PlanningPicks(
            cycles.ToDictionary(
                pair => pair.Key,
                pair => new OverviewCycle(
                    pair.Value.Id, pair.Value.Name, pair.Value.StartDate, pair.Value.EndDate,
                    cycleProgress.GetValueOrDefault(pair.Value.Id, PlanningProgress.Empty))),
            milestones.ToDictionary(
                pair => pair.Key,
                pair => new OverviewMilestone(
                    pair.Value.Id, pair.Value.Name, pair.Value.DueDate,
                    milestoneProgress.GetValueOrDefault(pair.Value.Id, PlanningProgress.Empty))));
    }

    private sealed record Candidates(IReadOnlySet<Guid> Ids, IReadOnlySet<Guid> OwnOnly);

    private sealed record PlanningPicks(
        IReadOnlyDictionary<Guid, OverviewCycle> Cycles, IReadOnlyDictionary<Guid, OverviewMilestone> Milestones);
}
