/**
 * AI 設定與 AI 助理在 API 上的形狀（internal/HANDOFF-ai-assistant.md 第 6 節）。兩個功能共用，所以型別放這一份；
 * 呼叫與快取各自在 aiSettings.ts（設定頁）與 assistant.ts（助理）。
 */

/** 能存成連線的供應商（AI 設定、我的 AI 帳號）。 */
export type AiProviderKind = 'anthropic' | 'openai' | 'google' | 'openai_compatible' | 'openrouter';

/**
 * 自己電腦上的引擎，透過本機代理（`kumi agent start`）跑：只出現在能選的清單（AiConnectionOption），
 * 不能存成連線。internal/HANDOFF-local-agent.md。
 */
export type LocalEngineKind = 'claude_code' | 'codex';

/** 能選的清單裡的連線種類：存的連線或本機的引擎。 */
export type AiOptionKind = AiProviderKind | LocalEngineKind;

/** 連線屬於誰：組織的（管理員設定）或自己的。 */
export type AiConnectionScope = 'organization' | 'personal';

/** 思考深度。每個模型能選的不一樣（AiModel.efforts）；null 是不指定，用模型自己的預設。 */
export type AiEffort = 'none' | 'low' | 'medium' | 'high' | 'xhigh' | 'max';

export interface PersonReference {
  readonly personId: string;
  readonly displayName: string;
}

/** 選了哪個連線的哪個模型、多深的思考。 */
export interface AiSelection {
  readonly connectionId: string;
  readonly model: string;
  readonly effort: AiEffort | null;
}

export interface AiModel {
  readonly id: string;
  readonly displayName: string;
  /** 由淺到深；空的是不能選。 */
  readonly efforts: readonly AiEffort[];
  /** 開放給成員用。 */
  readonly enabled: boolean;
  /** 管理員手動加的（不是從供應商的清單讀到的）。 */
  readonly custom: boolean;
}

/** 這個月（組織的時區）的用量，只算 AI 助理的回覆。 */
export interface AiUsage {
  /** 例如 2026-11。 */
  readonly month: string;
  readonly inputTokens: number;
  readonly outputTokens: number;
  readonly replies: number;
}

/** 組織接上的一個供應商。金鑰不會讀出來，只有 hasApiKey 與最後四個字。 */
export interface AiConnection {
  readonly id: string;
  readonly name: string;
  readonly kind: AiProviderKind;
  /** 只有 openai_compatible 有。 */
  readonly baseUrl: string | null;
  readonly hasApiKey: boolean;
  readonly apiKeyHint: string | null;
  readonly models: readonly AiModel[];
  readonly modelsCheckedAt: string | null;
  readonly usage: AiUsage;
  readonly createdAt: string;
  readonly updatedAt: string;
  readonly updatedBy: PersonReference;
}

/** 組織連線的用量上限；每一項 null 是不限。只算組織的連線（自己的連線、本機代理、主機來源不算）。 */
export interface AiUsageLimits {
  readonly dailyRequestsPerPerson: number | null;
  readonly monthlyRequests: number | null;
  readonly monthlyTokens: number | null;
}

/** 組織連線這個月（組織的時區）全組織的用量；month 例如 2026-11。 */
export interface AiMonthUsage {
  readonly month: string;
  readonly requests: number;
  readonly tokens: number;
}

export interface AiSettings {
  readonly enabled: boolean;
  /** 成員能不能接自己的 AI 帳號（「我的帳號 → 我的 AI 帳號」）。和 enabled 互相獨立。 */
  readonly allowPersonalConnections: boolean;
  readonly default: AiSelection | null;
  readonly connections: readonly AiConnection[];
  readonly updatedAt: string | null;
  readonly updatedBy: PersonReference | null;
  readonly limits: AiUsageLimits;
  readonly monthUsage: AiMonthUsage;
}

export interface AiSettingsInput {
  readonly enabled: boolean;
  readonly allowPersonalConnections: boolean;
  readonly default: AiSelection | null;
  /** 不帶是沿用存的。 */
  readonly limits?: AiUsageLimits;
}

/** 要存的連線。apiKey：新增時 null 是沒有金鑰；修改時 null 是沿用存的，空字串是清掉。 */
export interface AiConnectionInput {
  readonly name: string;
  readonly kind: AiProviderKind;
  readonly baseUrl: string | null;
  readonly apiKey: string | null;
  readonly enabledModels: readonly string[];
  readonly customModels: readonly string[];
}

/** 「讀取模型」：還沒存的設定。修改既有的連線時帶 connectionId，apiKey 是 null 時用它存的金鑰。 */
export interface AiDiscoveryInput {
  readonly kind: AiProviderKind;
  readonly baseUrl: string | null;
  readonly apiKey: string | null;
  readonly connectionId: string | null;
}

export interface AiDiscoveredModel {
  readonly id: string;
  readonly displayName: string;
  readonly efforts: readonly AiEffort[];
}

export interface AiModelOption {
  readonly id: string;
  readonly displayName: string;
  readonly efforts: readonly AiEffort[];
  /** 不指定思考深度時實際用的等級（選單寫「預設（中）」）；不知道是 null。 */
  readonly defaultEffort: AiEffort | null;
}

export interface AiConnectionOption {
  readonly id: string;
  readonly name: string;
  readonly kind: AiOptionKind;
  readonly models: readonly AiModelOption[];
  /** 自己的連線（組織的在前、自己的在後）。 */
  readonly personal: boolean;
  /**
   * 組織擁有者開的主機來源（「主機上的 Claude Code」）：kind 是 claude_code 或 codex，id 是來源的編號。
   * 組織的成員都看得到，不看組織的 AI 開關與「允許成員使用自己的 AI 帳號」。internal/HANDOFF-host-agent.md。
   */
  readonly hosted: boolean;
  /** 主機來源現在不能用時給成員看的一句話；能用是 null。 */
  readonly notice: string | null;
}

