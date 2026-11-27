import { http, HttpResponse } from 'msw';
import { afterEach } from 'vitest';
import type {
  AiOptions,
  AiSelection,
  AssistantConversation,
  AssistantConversationListing,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantMessageInput,
  ItemBatchEntry,
  CommentProposal,
  ItemBatchProposal,
  ItemRestoreProposal,
  PlanningProposal,
  ProjectChangeProposal,
  ItemDeletionProposal,
  ItemProposal,
  MemberChange,
  MemberProposal,
  NewItemDraft,
  NewItemProposal,
  ProjectProposal,
} from '../api/aiTypes';
import { failure, ok } from './api';
import { server } from './server';

export const CLAUDE_SELECTION: AiSelection = { connectionId: 'conn-1', model: 'claude-opus-5-5', effort: 'high' };

/** 一個連線、兩個模型（Opus 能選思考深度，Haiku 不能）。 */
export function makeAiOptions(overrides: Partial<AiOptions> = {}): AiOptions {
  return {
    available: true,
    connections: [
      {
        id: 'conn-1',
        name: 'Anthropic',
        kind: 'anthropic',
        personal: false, hosted: false, notice: null,
        models: [
          { id: 'claude-opus-5-5', displayName: 'Claude Opus 5.5', efforts: ['low', 'medium', 'high'], defaultEffort: 'medium' },
          { id: 'claude-haiku-5', displayName: 'Claude Haiku 5', efforts: [], defaultEffort: null },
        ],
      },
    ],
    default: CLAUDE_SELECTION,
    canAddPersonal: false,
    ...overrides,
  };
}

export function serveAiOptions(options: AiOptions = makeAiOptions()): void {
  server.use(http.get('/v1/ai/options', () => ok(options)));
}

export function makeConversationSummary(overrides: Partial<AssistantConversationSummary> = {}): AssistantConversationSummary {
  return {
    id: 'conv-1',
    title: null,
    messageCount: 0,
    isRunning: false,
    createdAt: '2026-11-23T02:00:00Z',
    updatedAt: '2026-11-23T02:00:00Z',
    ...overrides,
  };
}

export function makeMessage(overrides: Partial<AssistantMessage> = {}): AssistantMessage {
  return {
    id: 'msg-1',
    role: 'user',
    createdAt: '2026-11-23T02:00:00Z',
    parts: [{ type: 'text', text: '你好' }],
    status: null,
    error: null,
    selection: null,
    connectionName: null,
    itemKey: null,
    usage: null,
    attachments: [],
    ...overrides,
  };
}

export function makeAssistantMessage(overrides: Partial<AssistantMessage> = {}): AssistantMessage {
  return makeMessage({
    id: 'msg-a1',
    role: 'assistant',
    parts: [{ type: 'text', text: '好的。' }],
    status: 'complete',
    selection: CLAUDE_SELECTION,
    connectionName: 'Anthropic',
    usage: { inputTokens: 10, outputTokens: 5 },
    ...overrides,
  });
}

/** projectKey 是 null：不選專案的組織對話。 */
const base = (projectKey: string | null) => (projectKey === null ? '/v1/assistant/conversations' : `/v1/projects/${projectKey}/assistant/conversations`);

/**
 * 跨專案的對話清單（浮動面板的「對話」選單，GET /v1/assistant/all-conversations）由各邊的清單合起來、新的在前：
 * 測試照舊用 serveConversations／serveConversationList 設定每一邊，兩個端點讀到的永遠一致。
 */
const listedByScope = new Map<string | null, () => readonly AssistantConversationSummary[]>();

afterEach(() => {
  listedByScope.clear();
});

function allListings(): readonly AssistantConversationListing[] {
  return [...listedByScope.entries()]
    .flatMap(([projectKey, source]) => source().map((conversation) => ({ ...conversation, projectKey })))
    .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
}

/** 一邊的清單內容由 source 決定（每次讀都重算：測試中途改內容用這個）。 */
export function serveConversationList(projectKey: string | null, source: () => readonly AssistantConversationSummary[]): void {
  listedByScope.set(projectKey, source);
  server.use(
    http.get(base(projectKey), () => ok(source())),
    http.get('/v1/assistant/all-conversations', () => ok(allListings())),
  );
}

