namespace PmTool.Ai;

/// <summary>AI 模組的上限與時間。數字的理由見 <c>internal/HANDOFF-ai-assistant.md</c> 第 2 節。</summary>
public static class AiRules
{
    public const int MaxConnectionsPerOrganization = 20;

    /// <summary>一個人最多幾個自己的連線。</summary>
    public const int MaxPersonalConnections = 5;

    /// <summary>「用 OpenRouter 登入」從開始到回來最多多久（state 的有效時間；OpenRouter 的授權碼本身 10 分鐘過期）。</summary>
    public static readonly TimeSpan OpenRouterLoginLifetime = TimeSpan.FromMinutes(10);

    /// <summary>本機代理登記時最多等多久工作（長輪詢）。比常見的反向代理逾時（60 秒）短。</summary>
    public static readonly TimeSpan LocalAgentPollWait = TimeSpan.FromSeconds(25);

    /// <summary>本機代理多久沒登記就算離線：引擎從能選的清單消失。兩次長輪詢之間的空檔加上網路的延遲。</summary>
    public static readonly TimeSpan LocalAgentOnlineWindow = TimeSpan.FromSeconds(60);

    /// <summary>送出之後多久沒有本機代理領走，回覆就失敗（「你電腦上的 Kumi 代理沒有回應」）。</summary>
    public static readonly TimeSpan LocalAgentClaimTimeout = TimeSpan.FromSeconds(20);

    /// <summary>領走之後多久沒有任何回報，回覆就失敗（本機代理被關掉、網路斷了）。引擎讀 MCP、想很久時也會有回報，所以給得寬一點。</summary>
    public static readonly TimeSpan LocalAgentIdleTimeout = TimeSpan.FromSeconds(120);

    /// <summary>一個人最多登記幾台電腦，超過時最久沒在線的那台被換掉。</summary>
    public const int MaxLocalAgentMachines = 10;

    /// <summary>
    /// 本機代理與主機的 worker 要這個版本以上，才收得到圖片與 PDF（<see cref="LocalAgentJob.Attachments"/>）。
    /// 舊版的這一則帶了圖片或 PDF：<see cref="AiErrorCodes.AttachmentUnsupported"/>（請 kumi update）；歷史裡的換成說明。
    /// </summary>
    public const string MinAgentVersionForAttachments = "0.10.0";

    /// <summary>
    /// 不選專案的組織對話要用本機或主機的引擎時，kumi 要在這個版本以上：舊版一定要有專案代號、也不認得 list_projects、propose_project，
    /// 送過去整件工作會失敗。送出時擋下（<see cref="AiErrorCodes.AgentUpdateRequired"/>），什麼都不存。
    /// </summary>
    public const string MinAgentVersionForOrganizationChat = "0.12.0";

    /// <summary>
    /// 本機代理與主機的 worker 要這個版本以上，工作的工具清單才放 propose_new_items：舊版的 kumi 看到不認得的工具會整件工作不跑。
    /// 舊版照樣能用，只是少了這個工具，提示裡請模型叫使用者 <c>kumi update</c>。
    /// </summary>
    public const string MinAgentVersionForNewItemProposals = "0.14.0";

    public const int MaxMachineNameLength = 60;

    /// <summary>一個引擎最多列幾個模型。</summary>
    public const int MaxLocalEngineModels = 20;

    /// <summary>一次回報最多幾則進度。</summary>
    public const int MaxLocalAgentEventsPerReport = 200;

    /// <summary>一則文字進度最長幾個字元。</summary>
    public const int MaxLocalAgentTextDelta = 8000;

    /// <summary>一個組織最多幾台主機代理（internal/HANDOFF-host-agent.md）。一般一台就夠，多的是換主機時的過渡。</summary>
    public const int MaxHostAgentsPerOrganization = 3;

    /// <summary>主機代理的配對碼多久內有效。</summary>
    public static readonly TimeSpan HostPairingLifetime = TimeSpan.FromMinutes(15);

    /// <summary>擁有者在網頁開始的登入，多久內要在主機上完成（認領與回報都要在期限內）。</summary>
    public static readonly TimeSpan HostLoginLifetime = TimeSpan.FromMinutes(15);

