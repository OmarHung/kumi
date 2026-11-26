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

function customDetails(draft: NewItemDraft, detail: ProjectDetail | undefined, members: readonly Member[]): readonly string[] {
  return Object.entries(draft.fields).map(([key, value]) => {
    const definition = detail === undefined ? undefined : fieldDefinition(detail.blueprint, key);
    if (definition === undefined) return `${key}：${typeof value === 'string' ? value : JSON.stringify(value)}`;
    return `${definition.name}：${formatFieldValue(definition, value as FieldValue, members)}`;
  });
}

function coreDetails(draft: NewItemDraft, members: readonly Member[]): readonly string[] {
  const details: string[] = [];
  if (draft.assigneeId !== null) {
    details.push(`負責人：${members.find((member) => member.personId === draft.assigneeId)?.displayName ?? NOT_A_MEMBER}`);
  }
  if (draft.priority !== null) details.push(`優先度：${PRIORITY_LABELS[draft.priority]}`);
  if (draft.startDate !== null) details.push(`開始日：${formatDate(draft.startDate)}`);
  if (draft.dueDate !== null) details.push(`到期日：${formatDate(draft.dueDate)}`);
  if (draft.estimate !== null) details.push(`估計：${formatNumber(draft.estimate)}`);
  return details;
}

/** 建議裡的項目轉成卡片上的列；藍圖還沒讀到時類型與欄位先寫原本的 key。 */
export function newItemRows(drafts: readonly NewItemDraft[], detail: ProjectDetail | undefined): readonly NewItemRow[] {
  const members = detail?.members ?? [];
  return drafts.map((draft, index) => {
    const parentIndex = parentIndexOf(drafts, index);
    const description = draft.description !== null && draft.description.trim() !== '' ? draft.description : null;
    return {
      typeName: detail?.blueprint.itemTypes.find((type) => type.key === draft.type)?.name ?? draft.type,
      title: draft.title,
      depth: depthOf(drafts, index),
      details: [...coreDetails(draft, members), ...customDetails(draft, detail, members)],
      parentKey: parentIndex >= 0 ? null : draft.parentKey,
      parentTitle: parentIndex >= 0 ? (drafts[parentIndex]?.title ?? null) : null,
      description,
    };
  });
}