/** 成員能選的 AI。available 是 false 時不顯示 AI 助理的入口。 */
export interface AiOptions {
  readonly available: boolean;
  readonly connections: readonly AiConnectionOption[];
  /** 組織的預設；組織沒開 AI 時是 null（只有自己的連線時選第一個）。 */
  readonly default: AiSelection | null;
  /** 組織允許成員接自己的 AI 帳號：沒有可用的模型時提示到「我的帳號」加。 */
  readonly canAddPersonal: boolean;
}

/** 「用 OpenRouter 登入」：開始時拿到要導去的網址；完成時拿到新的連線與它屬於誰。 */
export interface OpenRouterLoginStart {
  readonly authorizeUrl: string;
}

export interface OpenRouterLoginResult {
  readonly scope: AiConnectionScope;
  readonly connection: AiConnection;
}

/** 助理的工具名稱。網頁照名稱顯示說法（features/ai/aiLabels.ts）；不認得的名稱照原文顯示。 */
export type AssistantToolName =
  | 'get_blueprint'
  | 'list_templates'
  | 'get_template'
  | 'describe_usage'
  | 'preview_changeset'
  | 'propose_changeset';

export interface AssistantTextPart {
  readonly type: 'text';
  readonly text: string;
}

export interface AssistantToolPart {
  readonly type: 'tool';
  readonly callId: string;
  readonly name: string;
  readonly status: 'running' | 'done' | 'failed';
  /** propose_changeset 成功時，提出的變更集編號。 */
  readonly changeSetNumber: number | null;
  /** 失敗時給人看的原因。 */
  readonly error: string | null;
  /** propose_item_changes 成功時，建立的修改建議（畫成有「套用」「不要」的卡片）。 */
  readonly itemProposalId: string | null;
}

export type AssistantPart = AssistantTextPart | AssistantToolPart;

export interface AssistantUsage {
  readonly inputTokens: number;
  readonly outputTokens: number;
}

export interface AssistantMessage {
  readonly id: string;
  readonly role: 'user' | 'assistant';
  readonly createdAt: string;
  readonly parts: readonly AssistantPart[];
  /** 以下只有助理的回覆有，使用者的訊息是 null。 */
  readonly status: 'complete' | 'stopped' | 'failed' | null;
  readonly error: string | null;
  readonly selection: AiSelection | null;
  /** 當時的連線名稱（之後改名或刪掉，這裡還是當時的）。 */
  readonly connectionName: string | null;
  readonly usage: AssistantUsage | null;
  /** 使用者的訊息才有：送出時選的項目（畫面標「關於 WEB-12」）。 */
  readonly itemKey: string | null;
}

export interface AssistantConversationSummary {
  readonly id: string;
  /** 第一則訊息的前 40 個字；還沒送過訊息是 null（畫面寫「新的對話」）。 */
  readonly title: string | null;
  readonly messageCount: number;
  readonly isRunning: boolean;
  readonly createdAt: string;
  readonly updatedAt: string;
}

export interface AssistantConversation {
  readonly conversation: AssistantConversationSummary;
  readonly messages: readonly AssistantMessage[];
}

/** 送一則訊息。 */
export interface AssistantMessageInput {
  readonly text: string;
  readonly selection: AiSelection;
  /** 這則訊息針對的項目；沒有選是 null。 */
  readonly itemKey: string | null;
}

/** 送訊息的回應（text/event-stream）裡的事件。最後一定是 done。 */
export type AssistantStreamEvent =
  | { readonly type: 'start'; readonly userMessage: AssistantMessage; readonly assistantMessageId: string }
  | { readonly type: 'text'; readonly delta: string }
  | { readonly type: 'tool'; readonly tool: AssistantToolPart }
  /** 主機來源才有：還在排隊，position 是前面還有幾件再加一（1 是下一個）。位置變了才再送。 */
  | { readonly type: 'queued'; readonly position: number }
  /** 主機來源才有：輪到了，開始執行。 */
  | { readonly type: 'started' }
  | { readonly type: 'done'; readonly message: AssistantMessage };

/** 「我的帳號」列的一台電腦（本機代理登記過的）。 */
export interface LocalAgentMachine {
  readonly id: string;
  readonly name: string;
  readonly agentVersion: string;
  readonly engines: readonly LocalAgentEngineSummary[];
  /** 最近 60 秒內登記過。 */
  readonly online: boolean;
  readonly lastSeenAt: string;
}

export interface LocalAgentEngineSummary {
  readonly kind: LocalEngineKind;
  readonly version: string;
}

/** AI 助理對一個項目提出的修改建議（internal/HANDOFF-ai-items.md）：按「套用」才以自己的權限寫入。 */
export interface ItemProposal {
  readonly id: string;
  readonly projectKey: string;
  readonly itemKey: string;
  /** 建立建議時的標題。 */
  readonly itemTitle: string;
  readonly changes: readonly ItemFieldChange[];
  /** 要加的留言（Markdown）。 */
  readonly comment: string | null;
  readonly status: 'pending' | 'applied' | 'dismissed';
  readonly createdAt: string;
  readonly decidedAt: string | null;
  /** 套用時有步驟沒有成功：哪些沒有套用。 */
  readonly error: string | null;
}

/**
 * 一個欄位改成什麼。field：title、description、status、assignee、priority、startDate、dueDate、estimate、fields.<key>；
 * value 照欄位的型別，null 是清掉。
 */
export interface ItemFieldChange {
  readonly field: string;
  readonly value: unknown;
}
