import { http } from 'msw';
import type {
  HostAgent,
  HostAgentInput,
  HostAgentOverview,
  HostEngineStatus,
  HostSource,
  HostSourceInput,
  HostSourceUsage,
  HostUsageTotals,
} from '../api/hostAgentTypes';
import { failure, ok } from './api';
import { server } from './server';

const OWNER = { personId: 'p-owner', displayName: '範例擁有者' } as const;
const AT = '2026-11-24T03:00:00+00:00';

export const EMPTY_HOST_OVERVIEW: HostAgentOverview = { agents: [], sources: [] };

export const ZERO_TOTALS: HostUsageTotals = { requests: 0, completed: 0, failed: 0, stopped: 0, inputTokens: 0, outputTokens: 0 };

/** 一個引擎的狀態：不給的欄位是「已安裝、API 帳號登入、擁有者授權過、可以接工作」。 */
export function exampleEngine(overrides: Partial<HostEngineStatus> = {}): HostEngineStatus {
  return {
    kind: 'claude_code',
    readiness: 'ready',
    version: '2.1.330',
    loggedIn: true,
    billing: 'api',
    models: [
      { id: 'opus', displayName: 'Opus', efforts: ['low', 'high'], defaultEffort: 'high' },
      { id: 'sonnet', displayName: 'Sonnet', efforts: [], defaultEffort: null },
    ],
    problem: null,
    authorizedAt: AT,
    authorizedBy: OWNER,
    ...overrides,
  };
}

/** 一台主機代理：不給的欄位是「已配對、在線、claude_code 可以接工作、codex 需要登入」。 */
export function exampleHostAgent(overrides: Partial<HostAgent> = {}): HostAgent {
  return {
    id: 'agent-1',
    name: '辦公室主機',
    state: 'active',
    online: true,
    lastSeenAt: AT,
    machineName: 'office-host',
    agentVersion: '1.4.0',
    serverUrl: 'https://kumi.example.com',
    engines: [
      exampleEngine(),
      exampleEngine({ kind: 'codex', readiness: 'not_logged_in', version: '0.9.1', loggedIn: false, billing: 'unknown', models: [], authorizedAt: null, authorizedBy: null, problem: '還沒登入 Codex。' }),
    ],
    logins: [],
    pairingExpiresAt: null,
    createdAt: AT,
    credentialExpiresAt: null,
    warning: null,
    ...overrides,
  };
}

/** 一個來源：不給的欄位是「辦公室主機上的 Claude Code，開著、可以使用、開放 opus」。 */
export function exampleHostSource(overrides: Partial<HostSource> = {}): HostSource {
  return {
    id: 'src-1',
    agentId: 'agent-1',
    engine: 'claude_code',
    name: '主機上的 Claude Code',
    enabled: true,
    status: 'ready',
    statusMessage: null,
    models: [
      { id: 'opus', displayName: 'Opus', efforts: ['low', 'high'], enabled: true, custom: false },
      { id: 'sonnet', displayName: 'Sonnet', efforts: [], enabled: false, custom: false },
    ],
    defaultModel: 'opus',
    defaultEffort: 'high',
    limits: { dailyRequestsPerPerson: 20, monthlyRequests: null, monthlyTokens: null },
    running: 1,
    queued: 2,
    usage: { requests: 12, completed: 10, failed: 1, stopped: 1, inputTokens: 120000, outputTokens: 8000 },
    billingAcknowledgedAt: AT,
    updatedAt: AT,
    updatedBy: OWNER,
    ...overrides,
  };
}

export function exampleSourceUsage(overrides: Partial<HostSourceUsage> = {}): HostSourceUsage {
  return {
    month: '2026-11',
    totals: { requests: 12, completed: 10, failed: 1, stopped: 1, inputTokens: 120000, outputTokens: 8000 },
    people: [
      { person: { personId: 'p-1', displayName: '小明' }, totals: { requests: 8, completed: 7, failed: 1, stopped: 0, inputTokens: 90000, outputTokens: 6000 } },
      { person: { personId: 'p-2', displayName: '小華' }, totals: { requests: 4, completed: 3, failed: 0, stopped: 1, inputTokens: 30000, outputTokens: 2000 } },
    ],
    projects: [
      { projectKey: 'WEB', totals: { requests: 10, completed: 8, failed: 1, stopped: 1, inputTokens: 100000, outputTokens: 7000 } },
      // 不選專案的組織對話：projectKey 是 null。
      { projectKey: null, totals: { requests: 2, completed: 2, failed: 0, stopped: 0, inputTokens: 20000, outputTokens: 1000 } },
    ],
    ...overrides,
  };
}

