using Microsoft.EntityFrameworkCore;
using PmTool.Abstractions;
using PmTool.Work.Persistence;

namespace PmTool.Work.Items;

/// <summary>
/// 項目的週期與里程碑要是同一個專案的：別的專案的編號與不存在的編號一樣擋下，路徑是 cycleId、milestoneId。
/// 讀的同時取 FOR KEY SHARE 直到交易結束：刪除週期的人要等這裡提交，寫進去的外鍵就不會指到剛被刪掉的列。
/// 只在交易裡有意義，新增與修改項目都在交易裡呼叫。
/// </summary>
internal static class PlanningReferenceRules
{
    public const string CyclePath = "cycleId";
    public const string MilestonePath = "milestoneId";

    public static async Task<IReadOnlyList<FailureDetail>> CheckAsync(
        WorkDbContext db, ProjectEntity project, WorkItemPatch patch, CancellationToken cancellationToken)
    {
        var problems = new List<FailureDetail>();
        if (patch.CycleId is { HasValue: true, Value: { } cycleId } && !await CycleExistsAsync(db, project, cycleId, cancellationToken))
        {
            problems.Add(Problem(CyclePath, "找不到這個週期；週期要是同一個專案的。"));
        }

        if (patch.MilestoneId is { HasValue: true, Value: { } milestoneId } && !await MilestoneExistsAsync(db, project, milestoneId, cancellationToken))
        {
            problems.Add(Problem(MilestonePath, "找不到這個里程碑；里程碑要是同一個專案的。"));
        }

        return problems;
    }

    private static async Task<bool> CycleExistsAsync(WorkDbContext db, ProjectEntity project, Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.Cycles
            .FromSql($"""
                SELECT * FROM work.cycles
                WHERE organization_id = {project.OrganizationId} AND project_id = {project.Id} AND id = {id}
                FOR KEY SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.Count > 0;
    }

    private static async Task<bool> MilestoneExistsAsync(WorkDbContext db, ProjectEntity project, Guid id, CancellationToken cancellationToken)
    {
        var rows = await db.Milestones
            .FromSql($"""
                SELECT * FROM work.milestones
                WHERE organization_id = {project.OrganizationId} AND project_id = {project.Id} AND id = {id}
                FOR KEY SHARE
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return rows.Count > 0;
    }

    private static FailureDetail Problem(string path, string message) => new(WorkErrorCodes.ItemInvalidValue, path, message);
}
