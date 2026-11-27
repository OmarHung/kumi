using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai.Actions;

/// <summary>
/// 一種動作建議（<see cref="ActionKinds"/>）的處理器：建立時檢查草稿、產生給卡片看的步驟與自己要的整理過的狀態；套用時一次做一步。
/// 通用的 <see cref="IActionProposalService"/>／<see cref="IActionProposalCreator"/> 負責存、讀、作廢、建立中與心跳，處理器只管這一種的規則。
/// 權限一律用 <see cref="ActionContext.Actor"/>（使用者自己）：建立時先擋、套用時各模組再照它的權限檢查一次。
/// </summary>
internal interface IActionProposalHandler
{
    /// <summary><see cref="ActionKinds"/> 之一。</summary>
    string Kind { get; }

    /// <summary>
    /// 檢查草稿（<paramref name="payload"/> 是工具收到的參數物件）。問題回 <see cref="AiErrorCodes.Forbidden"/> 或 <see cref="AiErrorCodes.FieldInvalid"/>
    /// （每個問題一個細項，一次回全部）。步數 1 到 <see cref="AiRules.MaxActionSteps"/>。
    /// </summary>
    Task<Result<PreparedAction>> PrepareAsync(ActionContext context, JsonElement payload, CancellationToken cancellationToken);

    /// <summary>
    /// 做第 <paramref name="index"/> 步（從 0 起）。<paramref name="state"/> 是 <see cref="PreparedAction.State"/> 原樣存起來的；
    /// <paramref name="inputs"/> 是卡片上使用者填的值（只在這一次用，不能存、不能寫進記錄）。
    /// 被明確拒絕（還沒寫入）回 <see cref="ActionStepResult.Failed"/>；沒料到的錯誤直接丟例外（通用服務會維持建立中）。
    /// </summary>
    Task<ActionStepResult> ApplyStepAsync(
        ActionContext context, JsonElement state, int index, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken);
}

/// <param name="ProjectKey">建議屬於哪個專案；不屬於專案的是 null。</param>
/// <param name="ConversationId">建立這份建議的對話；MCP 沒有時是 null。</param>
internal sealed record ActionContext(Actor Actor, string? ProjectKey, Guid? ConversationId);

/// <param name="Title">卡片的標題。</param>
/// <param name="Steps">給卡片看的步驟（還沒套用）。</param>
/// <param name="State">處理器自己的、整理過的每一步（套用時讀它）。不能放卡片上要使用者填的值。</param>
/// <param name="Inputs">套用時要使用者填的值的宣告；沒有是空的。</param>
internal sealed record PreparedAction(string Title, IReadOnlyList<ActionStep> Steps, JsonElement State, IReadOnlyList<ActionInput> Inputs);

/// <summary>一步的結果。</summary>
/// <param name="Wrote">
/// 有 <see cref="Failure"/> 時，這一步是不是已經寫進去了一部分（例如帳號建好了、職稱沒設成功）。是的話即使是第一步也不能退回待決定
/// （再按一次會重複做），通用服務照「有進展」處理、把失敗記在那一步。
/// </param>
internal sealed record ActionStepResult(Failure? Failure, string? Link, bool Wrote = false)
{
    public static ActionStepResult Done(string? link = null) => new(null, link);

    /// <summary>被明確拒絕、還沒寫入。</summary>
    public static ActionStepResult Failed(Failure failure) => new(failure, null);

    /// <summary>寫進去了一部分，其餘失敗：不退回待決定，那一步記這個失敗。</summary>
    public static ActionStepResult PartlyDone(Failure failure, string? link = null) => new(failure, link, Wrote: true);
}