export function serveConversations(projectKey: string | null, conversations: readonly AssistantConversationSummary[]): void {
  serveConversationList(projectKey, () => conversations);
}

/** GET 一則對話；回傳讀了幾次。 */
export function serveConversation(projectKey: string | null, conversation: AssistantConversation) {
  let reads = 0;
  server.use(
    http.get(`${base(projectKey)}/${conversation.conversation.id}`, () => {
      reads += 1;
      return ok(conversation);
    }),
  );
  return { reads: () => reads };
}

/** POST 開一則新的對話，回傳記下的次數。 */
export function serveCreateConversation(projectKey: string | null, created: AssistantConversationSummary) {
  const calls: unknown[] = [];
  server.use(
    http.post(base(projectKey), async ({ request }) => {
      calls.push(await request.text());
      return ok(created, 201);
    }),
  );
  return calls;
}

export function serveDeleteConversation(projectKey: string | null, id: string, reply = () => HttpResponse.json(null, { status: 204 })) {
  const deleted: string[] = [];
  server.use(
    http.delete(`${base(projectKey)}/${id}`, () => {
      deleted.push(id);
      return reply();
    }),
  );
  return deleted;
}

/** 一個還開著的串流：測試一段一段送 SSE 的文字進去，最後 close。 */
export interface StreamHandle {
  /** 送一個事件（event: 與 data: 兩行加空行）。 */
  readonly event: (name: string, data: unknown) => void;
  /** 照原樣送一段文字（心跳、被切開的事件）。 */
  readonly raw: (text: string) => void;
  readonly close: () => void;
}

export interface RecordedSend {
  readonly body: AssistantMessageInput;
  readonly headers: Headers;
}

export function sseEvent(name: string, data: unknown): string {
  return `event: ${name}\ndata: ${JSON.stringify(data)}\n\n`;
}

/**
 * 假的 POST …/messages：回 text/event-stream，內容由 script 一段一段送（拿到的 handle 可以留著之後再送）。
 * 請求被取消（停止）時串流關上。回傳記下的請求與目前的串流。
 */
export function serveReply(projectKey: string | null, id: string, script: (stream: StreamHandle, body: AssistantMessageInput) => void) {
  const sent: RecordedSend[] = [];
  let current: StreamHandle | null = null;
  server.use(
    http.post(`${base(projectKey)}/${id}/messages`, async ({ request }) => {
      const body = (await request.json()) as AssistantMessageInput;
      sent.push({ body, headers: request.headers });
      const encoder = new TextEncoder();
      let isClosed = false;
      const stream = new ReadableStream<Uint8Array>({
        start(controller) {
          const handle: StreamHandle = {
            event: (name, data) => {
              if (!isClosed) controller.enqueue(encoder.encode(sseEvent(name, data)));
            },
            raw: (text) => {
              if (!isClosed) controller.enqueue(encoder.encode(text));
            },
            close: () => {
              if (isClosed) return;
              isClosed = true;
              controller.close();
            },
          };
          current = handle;
          request.signal.addEventListener('abort', handle.close);
          script(handle, body);
        },
      });
      return new HttpResponse(stream, { headers: { 'Content-Type': 'text/event-stream; charset=utf-8' } });
    }),
  );
  return { sent, stream: () => current };
}

/** 送訊息被擋下（串流開始之前）：一般的 JSON 錯誤。 */
export function rejectReply(projectKey: string | null, id: string, status: number, code: string, message: string): void {
  server.use(http.post(`${base(projectKey)}/${id}/messages`, () => failure(status, code, message)));
}

/** start → text → done 的標準回覆。 */
export function completeReply(handle: StreamHandle, text: string, user = makeMessage(), assistant = makeAssistantMessage()): void {
  handle.event('start', { userMessage: user, assistantMessageId: assistant.id });
  handle.event('text', { delta: text });
  handle.event('done', { message: { ...assistant, parts: [{ type: 'text', text }] } });
  handle.close();
}

