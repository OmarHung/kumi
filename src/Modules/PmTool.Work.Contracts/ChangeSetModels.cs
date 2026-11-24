using PmTool.Abstractions;
using PmTool.Blueprint.Model;
using PmTool.Blueprint.Planning;

namespace PmTool.Work;

public enum ChangeSetStatus
{
    /// <summary>等待核准。基準版本還是目前的版本。</summary>
    Pending,

    Applied,

    Rejected,

    /// <summary>別的變更集先套用了，這份的基準版本已經不是目前的版本。</summary>
    Expired,
}

/// <summary>變更集是從哪裡提出的。存的是固定的文字（direct、assistant），JSON 也是。</summary>
public enum ChangeSetOrigin
{
    /// <summary>人自己送的：設定畫面、CLI、API。</summary>
    Direct,

    /// <summary>AI 助理代提出的人送出。核准的人要知道內容是模型寫的。</summary>
    Assistant,

    /// <summary>使用者自己的 AI 代理（Claude Code、Codex 這類）透過 MCP 送出。一樣是模型寫的。</summary>
    Agent,
}

/// <summary>一份送出的變更集。</summary>
/// <param name="Number">專案裡的流水號，從 1 開始，畫面上寫成「#3」。</param>
/// <param name="BaseVersion">送出時對照的藍圖版本。</param>
/// <param name="Summary">變更集文件裡的 summary。</param>
public sealed record ChangeSetSummary(
    long Number,
    ChangeSetStatus Status,
    long BaseVersion,
    string? Summary,
    Guid ProposedBy,
    DateTimeOffset ProposedAt)
{
    /// <summary>套用或拒絕的人。過期的變更集沒有。</summary>
    public Guid? DecidedBy { get; init; }

    /// <summary>套用、拒絕或被標成過期的時刻。</summary>
    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>套用之後的藍圖版本。只有已套用的有。</summary>
    public long? AppliedVersion { get; init; }

    /// <summary>從哪裡提出的。<see cref="ProposedBy"/> 一律是發起的人；AI 助理提出的也算他的。</summary>
    public ChangeSetOrigin Origin { get; init; } = ChangeSetOrigin.Direct;
}

/// <summary>計畫裡的一項資料影響，加上依目前的資料算出來的數量。</summary>
/// <param name="Affected">
/// 受影響的數量：狀態搬移、欄位刪除與轉換是會被改到的項目數；類型刪除、上層限制縮小是擋住套用的項目數；
/// 角色刪除是還是這個角色的成員數。已套用、拒絕、過期的變更集一律是 0（資料已經不是當時的樣子）。
/// </param>
/// <param name="BlocksApply">這一項讓變更集現在套用不了（例如被刪除的類型底下還有項目）。</param>
public sealed record PlannedImpact(DataImpact Impact, long Affected, bool BlocksApply);

/// <summary>變更集的全部內容：差異、資料影響、現在能不能套用。</summary>
/// <param name="Blueprint">變更集想要的藍圖全文。</param>
/// <param name="Changes">相對於基準版本的差異。</param>
/// <param name="Impacts">對既有資料的影響。只有等待核准的變更集會依目前的資料重算。</param>
/// <param name="Blockers">
/// 現在套用不了的原因，代碼是 <see cref="WorkErrorCodes"/> 裡 changeset.* 的那幾個。
/// 能套用時是空的；已套用、拒絕、過期的變更集也是空的（看 <see cref="ChangeSetSummary.Status"/>）。
/// </param>
/// <param name="CanApply">等待核准、沒有擋住的原因、而且呼叫者有 blueprint.apply。畫面用它決定要不要給「套用」按鈕。</param>
public sealed record ChangeSetDetail(
    ChangeSetSummary ChangeSet,
    ProjectBlueprint Blueprint,
    IReadOnlyList<BlueprintChange> Changes,
    IReadOnlyList<PlannedImpact> Impacts,
    IReadOnlyList<FailureDetail> Blockers,
    bool CanApply)
{
    /// <summary>
    /// 基準版本的藍圖全文，也就是 <see cref="Changes"/> 對照的「改之前」。畫面用它說出「從什麼改成什麼」與被拿掉的東西的名稱；
    /// 已套用的變更集不能拿專案目前的藍圖來比，那已經是改之後的樣子。
    /// </summary>
    public required ProjectBlueprint BaseBlueprint { get; init; }
}

/// <summary>沒有存下來的變更集：送出之前看會發生什麼事。</summary>
/// <param name="BaseVersion">文件的 baseVersion。</param>
/// <param name="CurrentVersion">專案目前的藍圖版本；和 <paramref name="BaseVersion"/> 不同時，送出會被擋下（Blockers 裡會有）。</param>
/// <param name="CanApply">沒有擋住的原因，而且呼叫者有 blueprint.apply：送出之後可以馬上套用。</param>
public sealed record ChangeSetPreview(
    long BaseVersion,
    long CurrentVersion,
    ProjectBlueprint Blueprint,
    IReadOnlyList<BlueprintChange> Changes,
    IReadOnlyList<PlannedImpact> Impacts,
    IReadOnlyList<FailureDetail> Blockers,
    bool CanApply);
