import type { ProjectBlueprint } from '../api/blueprint';
import type { ItemSort } from '../api/types';
import { isSortableField, knownFieldFilters, sortFieldKey } from './fieldFilters';
import type { ItemFilters } from './itemFilters';

/**
 * 依自訂欄位排序時，欄位還在藍圖裡、型別也能排才留著。網址與存的檢視可能是舊的（欄位被刪、型別被改），
 * 原樣送出去後端回 422 query.invalid，整張表就擋掉了。核心欄位的排序不看藍圖。
 */
export function knownSort(sort: ItemSort | null, blueprint: ProjectBlueprint): ItemSort | null {
  if (sort === null) return null;
  const key = sortFieldKey(sort.field);
  if (key === null) return sort;
  const field = blueprint.fields.find((candidate) => candidate.key === key);
  return field !== undefined && isSortableField(field) ? sort : null;
}

/** 表格從網址讀到的條件，去掉藍圖裡不在、不能篩的欄位條件，與不在、不能排的欄位排序。 */
export function knownItemFilters(filters: ItemFilters, blueprint: ProjectBlueprint): ItemFilters {
  return { ...filters, fields: knownFieldFilters(filters.fields, blueprint), sort: knownSort(filters.sort, blueprint) };
}

/** 看板、行事曆與時間軸的條件，同樣只留藍圖裡還能篩的欄位。 */
export function knownBoardFields<T extends { readonly fields: ItemFilters['fields'] }>(filters: T, blueprint: ProjectBlueprint): T {
  return { ...filters, fields: knownFieldFilters(filters.fields, blueprint) };
}
