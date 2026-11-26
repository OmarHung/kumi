import { http, HttpResponse } from 'msw';
import type {
  AiOptions,
  AiSelection,
  AssistantConversation,
  AssistantConversationSummary,
  AssistantMessage,
  AssistantMessageInput,
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

export function serveConversations(projectKey: string | null, conversations: readonly AssistantConversationSummary[]): void {
  server.use(http.get(base(projectKey), () => ok(conversations)));
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