export interface Rejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  /** 422 的細項：路徑是欄位名稱。 */
  readonly details?: readonly { readonly code: string; readonly path: string; readonly message: string }[];
}

type Operation = 'create' | 'login' | 'source' | 'usage';

export interface HostAgentsServer {
  readonly current: () => HostAgentOverview;
  /** 各端點收到的內容（照順序）。 */
  readonly createdAgents: string[];
  readonly updatedAgents: { readonly id: string; readonly input: HostAgentInput }[];
  readonly pairings: string[];
  readonly deletedAgents: string[];
  readonly logins: { readonly agentId: string; readonly engine: string }[];
  readonly logouts: { readonly agentId: string; readonly engine: string }[];
  readonly cancelledLogins: string[];
  readonly savedSources: { readonly id: string | null; readonly input: HostSourceInput }[];
  readonly deletedSources: string[];
  readonly usageRequests: string[];
  /** 下一次這個操作回這個錯誤（只一次）。 */
  readonly rejectNext: (operation: Operation, error: Rejection) => void;
  /** 換掉伺服器上的資料（模擬輪詢讀到變化）。 */
  readonly set: (next: HostAgentOverview) => void;
}

const PAIRING_CODE = 'PAIR-4821-XQ';
const LOGIN_CODE = 'LOGIN-7733-ZK';
const EXPIRES = '2026-11-24T03:15:00+00:00';

function sourceFrom(id: string, input: HostSourceInput, previous: HostSource | undefined, agents: readonly HostAgent[]): HostSource {
  const reported = agents.find((agent) => agent.id === input.agentId)?.engines.find((engine) => engine.kind === input.engine)?.models ?? [];
  const known = [...reported, ...(previous?.models ?? [])];
  const models = [...new Map(known.map((model) => [model.id, model])).values()].map((model) => ({
    id: model.id,
    displayName: model.displayName,
    efforts: model.efforts,
    enabled: input.enabledModels.includes(model.id),
    custom: false,
  }));
  return {
    ...exampleHostSource({ id, agentId: input.agentId, engine: input.engine }),
    ...(previous ?? {}),
    id,
    agentId: input.agentId,
    engine: input.engine,
    name: input.name,
    enabled: input.enabled,
    models,
    defaultModel: input.defaultModel,
    defaultEffort: input.defaultEffort,
    limits: input.limits,
    billingAcknowledgedAt: input.acknowledgeApiBilling ? AT : null,
  };
}

/**
 * 有狀態的主機 AI 代理端點（internal/HANDOFF-host-agent.md 第 5.1 節）。新增與重新配對回固定的配對碼、
 * 開始登入回固定的登入碼，方便測試比對。
 */
