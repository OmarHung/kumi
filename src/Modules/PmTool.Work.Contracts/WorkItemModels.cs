using System.Text.Json;
using PmTool.Abstractions;
using PmTool.Blueprint.Model;

namespace PmTool.Work;

public enum Priority
{
    None,
    Low,
    Medium,
    High,
    Urgent,
}

/// <summary>
/// 一個工作項目。核心欄位是固定的屬性；自訂欄位的值放在 <see cref="Fields"/>，以欄位的 key 為鍵，
/// 值的形狀依欄位型別：文字類與連結是字串，數字與金額是數字，日期是 YYYY-MM-DD 的字串，
/// 單選是選項的 key，多選是選項 key 的陣列，成員是人員編號的字串，勾選是布林。沒有值的欄位不出現。
/// </summary>
/// <param name="Key">項目編號，專案代號加流水號，例如 WEB-12。</param>
public sealed record WorkItem(
    string Key,
    string ProjectKey,
    long Number,
    string Type,
    string Title,
    string State,
    StateCategory StateCategory,
    Guid? AssigneeId,
    Priority Priority,
    DateOnly? StartDate,
    DateOnly? DueDate,
    decimal? Estimate,
    string? ParentKey,
    IReadOnlyDictionary<string, JsonElement> Fields,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    /// 說明，純文字（之後換成富文字）。只有單一項目的讀取、新增、修改、改狀態會帶；
    /// 清單與「我的工作」一律是 null，一頁兩百個項目不必帶著長文字。
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 說明的版本（<see cref="DescriptionRevisions.Of"/>，說明內容的雜湊）。只在有帶說明的讀取（單一項目）有意義。
    /// 改說明時把開始編輯時拿到的這個值放進 <see cref="WorkItemPatch.ExpectedDescriptionRevision"/>：
    /// 別人在這中間改過說明就會被擋下，不會默默蓋掉別人的修改。
    /// </summary>
    public string DescriptionRevision => DescriptionRevisions.Of(Description);

    /// <summary>排進的週期；沒有是 null。週期被刪除後項目就變成沒有。</summary>
    public Guid? CycleId { get; init; }

    /// <summary>歸屬的里程碑；沒有是 null。</summary>
    public Guid? MilestoneId { get; init; }

    /// <summary>
    /// 建立項目的人（人員編號）。建立之後不能改：PM 追「我開出去的項目」靠它，執行者「只看自己的」也靠它。
    /// 歷史匯入時是 <see cref="HistoryItem.CreatedBy"/>（對不上的人是匯入時指定的替代帳號）。
    /// </summary>
    public required Guid CreatedBy { get; init; }
}

/// <summary>刪除（封存）的結果。</summary>
/// <param name="Key">被刪除的項目編號。</param>
public sealed record DeletedWorkItem(string Key, DateTimeOffset DeletedAt);

/// <summary>新增項目。狀態不能指定，一律從流程的第一個狀態開始。</summary>
public sealed record NewWorkItem
{
    public required string Type { get; init; }

    public required string Title { get; init; }

    public Guid? AssigneeId { get; init; }

    public Priority Priority { get; init; } = Priority.None;

    public DateOnly? StartDate { get; init; }

    public DateOnly? DueDate { get; init; }

    public decimal? Estimate { get; init; }

    public string? ParentKey { get; init; }

    /// <summary>說明，長度上限見 <see cref="WorkRules.MaxDescriptionLength"/>。可以有換行。</summary>
    public string? Description { get; init; }

    /// <summary>必須是同一個專案的週期，否則回驗證錯誤，路徑是 cycleId。</summary>
    public Guid? CycleId { get; init; }

    /// <summary>必須是同一個專案的里程碑，否則回驗證錯誤，路徑是 milestoneId。</summary>
    public Guid? MilestoneId { get; init; }

    public IReadOnlyDictionary<string, JsonElement> Fields { get; init; } = new Dictionary<string, JsonElement>();
}

/// <summary>
/// 以欄位為單位的修改：只改有給的欄位，兩個人同時改不同欄位不會互相蓋掉。
/// 狀態不能用這個改，要走 <see cref="IWorkItemService.TransitionAsync"/>，流程的規則才會生效。
/// </summary>
public sealed record WorkItemPatch
{
    public Optional<string> Title { get; init; }

