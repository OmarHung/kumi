using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 組織擁有者（<c>OrganizationRole.Owner</c>）管理的主機 AI 代理：常駐在 Kumi 主機的 worker（<c>kumi host run</c>），
/// 上面的 Claude Code／Codex 由擁有者登入，開成「組織來源」之後組織的成員（擁有者、管理員、一般成員；訪客不行）都能在網頁的助理選用。
/// 規格見 <c>internal/HANDOFF-host-agent.md</c>。
/// <para>
/// 每個方法都先向身分模組重新確認：<paramref name="actor"/> 現在是這個組織的擁有者、帳號沒有停用；不是的一律
/// <see cref="AiErrorCodes.HostOwnerOnly"/>（403），不看 <see cref="Actor.IsOrganizationAdmin"/>（管理員也不行）。
/// 別的組織的代理、來源、登入一律當作不存在（各自的 NotFound）。
/// </para>
/// </summary>
public interface IHostAgentAdminService
{
    /// <summary>這個組織的主機代理（含引擎的狀態、進行中的登入）與組織來源（含狀態、排隊、這個月的用量）。</summary>
    Task<Result<HostAgentOverview>> GetAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 新增一台還沒配對的主機代理，回傳一次性的配對碼（明文只出現這一次，資料庫只存雜湊）。
    /// 一個組織最多 <see cref="AiRules.MaxHostAgentsPerOrganization"/> 台（細項 <see cref="AiErrorCodes.TooManyConnections"/>）。
    /// 名稱 1～<see cref="AiRules.MaxMachineNameLength"/> 字、不能有控制字元、組織裡不重複（<see cref="AiErrorCodes.SettingsInvalid"/>）。
    /// </summary>
    Task<Result<HostAgentPairing>> CreateAgentAsync(Actor actor, HostAgentInput input, CancellationToken cancellationToken);

