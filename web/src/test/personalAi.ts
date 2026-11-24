import { http } from 'msw';
import type {
  AiConnection,
  AiConnectionInput,
  AiConnectionScope,
  AiDiscoveredModel,
  AiDiscoveryInput,
  LocalAgentMachine,
} from '../api/aiTypes';
import { exampleConnection, type Rejection } from './aiSettings';
import { failure, ok } from './api';
import { server } from './server';

const UPDATED_AT = '2026-11-23T03:00:00+00:00';
const SELF = { personId: 'p-owner', displayName: '範例擁有者' } as const;

/** 自己的連線的假資料：和組織的一樣的形狀，名稱與編號不同。 */
export function examplePersonalConnection(overrides: Partial<AiConnection> = {}): AiConnection {
  return exampleConnection({ id: 'pc-1', name: '我的 OpenRouter', kind: 'openrouter', apiKeyHint: 'f9e8', ...overrides });
}

type Operation = 'connection' | 'refresh' | 'discover' | 'delete' | 'login' | 'complete';

export interface PersonalAiServer {
  readonly savedConnections: { readonly id: string | null; readonly input: AiConnectionInput }[];
  readonly discoveries: AiDiscoveryInput[];
  readonly deleted: string[];
  readonly refreshed: string[];
  /** 「用 OpenRouter 登入」開始時收到的 scope（照順序）。 */
  readonly loginScopes: AiConnectionScope[];
  /** 登入完成時收到的 { code, state }。 */
  readonly completions: { readonly code: string; readonly state: string }[];
  readonly current: () => readonly AiConnection[];
  readonly rejectNext: (operation: Operation, error: Rejection) => void;
}

export interface PersonalAiOptions {
  readonly connections?: readonly AiConnection[];
  /** 開始登入回的網址；預設是 OpenRouter 的授權頁。 */
  readonly authorizeUrl?: string;
  /** 完成登入時建出來的連線屬於誰（預設個人的）。 */
  readonly completedScope?: AiConnectionScope;
}

const DEFAULT_AUTHORIZE_URL = 'https://openrouter.ai/auth?callback_url=example&state=s1';
const DISCOVERED: readonly AiDiscoveredModel[] = [{ id: 'openai/gpt-5', displayName: 'GPT-5', efforts: ['low', 'high'] }];

/** 有狀態的自己的 AI 帳號與 OpenRouter 登入端點（internal/HANDOFF-ai-floating.md 第 4.4 節）。 */
export function servePersonalAi({ connections = [], authorizeUrl = DEFAULT_AUTHORIZE_URL, completedScope = 'personal' }: PersonalAiOptions = {}): PersonalAiServer {
  let current: readonly AiConnection[] = connections;
  let counter = 0;
  const rejections = new Map<Operation, Rejection>();
  const savedConnections: { id: string | null; input: AiConnectionInput }[] = [];
  const discoveries: AiDiscoveryInput[] = [];
  const deleted: string[] = [];
  const refreshed: string[] = [];
  const loginScopes: AiConnectionScope[] = [];
  const completions: { code: string; state: string }[] = [];

  function take(operation: Operation): Rejection | undefined {
    const error = rejections.get(operation);
    rejections.delete(operation);
    return error;
  }
  const reject = ({ status, code, message, details = [] }: Rejection) => failure(status, code, message, details);
  const find = (id: unknown) => current.find((connection) => connection.id === id);
  const notFound = () => failure(404, 'ai.connection_not_found', '找不到這個連線。');
  const fromInput = (id: string, input: AiConnectionInput, base: AiConnection): AiConnection => ({
    ...base,
    id,
    name: input.name,
    kind: input.kind,
    baseUrl: input.baseUrl,
    hasApiKey: input.apiKey === null ? base.hasApiKey : input.apiKey !== '',
    apiKeyHint: input.apiKey === null ? base.apiKeyHint : input.apiKey.slice(-4) || null,
    models: input.enabledModels.map((model) => ({ id: model, displayName: model, efforts: [], enabled: true, custom: input.customModels.includes(model) })),
    updatedAt: UPDATED_AT,
    updatedBy: SELF,
  });

  server.use(
    http.get('/v1/account/ai-connections', () => ok(current)),
    http.post('/v1/account/ai-connections', async ({ request }) => {
      const input = (await request.json()) as AiConnectionInput;
      savedConnections.push({ id: null, input });
      const error = take('connection');
      if (error !== undefined) return reject(error);
      counter += 1;
      const saved = fromInput(`pc-new-${String(counter)}`, input, examplePersonalConnection({ models: [], hasApiKey: false, apiKeyHint: null }));
      current = [...current, saved];
      return ok(saved, 201);
    }),
    http.put('/v1/account/ai-connections/:id', async ({ request, params }) => {
      const input = (await request.json()) as AiConnectionInput;
      savedConnections.push({ id: String(params.id), input });
      const previous = find(params.id);
      if (previous === undefined) return notFound();
      const error = take('connection');
      if (error !== undefined) return reject(error);
      const saved = fromInput(previous.id, input, previous);
      current = current.map((connection) => (connection.id === saved.id ? saved : connection));
      return ok(saved);
    }),
    http.post('/v1/account/ai-connections/:id/refresh', ({ params }) => {
      refreshed.push(String(params.id));
      const previous = find(params.id);
      if (previous === undefined) return notFound();
      const error = take('refresh');
      if (error !== undefined) return reject(error);
      return ok(previous);
    }),
    http.delete('/v1/account/ai-connections/:id', ({ params }) => {
      deleted.push(String(params.id));
      if (find(params.id) === undefined) return notFound();
      const error = take('delete');
      if (error !== undefined) return reject(error);
      current = current.filter((connection) => connection.id !== params.id);
      return new Response(null, { status: 204 });
    }),
    http.post('/v1/account/ai-models/discover', async ({ request }) => {
      discoveries.push((await request.json()) as AiDiscoveryInput);
      const error = take('discover');
      return error === undefined ? ok({ models: DISCOVERED }) : reject(error);
    }),
    http.post('/v1/ai/openrouter/login', async ({ request }) => {
      const body = (await request.json()) as { scope: AiConnectionScope };
      loginScopes.push(body.scope);
      const error = take('login');
      return error === undefined ? ok({ authorizeUrl }) : reject(error);
    }),
    http.post('/v1/ai/openrouter/complete', async ({ request }) => {
      completions.push((await request.json()) as { code: string; state: string });
      const error = take('complete');
      if (error !== undefined) return reject(error);
      counter += 1;
      const saved = examplePersonalConnection({ id: `pc-login-${String(counter)}`, models: [] });
      if (completedScope === 'personal') current = [...current, saved];
      return ok({ scope: completedScope, connection: saved }, 201);
    }),
  );
  return {
    savedConnections,
    discoveries,
    deleted,
    refreshed,
    loginScopes,
    completions,
    current: () => current,
    rejectNext: (operation, error) => {
      rejections.set(operation, error);
    },
  };
}

