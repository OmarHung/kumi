using PmTool.Abstractions;

namespace PmTool.Ai;

/// <summary>
/// 組織的 AI 設定：接了哪些供應商（連線）、開放哪些模型、預設用哪一個。只有組織管理員能讀、能改
/// （<see cref="AiErrorCodes.Forbidden"/>）。規格見 <c>internal/HANDOFF-ai-assistant.md</c> 第 3 節。
/// 金鑰加密後才存，不會讀出來（只說有沒有、最後幾個字）。
/// </summary>
public interface IAiSettingsService
{
    /// <summary>還沒設定過時回「沒開、沒有連線、沒有預設」，不是失敗。每個連線帶這個月（組織的時區）的用量。</summary>
    Task<Result<AiSettings>> GetAsync(Actor actor, CancellationToken cancellationToken);

    /// <summary>
    /// 存開關與預設。預設的連線、模型要存在而且開放給成員，思考深度要在那個模型能選的清單裡（或 null），
    /// 不然回 <see cref="AiErrorCodes.SettingsInvalid"/>，細項 <see cref="AiErrorCodes.ModelNotAvailable"/>。
    /// 開著時一定要有預設（細項 <see cref="AiErrorCodes.FieldRequired"/>，路徑 <c>default</c>）。
    /// </summary>
    Task<Result<AiSettings>> SaveAsync(Actor actor, AiSettingsInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 新增一個連線：先照填的金鑰去讀模型清單（讀不到回 <see cref="AiErrorCodes.ProviderFailed"/>，什麼都不存），
    /// 再存下清單與開放的模型。<see cref="AiConnectionInput.ApiKey"/> 是 null 時當作沒有金鑰（只有相容端點可以沒有）。
    /// </summary>
    Task<Result<AiConnection>> CreateConnectionAsync(Actor actor, AiConnectionInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 修改連線。<see cref="AiConnectionInput.ApiKey"/> 是 null 時沿用存的金鑰，這時供應商與基底網址不能和存的不同
    /// （細項 <see cref="AiErrorCodes.FieldRequired"/>，路徑 <c>apiKey</c>）：不然另一個管理員可以把存的金鑰送到自己的主機。
    /// 供應商、基底網址或金鑰換了才重新讀模型清單；只改名稱、開放的模型或手動的模型不連供應商。
    /// 預設用的模型被關掉時，回 <see cref="AiErrorCodes.SettingsInvalid"/>（細項 <see cref="AiErrorCodes.ModelNotAvailable"/>，
    /// 路徑 <c>enabledModels</c>）：請先換預設。
    /// </summary>
    Task<Result<AiConnection>> UpdateConnectionAsync(
        Actor actor, Guid connectionId, AiConnectionInput input, CancellationToken cancellationToken);

    /// <summary>
    /// 用存的金鑰重新讀模型清單。還在清單裡的模型保留開放與否，新出現的預設不開放。不見了的模型：沒開放的拿掉；
    /// 開放中的保留、改標成手動加的（供應商的清單可能只是沒列出來；真的不能用時，回覆會顯示供應商的錯誤），
    /// 不會讓預設或成員正在用的模型無聲無息地消失。手動加的模型不動。讀不到清單回 <see cref="AiErrorCodes.ProviderFailed"/>，什麼都不改。
    /// </summary>
    Task<Result<AiConnection>> RefreshModelsAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// 刪除連線。它是預設時一起清掉預設並關掉 AI（沒有預設不能開著）。
    /// 已經有的對話留著，裡面的回覆記的是當時的連線名稱。
    /// </summary>
    Task<Result<Unit>> DeleteConnectionAsync(Actor actor, Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// 用這份（還沒存的）設定讀模型清單，給設定頁的「讀取模型」按鈕用；不存任何東西。
    /// <see cref="AiDiscoveryInput.ApiKey"/> 是 null 且帶了 <see cref="AiDiscoveryInput.ConnectionId"/> 時用那個連線存的金鑰
    /// （供應商與基底網址要和存的相同，規則同 <see cref="UpdateConnectionAsync"/>）。
    /// </summary>
    Task<Result<IReadOnlyList<AiDiscoveredModel>>> DiscoverModelsAsync(
        Actor actor, AiDiscoveryInput input, CancellationToken cancellationToken);
}

/// <summary>
/// 成員能選的 AI：組織開著時的組織連線，加上組織允許時自己的連線，只列開放的模型。每個登入的人都能讀。
/// <see cref="AiOptions.Available"/> 是兩者任何一個有可用的模型；<see cref="AiOptions.Default"/> 只看組織的預設（組織沒開時是 null，網頁選第一個）。
/// </summary>
public interface IAiOptionsService
{
    Task<AiOptions> GetAsync(Actor actor, CancellationToken cancellationToken);
}

/// <summary>AI 的供應商。JSON 是 snake_case：anthropic、openai、google、openai_compatible。</summary>
public enum AiProviderKind
{
    Anthropic,

    Openai,

    /// <summary>Google 的 Gemini API（AI Studio 的金鑰），不是 Vertex AI。</summary>
    Google,

    /// <summary>OpenAI 相容的端點：自架的 Ollama、vLLM、LM Studio，或 OpenRouter 這類轉接服務。要填基底網址。</summary>
    OpenaiCompatible,

    /// <summary>
    /// OpenRouter：一個帳號能用好幾家的模型。可以「用 OpenRouter 登入」拿到綁那個帳號的金鑰（<see cref="IOpenRouterLoginService"/>），
    /// 也可以貼金鑰。JSON 是 <c>openrouter</c>。
    /// </summary>
    Openrouter,

    /// <summary>
    /// 使用者自己電腦上的 Claude Code（自己的訂閱），透過本機代理（<see cref="ILocalAgentService"/>）跑。
    /// 只出現在能選的清單（<see cref="AiConnectionOption"/>），不能存成連線。JSON 是 <c>claude_code</c>。
    /// </summary>
    ClaudeCode,

    /// <summary>使用者自己電腦上的 Codex（自己的 ChatGPT 方案），同 <see cref="ClaudeCode"/>。JSON 是 <c>codex</c>。</summary>
    Codex,
}

/// <summary>
/// 思考深度。每個模型能選的不一樣（<see cref="AiModel.Efforts"/>）；null 是不指定，用模型自己的預設。
/// JSON 是 snake_case：none、low、medium、high、xhigh、max。
/// </summary>
public enum AiEffort
{
    /// <summary>不思考。只有支援的 OpenAI 模型有。</summary>
    None,

    Low,

    Medium,

    High,

    /// <summary>很高。</summary>
    Xhigh,

    /// <summary>最高。只有 Anthropic 的模型有。</summary>
    Max,
}

/// <summary>一個連線上的模型。</summary>
/// <param name="Id">送給供應商的模型代號，例如 <c>claude-opus-5-5</c>。</param>
/// <param name="DisplayName">供應商給的名稱；沒有的話和 <paramref name="Id"/> 相同。</param>
/// <param name="Efforts">能選的思考深度，由淺到深；空的是不能選（不送這個參數）。</param>
/// <param name="Enabled">開放給成員用。</param>
/// <param name="Custom">管理員手動加的，不是從供應商的清單讀到的。重新讀清單時不會被拿掉。</param>
public sealed record AiModel(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts, bool Enabled, bool Custom);

/// <summary>連線這個月（組織的時區）的用量，只算 AI 助理的回覆。</summary>
/// <param name="Month">例如 <c>2026-11</c>。</param>
/// <param name="Replies">回覆的則數（含失敗與中斷的）。</param>
public sealed record AiUsage(string Month, long InputTokens, long OutputTokens, int Replies);

/// <summary>讀出來的連線。金鑰不會讀出來。</summary>
/// <param name="BaseUrl">只有 <see cref="AiProviderKind.OpenaiCompatible"/> 有，結尾沒有斜線，例如 <c>http://ollama.example.com:11434/v1</c>。</param>
/// <param name="HasApiKey">有沒有存金鑰。</param>
/// <param name="ApiKeyHint">金鑰的最後幾個字（<see cref="AiRules.ApiKeyHintLength"/>），讓管理員認得是哪一把；沒有或太短時是 null。</param>
/// <param name="Models">模型，依代號排序；手動加的也在裡面。</param>
/// <param name="ModelsCheckedAt">最後一次從供應商讀到清單的時間。</param>
public sealed record AiConnection(
    Guid Id,
    string Name,
    AiProviderKind Kind,
    string? BaseUrl,
    bool HasApiKey,
    string? ApiKeyHint,
    IReadOnlyList<AiModel> Models,
    DateTimeOffset? ModelsCheckedAt,
    AiUsage Usage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid UpdatedBy);

/// <summary>選了哪個連線的哪個模型、多深的思考。</summary>
/// <param name="Effort">null 是不指定，用模型自己的預設。</param>
public sealed record AiSelection(Guid ConnectionId, string Model, AiEffort? Effort);

/// <summary>組織的 AI 設定。</summary>
/// <param name="Enabled">開著時成員才看得到 AI 助理。</param>
/// <param name="Default">新的對話預設用的；開著時一定有。</param>
/// <param name="Connections">依名稱排序。</param>
/// <param name="UpdatedAt">開關或預設最後一次存的時間；沒存過是 null。連線的修改不算。</param>
public sealed record AiSettings(
    bool Enabled,
    AiSelection? Default,
    IReadOnlyList<AiConnection> Connections,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy)
{
    /// <summary>
    /// 成員能不能接自己的 AI 帳號（<see cref="IPersonalAiConnectionService"/>）。預設不行：專案的設定會送到成員自己的供應商帳號，
    /// 由組織管理員決定。和 <see cref="Enabled"/> 互相獨立：組織沒接任何供應商，成員也可以用自己的。
    /// </summary>
    public bool AllowPersonalConnections { get; init; }

    /// <summary>組織連線的用量上限（只算組織的連線；自己的連線、本機代理、主機來源不算）。沒存過時是預設（每人每天 <see cref="AiRules.DefaultDailyRequestsPerPerson"/> 則，其餘不限）。</summary>
    public AiUsageLimits Limits { get; init; } = AiUsageLimits.Default;

    /// <summary>組織連線這個月（組織的時區）全組織的用量，和 <see cref="Limits"/> 的每月上限比。</summary>
    public AiMonthUsage MonthUsage { get; init; } = new(string.Empty, 0, 0);
}

public sealed record AiSettingsInput(bool Enabled, AiSelection? Default)
{
    /// <summary>同 <see cref="AiSettings.AllowPersonalConnections"/>。</summary>
    public bool AllowPersonalConnections { get; init; }

    /// <summary>
    /// 新的上限；null 是沿用存的（舊的用戶端不送這一欄，不會被清成不限）。每一項 null 是不限；
    /// 則數要是 1～<see cref="AiRules.MaxHostRequestLimit"/>、token 要是 1～<see cref="AiRules.MaxHostTokenLimit"/>，
    /// 不然 <see cref="AiErrorCodes.SettingsInvalid"/>（細項 <see cref="AiErrorCodes.FieldInvalid"/>，路徑 <c>limits.dailyRequestsPerPerson</c> 這類）。
    /// </summary>
    public AiUsageLimits? Limits { get; init; }
}

/// <summary>組織連線的用量上限。每一項 null 是不限。</summary>
/// <param name="DailyRequestsPerPerson">每人每天（組織的時區）最多幾則回覆。</param>
/// <param name="MonthlyRequests">全組織每月最多幾則回覆。</param>
/// <param name="MonthlyTokens">全組織每月最多多少 token（輸入加輸出）。到了上限之後的下一則才擋，進行中的那則照樣做完。</param>
public sealed record AiUsageLimits(int? DailyRequestsPerPerson, int? MonthlyRequests, long? MonthlyTokens)
{
    public static AiUsageLimits Default { get; } = new(AiRules.DefaultDailyRequestsPerPerson, null, null);
}

/// <summary>組織連線某個月的全組織用量。</summary>
/// <param name="Month">例如 <c>2026-11</c>。</param>
/// <param name="Requests">回覆的則數（含失敗與中斷的；送出沒有完成而退還的不算）。</param>
/// <param name="Tokens">輸入加輸出。</param>
public sealed record AiMonthUsage(string Month, int Requests, long Tokens);

/// <summary>要存的連線。</summary>
/// <param name="Name">畫面上的名稱，例如「Anthropic（公司帳號）」；組織裡不能重複（不分大小寫）。</param>
/// <param name="BaseUrl">只看 <see cref="AiProviderKind.OpenaiCompatible"/>（必填）；其他供應商不看、存成 null。</param>
/// <param name="ApiKey">新增時 null 是沒有金鑰；修改時 null 是沿用存的，空字串是清掉，其他是換成新的。</param>
/// <param name="EnabledModels">開放給成員的模型代號；要在讀到的清單或 <paramref name="CustomModels"/> 裡。</param>
/// <param name="CustomModels">手動加的模型代號。</param>
public sealed record AiConnectionInput(
    string Name,
    AiProviderKind Kind,
    string? BaseUrl,
    string? ApiKey,
    IReadOnlyList<string> EnabledModels,
    IReadOnlyList<string> CustomModels)
{
    // 帶著金鑰：記錄、例外訊息與測試失敗的輸出都會呼叫 ToString，金鑰一律遮住。
    public override string ToString() =>
        $"AiConnectionInput {{ Name = {Name}, Kind = {Kind}, BaseUrl = {BaseUrl}, ApiKey = {(ApiKey is null ? "null" : "***")}, "
        + $"EnabledModels = [{string.Join(", ", EnabledModels)}], CustomModels = [{string.Join(", ", CustomModels)}] }}";
}

/// <summary>「讀取模型」用的、還沒存的設定。</summary>
/// <param name="ConnectionId">修改既有的連線時帶著，<paramref name="ApiKey"/> 是 null 時用它存的金鑰。</param>
public sealed record AiDiscoveryInput(AiProviderKind Kind, string? BaseUrl, string? ApiKey, Guid? ConnectionId)
{
    public override string ToString() =>
        $"AiDiscoveryInput {{ Kind = {Kind}, BaseUrl = {BaseUrl}, ApiKey = {(ApiKey is null ? "null" : "***")}, ConnectionId = {ConnectionId} }}";
}

/// <summary>從供應商讀到的一個模型。</summary>
public sealed record AiDiscoveredModel(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts);

/// <summary>成員能選的 AI。</summary>
/// <param name="Available">組織開著 AI，而且至少有一個開放的模型。false 時網頁不顯示 AI 助理的入口。</param>
/// <param name="Connections">有開放模型的連線，依名稱排序；沒開時是空的。</param>
/// <param name="Default">組織的預設；沒開時是 null。</param>
public sealed record AiOptions(bool Available, IReadOnlyList<AiConnectionOption> Connections, AiSelection? Default)
{
    /// <summary>組織允許成員接自己的 AI 帳號：網頁在沒有可用的模型時，提示「到我的帳號加自己的 AI 帳號」。</summary>
    public bool CanAddPersonal { get; init; }
}

/// <param name="Id">
/// 連線的編號；組織的與自己的都在同一份清單。本機代理的引擎（<see cref="AiProviderKind.ClaudeCode"/>、<see cref="AiProviderKind.Codex"/>）
/// 是「那台電腦的那個引擎」的編號，名稱是「Claude Code（電腦名稱）」，只在那台電腦在線時列出。
/// </param>
public sealed record AiConnectionOption(Guid Id, string Name, AiProviderKind Kind, IReadOnlyList<AiModelOption> Models)
{
    /// <summary>
    /// 自己的連線（<see cref="IPersonalAiConnectionService"/>）與自己電腦上的引擎。清單裡組織的在前、自己的在後（存的連線、再來本機的引擎），各自依名稱排序。
    /// 組織關掉 AI 時只有自己的；組織不允許個人連線時沒有自己的。
    /// </summary>
    public bool Personal { get; init; }

    /// <summary>
    /// 組織擁有者開的主機來源（<see cref="IHostAgentAdminService"/>）：Kind 是 <see cref="AiProviderKind.ClaudeCode"/> 或 <see cref="AiProviderKind.Codex"/>，
    /// <see cref="Id"/> 是來源的編號，名稱例如「主機上的 Claude Code」。組織的有效成員（擁有者、管理員、一般成員）都看得到，
    /// 不看 <see cref="AiSettings.Enabled"/> 與 <see cref="AiSettings.AllowPersonalConnections"/>。排在組織的連線後面、自己的前面。
    /// </summary>
    public bool Hosted { get; init; }

    /// <summary>主機來源現在不能用時給成員看的一句話（例如「主機代理目前離線，請組織擁有者檢查代理服務」）；能用是 null。不會叫成員執行任何指令。</summary>
    public string? Notice { get; init; }
}

/// <param name="Efforts">能選的思考深度，由淺到深；空的是不能選。</param>
public sealed record AiModelOption(string Id, string DisplayName, IReadOnlyList<AiEffort> Efforts)
{
    /// <summary>
    /// 不指定思考深度時這個模型實際用的等級（網頁的選單寫「預設（中）」）；只在供應商的文件寫明、或引擎自己回報時才有，
    /// 不知道是 null（網頁只寫「預設」）。一定是 <see cref="Efforts"/> 裡的一個。
    /// </summary>
    public AiEffort? DefaultEffort { get; init; }
}