    /// <summary>改名稱、停用或啟用。停用：不派新工作、等待中的工作取消、執行中的照取消流程停下；機器憑證保留（重新啟用就能用）。</summary>
    Task<Result<HostAgent>> UpdateAgentAsync(Actor actor, Guid agentId, HostAgentInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 重新配對（換機器憑證）：舊的憑證立刻失效、等待與執行中的工作照停用處理；回新的配對碼。機器編號在配對時由 worker 帶來，不會變。
    /// 引擎的登入授權不受影響（登入在主機上，換的只是 worker 對 Kumi 的身分）。
    /// </summary>
    Task<Result<HostAgentPairing>> ResetPairingAsync(Actor actor, Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// 移除：撤銷機器憑證與所有工作的工具授權、刪掉它的來源與登入紀錄、取消工作。之後那台 worker 用舊憑證登記一律 401，
    /// 不會自己重新出現（要重新新增、配對）。用量紀錄保留。
    /// </summary>
    Task<Result<Unit>> RemoveAgentAsync(Actor actor, Guid agentId, CancellationToken cancellationToken);

    /// <summary>
    /// 開始登入一個引擎（<see cref="AiProviderKind.ClaudeCode"/> 或 <see cref="AiProviderKind.Codex"/>）：回一次性的登入碼，
    /// 擁有者在主機上執行 <c>kumi host login</c> 並輸入它（<see cref="IHostAgentWorkerService.ClaimLoginAsync"/>）。
    /// 有效 <see cref="AiRules.HostLoginLifetime"/>；同一個代理的同一個引擎同時只有一個進行中的登入（新的開始時舊的作廢）。
    /// 代理還沒配對或已停用：<see cref="AiErrorCodes.HostAgentUnavailable"/>。
    /// </summary>
    Task<Result<HostLoginStart>> StartLoginAsync(Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken);

    /// <summary>作廢一個還沒完成的登入。已經完成或作廢的照樣成功、什麼都不改。</summary>
    Task<Result<Unit>> CancelLoginAsync(Actor actor, Guid loginId, CancellationToken cancellationToken);

    /// <summary>
    /// 登出一個引擎：撤銷目前的登入授權（來源立刻停止派工、等待中的取消、執行中的停下），worker 下一次登記時收到
    /// <see cref="HostCheckInResult.Logout"/>、執行 CLI 自己的登出。還沒登入時照樣成功。
    /// </summary>
    Task<Result<Unit>> LogoutAsync(Actor actor, Guid agentId, AiProviderKind engine, CancellationToken cancellationToken);

    /// <summary>
    /// 新增組織來源（一個代理的一個引擎最多一個）。<see cref="HostSourceInput.Enabled"/> 是 true 時：那個引擎的登入要是 API 用量計費
    /// （<see cref="HostBilling.Api"/>，訂閱的登入一律 <see cref="AiErrorCodes.HostBillingNotShareable"/>），而且
    /// <see cref="HostSourceInput.AcknowledgeApiBilling"/> 要是 true（細項 <see cref="AiErrorCodes.FieldRequired"/>，路徑 <c>acknowledgeApiBilling</c>）。
    /// 引擎還沒登入、離線時可以先存成停用的。模型要在引擎回報的清單裡（細項 <see cref="AiErrorCodes.ModelNotAvailable"/>）。
    /// </summary>
    Task<Result<HostSource>> CreateSourceAsync(Actor actor, HostSourceInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 修改來源（規則同新增；<see cref="HostSourceInput.AgentId"/> 與 <see cref="HostSourceInput.Engine"/> 不能改，細項 <see cref="AiErrorCodes.FieldInvalid"/>）。
    /// 改成停用：等待中的工作取消、執行中的照取消流程停下、工具授權失效。
    /// </summary>
    Task<Result<HostSource>> UpdateSourceAsync(Actor actor, Guid sourceId, HostSourceInput input, CancellationToken cancellationToken);

    /// <summary>刪除來源（工作照停用處理）。用量紀錄保留，已經有的回覆記的是當時的名稱。</summary>
    Task<Result<Unit>> DeleteSourceAsync(Actor actor, Guid sourceId, CancellationToken cancellationToken);

    /// <summary>
    /// 一個來源某個月（組織的時區；<paramref name="month"/> 是 <c>yyyy-MM</c>，null 是這個月）的用量，依成員與專案分。
    /// 只有則數、狀態與 token，沒有對話的內容與標題：管理來源不等於能看別人的對話。格式不對：<see cref="AiErrorCodes.SettingsInvalid"/>。
    /// </summary>
    Task<Result<HostSourceUsage>> GetUsageAsync(Actor actor, Guid sourceId, string? month, CancellationToken cancellationToken);
}

/// <summary>
/// worker 那一頭（<c>kumi host run</c>／<c>pair</c>／<c>login</c>）。身分是配對時拿到的機器憑證（<c>kha_…</c>，資料庫只存雜湊），
/// 不代表任何人：只能登記、領派給它的工作、回報、完成登入，不能呼叫其他 API（API 層只在 <c>/v1/host-agent/*</c> 認它）。
/// </summary>
public interface IHostAgentWorkerService
{
    /// <summary>機器憑證對應的代理；格式不對、不存在、被換掉或代理被移除時是 null（API 回 401）。停用的代理照樣認得（登記時回 <see cref="HostCheckInResult.Disabled"/>）。</summary>
    Task<HostAgentIdentity?> AuthenticateAsync(string credential, CancellationToken cancellationToken);

    /// <summary>
    /// 用配對碼換機器憑證。配對碼要對、沒過期、沒用過，而且開始配對的人現在還是那個組織的擁有者；不然
    /// <see cref="AiErrorCodes.HostPairingInvalid"/>（不說是哪一項，什麼都不改）。成功之後配對碼作廢，舊的憑證（重新配對時）失效。
    /// </summary>
    Task<Result<HostPairingResult>> PairAsync(HostPairingRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// 登記（記下在線、引擎的狀態），然後等最多 <see cref="AiRules.HostPollWait"/>，有可以派給這台的工作就領走一件回傳
    /// （同一件只會被領一次，每個來源同時最多 <see cref="AiRules.HostSourceMaxConcurrent"/> 件、每台最多 <see cref="AiRules.HostWorkerMaxConcurrent"/> 件）。<paramref name="capacity"/> 是這台<b>現在還能多跑幾件</b>（worker 自己數，和本機代理相同），0 時只登記。
    /// 領的時候建立這件工作的工具授權（<see cref="HostJob.ToolToken"/>），並再確認發問的人還是組織裡有效的成員（不是的話那件工作直接失敗、不派）。
    /// 內容不對：<see cref="AiErrorCodes.HostAgentInvalid"/>。
    /// </summary>
    Task<Result<HostCheckInResult>> CheckInAsync(
        HostAgentIdentity agent, HostAgentCheckIn checkIn, int capacity, CancellationToken cancellationToken);

    /// <summary>
    /// 回報一批進度。<paramref name="lease"/> 要是領走時拿到的；<paramref name="firstSeq"/> 是這批第一則的流水號（從 1 起算、連續）：
    /// 已經收過的（流水號不大於伺服器存的）略過，所以網路重送不會重複；跳號是 <see cref="AiErrorCodes.HostAgentInvalid"/>。
    /// 工作不是派給這台的、不存在：<see cref="AiErrorCodes.AgentJobNotFound"/>。租約不對、工作已經被伺服器結束（逾時、結果不明）：
    /// <see cref="AiErrorCodes.HostJobLeaseLost"/>，worker 要停下引擎、不再回報。
    /// </summary>
    Task<Result<HostReportAck>> ReportAsync(
        HostAgentIdentity agent, Guid jobId, int lease, int firstSeq, IReadOnlyList<LocalAgentEvent> events, CancellationToken cancellationToken);

    /// <summary>工作結束了。規則同 <see cref="ReportAsync"/>；重送同一件的結束不算錯（回成功、不改第一次存的結果）。</summary>
    Task<Result<Unit>> FinishAsync(
        HostAgentIdentity agent, Guid jobId, int lease, LocalAgentFinish finish, CancellationToken cancellationToken);

    /// <summary>
    /// 主機上的 <c>kumi host login</c> 用擁有者在網頁拿到的登入碼認領這次登入：要是派給這台、還沒過期、還沒被認領，
    /// 而且開始登入的人現在還是擁有者；不然 <see cref="AiErrorCodes.HostLoginRejected"/>（什麼都不改）。成功後同一個碼不能再用。
    /// </summary>
    Task<Result<HostLoginClaim>> ClaimLoginAsync(HostAgentIdentity agent, string code, CancellationToken cancellationToken);

    /// <summary>
    /// CLI 自己的登入跑完之後回報結果。登入要是這台認領的、還沒過期，開始登入的人現在還是擁有者，CLI 回報已登入，
    /// 而且是 API 用量計費（<see cref="HostBilling.Api"/>）；都符合才成為這個引擎目前的授權（之前的作廢）。
    /// 任何一項不符：<see cref="AiErrorCodes.HostLoginRejected"/>，訊息說明原因，worker 要執行 CLI 的登出、丟掉這份登入。
    /// 已經完成或作廢的登入再回報（重放）一律拒絕。
    /// </summary>
    Task<Result<HostLoginCompleted>> CompleteLoginAsync(
        HostAgentIdentity agent, Guid loginId, HostLoginReport report, CancellationToken cancellationToken);
}

/// <summary>
/// 主機代理每件工作的工具授權（<c>kjt_…</c>，給 <c>/mcp</c> 用）。授權綁組織、發問的人、專案、工作、worker、允許的工具與期限；
/// 每個請求都重新檢查（不快取）：工作還在執行、沒有要求取消、沒過期、來源與代理都還開著、發問的人帳號沒停用而且不是訪客。
/// 專案與項目的權限由各模組用發問的人的 <see cref="Actor"/> 照常判斷，不借用擁有者或 worker 的身分。
/// </summary>
public interface IHostToolGrants
{
    /// <summary>有效的授權；格式不對、不存在、已經失效都是 null（API 回 401）。</summary>
    Task<HostToolGrant?> ResolveAsync(string token, CancellationToken cancellationToken);

    /// <summary>
    /// 提出類的工具（propose_changeset、propose_item_changes）開始前呼叫：同一件工作裡同一個工具、同樣參數（<paramref name="argumentsHash"/>）
    /// 已經成功過時回那一次的結果（JSON），呼叫端直接回它、不再建立；正在進行中（另一個請求）回 <see cref="HostToolReplay.InProgress"/>；
    /// 都沒有時記下「進行中」、回 <see cref="HostToolReplay.Proceed"/>。
    /// </summary>
    Task<HostToolReplay> BeginWriteAsync(Guid jobId, string tool, string argumentsHash, CancellationToken cancellationToken);

    /// <summary>提出類的工具結束：成功時存下結果（之後同樣的呼叫拿到它）；失敗時清掉「進行中」，讓模型修正後再試。</summary>
    Task CompleteWriteAsync(Guid jobId, string tool, string argumentsHash, string? resultJson, CancellationToken cancellationToken);
}

/// <summary>配對時拿到的機器身分。<see cref="AgentId"/> 也是網頁上那台代理的編號。</summary>
public sealed record HostAgentIdentity(Guid OrganizationId, Guid AgentId);

/// <summary><see cref="IHostToolGrants.BeginWriteAsync"/> 的結果。</summary>
/// <param name="ResultJson">已經成功過的那一次的結果；只有 <see cref="Kind"/> 是 <see cref="HostToolReplayKind.Replay"/> 時有。</param>
public sealed record HostToolReplay(HostToolReplayKind Kind, string? ResultJson)
{
    public static HostToolReplay Proceed { get; } = new(HostToolReplayKind.Proceed, null);

    public static HostToolReplay InProgress { get; } = new(HostToolReplayKind.InProgress, null);
}

public enum HostToolReplayKind
{
    Proceed,

    Replay,

    InProgress,
}

/// <param name="ProjectKey">工作所屬的專案；所有工具只准碰它（和本機代理的 <c>X-Kumi-Project</c> 同一個效果，但由伺服器的工作資料決定）。</param>
/// <param name="AllowedTools">MCP 工具的名稱（不含前綴），和工作的 <see cref="HostJob.AllowedTools"/> 相同。</param>
/// <param name="IsOrganizationAdmin">發問的人<b>現在</b>的角色（擁有者或管理員），每次解析時從身分模組讀。</param>
public sealed record HostToolGrant(
    Guid OrganizationId, Guid PersonId, bool IsOrganizationAdmin, string ProjectKey, IReadOnlyList<string> AllowedTools, Guid JobId)
{
    public Actor ToActor() => new(OrganizationId, PersonId, IsOrganizationAdmin);
}

/// <summary>主機代理的狀態。JSON 是 snake_case：awaiting_pairing、active、disabled。</summary>
public enum HostAgentState
{
    /// <summary>新增了、還沒用配對碼配對（或重新配對後還沒配對）。</summary>
    AwaitingPairing,

    Active,

    /// <summary>擁有者停用：照樣登記、不派工作。</summary>
    Disabled,
}

/// <summary>引擎登入的計費方式（worker 從 CLI 的登入狀態判斷）。JSON 是 snake_case：api、subscription、unknown。</summary>
public enum HostBilling
{
    /// <summary>API 用量計費：Claude Code 的 Anthropic Console 登入（<c>claude auth login --console</c>）、Codex 的 API 金鑰登入。可以給全組織共用。</summary>
    Api,

    /// <summary>
    /// 訂閱：Claude 的 Pro／Max／Team／Enterprise 方案登入、ChatGPT 方案登入。兩家的條款都不允許一份訂閱處理其他人的請求，
    /// 一律不能開成組織來源（<see cref="AiErrorCodes.HostBillingNotShareable"/>）。
    /// </summary>
    Subscription,

    /// <summary>判斷不出來（沒登入、CLI 的輸出不認得、雲端供應商的設定）。當作不能共用。</summary>
    Unknown,
}

/// <summary>引擎在這台代理上能不能接工作。JSON 是 snake_case。</summary>
public enum HostEngineReadiness
{
    /// <summary>裝好、版本可以、已經用 Kumi 的登入流程登入、API 用量計費。</summary>
    Ready,

    /// <summary>worker 的映像檔裡沒有這個引擎。</summary>
    NotInstalled,

    /// <summary>版本太舊或缺安全用的旗標（見本機代理的探測）。</summary>
    VersionUnsupported,

    /// <summary>沒有登入，或登入失效了（需要擁有者重新授權）。</summary>
    NotLoggedIn,

    /// <summary>CLI 是登入的，但不是透過擁有者在網頁開始的登入（或那份授權已被撤銷／換掉）：不接工作，請擁有者重新登入。</summary>
    NotAuthorized,

    /// <summary>登入的是訂閱帳號（或判斷不出計費方式），不能給全組織共用。</summary>
    BillingNotShareable,
}

/// <summary>worker 最後一次登記時回報、伺服器判斷過的一個引擎的狀態。</summary>
/// <param name="Version">引擎自己回報的版本；沒裝時是 null。</param>
/// <param name="Models">能選的模型（第一個是預設，代號 <c>default</c>）；沒裝或版本不支援時是空的。</param>
/// <param name="Problem">給擁有者看的說明（怎麼裝、怎麼登入），繁體中文；沒問題是 null。</param>
/// <param name="AuthorizedAt">目前的登入授權是什麼時候、誰完成的；沒有是 null。</param>
public sealed record HostEngineStatus(
    AiProviderKind Kind,
    HostEngineReadiness Readiness,
    string? Version,
    bool LoggedIn,
    HostBilling Billing,
    IReadOnlyList<AiModelOption> Models,
    string? Problem,
    DateTimeOffset? AuthorizedAt,
    Guid? AuthorizedBy);

/// <summary>擁有者的設定頁看到的一台主機代理。</summary>
/// <param name="Online">最近 <see cref="AiRules.HostAgentOnlineWindow"/> 內登記過。</param>
/// <param name="MachineName">配對時 worker 帶來的主機名稱；還沒配對是 null。</param>
/// <param name="ServerUrl">worker 連 Kumi 用的網址（它回報的），工作的 MCP 網址照它組（同源檢查）。</param>
/// <param name="Engines">最後一次登記時的兩個引擎；還沒登記過是空的。</param>
/// <param name="Logins">進行中（等認領、已認領）的登入，與 <see cref="AiRules.HostLoginLifetime"/> 內失敗的（帶原因）。</param>
/// <param name="PairingExpiresAt">等配對時配對碼的期限；其他狀態是 null。</param>
public sealed record HostAgent(
    Guid Id,
    string Name,
    HostAgentState State,
    bool Online,
    DateTimeOffset? LastSeenAt,
    string? MachineName,
    string? AgentVersion,
    string? ServerUrl,
    IReadOnlyList<HostEngineStatus> Engines,
    IReadOnlyList<HostLogin> Logins,
    DateTimeOffset? PairingExpiresAt,
    DateTimeOffset CreatedAt)
{
    /// <summary>機器憑證什麼時候過期（worker 在線時會自己換新）；還沒配對是 null。</summary>
    public DateTimeOffset? CredentialExpiresAt { get; init; }

    /// <summary>要擁有者處理的狀況（要重新配對）；沒有是 null。</summary>
    public HostAgentWarning? Warning { get; init; }
}

/// <summary>主機代理要擁有者處理的狀況。三種都讓機器憑證暫時不能用，重新配對後清掉。</summary>
/// <param name="MachineName">只有 <see cref="HostAgentWarningKind.OtherMachine"/> 有：那台報的電腦名稱。</param>
/// <param name="At">發生的時間（過期是過期的時間）。</param>
public sealed record HostAgentWarning(HostAgentWarningKind Kind, string? MachineName, DateTimeOffset At);

/// <summary>JSON 是 snake_case：other_machine、paired_by_former_owner、credential_expired。</summary>
public enum HostAgentWarningKind
{
    /// <summary>有人拿這台的憑證、從另一台電腦（不同的機器編號）登記，被拒絕了。憑證可能外流，請重新配對。</summary>
    OtherMachine,

    /// <summary>配對這台的人已經不是組織擁有者：憑證不再被接受，要由現在的擁有者重新配對。</summary>
    PairedByFormerOwner,

    /// <summary>憑證過期了（worker 太久沒登記，沒來得及換新）。</summary>
    CredentialExpired,
}

public sealed record HostAgentInput(string Name)
{
    /// <summary>修改時：false 是停用。新增的一律是等配對。</summary>
    public bool Enabled { get; init; } = true;
}

/// <param name="PairingCode">一次性的配對碼（例如 <c>K7QD-3MZP-W9XA</c>），明文只在這個回應裡；擁有者在主機上執行 <c>kumi host pair</c> 時輸入。</param>
public sealed record HostAgentPairing(HostAgent Agent, string PairingCode, DateTimeOffset PairingExpiresAt)
{
    // 帶著一次性的配對碼：記錄與測試失敗的輸出都會呼叫 ToString。
    public override string ToString() => $"HostAgentPairing {{ Agent = {Agent.Id}, PairingCode = ***, PairingExpiresAt = {PairingExpiresAt:O} }}";
}

/// <summary>登入的狀態。JSON 是 snake_case。</summary>
public enum HostLoginState
{
    /// <summary>等主機上的 <c>kumi host login</c> 輸入登入碼。</summary>
    Pending,

    /// <summary>主機上已經認領，正在跑 CLI 自己的登入。</summary>
    Claimed,

    Completed,

    /// <summary>被拒絕或 CLI 的登入沒成功（<see cref="HostLogin.Error"/> 說明原因）。</summary>
    Failed,

    /// <summary>擁有者作廢、開始了新的登入、過期、被登出。</summary>
    Cancelled,
}

public sealed record HostLogin(
    Guid Id, AiProviderKind Engine, HostLoginState State, DateTimeOffset ExpiresAt, string? Error, Guid StartedBy, DateTimeOffset CreatedAt);

/// <param name="Code">一次性的登入碼，明文只在這個回應裡。</param>
public sealed record HostLoginStart(HostLogin Login, string Code)
{
    public override string ToString() => $"HostLoginStart {{ Login = {Login.Id}, Code = *** }}";
}

/// <summary>組織來源的狀態（給擁有者看；成員看到的是 <see cref="AiConnectionOption.Notice"/>）。JSON 是 snake_case。</summary>
public enum HostSourceStatus
{
    Ready,

    /// <summary>擁有者把來源關掉了。</summary>
    Disabled,

    /// <summary>代理還沒配對、被停用。</summary>
    AgentUnavailable,

    /// <summary>代理超過 <see cref="AiRules.HostAgentOnlineWindow"/> 沒登記。</summary>
    AgentOffline,

    /// <summary>引擎沒有可以接工作的登入：見 <see cref="HostEngineStatus.Readiness"/>（沒裝、版本、沒登入、沒授權、計費不能共用）。</summary>
    EngineUnavailable,

    /// <summary>這個月的則數或 token 已經用到擁有者設的上限。</summary>
    QuotaReached,
}

/// <summary>擁有者設的上限。null 是不限。則數含失敗與中斷的（已經用到引擎），排隊時被取消、逾時的不算。</summary>
/// <param name="DailyRequestsPerPerson">每個人每天（組織的時區）最多幾則。</param>
/// <param name="MonthlyRequests">這個來源每個月最多幾則（全組織合計）。</param>
/// <param name="MonthlyTokens">這個來源每個月最多多少 token（輸入加輸出，引擎回報的）。只看已經結束的：同時在跑的最多讓它超過一則。</param>
public sealed record HostSourceLimits(int? DailyRequestsPerPerson, int? MonthlyRequests, long? MonthlyTokens);

/// <summary>要存的組織來源。</summary>
/// <param name="Name">成員在模型選單看到的名稱，例如「主機上的 Claude Code」；組織裡的來源之間不重複（不分大小寫）。</param>
/// <param name="EnabledModels">開放的模型代號，要在引擎回報的清單裡；開著時至少一個。</param>
/// <param name="DefaultModel">新對話預設用的模型（要在 <paramref name="EnabledModels"/> 裡）；null 是清單的第一個。</param>
/// <param name="DefaultEffort">要在那個模型能選的清單裡；null 是不指定。</param>
/// <param name="AcknowledgeApiBilling">擁有者確認知道這個來源以 API 用量計費（計到登入的 Console 組織或 OpenAI 帳號），不是訂閱的額度。開著時一定要是 true。</param>
public sealed record HostSourceInput(
    Guid AgentId,
    AiProviderKind Engine,
    string Name,
    bool Enabled,
    IReadOnlyList<string> EnabledModels,
    string? DefaultModel,
    AiEffort? DefaultEffort,
    HostSourceLimits Limits,
    bool AcknowledgeApiBilling);

/// <summary>擁有者看到的一個組織來源。</summary>
/// <param name="StatusMessage">不是 <see cref="HostSourceStatus.Ready"/> 時給擁有者看的說明（繁體中文）。</param>
/// <param name="Models">引擎回報的模型加上開放與否；引擎離線時是最後一次回報的。</param>
/// <param name="Running">現在在執行的件數。</param>
/// <param name="Queued">現在在排隊的件數。</param>
/// <param name="Usage">這個月（組織的時區）的合計。</param>
public sealed record HostSource(
    Guid Id,
    Guid AgentId,
    AiProviderKind Engine,
    string Name,
    bool Enabled,
    HostSourceStatus Status,
    string? StatusMessage,
    IReadOnlyList<AiModel> Models,
    string? DefaultModel,
    AiEffort? DefaultEffort,
    HostSourceLimits Limits,
    int Running,
    int Queued,
    HostUsageTotals Usage,
    DateTimeOffset? BillingAcknowledgedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy);

public sealed record HostAgentOverview(IReadOnlyList<HostAgent> Agents, IReadOnlyList<HostSource> Sources);

/// <param name="Requests">則數（含失敗與中斷；排隊時取消、逾時的不算）。</param>
public sealed record HostUsageTotals(int Requests, int Completed, int Failed, int Stopped, long InputTokens, long OutputTokens);

public sealed record HostPersonUsage(Guid PersonId, HostUsageTotals Totals);

public sealed record HostProjectUsage(string ProjectKey, HostUsageTotals Totals);

/// <summary>一個來源一個月的用量。成員與專案依則數由多到少。</summary>
public sealed record HostSourceUsage(string Month, HostUsageTotals Totals, IReadOnlyList<HostPersonUsage> People, IReadOnlyList<HostProjectUsage> Projects);

/// <param name="MachineKey">worker 第一次執行時產生、存在持久資料夾的隨機編號（8～64 個英數字與 <c>-</c>）。重新配對、容器重建都不變。</param>
/// <param name="MachineName">主機名稱；最長 <see cref="AiRules.MaxMachineNameLength"/>。</param>
public sealed record HostPairingRequest(string Code, string MachineKey, string MachineName, string AgentVersion)
{
    // 帶著配對碼：記錄與測試失敗的輸出都會呼叫 ToString。
    public override string ToString() =>
        $"HostPairingRequest {{ Code = ***, MachineKey = {MachineKey}, MachineName = {MachineName}, AgentVersion = {AgentVersion} }}";
}

/// <param name="Credential">機器憑證（<c>kha_…</c>），明文只在這個回應裡；worker 存在持久資料夾（權限 600），不寫進記錄、命令列。</param>
public sealed record HostPairingResult(Guid AgentId, string AgentName, string Credential)
{
    public override string ToString() => $"HostPairingResult {{ AgentId = {AgentId}, AgentName = {AgentName}, Credential = *** }}";
}

/// <param name="ServerUrl">worker 連 Kumi 用的網址（例如 compose 裡的 <c>http://api:8080</c>）：http 或 https、沒有帳密、query 與 fragment，最長 <see cref="AiRules.MaxBaseUrlLength"/>。</param>
/// <param name="Engines">兩個引擎各一筆（沒裝也要報，<see cref="HostEngineReport.Installed"/> 是 false）；不能重複。</param>
public sealed record HostAgentCheckIn(string AgentVersion, string ServerUrl, IReadOnlyList<HostEngineReport> Engines)
{
    /// <summary>
    /// 這台 worker 的機器編號（配對時報的同一個，存在資料卷）。必填，格式同配對（不然 <see cref="AiErrorCodes.HostAgentInvalid"/>）。
    /// 和配對時存的不同時拒絕登記（當作憑證不對，401），記下報來的編號、電腦名稱與時間，擁有者在管理頁看到警告（<see cref="HostAgent.Warning"/>）。
    /// </summary>
    public string? MachineKey { get; init; }

    /// <summary>電腦名稱（同配對的格式）；只有機器編號不同、要記下警告時用到。</summary>
    public string? MachineName { get; init; }
}

/// <param name="Supported">版本與安全用的旗標都可以（本機代理探測的同一套規則）。</param>
/// <param name="LoginId">這份登入是哪一次 Kumi 登入完成的（<see cref="HostLoginCompleted.LoginId"/>，worker 存在持久資料夾）；CLI 是在 Kumi 以外登入的是 null。</param>
/// <param name="Models">規則同 <see cref="LocalEngineInfo.Models"/>。</param>
/// <param name="Problem">給擁有者看的說明（同 <see cref="HostEngineStatus.Problem"/>），最長 <see cref="AiRules.MaxErrorLength"/>。</param>
public sealed record HostEngineReport(
    AiProviderKind Kind,
    bool Installed,
    string? Version,
    bool Supported,
    bool LoggedIn,
    HostBilling Billing,
    Guid? LoginId,
    IReadOnlyList<AiModelOption> Models,
    string? Problem);

/// <param name="Job">領到的工作；沒有是 null。</param>
/// <param name="Logout">擁有者登出（撤銷授權）的引擎：worker 執行 CLI 的登出並清掉存的登入編號。</param>
/// <param name="Disabled">代理被停用：照樣保持登記，不會派工作。</param>
public sealed record HostCheckInResult(HostJob? Job, IReadOnlyList<AiProviderKind> Logout, bool Disabled)
{
    /// <summary>
    /// 機器憑證快過期（剩不到 <see cref="AiRules.HostCredentialRenewBefore"/>）時發的新憑證（<c>kha_…</c>），明文只在這個回應裡。
    /// worker 存好（取代舊的，權限 600）之後下一次請求就用它；伺服器在它第一次被用到時讓舊的作廢。沒有要換是 null。
    /// </summary>
    public string? RotatedCredential { get; init; }

    public override string ToString() =>
        $"HostCheckInResult {{ Job = {Job?.JobId}, Logout = [{string.Join(", ", Logout)}], Disabled = {Disabled}, RotatedCredential = {(RotatedCredential is null ? "null" : "***")} }}";
}

/// <summary>
/// 派給 worker 的一件工作。和本機代理的 <see cref="LocalAgentJob"/> 一樣跑（同一套隔離），差別：
/// MCP 用 <see cref="ToolToken"/>（這件工作專用、到 <see cref="ToolTokenExpiresAt"/> 或工作結束就失效），不是任何人的權杖；
/// 回報與結束要帶 <see cref="Lease"/>。
/// </summary>
/// <param name="McpUrl">worker 回報的 <see cref="HostAgentCheckIn.ServerUrl"/> 加 <c>/mcp</c>，所以和 worker 連的伺服器同源。</param>
public sealed record HostJob(
    Guid JobId,
    int Lease,
    AiProviderKind Engine,
    string Model,
    AiEffort? Effort,
    string ProjectKey,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<string> AllowedTools,
    string McpUrl,
    int MaxTurns,
    string ToolToken,
    DateTimeOffset ToolTokenExpiresAt)
{
    public override string ToString() => $"HostJob {{ JobId = {JobId}, Engine = {Engine}, Model = {Model}, ProjectKey = {ProjectKey}, ToolToken = *** }}";
}

/// <param name="Cancelled">使用者按了停止、來源或代理被停用、登出：停下引擎、送結束。</param>
/// <param name="NextSeq">伺服器下一則要的流水號（已經存到 NextSeq − 1）。</param>
public sealed record HostReportAck(bool Cancelled, int NextSeq);

public sealed record HostLoginClaim(Guid LoginId, AiProviderKind Engine, DateTimeOffset ExpiresAt);

/// <param name="LoggedIn">CLI 自己的登入跑完之後，狀態指令說已經登入。</param>
/// <param name="Billing">同 <see cref="HostEngineReport.Billing"/>。</param>
/// <param name="Problem">CLI 的登入沒成功時的說明。</param>
public sealed record HostLoginReport(bool LoggedIn, HostBilling Billing, string? Problem);

/// <param name="LoginId">worker 存在持久資料夾，之後每次登記帶著（<see cref="HostEngineReport.LoginId"/>）。</param>
public sealed record HostLoginCompleted(Guid LoginId, AiProviderKind Engine);
