import type { ActivityView } from './activityPages';
import type { ItemQuery } from './types';

/** 看板欄的查詢鍵的最後一段。 */
export const BOARD_COLUMN = 'board-column';

/** 行事曆與時間軸的清單的查詢鍵的最後一段（含兩邊的「沒有到期日」「沒有日期」）。 */
export const DATE_VIEW = 'date-view';

/**
 * TanStack Query 的鍵集中在這裡，失效（invalidate）時才不會拼錯。
 * 前綴有意義：invalidate(['project', key]) 會連那個專案的項目清單一起重抓。
 */
export const queryKeys = {
  session: ['session'],
  templates: ['templates'],
  projects: ['projects'],
  project: (projectKey: string) => ['project', projectKey],
  // 沒給 query 時是 {}：TanStack Query 的部分比對把 {} 當成「任何條件」，可以拿來對整個專案的項目清單做事。
  items: (projectKey: string, query: ItemQuery = {}) => ['project', projectKey, 'items', query],
  // 看板的一欄也是項目清單（前綴相同，對整個專案的項目清單做的事也包含它），最後多一段標記：
  // 改了狀態的項目要從舊的欄搬到新的欄，表格的清單則是在原位換掉（itemCache.ts）。
  boardColumn: (projectKey: string, query: ItemQuery) => ['project', projectKey, 'items', query, BOARD_COLUMN],
  // 行事曆與時間軸的清單：改了日期、狀態的項目可能進出這段時間，不能像表格那樣在原位留著，
  // 動到條件就整份重抓（itemCache.ts）。
  dateView: (projectKey: string, query: ItemQuery) => ['project', projectKey, 'items', query, DATE_VIEW],
  parentCandidates: (projectKey: string, types: readonly string[] = []) => [
    'project',
    projectKey,
    'parent-candidates',
    types,
  ],
  // 已刪除的項目放在專案底下（不在 'items' 底下：項目清單的快取操作不會碰到它）；刪除、復原之後讓它失效。
  deletedItems: (projectKey: string) => ['project', projectKey, 'deleted-items'],
  // 個人的檢視放在專案底下：專案失效時一起重抓。
  savedViews: (projectKey: string) => ['project', projectKey, 'saved-views'],
  // 週期與里程碑也放在專案底下：專案失效時一起重抓。
  cycles: (projectKey: string) => ['project', projectKey, 'cycles'],
  milestones: (projectKey: string) => ['project', projectKey, 'milestones'],
  // 專案說明也放在專案底下；說明格式是組織的，不跟著專案。
  projectDescription: (projectKey: string) => ['project', projectKey, 'description'],
  descriptionTemplates: ['description-templates'],
  // 組織的寄信設定（管理員）與自己的 Email 通知開關（我的帳號）。
  // 授權過的 AI 代理（OAuth 授權）：我的帳號的清單。
  oauthGrants: ['account', 'oauth-grants'],
  // 稽核紀錄（只有擁有者）：組織層級的，不跟著專案。
  auditEvents: ['audit-events'],
  emailSettings: ['email-settings'],
  emailPreference: ['account', 'email-notifications'],
  // 組織的 AI 設定（管理員）與成員能選的 AI；存了設定之後兩個都要失效（ai 前綴）。
  aiSettings: ['ai', 'settings'],
  aiOptions: ['ai', 'options'],
  // 擁有者的主機 AI 代理（代理、引擎、來源）與來源的月用量：改了之後 aiOptions 也要失效（成員的模型選單）。
  hostAgents: ['ai', 'host'],
  hostSourceUsage: (sourceId: string, month: string | null) => ['ai', 'host', 'usage', sourceId, month ?? 'current'],
  // 自己的 AI 帳號（我的帳號）：存了之後也要讓 aiOptions 失效。
  personalAiConnections: ['account', 'ai-connections'],
  // 自己電腦上的本機代理（我的帳號）：移除之後也要讓 aiOptions 失效（模型選單裡的引擎）。
  localAgents: ['account', 'local-agents'],
  // 伺服器提供下載的 kumi 執行檔（我的帳號的「命令列工具」）。
  cliCatalog: ['cli'],
  // AI 助理的對話放在專案底下，只有自己的。清單與一則對話各一份快取。
  // projectKey 是 null：不選專案的組織對話，不掛在 ['project', key] 底下（專案的快取失效、改名不牽動它）。
  assistantConversations: (projectKey: string | null) =>
    projectKey === null ? ['ai', 'assistant', 'conversations'] : ['project', projectKey, 'assistant', 'conversations'],
  assistantConversation: (projectKey: string | null, conversationId: string) => [
    ...(projectKey === null ? ['ai', 'assistant', 'conversations'] : ['project', projectKey, 'assistant', 'conversations']),
    conversationId,
  ],
  // 助理選項目時的搜尋結果（只有這個專案的項目）；和項目清單的鍵分開：那邊是分頁的查詢，形狀不同。
  assistantItemChoices: (projectKey: string, text: string) => ['project', projectKey, 'assistant-items', text],
  // 套用建議之後讓這個專案所有的搜尋結果一起失效（標題、狀態可能變了）。
  assistantItemChoiceLists: (projectKey: string) => ['project', projectKey, 'assistant-items'],
  // 助理提出的項目修改建議（套用、不要之後換成伺服器回的新狀態）。
  itemProposal: (proposalId: string) => ['ai', 'item-proposals', proposalId],
  // 助理提出的新專案建議。
  projectProposal: (proposalId: string) => ['ai', 'project-proposals', proposalId],
  // 助理提出的新增項目建議。
  newItemProposal: (proposalId: string) => ['ai', 'new-item-proposals', proposalId],
  // 助理提出的成員建議。
  memberProposal: (proposalId: string) => ['ai', 'member-proposals', proposalId],
  // 變更集放在專案底下：套用之後讓整個專案失效時，清單與每一份的內容也一起重抓。
  changeSets: (projectKey: string) => ['project', projectKey, 'changesets'],
  changeSet: (projectKey: string, number: number) => ['project', projectKey, 'changesets', number],
  // 知識庫（internal/HANDOFF-knowledge-base.md）：空間清單、一個空間（樹、垃圾桶、頁面都放在它底下，空間失效時一起重抓）。
  // 組織的儲存空間用量（只有管理員）。
  storageUsage: ['storage-usage'],
  knowledgeSpaces: ['knowledge', 'spaces'],
  knowledgeSpace: (spaceKey: string) => ['knowledge', 'space', spaceKey],
  knowledgeTree: (spaceKey: string) => ['knowledge', 'space', spaceKey, 'tree'],
  knowledgeTrash: (spaceKey: string) => ['knowledge', 'space', spaceKey, 'trash'],
  knowledgePage: (spaceKey: string, pageNumber: number) => ['knowledge', 'space', spaceKey, 'page', pageNumber],
  knowledgePageRestriction: (spaceKey: string, pageNumber: number) => [
    'knowledge',
    'space',
    spaceKey,
    'page',
    pageNumber,
    'restriction',
  ],
  knowledgePageAttachments: (spaceKey: string, pageNumber: number) => [
    'knowledge',
    'space',
    spaceKey,
    'page',
    pageNumber,
    'attachments',
  ],
  item: (itemKey: string) => ['item', itemKey],
  // 前綴：讓活動失效、重抓時兩份（留言、全部）一起處理；讀資料用下面那個，各自一份快取（條件換了要從第一頁讀）。
  activity: (itemKey: string) => ['item', itemKey, 'activity'],
  activityView: (itemKey: string, view: ActivityView) => ['item', itemKey, 'activity', view],
  // 留言的版本放在活動的前綴底下：活動失效（含即時更新）時，開著的版本清單一起重抓。
  commentRevisions: (itemKey: string, commentId: string) => ['item', itemKey, 'activity', 'revisions', commentId],
  // 附件清單在項目底下：項目被刪除、換成別人看時，跟著那個項目一起清掉。
  attachments: (itemKey: string) => ['item', itemKey, 'attachments'],
  // 專案總覽（跨專案的數字）：任何專案的項目變了就讓整組失效（features/live/liveInvalidation.ts）。
  overview: (scope: string) => ['overview', scope],
  overviews: ['overview'],
  myWork: ['my-work'],
  // 「我的工作」四種版面的清單（跨專案，GET /v1/items）放在 my-work 底下：項目改了讓 myWork 失效時一起重抓。
  // 側欄的數量用的是上面那一個（完全相同的鍵），兩邊的資料形狀不同，不能共用。
  // 這三種清單共同的前綴：項目在面板裡改了、刪除時，對底下每一份清單做事（itemCache.ts）。
  myWorkLists: ['my-work', 'items'],
  myWorkItems: (query: ItemQuery) => ['my-work', 'items', query],
  // 「我的工作」的看板與行事曆、時間軸：和專案的一樣，最後多一段標記，改了狀態、日期的項目整份重抓。
  myWorkBoard: (query: ItemQuery) => ['my-work', 'items', query, BOARD_COLUMN],
  myWorkDateView: (query: ItemQuery) => ['my-work', 'items', query, DATE_VIEW],
  // 個人的「我的工作」檢視：不在 my-work 底下，項目改了不必重抓它。
  myWorkViews: ['saved-views', 'my-work'],
  itemTypes: ['item-types'],
  searchItems: (text: string, limit: number) => ['search', 'items', text, limit],
  searchProjects: (text: string, limit: number) => ['search', 'projects', text, limit],
  // 通知：清單（未讀、全部各一份）與未讀數放在同一個前綴底下，標成已讀時一起處理（notificationCache.ts）。
  notifications: (unread: boolean) => ['notifications', 'list', unread ? 'unread' : 'all'],
  notificationLists: ['notifications', 'list'],
  unreadCount: ['notifications', 'unread-count'],
  // 關注狀態在項目底下：項目被刪除、換成別人看時，跟著那個項目一起清掉。
  itemSubscription: (itemKey: string) => ['item', itemKey, 'subscription'],
  people: ['people'],
  accessTokens: ['access-tokens'],
} as const;
