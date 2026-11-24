using PmTool.Abstractions;

namespace PmTool.Work;

/// <summary>
/// 一次對同一個專案的多個項目做同一件事。每個項目各自走單一項目的規則（權限、流程、上下層），
/// 各自一個交易、各自留活動與事件：一個不行不影響其他的，結果分成成功與失敗兩份。
/// 請求本身不合格（編號是空的、超過 <see cref="BulkItemUpdate.MaxItems"/> 個、狀態和其他欄位一起給、什麼都沒給）
/// 回 <see cref="WorkErrorCodes.BulkInvalid"/>；專案找不到或看不到回 NotFound；整件事都沒有權限（例如沒有 item.update）回 Forbidden。
/// 編號重複的只做一次；不是這個專案的編號（前綴不對、格式不對、找不到）算那一筆失敗，代碼是 item.not_found。
/// 結果的順序照請求裡第一次出現的順序。
/// </summary>
public interface IBulkItemService
{
    /// <summary>
    /// 批次修改。給 <see cref="BulkItemUpdate.State"/> 時是改狀態（每一筆照流程的轉換規則，需要 item.transition）；
    /// 否則改給了的欄位（需要 item.update）。值沒變的項目也算成功（不留活動）。
    /// </summary>
    Task<Result<BulkUpdateResult>> UpdateAsync(Actor actor, string projectKey, BulkItemUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// 批次刪除，需要 item.delete。上層和它的下層都在這一批時，先刪下層再刪上層，所以一起選的整串都刪得掉；
    /// 還有不在這一批、沒刪除的下層時，那一筆失敗（<see cref="WorkErrorCodes.ItemHasChildren"/>）。已經刪除的算成功。
    /// </summary>
    Task<Result<BulkDeleteResult>> DeleteAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken);

    /// <summary>
    /// 批次復原，需要 item.delete。上層和它的下層都在這一批時，先復原上層再復原下層（「復原」整批刪除的時候整串回來）；
    /// 上層是刪除的、又不在這一批時，那一筆失敗（<see cref="WorkErrorCodes.ItemParentDeleted"/>）。沒有刪除的算成功。
    /// </summary>
    Task<Result<BulkRestoreResult>> RestoreAsync(Actor actor, string projectKey, IReadOnlyList<string> itemKeys, CancellationToken cancellationToken);
}