    public Optional<Guid?> AssigneeId { get; init; }

    public Optional<Priority> Priority { get; init; }

    public Optional<DateOnly?> StartDate { get; init; }

    public Optional<DateOnly?> DueDate { get; init; }

    public Optional<decimal?> Estimate { get; init; }

    public Optional<string?> ParentKey { get; init; }

    /// <summary>給 null 或空字串是清掉說明。</summary>
    public Optional<string?> Description { get; init; }

    /// <summary>
    /// 改說明時帶著：開始編輯時的 <see cref="WorkItem.DescriptionRevision"/>。和現在的說明的版本不同（別人在這中間改過），
    /// 整個修改都不做，回 Conflict <see cref="WorkErrorCodes.ItemDescriptionConflict"/>。
    /// null 是不檢查（CLI、匯入、舊的網頁）。<see cref="Description"/> 沒有給時不看。
    /// </summary>
    public string? ExpectedDescriptionRevision { get; init; }

    /// <summary>null 是清掉。</summary>
    public Optional<Guid?> CycleId { get; init; }

    public Optional<Guid?> MilestoneId { get; init; }

    /// <summary>要改的自訂欄位。值是 JSON 的 null 表示清掉這個欄位。</summary>
    public IReadOnlyDictionary<string, JsonElement> Fields { get; init; } = new Dictionary<string, JsonElement>();
}

public enum WorkItemSortField
{
    /// <summary>
    /// 依自訂欄位排序，欄位的 key 在 <see cref="WorkItemQuery.SortFieldKey"/>。
    /// 能排的型別：文字、數字、金額、日期、單選（依選項在藍圖裡的順序，不認得的選項排在認得的後面）、勾選（沒勾在前）。
    /// 人員、多選、長文字、網址不能排：前兩種沒有單一的順序，後兩種排了沒有意義。沒有值的一律排最後，同值再依流水號。
    /// </summary>
    Field = -1,

    Number,
    Title,
    State,
    Priority,
    StartDate,
    DueDate,
    Estimate,
    CreatedAt,
    UpdatedAt,
}

/// <summary>查詢條件，彼此是「而且」的關係；同一個條件裡的多個值是「或」。</summary>
public sealed record WorkItemQuery
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 200;

    public IReadOnlyList<string> Types { get; init; } = [];

    public IReadOnlyList<string> States { get; init; } = [];

    public IReadOnlyList<StateCategory> StateCategories { get; init; } = [];

    /// <summary>只要這個人負責的。和 <see cref="OnlyUnassigned"/> 不能同時用。</summary>
    public Guid? AssigneeId { get; init; }

    public bool OnlyUnassigned { get; init; }

    /// <summary>只要這個人建立的（<see cref="WorkItem.CreatedBy"/>）。可以和負責人的條件一起用。</summary>
    public Guid? CreatedBy { get; init; }

    /// <summary>
    /// 只要最後更新（<see cref="WorkItem.UpdatedAt"/>）早於這個時間的。專案總覽的「停滯」連到清單時用
    /// （<see cref="ProjectOverviewRules.StaleDays"/>），和狀態類別的條件一起用才是「還沒結束而且很久沒動」。
    /// </summary>
    public DateTimeOffset? UpdatedBefore { get; init; }

    /// <summary>只要這些優先度的（None 是「沒有設定」）。空的是不限。</summary>
    public IReadOnlyList<Priority> Priorities { get; init; } = [];

    /// <summary>到期日在這一天或之後。和 <see cref="OnlyWithoutDueDate"/> 不能同時用。</summary>
    public DateOnly? DueFrom { get; init; }

    /// <summary>到期日在這一天或之前。</summary>
    public DateOnly? DueTo { get; init; }

    /// <summary>
    /// 項目的期間（開始日到到期日；只有其中一個時，期間就是那一天）和 [DatesFrom, DatesTo] 有重疊的。
    /// 兩個日期都沒有的項目不符合。只給一邊時，另一邊不限。和 <see cref="OnlyWithoutDueDate"/> 不能同時用。
    /// 時間軸用它：畫面上看得到的那一段時間裡，有落在這段時間的項目都要畫。
    /// </summary>
    public DateOnly? DatesFrom { get; init; }

    /// <summary>期間的另一端，見 <see cref="DatesFrom"/>。</summary>
    public DateOnly? DatesTo { get; init; }

    /// <summary>只要沒有到期日的。</summary>
    public bool OnlyWithoutDueDate { get; init; }

    /// <summary>標題包含這段文字，或項目編號等於它。不分大小寫。</summary>
    public string? Search { get; init; }

    /// <summary>只要這個項目的直接下層（項目編號，例如 WEB-12）。項目不存在或看不到時結果是空的。</summary>
    public string? ParentKey { get; init; }

    /// <summary>只要排進這個週期的。不存在或別的專案的週期，結果是空的，不算錯。和 <see cref="OnlyWithoutCycle"/> 不能同時用。</summary>
    public Guid? CycleId { get; init; }

    /// <summary>只要沒有週期的。</summary>
    public bool OnlyWithoutCycle { get; init; }

    /// <summary>只要歸在這個里程碑的。規則同 <see cref="CycleId"/>。</summary>
    public Guid? MilestoneId { get; init; }

    public bool OnlyWithoutMilestone { get; init; }

    /// <summary>自訂欄位的條件，彼此是「而且」。最多 <see cref="FieldFilter.MaxPerQuery"/> 個，同一個欄位只能出現一次。</summary>
    public IReadOnlyList<FieldFilter> FieldFilters { get; init; } = [];

    public WorkItemSortField SortBy { get; init; } = WorkItemSortField.Number;

    /// <summary><see cref="SortBy"/> 是 <see cref="WorkItemSortField.Field"/> 時，要排的自訂欄位的 key；其他時候要是 null。</summary>
    public string? SortFieldKey { get; init; }

    public bool Descending { get; init; }

    public int Limit { get; init; } = DefaultLimit;

    /// <summary>上一頁回傳的 <see cref="WorkItemPage.NextCursor"/>；第一頁是 null。內容對呼叫端沒有意義。</summary>
    public string? Cursor { get; init; }
}

