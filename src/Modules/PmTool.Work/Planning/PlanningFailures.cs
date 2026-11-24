using PmTool.Abstractions;
using PmTool.Work.Persistence;
using static PmTool.Work.Common.TextRules;

namespace PmTool.Work.Planning;

internal static class PlanningFailures
{
    /// <summary>別的專案的編號與不存在的編號回一樣的錯誤，不透露它存在。</summary>
    public static Failure CycleNotFound() => new(FailureKind.NotFound, WorkErrorCodes.CycleNotFound, "找不到這個週期。");

    public static Failure MilestoneNotFound() => new(FailureKind.NotFound, WorkErrorCodes.MilestoneNotFound, "找不到這個里程碑。");

    public static Failure CycleNameTaken(string name) => new(
        FailureKind.Conflict, WorkErrorCodes.CycleNameTaken, $"這個專案已經有一個叫{Quote(name)}的週期了（不分大小寫），請換一個名稱。");

    public static Failure MilestoneNameTaken(string name) => new(
        FailureKind.Conflict, WorkErrorCodes.MilestoneNameTaken, $"這個專案已經有一個叫{Quote(name)}的里程碑了（不分大小寫），請換一個名稱。");

    public static Failure CycleOverlaps(CycleEntity other) => new(
        FailureKind.Conflict,
        WorkErrorCodes.CycleOverlaps,
        $"日期和週期{Quote(other.Name)}（{PlanningChecks.Format(other.StartDate)} 到 {PlanningChecks.Format(other.EndDate)}）重疊了；同一個專案的週期日期不能重疊。");

    public static Failure CycleLimitReached() => new(
        FailureKind.Conflict, WorkErrorCodes.CycleLimitReached, $"一個專案最多 {PlanningRules.MaxPerProject} 個週期，請先刪掉用不到的。");

    public static Failure MilestoneLimitReached() => new(
        FailureKind.Conflict, WorkErrorCodes.MilestoneLimitReached, $"一個專案最多 {PlanningRules.MaxPerProject} 個里程碑，請先刪掉用不到的。");
}