export function serveHostAgents(initial: Partial<HostAgentOverview> = {}): HostAgentsServer {
  let current: HostAgentOverview = { ...EMPTY_HOST_OVERVIEW, ...initial };
  let counter = 0;
  const rejections = new Map<Operation, Rejection>();
  const server_: HostAgentsServer = {
    current: () => current,
    createdAgents: [],
    updatedAgents: [],
    pairings: [],
    deletedAgents: [],
    logins: [],
    logouts: [],
    cancelledLogins: [],
    savedSources: [],
    deletedSources: [],
    usageRequests: [],
    rejectNext: (operation, error) => {
      rejections.set(operation, error);
    },
    set: (next) => {
      current = next;
    },
  };
  const take = (operation: Operation) => {
    const error = rejections.get(operation);
    rejections.delete(operation);
    return error === undefined ? null : failure(error.status, error.code, error.message, error.details ?? []);
  };
  const agentById = (id: unknown) => current.agents.find((agent) => agent.id === id);
  const missingAgent = () => failure(404, 'ai.host_agent_not_found', '找不到這台主機代理。');
  const replaceAgent = (next: HostAgent) => {
    current = { ...current, agents: current.agents.map((agent) => (agent.id === next.id ? next : agent)) };
  };

  server.use(
    http.get('/v1/ai/host', () => ok(current)),
    http.post('/v1/ai/host/agents', async ({ request }) => {
      const { name } = (await request.json()) as { name: string };
      server_.createdAgents.push(name);
      const rejected = take('create');
      if (rejected !== null) return rejected;
      counter += 1;
      const agent = exampleHostAgent({ id: `agent-new-${String(counter)}`, name, state: 'awaiting_pairing', online: false, lastSeenAt: null, engines: [], pairingExpiresAt: EXPIRES });
      current = { ...current, agents: [...current.agents, agent] };
      return ok({ agent, pairingCode: PAIRING_CODE, pairingExpiresAt: EXPIRES }, 201);
    }),
    http.patch('/v1/ai/host/agents/:id', async ({ request, params }) => {
      const input = (await request.json()) as HostAgentInput;
      server_.updatedAgents.push({ id: String(params.id), input });
      const previous = agentById(params.id);
      if (previous === undefined) return missingAgent();
      const next: HostAgent = { ...previous, name: input.name, state: input.enabled ? (previous.state === 'disabled' ? 'active' : previous.state) : 'disabled' };
      replaceAgent(next);
      return ok(next);
    }),
    http.post('/v1/ai/host/agents/:id/pairing', ({ params }) => {
      server_.pairings.push(String(params.id));
      const previous = agentById(params.id);
      if (previous === undefined) return missingAgent();
      const next: HostAgent = { ...previous, state: 'awaiting_pairing', online: false, pairingExpiresAt: EXPIRES, credentialExpiresAt: null, warning: null };
      replaceAgent(next);
      return ok({ agent: next, pairingCode: PAIRING_CODE, pairingExpiresAt: EXPIRES });
    }),
    http.delete('/v1/ai/host/agents/:id', ({ params }) => {
      server_.deletedAgents.push(String(params.id));
      if (agentById(params.id) === undefined) return missingAgent();
      current = { agents: current.agents.filter((agent) => agent.id !== params.id), sources: current.sources.filter((source) => source.agentId !== params.id) };
      return new Response(null, { status: 204 });
    }),
    http.post('/v1/ai/host/agents/:id/engines/:engine/login', ({ params }) => {
      server_.logins.push({ agentId: String(params.id), engine: String(params.engine) });
      const rejected = take('login');
      if (rejected !== null) return rejected;
      const agent = agentById(params.id);
      if (agent === undefined) return missingAgent();
      const login = { id: `login-${String(server_.logins.length)}`, engine: params.engine === 'codex' ? ('codex' as const) : ('claude_code' as const), state: 'pending' as const, expiresAt: EXPIRES, error: null, startedBy: OWNER, createdAt: AT };
      replaceAgent({ ...agent, logins: [...agent.logins, login] });
      return ok({ login, code: LOGIN_CODE });
    }),
    http.post('/v1/ai/host/agents/:id/engines/:engine/logout', ({ params }) => {
      server_.logouts.push({ agentId: String(params.id), engine: String(params.engine) });
      const agent = agentById(params.id);
      if (agent === undefined) return missingAgent();
      replaceAgent({
        ...agent,
        engines: agent.engines.map((engine) => (engine.kind === params.engine ? { ...engine, readiness: 'not_logged_in' as const, loggedIn: false, authorizedAt: null, authorizedBy: null } : engine)),
      });
      return new Response(null, { status: 204 });
    }),
    http.delete('/v1/ai/host/logins/:id', ({ params }) => {
      server_.cancelledLogins.push(String(params.id));
      current = { ...current, agents: current.agents.map((agent) => ({ ...agent, logins: agent.logins.filter((login) => login.id !== params.id) })) };
      return new Response(null, { status: 204 });
    }),
    http.post('/v1/ai/host/sources', async ({ request }) => {
      const input = (await request.json()) as HostSourceInput;
      server_.savedSources.push({ id: null, input });
      const rejected = take('source');
      if (rejected !== null) return rejected;
      counter += 1;
      const saved = sourceFrom(`src-new-${String(counter)}`, input, undefined, current.agents);
      current = { ...current, sources: [...current.sources, saved] };
      return ok(saved, 201);
    }),
    http.put('/v1/ai/host/sources/:id', async ({ request, params }) => {
      const input = (await request.json()) as HostSourceInput;
      server_.savedSources.push({ id: String(params.id), input });
      const rejected = take('source');
      if (rejected !== null) return rejected;
      const previous = current.sources.find((source) => source.id === params.id);
      if (previous === undefined) return failure(404, 'ai.host_source_not_found', '找不到這個來源。');
      const saved = sourceFrom(previous.id, input, previous, current.agents);
      current = { ...current, sources: current.sources.map((source) => (source.id === saved.id ? saved : source)) };
      return ok(saved);
    }),
    http.delete('/v1/ai/host/sources/:id', ({ params }) => {
      server_.deletedSources.push(String(params.id));
      current = { ...current, sources: current.sources.filter((source) => source.id !== params.id) };
      return new Response(null, { status: 204 });
    }),
    http.get('/v1/ai/host/sources/:id/usage', ({ params, request }) => {
      server_.usageRequests.push(`${String(params.id)}${new URL(request.url).search}`);
      const rejected = take('usage');
      if (rejected !== null) return rejected;
      return ok(exampleSourceUsage());
    }),
  );
  return server_;
}

export { PAIRING_CODE, LOGIN_CODE };
