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
  | 'propose_changeset'
  | 'list_projects'
  | 'propose_project';

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
  /** propose_project 成功時，建立的新專案建議（畫成有「建立專案」「不要」的卡片）。 */
  readonly projectProposalId: string | null;
  /** propose_new_items 成功時，建立的新增項目建議（畫成有「全部建立」「不要」的卡片）。 */
  readonly newItemProposalId: string | null;
  /** propose_member_changes 成功時，建立的成員建議（畫成有「套用」「不要」的卡片；internal/HANDOFF-ai-assign.md）。 */
  readonly memberProposalId: string | null;
}

export type AssistantPart = AssistantTextPart | AssistantToolPart;

export interface AssistantUsage {
  readonly inputTokens: number;
  readonly outputTokens: number;
}

/** 附件的種類：看內容判斷。image 與 pdf 只有 API 的模型讀得到，text 所有來源都讀得到。 */
export type AssistantAttachmentKind = 'image' | 'pdf' | 'text';

/** 助理訊息的附件（待送的或已經送出的）。內容在 GET /v1/projects/{key}/assistant/attachments/{id}/content。 */
export interface AssistantAttachment {
  readonly id: string;
  readonly fileName: string;
  /** image/png、image/jpeg、image/gif、image/webp、application/pdf、text/plain。 */
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly kind: AssistantAttachmentKind;
  readonly uploadedAt: string;
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
  /** 使用者的訊息才有：送出時帶的附件，依順序；助理的回覆是空陣列。 */
  readonly attachments: readonly AssistantAttachment[];
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

/** 跨專案的對話清單（浮動面板的「對話」選單）的一則：多了它屬於哪個專案；null 是不選專案的組織對話。 */
export interface AssistantConversationListing extends AssistantConversationSummary {
  readonly projectKey: string | null;
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
  /** 待送附件的編號（上傳時拿到的），依順序，最多 5 個；沒有是空陣列。有附件時 text 可以是空的。 */
  readonly attachmentIds: readonly string[];
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

/**
 * AI 助理提出的新增項目建議（internal/HANDOFF-ai-new-items.md）：按「全部建立」才以自己的身分依序建立。
 * status 是 applying 時正在建立（另一個分頁按的）；建立的過程中斷了會讀成 applied 加 error。
 */
export interface NewItemProposal {
  readonly id: string;
  readonly projectKey: string;
  readonly items: readonly NewItemDraft[];
  /** 和 items 一一對應：已經建立的項目編號，沒建立是 null。 */
  readonly createdKeys: readonly (string | null)[];
  readonly status: 'pending' | 'applying' | 'applied' | 'dismissed';
  readonly createdAt: string;
  readonly decidedAt: string | null;
  /** 有項目沒建立成功、或建立的過程中斷：哪些沒建立。 */
  readonly error: string | null;
  /** 建立項目之前先加進專案的人（負責人還不是成員時）；套用時先加人、再建項目。 */
  readonly addMembers: readonly MemberAddition[];
}

/** 新增項目的建議裡，建立項目之前要先加進專案的一個人。名稱是建立建議時的。 */
export interface MemberAddition {
  readonly personId: string;
  readonly role: string;
  readonly displayName: string | null;
  readonly roleName: string | null;
}

/**
 * AI 助理提出的專案成員建議（internal/HANDOFF-ai-assign.md）：加入、改角色、移出，按「套用」才以自己的身分寫入。
 * 名稱都是建立建議時的。
 */
export interface MemberProposal {
  readonly id: string;
  readonly projectKey: string;
  readonly changes: readonly MemberChange[];
  readonly status: 'pending' | 'applied' | 'dismissed';
  readonly createdAt: string;
  readonly decidedAt: string | null;
  /** 有幾筆沒套用成功：哪些沒套用。 */
  readonly error: string | null;
}

/** 一筆成員的變動：role 是 null 是移出；previousRole 是 null 是還不是成員（加入）。 */
export interface MemberChange {
  readonly personId: string;
  readonly displayName: string;
  readonly role: string | null;
  readonly roleName: string | null;
  readonly previousRole: string | null;
  readonly previousRoleName: string | null;
}

/** 建議新增的一個項目；狀態一律從流程的第一個狀態開始。 */
export interface NewItemDraft {
  /** 項目類型的 key。 */
  readonly type: string;
  readonly title: string;
  /** 同一份建議裡的代稱，讓後面的項目掛在它底下。 */
  readonly ref: string | null;
  /** Markdown。 */
  readonly description: string | null;
  /** 人員編號。 */
  readonly assigneeId: string | null;
  readonly priority: 'none' | 'low' | 'medium' | 'high' | 'urgent' | null;
  /** yyyy-MM-dd。 */
  readonly startDate: string | null;
  readonly dueDate: string | null;
  readonly estimate: number | null;
  /** 掛在既有的項目底下（編號）。 */
  readonly parentKey: string | null;
  /** 掛在同一份建議裡前面的項目底下（它的 ref）。 */
  readonly parentRef: string | null;
  /** 排進的週期（編號）。 */
  readonly cycleId: string | null;
  /** 歸屬的里程碑（編號）。 */
  readonly milestoneId: string | null;
  /** 自訂欄位 {key: 值}。 */
  readonly fields: Readonly<Record<string, unknown>>;
}

/** AI 助理（不選專案的對話）提出的新專案建議（internal/HANDOFF-org-assistant.md 第 4 節）：按「建立專案」才以自己的身分建立。 */
export interface ProjectProposal {
  readonly id: string;
  readonly key: string;
  readonly name: string;
  /** 內建模板的代號。 */
  readonly template: string;
  readonly templateName: string;
  readonly includeExamples: boolean;
  /** 專案說明（Markdown）；沒有是 null。 */
  readonly description: string | null;
  readonly status: 'pending' | 'applied' | 'dismissed';
  readonly createdAt: string;
  readonly decidedAt: string | null;
  /** 套用成功後建出來的專案代號。 */
  readonly createdProjectKey: string | null;
}
