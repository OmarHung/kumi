using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work;

/// <summary>
/// 專案總覽：一次看多個專案的進度（交接 internal/HANDOFF-project-overview.md 第 5 節）。
/// 只讀，數字依呼叫者看得到的項目算（<c>ItemScope</c>：只看自己的人只算自己負責或建立的），看不到的專案不出現。
/// </summary>
public interface IProjectOverviewService
{
    /// <summary>
    /// 沒有封存、呼叫者看得到的專案，每個一列，依專案代號排序（畫面自己再排）。
    /// <see cref="ProjectOverviewScope.Managed"/> 只列呼叫者有 <c>project.manage</c> 的（組織管理員是全部）。
    /// 「今天」依組織的時區（<see cref="IOrganizationClock"/>）；一次查詢算完所有專案，不要每個專案各查一次。
    /// </summary>
    Task<IReadOnlyList<ProjectOverview>> ListAsync(Actor actor, ProjectOverviewScope scope, CancellationToken cancellationToken);
}

public enum ProjectOverviewScope
{
    /// <summary>呼叫者管理的專案（有 <c>project.manage</c>；組織管理員是全部）。</summary>
    Managed,

    /// <summary>呼叫者看得到的所有專案。</summary>
    Visible,
}

/// <summary>
/// 一個專案的總覽。數字都不含已刪除的項目，「還沒結束」是類別 backlog、not_started、in_progress。
/// </summary>
/// <param name="OwnItemsOnly">呼叫者在這個專案只看得到自己的項目（數字只算那些）。</param>
/// <param name="States">各狀態的項目數，依藍圖裡流程與狀態的順序；同一個 key 出現在多個流程時合併成一筆（名稱用第一個流程的）。數量是 0 的不列。</param>
/// <param name="Open">還沒結束的項目數。</param>
/// <param name="Overdue">還沒結束、到期日早於今天的。</param>
/// <param name="DueSoon">還沒結束、到期日在今天到 <see cref="ProjectOverviewRules.DueSoonDays"/> − 1 天後之間的。</param>
/// <param name="Unassigned">還沒結束、沒有負責人的。</param>
/// <param name="Stale">還沒結束、最後更新早於現在減 <see cref="ProjectOverviewRules.StaleDays"/> 天的。</param>
/// <param name="DoneRecently">
/// 現在是 done 類別、最後更新在 <see cref="ProjectOverviewRules.DoneRecentlyDays"/> 天內的。
/// 沒有記完成的時間，完成之後又改過也算進來，所以是近似值：畫面只顯示、不連到篩選。
/// </param>
/// <param name="CurrentCycle">今天落在期間內的週期（有好幾個時取開始日最晚的）；沒有是 null。</param>
/// <param name="NextMilestone">到期日是今天或之後、還有沒結束的項目、到期日最早的里程碑；沒有是 null。</param>
/// <param name="LastUpdatedAt">看得到的項目裡最後一次更新的時間；沒有項目是 null。</param>
public sealed record ProjectOverview(
    string ProjectKey,
    string Name,
    bool OwnItemsOnly,
    IReadOnlyList<OverviewStateCount> States,
    int Open,
    int Overdue,
    int DueSoon,
    int Unassigned,
    int Stale,
    int DoneRecently,
    OverviewCycle? CurrentCycle,
    OverviewMilestone? NextMilestone,
    DateTimeOffset? LastUpdatedAt);

public sealed record OverviewStateCount(string Key, string Name, StateCategory Category, int Count);

/// <param name="Progress">只算呼叫者看得到的項目（和規劃頁的進度條同一種算法，範圍照 <c>ItemScope</c>）。</param>
public sealed record OverviewCycle(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, ItemProgress Progress);

/// <param name="Progress">只算呼叫者看得到的項目。</param>
public sealed record OverviewMilestone(Guid Id, string Name, DateOnly DueDate, ItemProgress Progress);

/// <summary>總覽的門檻。網頁的說明文字與連結的篩選用同一組數字。</summary>
public static class ProjectOverviewRules
{
    /// <summary>「快到期」：今天起算幾天（含今天）。</summary>
    public const int DueSoonDays = 7;

    /// <summary>「停滯」：還沒結束、幾天沒有更新。</summary>
    public const int StaleDays = 14;

    /// <summary>「最近完成」：幾天內。</summary>
    public const int DoneRecentlyDays = 7;
}
