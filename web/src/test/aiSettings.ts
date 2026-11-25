import { http } from 'msw';
import type {
  AiConnection,
  AiConnectionInput,
  AiDiscoveredModel,
  AiDiscoveryInput,
  AiSettings,
  AiSettingsInput,
} from '../api/aiTypes';
import { failure, ok } from './api';
import { server } from './server';

export const EMPTY_AI_SETTINGS: AiSettings = {
  enabled: false,
  allowPersonalConnections: false,
  default: null,
  connections: [],
  updatedAt: null,
  updatedBy: null,
  limits: { dailyRequestsPerPerson: 200, monthlyRequests: null, monthlyTokens: null },
  monthUsage: { month: '2026-11', requests: 0, tokens: 0 },
};

const UPDATED_BY = { personId: 'p-owner', displayName: '範例擁有者' } as const;
const UPDATED_AT = '2026-11-23T03:00:00+00:00';

/** 一個連線的假資料；不給的欄位用 Anthropic 加兩個模型。 */
export function exampleConnection(overrides: Partial<AiConnection> = {}): AiConnection {
  return {
    id: 'c-1',
    name: 'Anthropic',
    kind: 'anthropic',
    baseUrl: null,
    hasApiKey: true,
    apiKeyHint: 'a1b2',
    models: [
      { id: 'claude-opus-5-5', displayName: 'Claude Opus 5.5', efforts: ['low', 'medium', 'high'], enabled: true, custom: false },
      { id: 'claude-haiku-5', displayName: 'Claude Haiku 5', efforts: [], enabled: false, custom: false },
    ],
    modelsCheckedAt: UPDATED_AT,
    usage: { month: '2026-11', inputTokens: 120000, outputTokens: 8000, replies: 12 },
    createdAt: UPDATED_AT,
    updatedAt: UPDATED_AT,
    updatedBy: UPDATED_BY,
    ...overrides,
  };
}

export interface Rejection {
  readonly status: number;
  readonly code: string;
  readonly message: string;
  /** 422 時的細項：路徑是欄位名稱（camelCase）。 */
  readonly details?: readonly { readonly code: string; readonly path: string; readonly message: string }[];
}

type Operation = 'settings' | 'connection' | 'refresh' | 'discover' | 'delete';

export interface AiSettingsServer {
  /** 各端點收到的本文（照順序）。 */
  readonly savedSettings: AiSettingsInput[];
  readonly savedConnections: { readonly id: string | null; readonly input: AiConnectionInput }[];
  readonly discoveries: AiDiscoveryInput[];
  readonly deleted: string[];
  readonly refreshed: string[];
  readonly current: () => AiSettings;
  /** 下一次這個操作回這個錯誤（只一次）。 */
  readonly rejectNext: (operation: Operation, error: Rejection) => void;
  /** 下一次「讀取模型」回這份清單（沒設就回 discoverable 預設的兩個）。 */
  readonly setDiscovered: (models: readonly AiDiscoveredModel[]) => void;
}

function rejected({ status, code, message, details = [] }: Rejection) {
  return failure(status, code, message, details);
}

const DEFAULT_DISCOVERED: readonly AiDiscoveredModel[] = [
  { id: 'claude-opus-5-5', displayName: 'Claude Opus 5.5', efforts: ['low', 'medium', 'high'] },
  { id: 'claude-sonnet-5-5', displayName: 'Claude Sonnet 5.5', efforts: ['low', 'high'] },
];

/** 套用連線的本文：模型是（已經有的 ∪ 讀到的 ∪ 手動加的）裡面有的，enabled 照 enabledModels。 */
function connectionFrom(id: string, input: AiConnectionInput, previous: AiConnection | undefined, known: readonly AiDiscoveredModel[]): AiConnection {
  const catalog = new Map<string, AiDiscoveredModel>([...known, ...(previous?.models ?? [])].map((model) => [model.id, model]));
  const ids = [...new Set([...(previous?.models.filter((model) => !model.custom).map((model) => model.id) ?? []), ...input.enabledModels, ...input.customModels])];
  const base = previous ?? exampleConnection({ id, models: [] });
  return {
    ...base,
    id,
    name: input.name,
    kind: input.kind,
    baseUrl: input.baseUrl,
    // null 沿用、空字串清掉、其他是新的金鑰。
    hasApiKey: input.apiKey === null ? base.hasApiKey && previous !== undefined : input.apiKey !== '',
    apiKeyHint: input.apiKey === null ? (previous === undefined ? null : base.apiKeyHint) : input.apiKey.slice(-4) || null,
    models: ids.map((modelId) => ({
      id: modelId,
      displayName: catalog.get(modelId)?.displayName ?? modelId,
      efforts: catalog.get(modelId)?.efforts ?? [],
      enabled: input.enabledModels.includes(modelId),
      custom: input.customModels.includes(modelId),
    })),
    updatedAt: UPDATED_AT,
    updatedBy: UPDATED_BY,
  };
}

/**
 * 後端的規則：金鑰 null（沿用）時，供應商或基底網址和已存的不同就擋下來（422，細項路徑 apiKey）：
 * 不能把已存的金鑰送給新的對象。
 */
