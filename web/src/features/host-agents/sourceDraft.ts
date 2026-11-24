import type { AiEffort, AiModelOption, LocalEngineKind } from '../../api/aiTypes';
import type { HostAgent, HostSource, HostSourceInput } from '../../api/hostAgentTypes';
import { engineLabel, MAX_REQUEST_LIMIT, MAX_TOKEN_LIMIT } from './hostAgentText';

/** 來源名稱的長度上限（和 AiRules.MaxNameLength 同一個數字）。 */
export const MAX_SOURCE_NAME = 60;

/** 空字串是不指定／不限。數字欄位照輸入的字存，送出時才轉。 */
export interface SourceDraft {
  readonly agentId: string;
  readonly engine: LocalEngineKind;
  readonly name: string;
  readonly enabled: boolean;
  readonly enabledModels: readonly string[];
  readonly defaultModel: string;
  readonly defaultEffort: AiEffort | '';
  readonly dailyRequestsPerPerson: string;
  readonly monthlyRequests: string;
  readonly monthlyTokens: string;
  readonly acknowledged: boolean;
}

export type SourceField =
  | 'agentId'
  | 'engine'
  | 'name'
  | 'enabledModels'
  | 'defaultModel'
  | 'defaultEffort'
  | 'dailyRequestsPerPerson'
  | 'monthlyRequests'
  | 'monthlyTokens'
  | 'acknowledgeApiBilling';

/** 畫面上的順序（送出有問題時焦點移到第一個）。 */
export const SOURCE_FIELDS: readonly SourceField[] = [
  'agentId',
  'engine',
  'name',
  'enabledModels',
  'defaultModel',
  'defaultEffort',
  'dailyRequestsPerPerson',
  'monthlyRequests',
  'monthlyTokens',
  'acknowledgeApiBilling',
];

/** 引擎回報的模型（代理上那個引擎現在的清單）。代理或引擎找不到是空的。 */
export function reportedModels(agent: HostAgent | undefined, engine: LocalEngineKind): readonly AiModelOption[] {
  return agent?.engines.find((candidate) => candidate.kind === engine)?.models ?? [];
}

/**
 * 勾選清單：引擎現在回報的，加上這個來源已經開放、但引擎這次沒回報的（引擎暫時讀不到清單時，已存的選擇不能憑空不見）。
 */
export function modelChoices(agent: HostAgent | undefined, engine: LocalEngineKind, source: HostSource | null): readonly AiModelOption[] {
  const reported = reportedModels(agent, engine);
  const missing = (source?.models ?? [])
    .filter((model) => model.enabled && !reported.some((candidate) => candidate.id === model.id))
    .map((model): AiModelOption => ({ id: model.id, displayName: model.displayName, efforts: model.efforts, defaultEffort: null }));
  return [...reported, ...missing];
}

/** 這台代理已經有來源的引擎（一個代理的一個引擎最多一個來源）。 */
export function takenEngines(agentId: string, sources: readonly HostSource[]): readonly LocalEngineKind[] {
  return sources.filter((source) => source.agentId === agentId).map((source) => source.engine);
}

/** 新增：第一台代理的第一個還沒有來源的引擎；那個引擎還不能接工作時先存成停用的（登入之後再開）。 */
export function emptySourceDraft(agents: readonly HostAgent[], sources: readonly HostSource[]): SourceDraft {
  const taken = (agentId: string, engine: LocalEngineKind) => sources.some((source) => source.agentId === agentId && source.engine === engine);
  const first = agents.find((agent) => agent.engines.length > 0 || agent.state === 'active') ?? agents[0];
  const engine = (['claude_code', 'codex'] as const).find((candidate) => first !== undefined && !taken(first.id, candidate)) ?? 'claude_code';
  return draftForEngine(
    { agentId: first?.id ?? '', engine, name: '', enabled: false, enabledModels: [], defaultModel: '', defaultEffort: '', dailyRequestsPerPerson: '', monthlyRequests: '', monthlyTokens: '', acknowledged: false },
    first,
  );
}

