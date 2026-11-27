using System.Text.Json;
using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 通用的「動作建議」（internal/HANDOFF-ai-coverage-2.md 第 3 節）：知識庫、人員管理、附件、存的檢視、說明格式這幾種建議共用一張表、
/// 一組服務、一組端點與一張通用卡片；每一種（<see cref="ActionKinds"/>）由 AI 模組裡自己的處理器檢查草稿、產生給卡片看的步驟、套用一步。
/// 助理（內建工具或 MCP 的同名工具）只建立建議，使用者在網頁按「套用」才以自己的身分照各模組的權限寫入。
/// 只有建立它的人看得到、能套用；別人的、不存在的、綁的專案已經看不到的一律 <see cref="AiErrorCodes.ActionProposalNotFound"/>。
/// </summary>
public interface IActionProposalService
{
    Task<Result<ActionProposal>> GetAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);

    /// <summary>
    /// 照新增項目建議的做法套用（有些步驟不冪等）：先在建議的 advisory lock 裡標成 <see cref="NewItemProposalStatus.Applying"/> 並提交，
    /// 再一步一步做，每做完一步立刻記下它的結果（<see cref="ActionStep.Done"/>／<see cref="ActionStep.Error"/>）並刷新 <see cref="ActionProposal.DecidedAt"/>（心跳）。
    /// 只有第一步被明確拒絕（還沒寫入）才回到待決定、回傳它的失敗；任何例外（含逾時）都維持建立中；之後的失敗不復原已經做的、繼續下一步。
    /// 停在建立中超過 <see cref="AiRules.NewItemProposalApplyStale"/> 讀出來是已套用，<see cref="ActionProposal.Error"/> 說明過程中斷了。
    /// <paramref name="inputs"/> 是卡片上使用者填的值（key 對到 <see cref="ActionProposal.Inputs"/>）；少了必填的回 <see cref="AiErrorCodes.FieldInvalid"/>、什麼都不做。
    /// 輸入只在這一次套用裡用，**不存、不寫進記錄、不放進任何回應**。已經套用、作廢或建立中的：<see cref="AiErrorCodes.ActionProposalClosed"/>。
    /// </summary>
    Task<Result<ActionProposal>> ApplyAsync(
        Actor actor, Guid proposalId, IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken);

    /// <summary>作廢。已經套用、作廢或建立中的：<see cref="AiErrorCodes.ActionProposalClosed"/>。</summary>
    Task<Result<ActionProposal>> DismissAsync(Actor actor, Guid proposalId, CancellationToken cancellationToken);
}

/// <summary>建立動作建議。依 <see cref="ActionProposalDraft.Kind"/> 交給那一種的處理器檢查；內建工具與 MCP 的同名工具都呼叫它。</summary>
public interface IActionProposalCreator
{
    /// <summary>
    /// 不認得的種類：<see cref="AiErrorCodes.FieldInvalid"/>。給了專案代號時專案要看得到（不然原樣回傳 <c>project.not_found</c>）。
    /// 其餘的檢查（權限、每一步的內容）由處理器做，問題回 <see cref="AiErrorCodes.Forbidden"/>（訊息寫需要什麼權限、誰能做）或
    /// <see cref="AiErrorCodes.FieldInvalid"/>（每個問題一個細項，路徑 <c>changes[2].title</c>），一次回全部，什麼都不存。
    /// 一份最多 <see cref="AiRules.MaxActionSteps"/> 步。
    /// </summary>
    Task<Result<ActionProposal>> CreateAsync(Actor actor, ActionProposalDraft draft, CancellationToken cancellationToken);
}

/// <param name="Kind"><see cref="ActionKinds"/> 之一。</param>
/// <param name="ProjectKey">這份建議屬於哪個專案（存的檢視、附到項目的檔案）；不屬於專案的（知識庫、人員、說明格式）是 null。</param>
/// <param name="Payload">工具收到的參數（物件，形狀依種類），處理器自己讀；不認得的屬性要回錯誤，不默默丟掉。</param>
public sealed record ActionProposalDraft(string Kind, string? ProjectKey, JsonElement Payload)
{
    /// <summary>內建助理建立的才有；MCP 是對話由本機、主機代理的工作找到的（見 <see cref="IAssistantProposalLedger"/>），也可能是 null。</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>為什麼這樣做（一行，最長 200 字），給卡片顯示；可以不給。擋控制字元。</summary>
    public string? Reason { get; init; }
}

