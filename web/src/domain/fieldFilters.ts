import type { FieldDefinition, FieldType, ProjectBlueprint } from '../api/blueprint';
import type { ItemSortField } from '../api/types';

/** 網址與 API 參數的前綴：`field.client=client_a`、排序 `field.client:asc`。 */
export const FIELD_PARAM_PREFIX = 'field.';

/** 「沒有填」。選項的 key 一定是小寫字母開頭、人員是編號、勾選是 true／false，所以不會撞到真的值。 */
export const FIELD_NONE = '_none';

/** 能篩選的型別，和後端的 FieldFilter 一樣。 */
export const FILTERABLE_FIELD_TYPES: readonly FieldType[] = ['single_select', 'multi_select', 'person', 'checkbox'];

/** 能排序的型別，和後端的 WorkItemSortField.Field 一樣。 */
export const SORTABLE_FIELD_TYPES: readonly FieldType[] = ['text', 'number', 'currency', 'date', 'single_select', 'checkbox'];

/**
 * 自訂欄位的篩選：欄位的 key 對到一個值（選項的 key、人員編號或 `me`、`true`／`false`、`_none`）。
 * 網頁一次只選一個值，和其他篩選一樣（規範 7.3：不做多選的下拉）；API 收得下多個值，給 CLI 與之後用。
 */
export type FieldFilters = Readonly<Record<string, string>>;

export const NO_FIELD_FILTERS: FieldFilters = {};

// 和藍圖的 key 同一個規則；網址可能是手打的，不像 key 的直接略過。
const KEY = /^[a-z][a-z0-9_]{1,39}$/;

export function isFieldKey(value: string): boolean {
  return KEY.test(value);
}

/** 網址上的 `field.<key>=值`。值是空的、key 不像 key 的略過。 */
export function fieldFiltersFromSearchParams(params: URLSearchParams): FieldFilters {
  const result: Record<string, string> = {};
  for (const [name, raw] of params) {
    if (!name.startsWith(FIELD_PARAM_PREFIX)) continue;
    const key = name.slice(FIELD_PARAM_PREFIX.length);
    const value = raw.trim();
    if (isFieldKey(key) && value !== '') result[key] = value;
  }
  return result;
}

/** 寫回網址的參數，依 key 排序，同樣的條件網址一樣。 */
export function fieldFilterEntries(filters: FieldFilters): [string, string][] {
  return Object.keys(filters)
    .sort()
    .map((key) => [`${FIELD_PARAM_PREFIX}${key}`, filters[key] ?? '']);
}

export function hasFieldFilters(filters: FieldFilters): boolean {
  return Object.keys(filters).length > 0;
}

/** 給 API 的條件（`ItemQuery.fields`）；沒有條件時是 undefined，不送。 */
export function fieldFiltersQuery(filters: FieldFilters): Readonly<Record<string, readonly string[]>> | undefined {
  const keys = Object.keys(filters);
  if (keys.length === 0) return undefined;
  return Object.fromEntries(keys.map((key) => [key, [filters[key] ?? '']]));
}

export function isFilterableField(field: FieldDefinition): boolean {
  return FILTERABLE_FIELD_TYPES.includes(field.type);
}

export function isSortableField(field: FieldDefinition): boolean {
  return SORTABLE_FIELD_TYPES.includes(field.type);
}

/** 專案裡能篩選的欄位，照藍圖的順序。 */
export function filterableFields(blueprint: ProjectBlueprint): readonly FieldDefinition[] {
  return blueprint.fields.filter(isFilterableField);
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** 後端收的值：選項 key 的格式、人員是 me／GUID、勾選是 true／false；`_none` 除了勾選都收。 */
function isValidFieldValue(type: FieldType, value: string): boolean {
  switch (type) {
    case 'single_select':
    case 'multi_select':
      return value === FIELD_NONE || KEY.test(value);
    case 'person':
      return value === FIELD_NONE || value === 'me' || GUID.test(value);
    case 'checkbox':
      return value === 'true' || value === 'false';
    default:
      return false;
  }
}

/**
 * 只留藍圖裡還在、而且能篩的欄位，且值的格式後端收得下。網址與存的檢視可能是舊的或手打的
 * （欄位被刪、型別被改、值亂寫），原樣送出去後端回 query.invalid，整張表就擋掉了；格式不對的值等於沒有這個條件。
 * 格式對、但藍圖裡已經沒有的選項不在這裡檢查：後端照樣查，自然找不到。
 */
export function knownFieldFilters(filters: FieldFilters, blueprint: ProjectBlueprint): FieldFilters {
  const filterable = new Map(filterableFields(blueprint).map((field) => [field.key, field.type]));
  return Object.fromEntries(
    Object.entries(filters).filter(([key, value]) => {
      const type = filterable.get(key);
      return type !== undefined && isValidFieldValue(type, value);
    }),
  );
}

/** `field.<key>` 這種排序欄位的 key；不是自訂欄位的排序時是 null。 */
export function sortFieldKey(field: string): string | null {
  if (!field.startsWith(FIELD_PARAM_PREFIX)) return null;
  const key = field.slice(FIELD_PARAM_PREFIX.length);
  return isFieldKey(key) ? key : null;
}

export function fieldSort(key: string): ItemSortField {
  return `${FIELD_PARAM_PREFIX}${key}`;
}
