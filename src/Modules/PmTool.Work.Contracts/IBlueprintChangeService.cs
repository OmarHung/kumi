using PmTool.Abstractions;
using PmTool.Blueprint;

namespace PmTool.Work;

/// <summary>
/// 修改專案的藍圖：送出變更集、預覽差異與資料影響、核准套用或拒絕。
/// 模板、設定畫面、CLI、AI 都走這裡，規則只有一套。看不到的專案一律回 NotFound。
/// </summary>
public interface IBlueprintChangeService
{
    /// <summary>
    /// 送出一份變更集（JSON 或 YAML 的文件）。先驗證、再對照目前的藍圖算計畫，有問題回
    /// <see cref="WorkErrorCodes.ChangeSetInvalid"/>，Details 是每一個問題；通過的話存成等待核准。
    /// 需要 blueprint.propose。
    /// </summary>
    Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken);

    /// <summary>
    /// 同 <see cref="ProposeAsync(Actor, string, string, DocumentFormat, CancellationToken)"/>，另外記下從哪裡提出的
    /// （<see cref="ChangeSetSummary.Origin"/>）。權限照舊看 <paramref name="actor"/>：AI 助理不會因為是 AI 就多拿到什麼。
    /// </summary>
    Task<Result<ChangeSetDetail>> ProposeAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, ChangeSetOrigin origin, CancellationToken cancellationToken);

    /// <summary>
    /// 只算、不存：驗證文件、對照目前的藍圖算差異、資料影響與擋住的原因。CLI 的 <c>pm blueprint plan</c> 與送出前的預覽用。
    /// 文件有問題回 <see cref="WorkErrorCodes.ChangeSetInvalid"/>（和 <see cref="ProposeAsync"/> 一樣）。需要 blueprint.read。
    /// </summary>
    Task<Result<ChangeSetPreview>> PreviewAsync(
        Actor actor, string projectKey, string document, DocumentFormat format, CancellationToken cancellationToken);

    /// <summary>專案的變更集，新的在前，最多 100 份。需要 blueprint.read。</summary>
    Task<Result<IReadOnlyList<ChangeSetSummary>>> ListAsync(Actor actor, string projectKey, CancellationToken cancellationToken);

    /// <summary>一份變更集；等待核准的會依目前的資料重算影響與擋住的原因。需要 blueprint.read。</summary>
    Task<Result<ChangeSetDetail>> GetAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken);

    /// <summary>
    /// 核准並套用：在一個交易裡存成新的藍圖版本、依計畫搬移項目（每個被動到的項目留一筆
    /// <see cref="ActivityKind.Migrated"/> 的活動）、把專案其他等待核准的變更集標成過期。
    /// 任何一步失敗，什麼都不會改。需要 blueprint.apply；提出的人可以自己核准。
    /// </summary>
    Task<Result<ChangeSetDetail>> ApplyAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken);

    /// <summary>
    /// 拒絕（或放棄）一份等待核准的變更集。提出的人，或有 blueprint.apply 的人可以做；兩者都還要有 blueprint.read，
    /// 沒有的話先回 Forbidden，不透露這個編號存不存在。
    /// </summary>
    Task<Result<ChangeSetDetail>> RejectAsync(Actor actor, string projectKey, long number, CancellationToken cancellationToken);
}
