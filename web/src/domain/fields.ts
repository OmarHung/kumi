import type { FieldDefinition, ProjectBlueprint } from '../api/blueprint';
import type { FieldValue, Member } from '../api/types';
import { findItemType } from './blueprint';
import { formatDate } from './dates';

/** 核心欄位在錯誤的 path 與轉換的 requireFields 裡用的名字。 */
export const CORE_FIELD_LABELS: Readonly<Record<string, string>> = {
  title: '標題',
  type: '類型',
  state: '狀態',
  assignee: '負責人',
  priority: '優先度',
  start_date: '開始日',
  due_date: '到期日',
  estimate: '估計',
  parent: '上層項目',
  cycle: '週期',
  milestone: '里程碑',
  description: '說明',
};

export function fieldDefinition(
  blueprint: ProjectBlueprint,
  fieldKey: string,
): FieldDefinition | undefined {
  return blueprint.fields.find((field) => field.key === fieldKey);
}

/** 欄位的顯示名稱：核心欄位用中文，自訂欄位用藍圖的名稱，都不是時原樣顯示。 */
export function fieldLabel(blueprint: ProjectBlueprint, path: string): string {
  return CORE_FIELD_LABELS[path] ?? fieldDefinition(blueprint, path)?.name ?? path;
}

export function typeHasField(blueprint: ProjectBlueprint, typeKey: string, fieldKey: string): boolean {
  return findItemType(blueprint, typeKey)?.fields?.includes(fieldKey) ?? false;
}

/** 沒有設 parents 的類型只能放在最上層（後端回 item.parent_not_allowed）。 */
export function typeAllowsParent(blueprint: ProjectBlueprint, typeKey: string): boolean {
  return (findItemType(blueprint, typeKey)?.parents ?? []).length > 0;
}

export function anyTypeHasParents(blueprint: ProjectBlueprint): boolean {
  return blueprint.itemTypes.some((itemType) => (itemType.parents ?? []).length > 0);
}

const numberFormat = new Intl.NumberFormat('zh-TW', { maximumFractionDigits: 6 });

function formatCurrency(value: number, currency: string | undefined): string {
  if (currency === undefined) {
    return numberFormat.format(value);
  }
  try {
    return new Intl.NumberFormat('zh-TW', {
      style: 'currency',
      currency,
      minimumFractionDigits: 0,
      maximumFractionDigits: 2,
    }).format(value);
  } catch {
    // 藍圖的幣別代碼由後端驗證；萬一瀏覽器不認得，至少把數字和代碼都顯示出來。
    return `${numberFormat.format(value)} ${currency}`;
  }
}

export function formatNumber(value: number): string {
  return numberFormat.format(value);
}

function optionName(definition: FieldDefinition, key: string): string {
  return definition.options?.find((option) => option.key === key)?.name ?? key;
}

/** Array.isArray 認不出唯讀陣列的型別，用這個收窄成多選的值。 */
export function isOptionList(value: FieldValue | undefined): value is readonly string[] {
  return Array.isArray(value);
}

/** 單選是一個 key，多選是 key 的陣列；兩種都轉成陣列處理。 */
export function optionKeys(value: FieldValue): readonly string[] {
  return isOptionList(value) ? value : [String(value)];
}

function formatList(definition: FieldDefinition, value: FieldValue): string {
  return optionKeys(value)
    .map((key) => optionName(definition, key))
    .join('、');
}

/**
 * 成員欄位的值是人員編號，名字從成員清單找；被移出專案的人已經不在清單裡、找不到名字，就這樣稱呼他。
 * 負責人不一樣：伺服器回的就帶名字。
 */
export const NOT_A_MEMBER = '不是成員的人';

function formatPerson(value: FieldValue, members: readonly Member[]): string {
  return members.find((member) => member.personId === value)?.displayName ?? NOT_A_MEMBER;
}

/** 自訂欄位的值轉成文字，給畫面與輔助科技用。沒有值是空字串（勾選是「否」）。 */
export function formatFieldValue(
  definition: FieldDefinition,
  value: FieldValue | undefined,
  members: readonly Member[],
): string {
  if (definition.type === 'checkbox') {
    return value === true ? '是' : '否';
  }
  if (value === undefined) {
    return '';
  }
  switch (definition.type) {
    case 'single_select':
    case 'multi_select':
      return formatList(definition, value);
    case 'date':
      return formatDate(String(value));
    case 'number':
      return typeof value === 'number' ? formatNumber(value) : String(value);
    case 'currency':
      return typeof value === 'number' ? formatCurrency(value, definition.currency) : String(value);
    case 'person':
      return formatPerson(value, members);
    default:
      return String(value);
  }
}

/** 連結欄位只把 http 與 https 做成可以點的連結；其他寫法（javascript: 之類）只當文字。 */
export function safeHttpUrl(value: string): string | undefined {
  try {
    const url = new URL(value);
    return url.protocol === 'http:' || url.protocol === 'https:' ? value : undefined;
  } catch {
    return undefined;
  }
}