/** 一台電腦的假資料：在線、有 Claude Code 與 Codex。 */
export function exampleMachine(overrides: Partial<LocalAgentMachine> = {}): LocalAgentMachine {
  return {
    id: 'machine-1',
    name: '範例筆電',
    agentVersion: '1.0.0',
    engines: [
      { kind: 'claude_code', version: '2.1.330' },
      { kind: 'codex', version: '0.163.0' },
    ],
    online: true,
    lastSeenAt: '2026-11-23T03:00:00+00:00',
    ...overrides,
  };
}

export interface LocalAgentsServer {
  /** 送出過的移除（電腦編號，照順序）。 */
  readonly removed: string[];
  /** GET 清單被打了幾次（每 15 秒重讀的測試用）。 */
  readonly listCalls: () => number;
  /** 換成新的清單：下一次讀到的就是它（例如電腦上線、離線）。 */
  readonly set: (machines: readonly LocalAgentMachine[]) => void;
  readonly current: () => readonly LocalAgentMachine[];
  /** 下一次移除失敗（預設 500）。 */
  readonly rejectNextRemove: (error?: Rejection) => void;
}

/** 有狀態的本機代理清單端點（internal/HANDOFF-local-agent.md 第 4.4 節）：移除之後不在清單裡，找不到回 404。 */
export function serveLocalAgents(initial: readonly LocalAgentMachine[] = []): LocalAgentsServer {
  let machines = initial;
  let calls = 0;
  let nextRejection: Rejection | undefined;
  const removed: string[] = [];
  server.use(
    http.get('/v1/account/local-agents', () => {
      calls += 1;
      return ok(machines);
    }),
    http.delete('/v1/account/local-agents/:id', ({ params }) => {
      removed.push(String(params.id));
      if (nextRejection !== undefined) {
        const { status, code, message, details = [] } = nextRejection;
        nextRejection = undefined;
        return failure(status, code, message, details);
      }
      if (!machines.some((machine) => machine.id === params.id)) return failure(404, 'ai.connection_not_found', '找不到這台電腦。');
      machines = machines.filter((machine) => machine.id !== params.id);
      return new Response(null, { status: 204 });
    }),
  );
  return {
    removed,
    listCalls: () => calls,
    set: (next) => {
      machines = next;
    },
    current: () => machines,
    rejectNextRemove: (error = { status: 500, code: 'server.error', message: '伺服器出錯了。' }) => {
      nextRejection = error;
    },
  };
}
