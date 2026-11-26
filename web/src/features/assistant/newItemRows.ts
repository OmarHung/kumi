import type { NewItemDraft } from '../../api/aiTypes';
import type { FieldValue, Member, ProjectDetail } from '../../api/types';
import { formatDate } from '../../domain/dates';
import { fieldDefinition, formatFieldValue, formatNumber, NOT_A_MEMBER } from '../../domain/fields';
import { PRIORITY_LABELS } from '../../domain/labels';

/** 縮排最多幾層：卡片在 320px 寬的浮動面板裡也排得下。 */
export const MAX_INDENT_DEPTH = 3;

/** 每個項目在卡片上的樣子：縮排層數（掛在前面的項目底下才縮排）與第二行小字的各段。 */
export interface NewItemRow {
  readonly typeName: string;
  readonly title: string;
  readonly depth: number;
  readonly details: readonly string[];
  /** 掛在既有項目底下時的編號（畫成連結）；掛在前面的項目底下時是 null。 */
  readonly parentKey: string | null;
  /** 掛在前面的項目底下時，那個項目的標題。 */
  readonly parentTitle: string | null;
  readonly description: string | null;
}

/** parentRef 指到的項目的列號；找不到、指到自己或之後的項目（資料不合規格）時是 -1，當作沒有上層。 */
function parentIndexOf(drafts: readonly NewItemDraft[], index: number): number {
  const ref = drafts[index]?.parentRef ?? null;
  if (ref === null) return -1;
  const found = drafts.findIndex((draft) => draft.ref === ref);
  return found >= 0 && found < index ? found : -1;
}

function depthOf(drafts: readonly NewItemDraft[], index: number): number {
  let depth = 0;
  let current = parentIndexOf(drafts, index);
  // 上層一定在前面（parentIndexOf 擋了其他情況），迴圈一定會結束。
  while (current >= 0) {
    depth += 1;
    current = parentIndexOf(drafts, current);
  }
  return Math.min(depth, MAX_INDENT_DEPTH);
}

/** 專案（藍圖與成員）讀取的狀態：還沒讀到與讀失敗時不能對負責人下「不是成員」的結論。 */
export type ProjectLoadState = 'loading' | 'error' | 'ready';

const LOADING_ASSIGNEE = '負責人：載入中…';
const UNREADABLE_ASSIGNEE = '負責人：（讀不到成員名單）';

/** 只收看得懂的值：字串、有限的數字、布林、字串陣列；null、空字串、物件之類回 null（略過這一欄）。 */
function plainValue(value: unknown): FieldValue | null {
  if (typeof value === 'string') return value.trim() === '' ? null : value;
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value === 'boolean') return value;
  if (Array.isArray(value)) {
    const texts = value.filter((entry): entry is string => typeof entry === 'string' && entry.trim() !== '');
    return texts.length === 0 ? null : texts;
  }
  return null;
}

function customDetails(draft: NewItemDraft, detail: ProjectDetail | undefined, members: readonly Member[]): readonly string[] {
  return Object.entries(draft.fields).flatMap(([key, raw]) => {
    const value = plainValue(raw);
    if (value === null) return [];
    const definition = detail === undefined ? undefined : fieldDefinition(detail.blueprint, key);
    if (definition === undefined) return [`${key}：${Array.isArray(value) ? value.join('、') : String(value)}`];
    return [`${definition.name}：${formatFieldValue(definition, value, members)}`];
  });
}

function assigneeDetail(assigneeId: string, state: ProjectLoadState, members: readonly Member[]): string {
  if (state === 'loading') return LOADING_ASSIGNEE;
  if (state === 'error') return UNREADABLE_ASSIGNEE;
  return `負責人：${members.find((member) => member.personId === assigneeId)?.displayName ?? NOT_A_MEMBER}`;
}

function coreDetails(draft: NewItemDraft, state: ProjectLoadState, members: readonly Member[]): readonly string[] {
  const details: string[] = [];
  if (draft.assigneeId !== null) details.push(assigneeDetail(draft.assigneeId, state, members));
  const priority = draft.priority === null ? undefined : (PRIORITY_LABELS as Readonly<Record<string, string | undefined>>)[draft.priority];
  if (priority !== undefined) details.push(`優先度：${priority}`);
  if (draft.startDate !== null) details.push(`開始日：${formatDate(draft.startDate)}`);
  if (draft.dueDate !== null) details.push(`到期日：${formatDate(draft.dueDate)}`);
  if (draft.estimate !== null) details.push(`估計：${formatNumber(draft.estimate)}`);
  return details;
}

/** 建議裡的項目轉成卡片上的列；藍圖還沒讀到時類型與欄位先寫原本的 key。 */
export function newItemRows(
  drafts: readonly NewItemDraft[],
  detail: ProjectDetail | undefined,
  state: ProjectLoadState = detail === undefined ? 'loading' : 'ready',
): readonly NewItemRow[] {
  const members = detail?.members ?? [];
  return drafts.map((draft, index) => {
    const parentIndex = parentIndexOf(drafts, index);
    const description = draft.description !== null && draft.description.trim() !== '' ? draft.description : null;
    return {
      typeName: detail?.blueprint.itemTypes.find((type) => type.key === draft.type)?.name ?? draft.type,
      title: draft.title,
      depth: depthOf(drafts, index),
      details: [...coreDetails(draft, state, members), ...customDetails(draft, detail, members)],
      parentKey: parentIndex >= 0 ? null : draft.parentKey,
      parentTitle: parentIndex >= 0 ? (drafts[parentIndex]?.title ?? null) : null,
      description,
    };
  });
}
