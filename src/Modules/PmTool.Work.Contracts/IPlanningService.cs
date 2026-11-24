using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>一群項目的完成情形。已刪除的項目不算。</summary>
/// <param name="Total">全部的項目數。</param>
/// <param name="Done">狀態類別是 done 的。</param>
/// <param name="Canceled">狀態類別是 canceled 的。</param>
/// <param name="EstimateTotal">有填估計的項目，估計的總和；一個都沒填是 null。</param>
/// <param name="EstimateDone">其中 done 的估計總和。<paramref name="EstimateTotal"/> 不是 null 時至少是 0（沒有完成的就是 0）；兩個都沒有估計才都是 null。</param>
public sealed record ItemProgress(int Total, int Done, int Canceled, decimal? EstimateTotal, decimal? EstimateDone);

public sealed record Cycle(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ItemProgress Progress);

public sealed record CycleDraft(string Name, DateOnly StartDate, DateOnly EndDate);

public sealed record Milestone(Guid Id, string Name, DateOnly DueDate, string? Description, ItemProgress Progress);

public sealed record MilestoneDraft(string Name, DateOnly DueDate, string? Description);

/// <summary>
/// 週期與里程碑。讀要能讀專案的項目（item.read，或組織管理員）；新增、修改、刪除要 item.update。
/// 「目前的週期」由呼叫端依自己的「今天」判斷：伺服器不知道使用者的時區。
/// </summary>
public interface IPlanningService
{
    /// <summary>依開始日排序。</summary>
    Task<Result<IReadOnlyList<Cycle>>> ListCyclesAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    Task<Result<Cycle>> CreateCycleAsync(Actor actor, string projectKey, CycleDraft draft, CancellationToken cancellationToken);

    Task<Result<Cycle>> UpdateCycleAsync(
        Actor actor, string projectKey, Guid cycleId, CycleDraft draft, CancellationToken cancellationToken);

    /// <summary>項目不會被刪除，只是變成沒有週期（不留活動紀錄、不寫事件，已知限制）。</summary>
    Task<Result<Unit>> DeleteCycleAsync(Actor actor, string projectKey, Guid cycleId, CancellationToken cancellationToken);

    /// <summary>依到期日排序，同一天依名稱。</summary>
    Task<Result<IReadOnlyList<Milestone>>> ListMilestonesAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    Task<Result<Milestone>> CreateMilestoneAsync(
        Actor actor, string projectKey, MilestoneDraft draft, CancellationToken cancellationToken);

    Task<Result<Milestone>> UpdateMilestoneAsync(
        Actor actor, string projectKey, Guid milestoneId, MilestoneDraft draft, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteMilestoneAsync(Actor actor, string projectKey, Guid milestoneId, CancellationToken cancellationToken);
}

/// <summary>週期與里程碑的限制，網頁的欄位也用同一組數字。</summary>
public static class PlanningRules
{
    public const int MaxCycleNameLength = 50;
    public const int MaxMilestoneNameLength = 100;
    public const int MaxDescriptionLength = 2_000;

    /// <summary>週期最長幾天，頭尾都算（兩週的衝刺是 14 天）。</summary>
    public const int MaxCycleDays = 365;

    /// <summary>一個專案最多幾個週期、幾個里程碑，各自計算。</summary>
    public const int MaxPerProject = 200;
}