export function makeItemProposal(overrides: Partial<ItemProposal> = {}): ItemProposal {
  return {
    id: 'prop-1',
    projectKey: 'WEB',
    itemKey: 'WEB-12',
    itemTitle: '修正登入頁',
    changes: [{ field: 'status', value: 'done' }],
    comment: null,
    replyTo: null,
    status: 'pending',
    createdAt: '2026-11-23T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

export interface ProposalBackend {
  readonly applied: () => number;
  readonly dismissed: () => number;
}

/**
 * 假的修改建議端點：GET 回目前的狀態；apply／dismiss 把狀態換掉並回傳（可以給 onApply 回失敗）。
 * 回傳各自被呼叫的次數。
 */
export function serveItemProposal(
  proposal: ItemProposal,
  onApply: () => Response | null = () => null,
): ProposalBackend {
  let current = proposal;
  let applied = 0;
  let dismissed = 0;
  const path = `/v1/ai/item-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applied += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = { ...current, status: 'applied', decidedAt: '2026-11-23T02:05:00Z' };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissed += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-23T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applied, dismissed: () => dismissed };
}

export function makeProjectProposal(overrides: Partial<ProjectProposal> = {}): ProjectProposal {
  return {
    id: 'pp-1',
    key: 'WEB',
    name: '網站改版',
    template: 'simple_tasks',
    templateName: '簡單任務',
    includeExamples: false,
    description: null,
    status: 'pending',
    createdAt: '2026-11-26T02:00:00Z',
    decidedAt: null,
    createdProjectKey: null,
    ...overrides,
  };
}

/**
 * 假的新專案建議端點：GET 回目前的狀態；apply 成功時換成 applied（帶 createdProjectKey），可以用 onApply 回失敗；dismiss 換成 dismissed。
 * 回傳各自被呼叫的次數。
 */
export function serveProjectProposal(proposal: ProjectProposal, onApply: () => Response | null = () => null): ProposalBackend {
  let current = proposal;
  let applied = 0;
  let dismissed = 0;
  const path = `/v1/ai/project-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applied += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = { ...current, status: 'applied', decidedAt: '2026-11-26T02:05:00Z', createdProjectKey: current.key };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissed += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-26T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applied, dismissed: () => dismissed };
}

export function makeNewItemDraft(overrides: Partial<NewItemDraft> = {}): NewItemDraft {
  return {
    type: 'task',
    title: '盤點 GCP 資源',
    ref: null,
    description: null,
    assigneeId: null,
    priority: null,
    startDate: null,
    dueDate: null,
    estimate: null,
    parentKey: null,
    parentRef: null,
    cycleId: null,
    milestoneId: null,
    fields: {},
    ...overrides,
  };
}

export function makeNewItemProposal(overrides: Partial<NewItemProposal> = {}): NewItemProposal {
  const items = overrides.items ?? [makeNewItemDraft(), makeNewItemDraft({ title: '遷移資料庫' })];
  return {
    id: 'nip-1',
    projectKey: 'SMK',
    items,
    createdKeys: items.map(() => null),
    status: 'pending',
    createdAt: '2026-11-26T02:00:00Z',
    decidedAt: null,
    error: null,
    addMembers: [],
    ...overrides,
  };
}

/**
 * 假的新增項目建議端點：GET 回目前的狀態；apply 成功時依序編 SMK-13 起的編號並換成 applied（可用 applied 蓋掉結果，例如部分失敗帶 error），
 * 可以用 onApply 回失敗；dismiss 換成 dismissed。回傳各自被呼叫的次數。
 */
export function serveNewItemProposal(
  proposal: NewItemProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<NewItemProposal> = {},
): ProposalBackend {
  let current = proposal;
  let applyCount = 0;
  let dismissCount = 0;
  const path = `/v1/ai/new-item-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applyCount += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = {
        ...current,
        status: 'applied',
        decidedAt: '2026-11-26T02:05:00Z',
        createdKeys: current.items.map((_, index) => `${current.projectKey}-${13 + index}`),
        ...applied,
      };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissCount += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-26T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applyCount, dismissed: () => dismissCount };
}

export function makeMemberChange(overrides: Partial<MemberChange> = {}): MemberChange {
  return {
    personId: 'p-2',
    displayName: '小美',
    role: 'contributor',
    roleName: '執行者',
    previousRole: null,
    previousRoleName: null,
    ...overrides,
  };
}

export function makeMemberProposal(overrides: Partial<MemberProposal> = {}): MemberProposal {
  return {
    id: 'mp-1',
    projectKey: 'SMK',
    changes: [makeMemberChange()],
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

/** 假的成員建議端點：GET 回目前的狀態；apply 成功時換成 applied（applied 可蓋掉結果，例如帶 error），onApply 可回失敗；dismiss 換成 dismissed。 */
export function serveMemberProposal(
  proposal: MemberProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<MemberProposal> = {},
): ProposalBackend {
  let current = proposal;
  let applyCount = 0;
  let dismissCount = 0;
  const path = `/v1/ai/member-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applyCount += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = { ...current, status: 'applied', decidedAt: '2026-11-27T02:05:00Z', ...applied };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissCount += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applyCount, dismissed: () => dismissCount };
}

