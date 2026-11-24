import type {
  AiConnection,
  AiConnectionInput,
  AiDiscoveredModel,
  AiDiscoveryInput,
  AiEffort,
  AiProviderKind,
} from '../../api/aiTypes';
import { PROVIDER_LABELS } from '../ai/aiLabels';

/** 對話框上的欄位（細項路徑的名稱，camelCase）。模型的兩個欄位的錯誤都放在模型清單下面。 */
export type ConnectionField = 'name' | 'baseUrl' | 'apiKey' | 'enabledModels' | 'customModels';

export interface ListedModel {
  readonly id: string;
  readonly displayName: string;
  readonly efforts: readonly AiEffort[];
}

/** 對話框的草稿。listed 是供應商清單裡的模型（讀到的，或編輯時存著的）；custom 是手動加的。 */
export interface ConnectionDraft {
  readonly name: string;
  /** 使用者改過名稱之後，換供應商就不再替他換名稱。 */
  readonly isNameEdited: boolean;
  readonly kind: AiProviderKind;
  readonly baseUrl: string;
  /** 空白：新增是沒有金鑰，編輯是沿用存的。 */
  readonly apiKey: string;
  readonly listed: readonly ListedModel[];
  readonly custom: readonly ListedModel[];
  /** 開放的模型代號。 */
  readonly enabled: readonly string[];
}

export const MODEL_FILTER_THRESHOLD = 12;

export function emptyDraft(): ConnectionDraft {
  return {
    name: PROVIDER_LABELS.anthropic,
    isNameEdited: false,
    kind: 'anthropic',
    baseUrl: '',
    apiKey: '',
    listed: [],
    custom: [],
    enabled: [],
  };
}

export function draftFrom(connection: AiConnection): ConnectionDraft {
  const toListed = (model: AiConnection['models'][number]): ListedModel => ({ id: model.id, displayName: model.displayName, efforts: model.efforts });
  return {
    name: connection.name,
    isNameEdited: true,
    kind: connection.kind,
    baseUrl: connection.baseUrl ?? '',
    apiKey: '',
    listed: connection.models.filter((model) => !model.custom).map(toListed),
    custom: connection.models.filter((model) => model.custom).map(toListed),
    enabled: connection.models.filter((model) => model.enabled).map((model) => model.id),
  };
}

function blankToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed === '' ? null : trimmed;
}

/** 欄位上的基底網址：只有相容端點有。 */
function baseUrlOf(draft: ConnectionDraft): string | null {
  return draft.kind === 'openai_compatible' ? blankToNull(draft.baseUrl) : null;
}

/** 要存的連線：只送還留在清單裡的開放代號；金鑰留白是 null（新增是沒有、編輯是沿用）。 */
export function toInput(draft: ConnectionDraft): AiConnectionInput {
  const known = new Set([...draft.listed, ...draft.custom].map((model) => model.id));
  return {
    name: draft.name.trim(),
    kind: draft.kind,
    baseUrl: baseUrlOf(draft),
    apiKey: blankToNull(draft.apiKey),
    enabledModels: draft.enabled.filter((id) => known.has(id)),
    customModels: draft.custom.map((model) => model.id),
  };
}

export function toDiscoveryInput(draft: ConnectionDraft, connectionId: string | null): AiDiscoveryInput {
  return { kind: draft.kind, baseUrl: baseUrlOf(draft), apiKey: blankToNull(draft.apiKey), connectionId };
}

/** 換供應商：名稱還是自動填的就跟著換；清單是上一家的，不再適用（手動加的代號留著）。 */
export function withKind(draft: ConnectionDraft, kind: AiProviderKind): ConnectionDraft {
  const customIds = new Set(draft.custom.map((model) => model.id));
  return {
    ...draft,
    kind,
    name: draft.isNameEdited ? draft.name : PROVIDER_LABELS[kind],
    listed: [],
    enabled: draft.enabled.filter((id) => customIds.has(id)),
  };
}

/** 讀到新的清單：換掉供應商清單的部分；已經開放的（包括手動加的）保持勾選，清單裡沒有的不再開放。 */
export function withDiscovered(draft: ConnectionDraft, discovered: readonly AiDiscoveredModel[]): ConnectionDraft {
  const listedIds = new Set(discovered.map((model) => model.id));
  // 手動加的代號供應商的清單裡也有了，就只留清單裡的那一筆。
  const custom = draft.custom.filter((model) => !listedIds.has(model.id));
  const known = new Set([...listedIds, ...custom.map((model) => model.id)]);
  return { ...draft, listed: discovered, custom, enabled: draft.enabled.filter((id) => known.has(id)) };
}

export function withModelToggled(draft: ConnectionDraft, id: string): ConnectionDraft {
  const enabled = draft.enabled.includes(id) ? draft.enabled.filter((other) => other !== id) : [...draft.enabled, id];
  return { ...draft, enabled };
}

export type AddCustomResult = { readonly draft: ConnectionDraft; readonly error: string | null };

/** 手動加模型：加進來就開放；代號已經在清單裡只是把它打開。 */
export function withCustomModel(draft: ConnectionDraft, rawId: string): AddCustomResult {
  const id = rawId.trim();
  if (id === '') return { draft, error: '請輸入模型代號。' };
  const exists = [...draft.listed, ...draft.custom].some((model) => model.id === id);
  const enabled = draft.enabled.includes(id) ? draft.enabled : [...draft.enabled, id];
  if (exists) return { draft: { ...draft, enabled }, error: null };
  const added: ListedModel = { id, displayName: id, efforts: [] };
  return { draft: { ...draft, custom: [...draft.custom, added], enabled }, error: null };
}

export function withoutCustomModel(draft: ConnectionDraft, id: string): ConnectionDraft {
  return {
    ...draft,
    custom: draft.custom.filter((model) => model.id !== id),
    enabled: draft.enabled.filter((other) => other !== id),
  };
}

export function hasStoredKeyChanged(draft: ConnectionDraft, stored: AiConnection | null): boolean {
  if (stored === null || !stored.hasApiKey) return false;
  return draft.kind !== stored.kind || baseUrlOf(draft) !== stored.baseUrl;
}

/** 編輯時，換了供應商或基底網址而金鑰還是空的：存著的金鑰不能送給新的對象，要重新輸入。 */
export function needsNewApiKey(draft: ConnectionDraft, stored: AiConnection | null): boolean {
  return hasStoredKeyChanged(draft, stored) && draft.apiKey.trim() === '';
}

export function matchesFilter(model: ListedModel, filter: string): boolean {
  const needle = filter.trim().toLowerCase();
  return needle === '' || model.id.toLowerCase().includes(needle) || model.displayName.toLowerCase().includes(needle);
}

/**
 * 打了、但離開這個對話框就會不見的東西（金鑰、勾選或手動加的模型）；名稱只在它看得到的時候才算
 * （用 OpenRouter 登入時名稱由伺服器取，欄位是收起來的）。
 */
export function hasEnteredInput(draft: ConnectionDraft, customId: string, includesName: boolean): boolean {
  const hasModels = draft.enabled.length > 0 || draft.custom.length > 0 || customId.trim() !== '';
  return draft.apiKey !== '' || hasModels || (includesName && draft.isNameEdited);
}