    /// <summary>主機代理登記時最多等多久工作（長輪詢）。和本機代理一樣比常見的反向代理逾時短。</summary>
    public static readonly TimeSpan HostPollWait = TimeSpan.FromSeconds(25);

    /// <summary>主機代理多久沒登記就算離線。</summary>
    public static readonly TimeSpan HostAgentOnlineWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 排隊的期限：送出之後多久還沒輪到（沒被 worker 領走）就失敗，排隊時取消的不算用量。和執行的期限（<see cref="ReplyTimeout"/>，
    /// 從領走開始算）分開：團隊共用時排隊是常態，不能沿用本機代理「20 秒沒領走」的規則。
    /// </summary>
    public static readonly TimeSpan HostQueueTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// 領走之後多久沒有任何回報（worker 每 5 秒有心跳）就結束這件工作、標成「結果不明」：引擎可能已經提出了變更集或修改建議，
    /// 所以不會自動重跑。
    /// </summary>
    public static readonly TimeSpan HostLeaseTimeout = TimeSpan.FromSeconds(60);

    /// <summary>每個組織來源同時最多執行幾件（第一版 1；之後依量測調整）。</summary>
    public const int HostSourceMaxConcurrent = 1;

    /// <summary>每個人在一個來源最多同時等候幾件（還沒開始的）。和 FIFO 一起讓一個人佔不滿來源：輪到他之後才能再排。</summary>
    public const int HostMaxQueuedPerPerson = 1;

    /// <summary>一個來源最多同時排幾件（全組織）。</summary>
    public const int HostMaxQueuedPerSource = 20;

    /// <summary>每台 worker 同時最多跑幾件（兩個引擎各一）。</summary>
    public const int HostWorkerMaxConcurrent = 2;

    /// <summary>擁有者設的上限能設多大（每天每人的則數、每月的則數）。</summary>
    public const int MaxHostRequestLimit = 1_000_000;

    /// <summary>擁有者設的每月 token 上限能設多大。</summary>
    public const long MaxHostTokenLimit = 100_000_000_000;

    /// <summary>一份修改建議最多改幾個欄位。</summary>
    public const int MaxItemProposalChanges = 20;

    /// <summary>一份新增項目的建議最多幾個項目（把一份需求拆成幾個任務夠用；再多請分幾次）。</summary>
    public const int MaxNewItemsPerProposal = 20;

    /// <summary>新增項目的建議停在「建立中」超過這麼久，當作建立的過程中斷了（程序在中途結束），讀出來是已套用加說明。</summary>
    public static readonly TimeSpan NewItemProposalApplyStale = TimeSpan.FromMinutes(5);

    /// <summary>助理的 search_items 一次最多回幾個項目。</summary>
    public const int MaxItemSearchResults = 20;

    public const int MaxNameLength = 60;

    public const int MaxBaseUrlLength = 200;

    public const int MaxApiKeyLength = 512;

    /// <summary>存的時候留金鑰的最後幾個字，讓管理員認得是哪一把（例如「…a1b2」）。金鑰短於兩倍這個長度時不留。</summary>
    public const int ApiKeyHintLength = 4;

    public const int MaxModelIdLength = 200;

    public const int MaxDisplayNameLength = 200;

    /// <summary>從供應商讀到的模型最多存幾個（OpenAI 的清單有上百個），超過的依代號排序後截掉。</summary>
    public const int MaxCatalogModels = 500;

    /// <summary>一個連線最多開放幾個模型給成員用。</summary>
    public const int MaxEnabledModels = 50;

    /// <summary>手動加的模型代號（相容端點讀不到清單，或清單裡還沒有的新模型）最多幾個。</summary>
    public const int MaxCustomModels = 20;

    /// <summary>讀模型清單的逾時。</summary>
    public static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(20);

    /// <summary>使用者一則訊息最長幾個字元。</summary>
    public const int MaxMessageLength = 8000;

    /// <summary>一則對話最多幾則訊息（使用者與助理都算）。再長就要開新的對話：每一則回覆都要把整段對話送給模型。</summary>
    public const int MaxMessagesPerConversation = 60;