export function makeItemDeletionProposal(overrides: Partial<ItemDeletionProposal> = {}): ItemDeletionProposal {
  return {
    id: 'dp-1',
    projectKey: 'SMK',
    items: [
      { itemKey: 'SMK-45', title: '重複的任務甲' },
      { itemKey: 'SMK-46', title: '重複的任務乙' },
    ],
    reason: null,
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    deletedKeys: [],
    error: null,
    ...overrides,
  };
}

/** 假的刪除建議端點：apply 成功時 deletedKeys 預設是全部的項目（applied 可蓋掉，例如部分失敗帶 error）；onApply 可回失敗。 */
export function serveItemDeletionProposal(
  proposal: ItemDeletionProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<ItemDeletionProposal> = {},
): ProposalBackend {
  let current = proposal;
  let applyCount = 0;
  let dismissCount = 0;
  const path = `/v1/ai/item-deletion-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applyCount += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = {
        ...current,
        status: 'applied',
        decidedAt: '2026-11-27T02:05:00Z',
        deletedKeys: current.items.map((item) => item.itemKey),
        ...applied,
      };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissCount += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applyCount, dismissed: () => dismissCount };
}

export function makeItemBatchEntry(overrides: Partial<ItemBatchEntry> = {}): ItemBatchEntry {
  return {
    itemKey: 'SMK-12',
    itemTitle: '修正登入頁',
    changes: [{ field: 'dueDate', value: '2026-12-15' }],
    comment: null,
    replyTo: null,
    error: null,
    ...overrides,
  };
}

export function makeItemBatchProposal(overrides: Partial<ItemBatchProposal> = {}): ItemBatchProposal {
  return {
    id: 'bp-1',
    projectKey: 'SMK',
    items: [makeItemBatchEntry(), makeItemBatchEntry({ itemKey: 'SMK-13', itemTitle: '改版首頁' })],
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

/** 假的批次修改建議端點：apply 成功時換成 applied（applied 可蓋掉結果，例如某一筆帶 error）；onApply 可回失敗。 */
export function serveItemBatchProposal(
  proposal: ItemBatchProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<ItemBatchProposal> = {},
): ProposalBackend {
  let current = proposal;
  let applyCount = 0;
  let dismissCount = 0;
  const path = `/v1/ai/item-batch-proposals/${proposal.id}`;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applyCount += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = { ...current, status: 'applied', decidedAt: '2026-11-27T02:05:00Z', ...applied };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissCount += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applyCount, dismissed: () => dismissCount };
}

/** 四種新建議（週期與里程碑、專案、留言、復原項目）共用的假端點：apply 成功時換成 applied（applied 可蓋掉結果）；onApply 可回失敗。 */
function serveProposal<T extends { readonly id: string; readonly status: string }>(
  path: string,
  proposal: T,
  onApply: () => Response | null,
  applied: Partial<T>,
): ProposalBackend {
  let current = proposal;
  let applyCount = 0;
  let dismissCount = 0;
  server.use(
    http.get(path, () => ok(current)),
    http.post(`${path}/apply`, () => {
      applyCount += 1;
      const failed = onApply();
      if (failed !== null) return failed;
      current = { ...current, status: 'applied', decidedAt: '2026-11-27T02:05:00Z', ...applied };
      return ok(current);
    }),
    http.post(`${path}/dismiss`, () => {
      dismissCount += 1;
      current = { ...current, status: 'dismissed', decidedAt: '2026-11-27T02:05:00Z' };
      return ok(current);
    }),
  );
  return { applied: () => applyCount, dismissed: () => dismissCount };
}

export function makePlanningProposal(overrides: Partial<PlanningProposal> = {}): PlanningProposal {
  return {
    id: 'pl-1',
    projectKey: 'SMK',
    changes: [
      {
        target: 'cycle',
        action: 'create',
        id: null,
        before: null,
        after: { name: '十二月週期', startDate: '2026-12-01', endDate: '2026-12-14', dueDate: null, description: null },
        itemCount: null,
        error: null,
      },
      {
        target: 'milestone',
        action: 'delete',
        id: 'ms-1',
        before: { name: '第一版上線', startDate: null, endDate: null, dueDate: '2026-12-31', description: null },
        after: null,
        itemCount: 3,
        error: null,
      },
    ],
    reason: null,
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

export function servePlanningProposal(
  proposal: PlanningProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<PlanningProposal> = {},
): ProposalBackend {
  return serveProposal(`/v1/ai/planning-proposals/${proposal.id}`, proposal, onApply, applied);
}

export function makeProjectChangeProposal(overrides: Partial<ProjectChangeProposal> = {}): ProjectChangeProposal {
  return {
    id: 'pc-1',
    projectKey: 'SMK',
    name: '新的名稱',
    archived: null,
    changesDescription: false,
    description: null,
    before: { name: '舊的名稱', archived: false, description: null, descriptionUpdatedAt: null },
    reason: null,
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

export function serveProjectChangeProposal(
  proposal: ProjectChangeProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<ProjectChangeProposal> = {},
): ProposalBackend {
  return serveProposal(`/v1/ai/project-change-proposals/${proposal.id}`, proposal, onApply, applied);
}

export function makeCommentProposal(overrides: Partial<CommentProposal> = {}): CommentProposal {
  return {
    id: 'cm-1',
    projectKey: 'SMK',
    changes: [
      {
        itemKey: 'SMK-12',
        commentId: 'c-1',
        action: 'edit',
        authorName: '王小明',
        commentedAt: '2026-11-26T03:00:00Z',
        excerpt: '先等客戶回覆',
        body: '客戶已經回覆，可以開始了',
        error: null,
      },
      {
        itemKey: 'SMK-13',
        commentId: 'c-2',
        action: 'delete',
        authorName: '王小明',
        commentedAt: '2026-11-26T04:00:00Z',
        excerpt: '貼錯地方了',
        body: null,
        error: null,
      },
    ],
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    error: null,
    ...overrides,
  };
}

export function serveCommentProposal(
  proposal: CommentProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<CommentProposal> = {},
): ProposalBackend {
  return serveProposal(`/v1/ai/comment-proposals/${proposal.id}`, proposal, onApply, applied);
}

export function makeItemRestoreProposal(overrides: Partial<ItemRestoreProposal> = {}): ItemRestoreProposal {
  return {
    id: 'ir-1',
    projectKey: 'SMK',
    items: [
      { itemKey: 'SMK-45', title: '重複的任務甲', deletedAt: '2026-11-26T05:00:00Z' },
      { itemKey: 'SMK-46', title: '重複的任務乙', deletedAt: '2026-11-26T05:00:00Z' },
    ],
    reason: null,
    status: 'pending',
    createdAt: '2026-11-27T02:00:00Z',
    decidedAt: null,
    restoredKeys: [],
    error: null,
    ...overrides,
  };
}

export function serveItemRestoreProposal(
  proposal: ItemRestoreProposal,
  onApply: () => Response | null = () => null,
  applied: Partial<ItemRestoreProposal> = {},
): ProposalBackend {
  return serveProposal(`/v1/ai/item-restore-proposals/${proposal.id}`, proposal, onApply, {
    restoredKeys: proposal.items.map((item) => item.itemKey),
    ...applied,
  });
}
