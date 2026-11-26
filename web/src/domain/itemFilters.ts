import type { SortDirection } from '../api/blueprint';
import type { Cycle } from '../api/planning';
import type { CalendarDate, ItemQuery, ItemSort, ItemSortField, Priority } from '../api/types';
import type { WeekStart } from '../lib/weekStart';
import { dueQuery, parseDueFilter, parsePriority, type DueFilter } from './dueFilter';
import {
  fieldFilterEntries,
  fieldFiltersFromSearchParams,
  fieldFiltersQuery,
  hasFieldFilters,
  NO_FIELD_FILTERS,
  sortFieldKey,
  type FieldFilters,
} from './fieldFilters';
import { cycleQuery, milestoneQuery, parseCycleFilter, parseMilestoneFilter } from './planningFilter';

/** 項目表格的篩選與排序。放在網址的查詢字串裡，重新整理或分享網址都還在。 */
export interface ItemFilters {
  readonly type: string | null;
  readonly state: string | null;
  /** `me`、`unassigned` 或人員編號。 */
  readonly assignee: string | null;
  /** `me` 或人員編號。 */
  readonly createdBy: string | null;
  readonly due: DueFilter | null;
  readonly priority: Priority | null;
  /** `current`（目前的週期）、`none` 或週期編號。 */
  readonly cycle: string | null;
  /** `none` 或里程碑編號。 */
  readonly milestone: string | null;
  /** 自訂欄位的條件，網址上是 `field.<key>=值`。 */
  readonly fields: FieldFilters;
  readonly q: string;
  readonly sort: ItemSort | null;
}

export const EMPTY_FILTERS: ItemFilters = {
  type: null,
  state: null,
  assignee: null,
  createdBy: null,
  due: null,
  priority: null,
  cycle: null,
  milestone: null,
  fields: NO_FIELD_FILTERS,
  q: '',
  sort: null,
};

export const SORT_FIELDS: readonly ItemSortField[] = [
  'number',
  'title',
  'state',
  'priority',
  'start_date',
  'due_date',
  'estimate',
  'created_at',
  'updated_at',
];

// 自訂欄位寫成 field.<key>；能不能排（欄位還在、型別能排）要看藍圖，由畫面在套用前檢查。
function isSortField(value: string): value is ItemSortField {
  return SORT_FIELDS.some((field) => field === value) || sortFieldKey(value) !== null;
}

function isSortDirection(value: string): value is SortDirection {
  return value === 'asc' || value === 'desc';
}

// 網址可能是手打的：不認得的排序直接略過，不要讓後端回 query.invalid 把整張表擋掉。
function parseSort(value: string | null): ItemSort | null {
  const [field = '', direction = ''] = (value ?? '').split(':');
  return isSortField(field) && isSortDirection(direction) ? { field, direction } : null;
}

function nonEmpty(value: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed === '' ? null : trimmed;
}

export function filtersFromSearchParams(params: URLSearchParams): ItemFilters {
  return {
    type: nonEmpty(params.get('type')),
    state: nonEmpty(params.get('state')),
    assignee: nonEmpty(params.get('assignee')),
    createdBy: nonEmpty(params.get('createdBy')),
    due: parseDueFilter(params.get('due')),
    priority: parsePriority(params.get('priority')),
    cycle: parseCycleFilter(params.get('cycle')),
    milestone: parseMilestoneFilter(params.get('milestone')),
    fields: fieldFiltersFromSearchParams(params),
    q: params.get('q')?.trim() ?? '',
    sort: parseSort(params.get('sort')),
  };
}

export function filtersToSearchParams(filters: ItemFilters): URLSearchParams {
  const entries: [string, string][] = [];
  if (filters.type !== null) entries.push(['type', filters.type]);
  if (filters.state !== null) entries.push(['state', filters.state]);
  if (filters.assignee !== null) entries.push(['assignee', filters.assignee]);
  if (filters.createdBy !== null) entries.push(['createdBy', filters.createdBy]);
  if (filters.due !== null) entries.push(['due', filters.due]);
  if (filters.priority !== null) entries.push(['priority', filters.priority]);
  if (filters.cycle !== null) entries.push(['cycle', filters.cycle]);
  if (filters.milestone !== null) entries.push(['milestone', filters.milestone]);
  entries.push(...fieldFilterEntries(filters.fields));
  if (filters.q.trim() !== '') entries.push(['q', filters.q.trim()]);
  if (filters.sort !== null) entries.push(['sort', `${filters.sort.field}:${filters.sort.direction}`]);
  return new URLSearchParams(entries);
}

/**
 * 給 API 的條件。沒設的不送，後端用預設（依編號遞增）。todayDate 是使用者所在地的今天（到期與「目前的週期」用），
 * cycles 是專案的週期清單（「目前的週期」從這裡換成編號）。
 */
export function toItemQuery(filters: ItemFilters, limit: number, todayDate: CalendarDate, weekStart: WeekStart, cycles: readonly Cycle[] = []): ItemQuery {
  return {
    ...(filters.type !== null && { type: [filters.type] }),
    ...(filters.state !== null && { state: [filters.state] }),
    ...(filters.assignee !== null && { assignee: filters.assignee }),
    ...(filters.createdBy !== null && { createdBy: filters.createdBy }),
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...dueQuery(filters.due, todayDate, weekStart),
    ...cycleQuery(filters.cycle, cycles, todayDate),
    ...milestoneQuery(filters.milestone),
    ...fieldsQuery(filters.fields),
    ...(filters.q !== '' && { q: filters.q }),
    ...(filters.sort !== null && { sort: filters.sort }),
    limit,
  };
}

/** 有沒有篩掉項目。排序不算：只換順序，項目還是全部。 */
export function hasActiveFilters(filters: ItemFilters): boolean {
  return (
    filters.type !== null ||
    filters.state !== null ||
    filters.assignee !== null ||
    filters.createdBy !== null ||
    filters.due !== null ||
    filters.priority !== null ||
    filters.cycle !== null ||
    filters.milestone !== null ||
    hasFieldFilters(filters.fields) ||
    filters.q !== ''
  );
}

/** 展開進 ItemQuery：沒有條件時什麼都不加。 */
export function fieldsQuery(fields: FieldFilters): Pick<ItemQuery, 'fields'> {
  const query = fieldFiltersQuery(fields);
  return query === undefined ? {} : { fields: query };
}

/** 點欄位標題：遞增 → 遞減 → 回到預設；換一欄從遞增開始。 */
export function nextSort(current: ItemSort | null, field: ItemSortField): ItemSort | null {
  if (current?.field !== field) {
    return { field, direction: 'asc' };
  }
  return current.direction === 'asc' ? { field, direction: 'desc' } : null;
}
