import type { Cycle } from '../api/planning';
import type { CalendarDate, Item, ItemQuery, Priority } from '../api/types';
import type { WeekStart } from '../lib/weekStart';
import { GROUP_PARAM, boardGroupParam, parseBoardGroup, STATE_GROUP, type BoardGroup } from './boardGroup';
import { dueQuery, matchesDue, type DueFilter } from './dueFilter';
import { FIELD_NONE, fieldFilterEntries, hasFieldFilters, type FieldFilters } from './fieldFilters';
import { fieldsQuery, filtersFromSearchParams } from './itemFilters';
import { cycleQuery, matchesCycle, matchesMilestone, milestoneQuery } from './planningFilter';

/** 專案頁的四種版面。放在網址的 view 參數，重新整理或分享網址都還在；沒給就是表格。 */
export type ProjectView = 'table' | 'board' | 'calendar' | 'timeline';

/** 表格以外的版面：都吃同一套篩選（BoardFilters；行事曆與時間軸再加類型與狀態），網址一律帶 view。 */
export type FilteredView = Exclude<ProjectView, 'table'>;

export const FILTERED_VIEWS: readonly FilteredView[] = ['board', 'calendar', 'timeline'];

export function isFilteredView(value: string | null): value is FilteredView {
  return FILTERED_VIEWS.some((view) => view === value);
}

const VIEW_PARAM = 'view';

/**
 * 行事曆與時間軸各自記在網址上的參數（月份、刻度、第一天）。換篩選條件時留著：
 * 看著十二月調一下負責人，不該跳回今天所在的月份；切換版面時不帶（那是另一個版面的位置）。
 */
export const DATE_VIEW_PARAMS: readonly string[] = ['month', 'scale', 'from'];

/** 一欄一次讀這麼多個，往下用「載入更多」。和表格一樣。 */
export const BOARD_PAGE_SIZE = 50;

export function viewFromSearchParams(params: URLSearchParams): ProjectView {
  const view = params.get(VIEW_PARAM);
  return isFilteredView(view) ? view : 'table';
}

/**
 * 切到另一種版面的查詢字串。只帶兩邊都有的條件（搜尋、負責人、到期、優先度、週期、里程碑、自訂欄位）：
 * 排序在看板、行事曆與時間軸上沒有意義（看板照編號排；行事曆與時間軸依日期排），看板的流程在其他版面上也沒有。
 * 類型與狀態在表格、行事曆與時間軸之間帶著（合約要求這兩種日期版面有類型、狀態篩選）；
 * 只有去看板時丟掉：看板依狀態分欄，狀態篩選沒有意義。
 */
export function switchViewParams(params: URLSearchParams, view: ProjectView): URLSearchParams {
  const { type, state, q, assignee, due, priority, cycle, milestone, fields } = filtersFromSearchParams(params);
  const entries: [string, string][] = view === 'table' ? [] : [[VIEW_PARAM, view]];
  if (view !== 'board') {
    if (type !== null) entries.push(['type', type]);
    if (state !== null) entries.push(['state', state]);
  }
  if (q !== '') entries.push(['q', q]);
  if (assignee !== null) entries.push(['assignee', assignee]);
  if (due !== null) entries.push(['due', due]);
  if (priority !== null) entries.push(['priority', priority]);
  if (cycle !== null) entries.push(['cycle', cycle]);
  if (milestone !== null) entries.push(['milestone', milestone]);
  // 自訂欄位的條件每種版面都有；group 只屬於看板，不帶。
  entries.push(...fieldFilterEntries(fields));
  return new URLSearchParams(entries);
}

/** 看板的條件。流程是「看哪一個看板」，不算篩選；搜尋、負責人、到期與優先度套用到每一欄。 */
export interface BoardFilters {
  /** 網址上的流程 key；沒給（或不認得）時用預設的流程。 */
  readonly workflow: string | null;
  readonly assignee: string | null;
  readonly due: DueFilter | null;
  readonly priority: Priority | null;
  readonly cycle: string | null;
  readonly milestone: string | null;
  /** 自訂欄位的條件，和表格同一套（網址上是 `field.<key>=值`）。 */
  readonly fields: FieldFilters;
  /** 看板依什麼分欄；行事曆與時間軸不用（網址上有也不寫回去）。 */
  readonly group: BoardGroup;
  readonly q: string;
}

/**
 * 行事曆與時間軸的條件：看板那一套再加表格的類型與狀態。看板沒有這兩個（依狀態分欄），
 * 所以放在另一個型別，看板的程式仍然用 BoardFilters。
 */
export interface DateViewTypeState {
  readonly type: string | null;
  readonly state: string | null;
}

export type DateViewFilters = BoardFilters & DateViewTypeState;

/** 網址是行事曆或時間軸時才讀類型與狀態；看板與表格的網址上有也不算（那兩種版面不用）。 */
export function dateViewFiltersFromSearchParams(params: URLSearchParams): DateViewFilters {
  const view = viewFromSearchParams(params);
  const { type, state } = filtersFromSearchParams(params);
  const isDateView = view === 'calendar' || view === 'timeline';
  return { ...boardFiltersFromSearchParams(params), type: isDateView ? type : null, state: isDateView ? state : null };
}

export function boardFiltersFromSearchParams(params: URLSearchParams): BoardFilters {
  const { q, assignee, due, priority, cycle, milestone, fields } = filtersFromSearchParams(params);
  const workflow = params.get('workflow')?.trim() ?? '';
  const group = viewFromSearchParams(params) === 'board' ? parseBoardGroup(params.get(GROUP_PARAM)) : STATE_GROUP;
  return { workflow: workflow === '' ? null : workflow, assignee, due, priority, cycle, milestone, fields, group, q };
}

