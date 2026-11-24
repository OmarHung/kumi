import type { FieldDefinition, ProjectBlueprint } from '../api/blueprint';
import type { FieldValue, Item } from '../api/types';

/*
 * 假伺服器依自訂欄位篩選與排序（internal/HANDOFF-pm6-field-filters.md 第 3 節）：
 * `field.<key>=值` 可以重複（同一個欄位是「或」，不同欄位是「而且」）；`sort=field.<key>:asc|desc`。
 * 只有單選、多選、人員、勾選能篩；沒有值的排最後（兩個方向都是）；勾選沒勾的在前；單選依藍圖的選項順序，沒給藍圖時依 key。
 */

const PREFIX = 'field.';
const NONE = '_none';

function isEmpty(value: FieldValue | undefined): boolean {
  return value === undefined || (Array.isArray(value) && value.length === 0);
}

function matchesValue(value: FieldValue | undefined, wanted: string, me: string): boolean {
  if (wanted === NONE) return isEmpty(value);
  if (wanted === 'true') return value === true;
  if (wanted === 'false') return value !== true;
  const target = wanted === 'me' ? me : wanted;
  return Array.isArray(value) ? value.includes(target) : value === target;
}

/** 請求裡所有 `field.<key>` 條件（排序的 `sort` 不算）。 */
function fieldConditions(params: URLSearchParams): [string, string[]][] {
  const keys = new Set([...params.keys()].filter((name) => name.startsWith(PREFIX)));
  return [...keys].map((name) => [name.slice(PREFIX.length), params.getAll(name)]);
}

export function matchesFieldParams(item: Item, params: URLSearchParams, me: string): boolean {
  return fieldConditions(params).every(([key, wanted]) => wanted.some((value) => matchesValue(item.fields[key], value, me)));
}

function rankOf(definition: FieldDefinition | undefined, value: FieldValue): string | number | boolean {
  if (definition?.type === 'single_select') {
    const index = (definition.options ?? []).findIndex((option) => option.key === value);
    return index === -1 ? (definition.options ?? []).length : index;
  }
  return typeof value === 'object' ? value.join(',') : value;
}

function compareRanks(a: string | number | boolean, b: string | number | boolean): number {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

/** 依 `sort=field.<key>:dir` 排；不是自訂欄位的排序（或沒給）時照編號遞增。 */
export function sortByFieldParam(items: readonly Item[], sort: string | null, blueprint?: ProjectBlueprint): Item[] {
  const byNumber = (a: Item, b: Item) => a.number - b.number;
  const [name = '', direction = 'asc'] = (sort ?? '').split(':');
  if (!name.startsWith(PREFIX)) return items.toSorted(byNumber);
  const key = name.slice(PREFIX.length);
  const definition = blueprint?.fields.find((field) => field.key === key);
  const sign = direction === 'desc' ? -1 : 1;
  const isCheckbox = definition?.type === 'checkbox';
  return items.toSorted((a, b) => {
    const left = isCheckbox ? (a.fields[key] ?? false) : a.fields[key];
    const right = isCheckbox ? (b.fields[key] ?? false) : b.fields[key];
    if (isEmpty(left) !== isEmpty(right)) return isEmpty(left) ? 1 : -1;
    if (left === undefined || right === undefined) return byNumber(a, b);
    return sign * compareRanks(rankOf(definition, left), rankOf(definition, right)) || byNumber(a, b);
  });
}
