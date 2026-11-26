using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PmTool.Work.Access;
using PmTool.Work.Items;
using PmTool.Work.Persistence;

namespace PmTool.Work.Planning;

/// <summary>週期與里程碑的進度。一次查詢把每一組的數字都算出來，不是一個一個查。</summary>
internal static class PlanningProgress
{
    public static readonly ItemProgress Empty = new(0, 0, 0, null, null);

    private sealed record Row(
        Guid? Key, int Total, int Done, int Canceled, int Estimated, decimal? EstimateTotal, decimal? EstimateDone);

    /// <summary>每個週期的進度；一個項目都沒有的週期不在結果裡，呼叫端用 <see cref="Empty"/>。</summary>
    public static Task<IReadOnlyDictionary<Guid, ItemProgress>> OfCyclesAsync(
        WorkDbContext db, ProjectAccess access, Guid? onlyCycle, CancellationToken cancellationToken) =>
        SummarizeAsync(
            InScope(db, access).Where(i => i.CycleId != null && (onlyCycle == null || i.CycleId == onlyCycle)),
            i => i.CycleId,
            cancellationToken);

    public static Task<IReadOnlyDictionary<Guid, ItemProgress>> OfMilestonesAsync(
        WorkDbContext db, ProjectAccess access, Guid? onlyMilestone, CancellationToken cancellationToken) =>
        SummarizeAsync(
            InScope(db, access).Where(i => i.MilestoneId != null && (onlyMilestone == null || i.MilestoneId == onlyMilestone)),
            i => i.MilestoneId,
            cancellationToken);

    /// <summary>
    /// 一批週期的進度，項目範圍由呼叫端給（總覽跨專案，已經套好各專案的範圍）。週期的編號全域唯一，不必再分專案。
    /// </summary>
    public static Task<IReadOnlyDictionary<Guid, ItemProgress>> OfCyclesAsync(
        IQueryable<ItemEntity> scopedItems, IReadOnlyCollection<Guid> cycleIds, CancellationToken cancellationToken)
    {
        var ids = cycleIds.ToArray();
        return SummarizeAsync(scopedItems.Where(i => i.CycleId != null && ids.Contains(i.CycleId.Value)), i => i.CycleId, cancellationToken);
    }

    public static Task<IReadOnlyDictionary<Guid, ItemProgress>> OfMilestonesAsync(
        IQueryable<ItemEntity> scopedItems, IReadOnlyCollection<Guid> milestoneIds, CancellationToken cancellationToken)
    {
        var ids = milestoneIds.ToArray();
        return SummarizeAsync(
            scopedItems.Where(i => i.MilestoneId != null && ids.Contains(i.MilestoneId.Value)), i => i.MilestoneId, cancellationToken);
    }

    /// <summary>這個專案、呼叫者看得到的項目：只看自己的人，進度只算他看得到的，不讓人從數字推出看不到的項目。</summary>
    private static IQueryable<ItemEntity> InScope(WorkDbContext db, ProjectAccess access) =>
        access.Items.Apply(ItemStore.Live(db).Where(i => i.OrganizationId == access.Project.OrganizationId && i.ProjectId == access.Project.Id));

    // 刪除的項目已經被 Live 排掉。估計只加有填的：沒填的不是 0，一個都沒填時總和是 null。
    // EF 會把一組的總和補成 0（COALESCE），分不出「都沒填」和「加起來是 0」，所以另外數有填的有幾個。
    // 有估計時完成的那份至少是 0（還沒有完成的就是 0 點），畫面才分得出「沒有資料」與「一點都還沒完成」。
    private static async Task<IReadOnlyDictionary<Guid, ItemProgress>> SummarizeAsync(
        IQueryable<ItemEntity> items, Expression<Func<ItemEntity, Guid?>> key, CancellationToken cancellationToken)
    {
        var done = StateCategoryText.Done;
        var canceled = StateCategoryText.Canceled;
        var rows = await items
            .GroupBy(key)
            .Select(g => new Row(
                g.Key,
                g.Count(),
                g.Count(i => i.StateCategory == done),
                g.Count(i => i.StateCategory == canceled),
                g.Count(i => i.Estimate != null),
                g.Sum(i => i.Estimate),
                g.Where(i => i.StateCategory == done).Sum(i => i.Estimate)))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(
            r => r.Key!.Value,
            r => new ItemProgress(
                r.Total, r.Done, r.Canceled, r.Estimated > 0 ? r.EstimateTotal : null, r.Estimated > 0 ? r.EstimateDone ?? 0 : null));
    }
}