/** 換了代理或引擎：名稱預設「主機上的 Claude Code」（還沒改過的才換）、開放引擎回報的全部模型、能接工作才預設開著。 */
export function draftForEngine(draft: SourceDraft, agent: HostAgent | undefined, previousDefaultName = ''): SourceDraft {
  const models = reportedModels(agent, draft.engine);
  const isReady = agent?.engines.find((candidate) => candidate.kind === draft.engine)?.readiness === 'ready';
  const defaultName = `主機上的 ${engineLabel(draft.engine)}`;
  return {
    ...draft,
    name: draft.name === '' || draft.name === previousDefaultName ? defaultName : draft.name,
    enabled: isReady,
    enabledModels: models.map((model) => model.id),
    defaultModel: '',
    defaultEffort: '',
  };
}

const text = (value: number | null): string => (value === null ? '' : String(value));

export function draftFromSource(source: HostSource): SourceDraft {
  return {
    agentId: source.agentId,
    engine: source.engine,
    name: source.name,
    enabled: source.enabled,
    enabledModels: source.models.filter((model) => model.enabled).map((model) => model.id),
    defaultModel: source.defaultModel ?? '',
    defaultEffort: source.defaultEffort ?? '',
    dailyRequestsPerPerson: text(source.limits.dailyRequestsPerPerson),
    monthlyRequests: text(source.limits.monthlyRequests),
    monthlyTokens: text(source.limits.monthlyTokens),
    acknowledged: source.billingAcknowledgedAt !== null,
  };
}

function parseLimit(value: string, max: number): number | null | 'invalid' {
  const trimmed = value.trim();
  if (trimmed === '') return null;
  if (!/^\d+$/.test(trimmed)) return 'invalid';
  const parsed = Number(trimmed);
  return parsed >= 1 && parsed <= max ? parsed : 'invalid';
}

export type SourceErrors = Readonly<Partial<Record<SourceField, string>>>;

const limitMessage = (max: number) => `填 1 到 ${max.toLocaleString('en-US')} 的整數，留白是不限`;

/** 送出前的檢查（和後端同一套規則；真正的檢查還是後端）：名稱、開著時至少一個模型與計費確認、上限的範圍、預設要在開放的模型裡。 */
export function validateSource(draft: SourceDraft): SourceErrors {
  const errors: Partial<Record<SourceField, string>> = {};
  const name = draft.name.trim();
  if (draft.agentId === '') errors.agentId = '先新增一台主機代理';
  if (name === '') errors.name = '請填名稱';
  else if (name.length > MAX_SOURCE_NAME) errors.name = `名稱最多 ${String(MAX_SOURCE_NAME)} 個字`;
  if (draft.enabled && draft.enabledModels.length === 0) errors.enabledModels = '開著時至少開放一個模型';
  if (draft.defaultModel !== '' && !draft.enabledModels.includes(draft.defaultModel)) errors.defaultModel = '預設的模型要在開放的模型裡';
  if (parseLimit(draft.dailyRequestsPerPerson, MAX_REQUEST_LIMIT) === 'invalid') errors.dailyRequestsPerPerson = limitMessage(MAX_REQUEST_LIMIT);
  if (parseLimit(draft.monthlyRequests, MAX_REQUEST_LIMIT) === 'invalid') errors.monthlyRequests = limitMessage(MAX_REQUEST_LIMIT);
  if (parseLimit(draft.monthlyTokens, MAX_TOKEN_LIMIT) === 'invalid') errors.monthlyTokens = limitMessage(MAX_TOKEN_LIMIT);
  if (draft.enabled && !draft.acknowledged) errors.acknowledgeApiBilling = '開著之前要先確認以 API 用量計費';
  return errors;
}

function limitOf(value: string, max: number): number | null {
  const parsed = parseLimit(value, max);
  return parsed === 'invalid' ? null : parsed;
}

/** 草稿通過 validateSource 之後換成要送的內容。 */
export function toSourceInput(draft: SourceDraft): HostSourceInput {
  return {
    agentId: draft.agentId,
    engine: draft.engine,
    name: draft.name.trim(),
    enabled: draft.enabled,
    enabledModels: draft.enabledModels,
    defaultModel: draft.defaultModel === '' ? null : draft.defaultModel,
    defaultEffort: draft.defaultEffort === '' ? null : draft.defaultEffort,
    limits: {
      dailyRequestsPerPerson: limitOf(draft.dailyRequestsPerPerson, MAX_REQUEST_LIMIT),
      monthlyRequests: limitOf(draft.monthlyRequests, MAX_REQUEST_LIMIT),
      monthlyTokens: limitOf(draft.monthlyTokens, MAX_TOKEN_LIMIT),
    },
    acknowledgeApiBilling: draft.acknowledged,
  };
}
