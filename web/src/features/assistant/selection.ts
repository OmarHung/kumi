import type { AiEffort, AiModelOption, AiOptions, AiSelection } from '../../api/aiTypes';

export const SELECTION_STORAGE_KEY = 'assistant.selection';

const EFFORTS: readonly AiEffort[] = ['none', 'low', 'medium', 'high', 'xhigh', 'max'];

function isEffort(value: unknown): value is AiEffort {
  return EFFORTS.some((effort) => effort === value);
}

function parseSelection(value: unknown): AiSelection | null {
  if (typeof value !== 'object' || value === null) return null;
  const { connectionId, model, effort } = value as Record<string, unknown>;
  if (typeof connectionId !== 'string' || typeof model !== 'string') return null;
  if (effort !== null && !isEffort(effort)) return null;
  return { connectionId, model, effort };
}

/** 讀不到（私密瀏覽、被擋下）或內容不認得時當成沒記過。 */
export function readStoredSelection(): AiSelection | null {
  try {
    const raw = localStorage.getItem(SELECTION_STORAGE_KEY);
    return raw === null ? null : parseSelection(JSON.parse(raw));
  } catch {
    return null;
  }
}

/** 寫不進去時這次照樣用（呼叫端的狀態還在），下次載入回到預設：只是個人偏好。 */
export function storeSelection(selection: AiSelection): void {
  try {
    localStorage.setItem(SELECTION_STORAGE_KEY, JSON.stringify(selection));
  } catch {
    // 見上面的說明：寫不進去不算錯誤。
  }
}

export function findModel(options: AiOptions, selection: AiSelection): AiModelOption | undefined {
  return options.connections
    .find((connection) => connection.id === selection.connectionId)
    ?.models.find((model) => model.id === selection.model);
}

/** 這個選擇現在還能用：連線與模型都還開放。思考深度那個模型沒有時換成「預設」（null），模型本身沒問題就不算失效。 */
function usable(options: AiOptions, selection: AiSelection | null): AiSelection | null {
  if (selection === null) return null;
  const connection = options.connections.find((candidate) => candidate.id === selection.connectionId);
  // 主機來源現在不能用（notice 不是 null）：選了也送不出去，當作失效，退回別的。
  if (connection === undefined || connection.notice !== null) return null;
  const model = connection.models.find((candidate) => candidate.id === selection.model);
  if (model === undefined) return null;
  const isEffortKept = selection.effort === null || model.efforts.includes(selection.effort);
  return isEffortKept ? selection : { ...selection, effort: null };
}

function firstAvailable(options: AiOptions): AiSelection | null {
  for (const connection of options.connections) {
    if (connection.notice !== null) continue;
    const [model] = connection.models;
    if (model !== undefined) return { connectionId: connection.id, model: model.id, effort: null };
  }
  return null;
}

/**
 * 輸入框現在用哪一個：記在瀏覽器的（或這一頁剛選的）還能用就用它；不能用了（連線被刪、模型不再開放）退回組織的預設；
 * 預設也沒有或不能用時，取第一個開放的模型。一個都沒有是 null（不能送）。
 */
export function resolveSelection(options: AiOptions, chosen: AiSelection | null): AiSelection | null {
  return usable(options, chosen) ?? usable(options, options.default) ?? firstAvailable(options);
}

/** 換模型：思考深度保留（模型有這一級）或換成「預設」。 */
export function withModel(options: AiOptions, current: AiSelection, connectionId: string, model: string): AiSelection {
  const next: AiSelection = { connectionId, model, effort: current.effort };
  return usable(options, next) ?? { connectionId, model, effort: null };
}