/// <param name="NextCursor">還有下一頁時的游標；沒有了就是 null。</param>
public sealed record WorkItemPage(IReadOnlyList<WorkItem> Items, string? NextCursor);

/// <summary>
/// 一個自訂欄位的條件：值之間是「或」。只限這個專案的清單用（跨專案的「我的工作」、搜尋不支援：各專案的欄位不一樣）。
/// <list type="bullet">
/// <item>單選：值是選項的 key，等於其中一個就符合。</item>
/// <item>多選：值是選項的 key，選了其中任何一個就符合。</item>
/// <item>人員：值是人員編號，或 <c>me</c>（模組換成 actor 自己；只有人員欄位這樣換：單選的選項也可能叫 me）。</item>
/// <item>勾選：值只能是 <c>true</c> 或 <c>false</c>；沒勾的不會存，所以 <c>false</c> 是「沒有存這個欄位」。</item>
/// </list>
/// 其他型別不能篩選。欄位的 key 一定要在專案的藍圖裡；選項的 key 只檢查格式（藍圖可能改過，不認得的自然找不到，和類型、狀態一樣）。
/// 類型沒有這個欄位的項目，只符合 <see cref="IncludeEmpty"/>。
/// </summary>
/// <param name="Key">自訂欄位的 key。</param>
/// <param name="Values">要符合的值，最多 <see cref="MaxValues"/> 個；可以是空的，這時只看 <see cref="IncludeEmpty"/>（兩個都沒有是錯）。</param>
/// <param name="IncludeEmpty">沒有填的也算符合。勾選欄位不能用（沒勾就是 <c>false</c>）。</param>
public sealed record FieldFilter(string Key, IReadOnlyList<string> Values, bool IncludeEmpty = false)
{
    public const int MaxPerQuery = 10;

    public const int MaxValues = 50;
}

/// <summary>說明的版本：內容（UTF-8）的 SHA-256 取前 16 個十六進位字（小寫）。沒有說明和空字串是同一個版本。</summary>
public static class DescriptionRevisions
{
    private const int Length = 16;

    public static string Of(string? description)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(description ?? string.Empty));
        return Convert.ToHexStringLower(hash)[..Length];
    }
}
