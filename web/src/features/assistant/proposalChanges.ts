import type { ItemFieldChange } from '../../api/aiTypes';
import type { FieldValue, ItemDetail, Member, Priority, ProjectDetail } from '../../api/types';
import { findState } from '../../domain/blueprint';
import { formatDate } from '../../domain/dates';
import { fieldDefinition, formatFieldValue, formatNumber, NOT_A_MEMBER } from '../../domain/fields';
import { PRIORITY_LABELS } from '../../domain/labels';

/** 現在的值：負責人用項目自己帶的名字（對方已經不是成員也查得到），其他欄位照 coreText。 */
function currentCoreText(field: string, item: ItemDetail, context: Context): string {
  if (field === 'assignee') return item.assignee?.displayName ?? EMPTY_VALUE;
  return coreText(field, currentCore(field, item), context);
}

/** 卡片上的一列：欄位名稱、現在的值（讀不到項目時是 null）與建議的值。 */
export type ChangeRow =
  | { readonly kind: 'value'; readonly field: string; readonly label: string; readonly before: string | null; readonly after: string }
  | { readonly kind: 'description'; readonly field: string; readonly label: string; readonly text: string | null };

export const EMPTY_VALUE = '（空）';
export const CLEARED_VALUE = '（清掉）';

const CORE_LABELS: Readonly<Record<string, string>> = {
  title: '標題',
  description: '說明',
  status: '狀態',
  assignee: '負責人',
  priority: '優先度',
  startDate: '開始日',
  dueDate: '到期日',
  estimate: '估計',
};

const CUSTOM_PREFIX = 'fields.';

interface Context {
  readonly detail: ProjectDetail | undefined;
  readonly item: ItemDetail | undefined;
}

/** 不認得的值（型別對不上）照原文顯示：物件寫成 JSON，不寫成 [object Object]。 */
function plain(value: unknown): string {
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function isPriority(value: unknown): value is Priority {
  return typeof value === 'string' && value in PRIORITY_LABELS;
}

function personName(personId: unknown, members: readonly Member[]): string {
  return members.find((member) => member.personId === personId)?.displayName ?? NOT_A_MEMBER;
}

function priorityText(value: unknown): string {
  return isPriority(value) ? PRIORITY_LABELS[value] : plain(value);
}

function dateText(value: unknown): string {
  return typeof value === 'string' ? formatDate(value) : plain(value);
}

function stateText(context: Context, key: unknown): string {
  const stateKey = plain(key);
  const { detail, item } = context;
  if (detail === undefined || item === undefined) return stateKey;
  return findState(detail.blueprint, item.type, stateKey)?.name ?? stateKey;
}

/** 核心欄位的值（建議的，或項目現在的）轉成人看得懂的字。 */
function coreText(field: string, value: unknown, context: Context): string {
  if (value === null || value === undefined) return EMPTY_VALUE;
  switch (field) {
    case 'status':
      return stateText(context, value);
    case 'assignee':
      return personName(value, context.detail?.members ?? []);
    case 'priority':
      return priorityText(value);
    case 'startDate':
    case 'dueDate':
      return dateText(value);
    case 'estimate':
      return typeof value === 'number' ? formatNumber(value) : plain(value);
    default:
      return plain(value);
  }
}

function currentCore(field: string, item: ItemDetail): unknown {
  switch (field) {
    case 'title':
      return item.title;
    case 'status':
      return item.state;
    case 'assignee':
      return item.assignee?.personId ?? null;
    case 'priority':
      return item.priority;
    case 'startDate':
      return item.startDate;
    case 'dueDate':
      return item.dueDate;
    case 'estimate':
      return item.estimate;
    default:
      return null;
  }
}

function customText(key: string, value: unknown, context: Context): string {
  if (value === null || value === undefined) return EMPTY_VALUE;
  const definition = context.detail === undefined ? undefined : fieldDefinition(context.detail.blueprint, key);
  // 值的型別對不上藍圖時（套用時工作引擎才會擋）照原文顯示。
  if (definition === undefined) return plain(value);
  return formatFieldValue(definition, value as FieldValue, context.detail?.members ?? []);
}

function rowFor(change: ItemFieldChange, context: Context): ChangeRow {
  const { field, value } = change;
  const { item } = context;
  if (field === 'description' && (typeof value !== 'string' || value.trim() === '')) {
    // 清空說明：不能寫成「改成新的內容」，人看不出按下去會清掉。
    return { kind: 'value', field, label: CORE_LABELS.description ?? field, before: null, after: CLEARED_VALUE };
  }
  if (field === 'description') {
    return { kind: 'description', field, label: CORE_LABELS.description ?? field, text: typeof value === 'string' ? value : null };
  }
  if (field.startsWith(CUSTOM_PREFIX)) {
    const key = field.slice(CUSTOM_PREFIX.length);
    const definition = context.detail === undefined ? undefined : fieldDefinition(context.detail.blueprint, key);
    return {
      kind: 'value',
      field,
      label: definition?.name ?? key,
      before: item === undefined ? null : customText(key, item.fields[key], context),
      after: value === null ? CLEARED_VALUE : customText(key, value, context),
    };
  }
  return {
    kind: 'value',
    field,
    label: CORE_LABELS[field] ?? field,
    before: item === undefined ? null : currentCoreText(field, item, context),
    after: value === null ? CLEARED_VALUE : coreText(field, value, context),
  };
}

/** 建議裡每個欄位轉成一列；專案或項目還沒讀到時沒有「現在的值」，狀態、負責人先寫原本的編號。 */
export function changeRows(changes: readonly ItemFieldChange[], detail: ProjectDetail | undefined, item: ItemDetail | undefined): readonly ChangeRow[] {
  const context: Context = { detail, item };
  return changes.map((change) => rowFor(change, context));
}

/** 已處理的卡片只列欄位名稱：「標題、狀態、留言」。 */
export function changedLabels(changes: readonly ItemFieldChange[], detail: ProjectDetail | undefined, hasComment: boolean): string {
  const labels = changeRows(changes, detail, undefined).map((row) => row.label);
  return (hasComment ? [...labels, '留言'] : labels).join('、');
}
