using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 把一批項目一次建立起來（網頁的試算表匯入：檔案在網頁上讀、對應欄位、轉好值，再送到這裡）。
/// 全部或全部不：任何一列不合格就一列都不建立，回 <see cref="WorkErrorCodes.ImportInvalid"/>，Details 列出每一列的問題；
/// 合格時在一個交易裡照列的順序建立（編號照順序發），每一列各自留 Created 活動、寫 item_created 事件。
/// 每一列的規則和 <see cref="IWorkItemService.CreateAsync"/> 一樣（類型、標題、負責人是成員、上層存在而且允許、週期與里程碑是這個專案的、自訂欄位的值…）；
/// 上層只能是已經存在的項目，不能指向同一批裡的列。
/// 需要 item.create；有任何一列指定了不是流程起始狀態的狀態時，還需要 project.manage（直接設定狀態會跳過流程的轉換規則與角色限制，只給管理專案的人）。
/// 沒有權限整個 Forbidden；專案看不到 NotFound；封存的專案 <see cref="WorkErrorCodes.ProjectArchived"/>。
/// </summary>
public interface IItemImportService
{
    Task<Result<ItemImportResult>> ImportAsync(Actor actor, string projectKey, ItemImport import, CancellationToken cancellationToken);
}

/// <summary>一次匯入的內容。</summary>
public sealed record ItemImport
{
    /// <summary>一次最多幾列：一個交易建立完，太多會長時間鎖住專案的編號。</summary>
    public const int MaxRows = 1_000;

    public required IReadOnlyList<ImportedItem> Rows { get; init; }

    /// <summary>只檢查、不建立：網頁的預覽用它問伺服器「這些列送出去會不會被擋」。</summary>
    public bool DryRun { get; init; }
}

/// <summary>匯入的一列。</summary>
public sealed record ImportedItem
{
    public required NewWorkItem Item { get; init; }

    /// <summary>
    /// 狀態的 key；null 是流程的第一個狀態（和新增一樣）。給了就直接設定，不走轉換：匯入是把別處的資料搬進來，不是工作流程裡的一步。
    /// 不在這個類型的流程裡是那一列的問題，路徑 rows/i/state。
    /// </summary>
    public string? State { get; init; }
}

/// <param name="DryRun">這一次是不是只檢查。</param>
/// <param name="Count">合格的列數（只檢查時是「會建立幾個」）。</param>
/// <param name="CreatedKeys">建立的項目編號，照列的順序；只檢查時是空的。</param>
public sealed record ItemImportResult(bool DryRun, int Count, IReadOnlyList<string> CreatedKeys);
