using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>個人的檢視：存起來的篩選、排序與版面，不複製資料。只有建立的人看得到。</summary>
public interface ISavedViewService
{
    /// <summary>自己在這個專案的檢視，依名稱排序。</summary>
    Task<Result<IReadOnlyList<SavedView>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    Task<Result<SavedView>> CreateAsync(Actor actor, string projectKey, SavedViewDraft draft, CancellationToken cancellationToken);

    /// <summary>整個換掉名稱、版面與條件（網頁的「更新成目前的條件」「重新命名」都送完整的一份）。</summary>
    Task<Result<SavedView>> ReplaceAsync(
        Actor actor, string projectKey, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteAsync(Actor actor, string projectKey, Guid viewId, CancellationToken cancellationToken);

    // 「我的工作」的檢視：不屬於任何專案，名稱只在自己的「我的工作」裡不能重複。條件的意思和專案的不同（見 SavedViewQuery），
    // 驗證也分開：專案的檢視不收 Category、Project，「我的工作」的不收 Cycle、Milestone。

    /// <summary>自己的「我的工作」檢視，依名稱排序。</summary>
    Task<IReadOnlyList<SavedView>> ListMyWorkAsync(Actor actor, CancellationToken cancellationToken);

    Task<Result<SavedView>> CreateMyWorkAsync(Actor actor, SavedViewDraft draft, CancellationToken cancellationToken);

    Task<Result<SavedView>> ReplaceMyWorkAsync(Actor actor, Guid viewId, SavedViewDraft draft, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteMyWorkAsync(Actor actor, Guid viewId, CancellationToken cancellationToken);
}

public enum SavedViewLayout
{
    Table,
    Board,
    Calendar,
    Timeline,
}

/// <summary>
/// 和網頁網址上的篩選同一套（web/src/domain/itemFilters.ts），每一個都是選填。
/// 存的是「本週到期」這種相對的說法，不是日期：檢視過了一週還是本週。
/// 類型、狀態只檢查格式，不檢查現在的藍圖有沒有：藍圖之後可能改，網頁套用時不認得的就略過。
/// </summary>
/// <param name="Cycle">current（目前的週期）、none（沒有週期）或週期編號。存 current 不存編號：過了一個衝刺，打開還是目前的那一個。</param>
/// <param name="Milestone">none（沒有里程碑）或里程碑編號。</param>
/// <param name="Fields">
/// 自訂欄位的條件：欄位的 key 對到值，和網址的 <c>field.&lt;key&gt;</c> 同一種寫法（多個值用逗號分開，<c>_none</c> 是「沒有填」，人員可以是 <c>me</c>）。
/// key 與值只檢查格式，不檢查現在的藍圖：欄位可能被刪、選項可能被改，網頁套用時不認得的就略過。最多 <see cref="FieldFilter.MaxPerQuery"/> 個欄位。
/// </param>
/// <param name="Group">
/// 專案的檢視：看板的分組，<c>state</c>（預設）、<c>assignee</c> 或 <c>field.&lt;key&gt;</c>（單選欄位）。其他版面不用，存著也無妨。
/// 「我的工作」的檢視：表格的分組，<c>project</c>；依急迫程度（預設）時是 null。
/// </param>
/// <param name="Category">
/// 只有「我的工作」用：狀態類別的條件。null 是還沒結束的（backlog、not_started、in_progress，「我的工作」的預設）；
/// <c>all</c> 是所有狀態；或一個類別（<c>backlog</c>、<c>not_started</c>、<c>in_progress</c>、<c>done</c>、<c>canceled</c>）。
/// 和 State 只能有一個。「我的工作」的 Assignee 也不同：null 是「我」（預設），<c>all</c> 是所有人，另外是 <c>unassigned</c> 或人員編號。
/// </param>
/// <param name="Project">只有「我的工作」用：只看這個專案（代號）。「我的工作」的 State 只有在有 Project 時才有意義（狀態是專案的流程定義的）。</param>
/// <param name="CreatedBy">
/// 只有專案的檢視用：建立者的條件，<c>me</c> 或人員編號（和網址的 <c>createdBy</c> 同一種寫法）。
/// </param>
/// <param name="Open">只有專案的檢視用：true 是「只看還沒結束的」（網址 <c>open=1</c>）；沒開是 null。</param>
/// <param name="Stale">
/// 只有專案的檢視用：true 是「很久沒有更新」（網址 <c>stale=1</c>，<see cref="ProjectOverviewRules.StaleDays"/> 天）。
/// 存的是這個說法不是日期，套用時才換成當下的時間。
/// </param>
/// <param name="Mine">
/// 只有「我的工作」用：null 是我負責的（預設），<c>created</c> 是我建立的（這時只看我建立的，負責人不列為條件）。
/// </param>
public sealed record SavedViewQuery(
    string? Type,
    string? State,
    string? Assignee,
    string? Search,
    string? Sort,
    string? Due,
    string? Priority,
    string? Cycle = null,
    string? Milestone = null,
    IReadOnlyDictionary<string, string>? Fields = null,
    string? Group = null,
    string? Category = null,
    string? Project = null,
    string? CreatedBy = null,
    string? Mine = null,
    bool? Open = null,
    bool? Stale = null);

public sealed record SavedViewDraft(string Name, SavedViewLayout Layout, SavedViewQuery Query);

public sealed record SavedView(
    Guid Id, string Name, SavedViewLayout Layout, SavedViewQuery Query, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>檢視的限制，網頁的欄位也用同一組數字。</summary>
public static class SavedViewRules
{
    public const int MaxNameLength = 50;
    public const int MaxSearchLength = 200;

    /// <summary>一個人在一個專案最多幾個檢視。</summary>
    public const int MaxPerProject = 50;

    /// <summary>一個人的「我的工作」最多幾個檢視。</summary>
    public const int MaxMyWork = 50;
}