function reusedKeyRejection(previous: AiConnection | undefined, input: AiConnectionInput | AiDiscoveryInput): Rejection | null {
  if (previous === undefined || !previous.hasApiKey || input.apiKey !== null) return null;
  if (input.kind === previous.kind && input.baseUrl === previous.baseUrl) return null;
  const message = '換了供應商或網址，要重新輸入金鑰。';
  return { status: 422, code: 'ai.settings_invalid', message, details: [{ code: 'ai.field_required', path: 'apiKey', message }] };
}

/** 有狀態的 AI 設定端點（internal/HANDOFF-ai-assistant.md 第 6.1 節）：存了之後 GET 讀到新的，金鑰不會讀出來。 */
export function serveAiSettings(initial: Partial<AiSettings> = {}): AiSettingsServer {
  let current: AiSettings = { ...EMPTY_AI_SETTINGS, ...initial };
  let discovered = DEFAULT_DISCOVERED;
  let counter = 0;
  const rejections = new Map<Operation, Rejection>();
  const savedSettings: AiSettingsInput[] = [];
  const savedConnections: { id: string | null; input: AiConnectionInput }[] = [];
  const discoveries: AiDiscoveryInput[] = [];
  const deleted: string[] = [];
  const refreshed: string[] = [];

  function take(operation: Operation): Rejection | undefined {
    const error = rejections.get(operation);
    rejections.delete(operation);
    return error;
  }
  const connectionById = (id: unknown) => current.connections.find((connection) => connection.id === id);
  const notFound = () => failure(404, 'ai.connection_not_found', '找不到這個連線。');
  const withConnection = (saved: AiConnection) => {
    const exists = connectionById(saved.id) !== undefined;
    current = {
      ...current,
      connections: exists ? current.connections.map((connection) => (connection.id === saved.id ? saved : connection)) : [...current.connections, saved],
    };
  };

  server.use(
    // 擁有者的設定頁多一段「主機 AI 代理」，開頁就會讀；這裡預設是沒有任何代理，要測它的測試用 serveHostAgents 蓋掉。
    http.get('/v1/ai/host', () => ok({ agents: [], sources: [] })),
    http.get('/v1/ai/settings', () => ok(current)),
    http.put('/v1/ai/settings', async ({ request }) => {
      const body = (await request.json()) as AiSettingsInput;
      savedSettings.push(body);
      const error = take('settings');
      if (error !== undefined) return rejected(error);
      current = { ...current, enabled: body.enabled, allowPersonalConnections: body.allowPersonalConnections, default: body.default, limits: body.limits ?? current.limits, updatedAt: UPDATED_AT, updatedBy: UPDATED_BY };
      return ok(current);
    }),
    http.post('/v1/ai/connections', async ({ request }) => {
      const input = (await request.json()) as AiConnectionInput;
      savedConnections.push({ id: null, input });
      const error = take('connection');
      if (error !== undefined) return rejected(error);
      counter += 1;
      const saved = connectionFrom(`c-new-${String(counter)}`, input, undefined, discovered);
      withConnection(saved);
      return ok(saved, 201);
    }),
    http.put('/v1/ai/connections/:id', async ({ request, params }) => {
      const input = (await request.json()) as AiConnectionInput;
      savedConnections.push({ id: String(params.id), input });
      const previous = connectionById(params.id);
      if (previous === undefined) return notFound();
      const error = take('connection') ?? reusedKeyRejection(previous, input);
      if (error !== null) return rejected(error);
      const saved = connectionFrom(previous.id, input, previous, discovered);
      withConnection(saved);
      return ok(saved);
    }),
    http.post('/v1/ai/connections/:id/refresh', ({ params }) => {
      refreshed.push(String(params.id));
      const previous = connectionById(params.id);
      if (previous === undefined) return notFound();
      const error = take('refresh');
      if (error !== undefined) return rejected(error);
      const saved = { ...previous, models: [...discovered.map((model) => ({ ...model, enabled: previous.models.some((old) => old.id === model.id && old.enabled), custom: false })), ...previous.models.filter((model) => model.custom)] };
      withConnection(saved);
      return ok(saved);
    }),
    http.delete('/v1/ai/connections/:id', ({ params }) => {
      deleted.push(String(params.id));
      if (connectionById(params.id) === undefined) return notFound();
      const error = take('delete');
      if (error !== undefined) return rejected(error);
      current = { ...current, connections: current.connections.filter((connection) => connection.id !== params.id) };
      return new Response(null, { status: 204 });
    }),
    http.post('/v1/ai/models/discover', async ({ request }) => {
      const input = (await request.json()) as AiDiscoveryInput;
      discoveries.push(input);
      const error = take('discover') ?? reusedKeyRejection(input.connectionId === null ? undefined : connectionById(input.connectionId), input);
      if (error !== null) return rejected(error);
      return ok({ models: discovered });
    }),
  );
  return {
    savedSettings,
    savedConnections,
    discoveries,
    deleted,
    refreshed,
    current: () => current,
    rejectNext: (operation, error) => {
      rejections.set(operation, error);
    },
    setDiscovered: (models) => {
      discovered = models;
    },
  };
}

/** 不是管理員：讀是 403。 */
export function forbidAiSettings(): void {
  server.use(http.get('/v1/ai/settings', () => failure(403, 'ai.forbidden', '只有組織的擁有者與管理員可以設定 AI。')));
}
