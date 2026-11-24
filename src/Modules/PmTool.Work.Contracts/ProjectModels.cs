using PmTool.Blueprint.Model;

namespace PmTool.Work;

/// <summary>內建的模板：一份可以直接拿來開專案的藍圖。</summary>
/// <param name="Workflows">有類型在用的流程，照藍圖的順序；給建立專案時預覽用。</param>
/// <param name="Parents">有 parents 的類型可以掛在哪些類型底下，照藍圖的順序；沒有 parents 的類型不列。</param>
/// <param name="FieldNames">自訂欄位的名稱，照藍圖的順序。</param>
/// <param name="ExampleCount">範例項目的筆數；0 表示這個模板沒有範例。</param>
public sealed record TemplateSummary(
    string Key, string Name, string? Description,
    IReadOnlyList<TemplateWorkflowPreview> Workflows, IReadOnlyList<TemplateParentPreview> Parents,
    IReadOnlyList<string> FieldNames, int ExampleCount);

/// <param name="ItemTypeNames">用這個流程的類型名稱，照藍圖的順序。</param>
/// <param name="StateNames">類別不是 canceled 的狀態，照流程的順序（主線）。</param>
/// <param name="CanceledStateNames">類別是 canceled 的狀態（不處理、撤回、擱置…），照流程的順序；預覽寫成「（或 …）」，不當成最後一步。</param>
public sealed record TemplateWorkflowPreview(
    string Name, IReadOnlyList<string> ItemTypeNames, IReadOnlyList<string> StateNames, IReadOnlyList<string> CanceledStateNames)
{
    /// <summary>
    /// <see cref="StateNames"/> 每個狀態的類別，一一對應。預覽用它畫狀態的形狀（和清單上的狀態同一組），
    /// 也用它把「完成」之後的狀態（暫停這類）和主線分開。
    /// </summary>
    public IReadOnlyList<StateCategory> StateCategories { get; init; } = [];
}

/// <param name="ParentTypeNames">可以當它上層的類型名稱，照藍圖 parents 的順序。</param>
public sealed record TemplateParentPreview(string ItemTypeName, IReadOnlyList<string> ParentTypeNames);

/// <param name="Key">專案代號，會成為項目編號的前綴（WEB-12）。格式見 <see cref="WorkRules.ProjectKeyPattern"/>。</param>
/// <param name="Template">模板的 key。</param>
/// <param name="IncludeExamples">一起建立模板的範例項目。模板沒有範例時忽略。</param>
public sealed record NewProject(string Key, string Name, string Template, bool IncludeExamples = false)
{
    /// <summary>
    /// 一起寫進去的專案說明（通常是選好的說明格式的內容），在建立專案的同一個交易裡存，更新的人是建立者。
    /// null 或整段空白就是不寫。和改說明同一套檢查（<see cref="IProjectDescriptionService.SetAsync"/>），路徑是 description，和代號、名稱的問題一次回齊。
    /// </summary>
    public string? Description { get; init; }
}

/// <param name="MyRole">呼叫者在這個專案的角色。組織管理員即使不是專案成員也看得到專案，這時是 null。</param>
public sealed record ProjectSummary(
    Guid Id, string Key, string Name, long BlueprintVersion, string? MyRole, DateTimeOffset CreatedAt)
{
    /// <summary>
    /// 封存的時刻；沒有封存是 null。封存的專案只能看：項目、藍圖、週期與里程碑的寫入都擋下（<see cref="WorkErrorCodes.ProjectArchived"/>），
    /// 呼叫者的權限（<see cref="ProjectDetail.MyPermissions"/>）只剩讀取與 project.manage（取消封存、改名、管理成員）。
    /// </summary>
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed record ProjectMember(Guid PersonId, string Role);

/// <param name="Blueprint">
/// 專案目前的藍圖。沒有 blueprint.read 權限的人拿到的是精簡版：只有畫面運作需要的部分
/// （類型、欄位、流程、角色、檢視），自動化與外部系統的綁定是空的。
/// </param>
/// <param name="MyPermissions">呼叫者在這個專案能做的事（item.read、item.update…），畫面用它決定哪些操作要開放。</param>
public sealed record ProjectDetail(
    ProjectSummary Project,
    ProjectBlueprint Blueprint,
    IReadOnlyList<ProjectMember> Members,
    IReadOnlyList<string> MyPermissions);

public static class WorkRules
{
    /// <summary>專案代號：大寫英文字母開頭，只能有大寫字母與數字，2 到 10 個字元。</summary>
    public const string ProjectKeyPattern = "^[A-Z][A-Z0-9]{1,9}$";

    public const int MaxProjectNameLength = 100;

    public const int MaxTitleLength = 300;

    public const int MaxTextFieldLength = 2_000;

    public const int MaxLongTextFieldLength = 50_000;

    public const int MaxDescriptionLength = 50_000;

    public const int MaxCommentLength = 10_000;

    /// <summary>
    /// 一則留言最多幾個版本（含第一次留言）。每次編輯都留下舊的內容，沒有上限的話一則留言可以撐到幾 MB；
    /// 改這麼多次的話，另外留一則比較清楚。
    /// </summary>
    public const int MaxCommentRevisions = 100;

    /// <summary>
    /// 套用一份變更集時，最多搬移幾個項目。套用是在一個交易裡同步做完的，太多會長時間鎖住項目；
    /// 超過就擋下，之後要做分批或背景工作時再調整。
    /// </summary>
    public const int MaxItemsPerApply = 5_000;

    /// <summary>變更集摘要的長度上限。摘要會出現在清單裡，一份太長會拖慢整個清單。</summary>
    public const int MaxChangeSetSummaryLength = 500;

    /// <summary>
    /// 數字類的值（估計、數字欄位、金額欄位）絕對值的上限，以及小數最多幾位。
    /// 兩個合起來最多 15 位有效數字：用戶端是 JavaScript，再多就會在來回之間悄悄失去精度。
    /// </summary>
    public const decimal MaxNumberMagnitude = 99_999_999_999.9999m;

    public const int MaxNumberDecimalPlaces = 4;
}

/// <summary>專案裡可以被允許的動作，和藍圖 Schema 裡角色的 permissions 用同一組字串。</summary>
public static class WorkPermissions
{
    public const string ItemRead = "item.read";

    /// <summary>
    /// 只看得到自己的項目：負責人是自己、或是自己建立的（<c>assignee_id</c> 或 <c>created_by</c>）。
    /// 角色同時有 <see cref="ItemRead"/> 時以它為準（看得到全部）。其他項目的權限（改、改狀態、留言、刪除、附件、關注）
    /// 只作用在看得到的項目上；看不到的項目一律當作不存在（NotFound，不是 Forbidden），清單、計數、搜尋、通知都不含。
    /// 內建角色 contributor（執行者）就是這種。
    /// </summary>
    public const string ItemReadOwn = "item.read_own";
    public const string ItemCreate = "item.create";
    public const string ItemUpdate = "item.update";
    public const string ItemTransition = "item.transition";
    public const string ItemDelete = "item.delete";
    public const string CommentCreate = "comment.create";
    public const string BlueprintRead = "blueprint.read";
    public const string BlueprintPropose = "blueprint.propose";
    public const string BlueprintApply = "blueprint.apply";
    public const string ProjectManage = "project.manage";
}