    /// <summary>對話清單最多列幾則，新的在前。</summary>
    public const int MaxConversationsListed = 100;

    /// <summary>對話的標題取第一則訊息的前幾個字。</summary>
    public const int MaxTitleLength = 40;

    /// <summary>一則回覆裡模型最多呼叫幾次工具，到了就停下來，請使用者再說一次要做什麼。</summary>
    public const int MaxToolCallsPerReply = 12;

    /// <summary>
    /// 每一次呼叫模型的輸出上限（含思考）。串流的請求沒有逾時的問題，思考深度開到最高時 16k 不夠。
    /// </summary>
    public const int MaxOutputTokens = 32000;

    /// <summary>一則回覆從開始到結束最多多久（含所有工具呼叫），到了就中斷、存下已經有的部分。</summary>
    public static readonly TimeSpan ReplyTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 對話「正在回覆」的租約。比 <see cref="ReplyTimeout"/> 長一點：程序在回覆中途當掉時，過了租約就能再送，不會永遠卡住。
    /// </summary>
    public static readonly TimeSpan RunLease = TimeSpan.FromMinutes(6);

    /// <summary>一個人同時最多幾則對話在回覆。</summary>
    public const int MaxConcurrentRunsPerPerson = 2;

    /// <summary>工具的結果存進對話、之後再送給模型時最長幾個字元，超過的截斷並註明。</summary>
    public const int MaxToolResultLength = 60_000;

    /// <summary>
    /// 本機與主機上的引擎一次最多讀幾個字元（整段對話加這一則，含文字附件）。提示是排成一整段文字交給引擎的命令列，
    /// 不能無限長：超過時從最舊的對話開始省略；這一則自己放不進去的，送出時就擋下來（<see cref="AiErrorCodes.AttachmentTooLarge"/>）。
    /// </summary>
    public const int MaxLocalPromptLength = 60_000;

    /// <summary>錯誤原因（我們的說明加上供應商回的一小段話）最多存這麼長。</summary>
    public const int MaxErrorLength = 500;

    /// <summary>組織連線每人每天的則數上限的預設（管理員在「AI 設定」頁可調、可設成不限）。偏寬：正常使用碰不到，只擋腳本。</summary>
    public const int DefaultDailyRequestsPerPerson = 200;

    /// <summary>內建助理一則回覆裡最多提出幾份變更集（propose_changeset）。</summary>
    public const int MaxChangeSetsPerReply = 1;

    /// <summary>一則回覆最多提出幾份新專案建議（和變更集一樣，避免模型一口氣開一堆）。</summary>
    public const int MaxProjectProposalsPerReply = 1;

    /// <summary>本機代理與主機代理的一件工作最多呼叫幾次工具（伺服器端計，和引擎拿到的上限無關）。</summary>
    public const int MaxAgentToolCallsPerJob = 13;

    /// <summary>本機代理與主機代理的一件工作最多成功提出幾份（變更集、修改建議、新增項目的建議、新專案建議合計）。</summary>
    public const int MaxAgentProposalsPerJob = 2;

    /// <summary>對話多久沒有更新就刪掉（整則，含訊息）。</summary>
    public static readonly TimeSpan ConversationRetention = TimeSpan.FromDays(180);

    /// <summary>主機代理的機器憑證（<c>kha_</c>）發出後多久過期。</summary>
    public static readonly TimeSpan HostCredentialLifetime = TimeSpan.FromDays(90);

    /// <summary>機器憑證剩下不到這麼久時，登記的回應帶一把新的（<see cref="HostCheckInResult.RotatedCredential"/>）。</summary>
    public static readonly TimeSpan HostCredentialRenewBefore = TimeSpan.FromDays(30);

    /// <summary>個人的 OpenAI 相容端點只能連這個連接埠（而且只能 https）。</summary>
    public const int PersonalEndpointPort = 443;

    /// <summary>從供應商讀的回應（模型清單、非串流的錯誤內文、串流的整則回覆）最多讀多少位元組，超過就中斷。</summary>
    public const long MaxProviderResponseBytes = 16 * 1024 * 1024;
}