/**
 * 寫回網址。選的是預設的流程就不寫，網址短一點，藍圖換了預設也跟著換；
 * 流程只有看板有（行事曆與時間軸不分流程），其他版面不寫。
 */
export function boardFiltersToSearchParams(
  filters: BoardFilters & Partial<DateViewTypeState>,
  defaultWorkflow: string | undefined,
  view: FilteredView = 'board',
): URLSearchParams {
  const entries: [string, string][] = [[VIEW_PARAM, view]];
  if (view === 'board' && filters.workflow !== null && filters.workflow !== defaultWorkflow) {
    entries.push(['workflow', filters.workflow]);
  }
  const group = view === 'board' ? boardGroupParam(filters.group) : null;
  if (group !== null) entries.push([GROUP_PARAM, group]);
  if (view !== 'board') {
    if (filters.type != null) entries.push(['type', filters.type]);
    if (filters.state != null) entries.push(['state', filters.state]);
  }
  if (filters.assignee !== null) entries.push(['assignee', filters.assignee]);
  if (filters.due !== null) entries.push(['due', filters.due]);
  if (filters.priority !== null) entries.push(['priority', filters.priority]);
  if (filters.cycle !== null) entries.push(['cycle', filters.cycle]);
  if (filters.milestone !== null) entries.push(['milestone', filters.milestone]);
  entries.push(...fieldFilterEntries(filters.fields));
  if (filters.q.trim() !== '') entries.push(['q', filters.q.trim()]);
  return new URLSearchParams(entries);
}

/** 分組不算篩選：只換分欄的方式，項目還是全部。 */
export function hasActiveBoardFilters(filters: BoardFilters & Partial<DateViewTypeState>): boolean {
  return (
    (filters.type ?? null) !== null ||
    (filters.state ?? null) !== null ||
    filters.assignee !== null ||
    filters.due !== null ||
    filters.priority !== null ||
    filters.cycle !== null ||
    filters.milestone !== null ||
    hasFieldFilters(filters.fields) ||
    filters.q !== ''
  );
}

/** 一欄的條件：這個狀態、用這個流程的類型、照編號遞增，再加上搜尋、負責人、到期、優先度、週期與里程碑。 */
export function boardColumnQuery(
  filters: BoardFilters,
  types: readonly string[],
  state: string,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[] = [],
): ItemQuery {
  return {
    type: types,
    state: [state],
    ...(filters.assignee !== null && { assignee: filters.assignee }),
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...dueQuery(filters.due, todayDate, weekStart),
    ...cycleQuery(filters.cycle, cycles, todayDate),
    ...milestoneQuery(filters.milestone),
    ...fieldsQuery(filters.fields),
    ...(filters.q !== '' && { q: filters.q }),
    sort: { field: 'number', direction: 'asc' },
    limit: BOARD_PAGE_SIZE,
  };
}

const ITEM_NUMBER = /^\d+$/;

function matchesAssignee(item: Item, assignee: string | null, me: string): boolean {
  if (assignee === null) return true;
  if (assignee === 'unassigned') return item.assignee === null;
  return item.assignee?.personId === (assignee === 'me' ? me : assignee);
}

/** 和後端一樣：標題包含這段文字（不分大小寫），或這段文字就是這個項目的編號（SMK-7、smk-07 都算）。 */
export function matchesSearch(item: Item, q: string): boolean {
  const text = q.trim();
  if (text === '' || item.title.toLocaleLowerCase().includes(text.toLocaleLowerCase())) return true;
  const dash = text.lastIndexOf('-');
  const digits = text.slice(dash + 1);
  return dash > 0 && ITEM_NUMBER.test(digits) && text.slice(0, dash).toUpperCase() === item.projectKey && Number(digits) === item.number;
}

function isEmptyFieldValue(value: unknown): boolean {
  return value === undefined || value === null || (Array.isArray(value) && value.length === 0);
}

/**
 * 一個自訂欄位的條件，和後端一樣：多選看有沒有選這一個、勾選的 false 是沒勾、`_none` 是沒有填、人員的 me 是自己。
 * 不看藍圖：值的形狀就分得出型別（陣列是多選、布林是勾選、字串是單選或人員）。
 */
export function matchesFieldFilter(item: Item, key: string, wanted: string, me: string): boolean {
  const value: unknown = item.fields[key];
  if (wanted === FIELD_NONE) return isEmptyFieldValue(value);
  if (wanted === 'true') return value === true;
  if (wanted === 'false') return value !== true;
  const target = wanted === 'me' ? me : wanted;
  return Array.isArray(value) ? value.includes(target) : value === target;
}

export function matchesFieldFilters(item: Item, fields: FieldFilters, me: string): boolean {
  return Object.entries(fields).every(([key, wanted]) => matchesFieldFilter(item, key, wanted, me));
}

/**
 * 項目符不符合看板的搜尋、負責人、週期與里程碑。只用在送出中的卡片（它還不在伺服器那一欄的結果裡）：
 * 送出中換了條件，不符合的不畫出來。欄裡其他的卡片一律以伺服器的結果為準。me 是目前使用者的人員編號。
 */
export function matchesBoardFilters(
  item: Item,
  filters: BoardFilters,
  me: string,
  todayDate: CalendarDate,
  weekStart: WeekStart,
  cycles: readonly Cycle[] = [],
): boolean {
  return (
    matchesCycle(item, filters.cycle, cycles, todayDate) &&
    matchesMilestone(item, filters.milestone) &&
    matchesAssignee(item, filters.assignee, me) &&
    (filters.priority === null || item.priority === filters.priority) &&
    matchesDue(filters.due, item.dueDate, item.stateCategory, todayDate, weekStart) &&
    matchesFieldFilters(item, filters.fields, me) &&
    matchesSearch(item, filters.q)
  );
}