/// <summary>動作建議的種類。字串是對外合約（存在資料庫、網頁照它決定套用後讓哪些快取失效），不能改名。</summary>
public static class ActionKinds
{
    public const string Knowledge = "knowledge";

    public const string People = "people";

    public const string Attachments = "attachments";

    public const string SavedViews = "saved_views";

    public const string DescriptionTemplates = "description_templates";
}

/// <summary>步驟的「之前」「之後」怎麼顯示：純文字，或 Markdown（頁面內文、說明格式的內容，卡片照修改建議的說明差異顯示）。</summary>
public enum ActionTextFormat
{
    Text,
    Markdown,
}

/// <summary>卡片上要使用者填的值。</summary>
public enum ActionInputKind
{
    /// <summary>密碼：輸入框遮住、不自動填、不記住；長度至少 <c>IdentityRules.MinPasswordLength</c>。</summary>
    Password,

    Text,
}

/// <param name="Key">套用時 <see cref="IActionProposalService.ApplyAsync"/> 的 inputs 用的 key，例如 <c>password:2</c>（第 3 步的密碼）。</param>
/// <param name="Label">輸入框的名稱，例如「王小明的初始密碼」。</param>
/// <param name="Hint">輸入框下面的說明；沒有是 null。</param>
public sealed record ActionInput(string Key, string Label, ActionInputKind Kind, string? Hint);

/// <summary>
/// 卡片上的一步。文字都是建立建議時處理器寫好的、給人看的繁體中文（含使用者寫的名稱與內容，所以不放進給模型的附註）。
/// </summary>
/// <param name="Label">做什麼，例如「新增頁面」「改組織角色」「附加檔案」。</param>
/// <param name="Target">對象，例如「HR／新人須知」「王小明」「WEB-12」；沒有是 null。</param>
/// <param name="Before">之前的樣子；新增的是 null。</param>
/// <param name="After">之後的樣子；刪除的是 null。</param>
/// <param name="Note">補充，例如「底下的 3 頁會一起進垃圾桶」；沒有是 null。</param>
public sealed record ActionStep(string Label, string? Target, string? Before, string? After, string? Note, ActionTextFormat Format)
{
    /// <summary>套用之後這一步做完了。</summary>
    public bool Done { get; init; }

    /// <summary>套用之後這一步沒成功的原因；還沒套用或成功是 null。</summary>
    public string? Error { get; init; }

    /// <summary>做完之後可以點過去看的網頁路徑（例如 <c>/knowledge/HR/12</c>）；沒有是 null。只會是站內的相對路徑。</summary>
    public string? Link { get; init; }
}

/// <param name="Title">卡片的標題，例如「建議調整知識庫（HR）」。</param>
/// <param name="Status">待決定、建立中、已套用、作廢（和新增項目建議同一組）。</param>
public sealed record ActionProposal(
    Guid Id,
    string Kind,
    string? ProjectKey,
    string Title,
    IReadOnlyList<ActionStep> Steps,
    NewItemProposalStatus Status,
    DateTimeOffset CreatedAt)
{
    public string? Reason { get; init; }

    /// <summary>套用時要使用者填的值；沒有是空的。</summary>
    public IReadOnlyList<ActionInput> Inputs { get; init; } = [];

    public DateTimeOffset? DecidedAt { get; init; }

    /// <summary>有幾步沒成功，或過程中斷了：摘要，繁體中文。</summary>
    public string? Error { get; init; }
}

/// <summary>
/// 這則對話讀過哪些專案（<c>ai.conversation_reads</c>，規則見 <c>CLAUDE.md</c> 的「AI 助理跨專案讀項目」）。組織對話裡用專案工具時記下，
/// 之後使用者在那個專案看不到全部項目時整則對話刪掉（<c>ConversationReadGuard</c>）。內建工具與 MCP 都用它。
/// </summary>
public interface IAssistantConversationReads
{
    /// <summary>
    /// 記下這則對話讀過這些專案。只記使用者**現在看得到全部項目**的專案（只看自己的人本來就只讀得到自己的，記了反而會讓對話馬上被刪）；
    /// 對話不是這個人的、或 <paramref name="conversationId"/> 是 null 時什麼都不做。同一個專案只記一次。
    /// </summary>
    Task RememberAsync(Actor actor, Guid? conversationId, IReadOnlyCollection<string> projectKeys, CancellationToken cancellationToken);
}
