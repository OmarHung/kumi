import type { StateCategory } from '../api/blueprint';
import type { CalendarDate, Item, ItemQuery, ItemSort, Priority } from '../api/types';
import type { WeekStart } from '../lib/weekStart';
import { dueQuery, matchesDue, OPEN_CATEGORIES, parseDueFilter, parsePriority, type DueFilter } from './dueFilter';
import { matchesSearch, type ProjectView } from './projectView';

/**
 * 「我的工作」的版面與條件（internal/HANDOFF-my-work-views.md 第 3 節）。和專案頁同一種做法：網址就是狀態，
 * 但條件的意思不同——預設是「我負責、還沒結束的」，跨專案，所以狀態用類別、類型用 key。
 */

/** 四種版面，和專案頁一樣；網址的 view，沒給就是表格。 */
export type MyWorkLayout = ProjectView;

const LAYOUTS: readonly MyWorkLayout[] = ['table', 'board', 'calendar', 'timeline'];

/** 狀態類別，照流程的先後。看板的欄也照這個順序。 */
export const STATE_CATEGORIES: readonly StateCategory[] = ['backlog', 'not_started', 'in_progress', 'done', 'canceled'];

/**
 * 狀態的條件。跨專案時只能用類別（各專案的流程不同，同一個 key 在不同專案是不同的狀態）；
 * 選了一個專案時才可以選那個專案的某一個狀態。
 */
export type MyWorkStatus =
  | { readonly kind: 'open' }
  | { readonly kind: 'all' }
  | { readonly kind: 'category'; readonly category: StateCategory }
  | { readonly kind: 'state'; readonly state: string };

/** 「我的工作」就是我負責的：送給後端的負責人一律是這個值（使用者 2026-11-19 的決定，沒有負責人的篩選）。 */
const ASSIGNEE_ME = 'me';

export interface MyWorkFilters {
  readonly q: string;
  /** 類型的 key（跨專案依 key 合併，見 api/types.ts 的 ItemTypeSummary）。 */
  readonly type: string | null;
  readonly status: MyWorkStatus;
  readonly due: DueFilter | null;
  readonly priority: Priority | null;
  /** 專案代號（大寫）；null 是所有看得到的專案。 */
  readonly project: string | null;
}

export const DEFAULT_MY_WORK_FILTERS: MyWorkFilters = {
  q: '',
  type: null,
  status: { kind: 'open' },
  due: null,
  priority: null,
  project: null,
};

/** 清單、看板一次讀這麼多個（API 的上限），往下用「載入更多」。 */
export const MY_WORK_PAGE_SIZE = 200;

/** 表格與看板的順序：到期日早的在前，沒有到期日的最後（和原本的「我的工作」一樣）。 */
export const MY_WORK_SORT: ItemSort = { field: 'due_date', direction: 'asc' };

const VIEW_PARAM = 'view';
const CATEGORY_ALL = 'all';

function nonEmpty(value: string | null): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed === '' ? null : trimmed;
}

function isCategory(value: string | null): value is StateCategory {
  return STATE_CATEGORIES.some((category) => category === value);
}

export function myWorkLayoutOf(params: URLSearchParams): MyWorkLayout {
  const view = params.get(VIEW_PARAM);
  return LAYOUTS.find((layout) => layout !== 'table' && layout === view) ?? 'table';
}

// 網址可能是手打的：不認得的值當成預設，不讓後端回 query.invalid 把整頁擋掉。
function statusOf(params: URLSearchParams, project: string | null): MyWorkStatus {
  const state = nonEmpty(params.get('state'));
  if (state !== null && project !== null) return { kind: 'state', state };
  const category = params.get('category');
  if (category === CATEGORY_ALL) return { kind: 'all' };
  return isCategory(category) ? { kind: 'category', category } : { kind: 'open' };
}

export function myWorkFiltersFromParams(params: URLSearchParams): MyWorkFilters {
  const project = nonEmpty(params.get('project'))?.toUpperCase() ?? null;
  return {
    q: params.get('q')?.trim() ?? '',
    type: nonEmpty(params.get('type')),
    status: statusOf(params, project),
    due: parseDueFilter(params.get('due')),
    priority: parsePriority(params.get('priority')),
    project,
  };
}

function statusEntries(status: MyWorkStatus): [string, string][] {
  switch (status.kind) {
    case 'open':
      return [];
    case 'all':
      return [['category', CATEGORY_ALL]];
    case 'category':
      return [['category', status.category]];
    case 'state':
      return [['state', status.state]];
  }
}

/** 條件寫成網址的參數；預設的不寫（網址短一點，預設改了也跟著換）。 */
export function myWorkFilterEntries(filters: MyWorkFilters): [string, string][] {
  const entries: [string, string][] = [];
  if (filters.q.trim() !== '') entries.push(['q', filters.q.trim()]);
  if (filters.type !== null) entries.push(['type', filters.type]);
  entries.push(...statusEntries(filters.status));
  if (filters.due !== null) entries.push(['due', filters.due]);
  if (filters.priority !== null) entries.push(['priority', filters.priority]);
  if (filters.project !== null) entries.push(['project', filters.project]);
  return entries;
}

/** 版面加上條件的網址參數（不含分組、行事曆的月份、開著的面板，那些由呼叫端照需要帶）。 */
export function myWorkSearchParams(layout: MyWorkLayout, filters: MyWorkFilters): URLSearchParams {
  const view: [string, string][] = layout === 'table' ? [] : [[VIEW_PARAM, layout]];
  return new URLSearchParams([...view, ...myWorkFilterEntries(filters)]);
}

/** 只有表格讀、換版面時照樣帶著的參數：分組與排序（features/my-work 的 myWorkGroups.ts、myWorkSort.ts）。 */
const TABLE_PARAMS: readonly string[] = ['group', 'sort'];

/**
 * 換到另一種版面：條件、分組與排序（只有表格讀，換回表格時還在）留著；行事曆與時間軸各自的位置（月份、刻度、第一天）不帶，
 * 那是另一個版面的位置。開著的面板（item）由呼叫端照專案頁的做法帶過去。
 */
export function switchMyWorkLayoutParams(params: URLSearchParams, layout: MyWorkLayout): URLSearchParams {
  const next = myWorkSearchParams(layout, myWorkFiltersFromParams(params));
  for (const name of TABLE_PARAMS) {
    const value = params.get(name);
    if (value !== null) next.append(name, value);
  }
  return next;
}

/** 和預設不一樣就算有條件（「清除條件」回到預設：我負責、還沒結束的）。 */
export function hasActiveMyWorkFilters(filters: MyWorkFilters): boolean {
  return myWorkFilterEntries(filters).length > 0;
}

/** 狀態的條件包含哪些類別；null 是不限類別（所有狀態，或某一個狀態——那個狀態的類別要看專案的藍圖）。 */
export function statusCategories(status: MyWorkStatus): readonly StateCategory[] | null {
  switch (status.kind) {
    case 'open':
      return OPEN_CATEGORIES;
    case 'all':
    case 'state':
      return null;
    case 'category':
      return [status.category];
  }
}

function intersect(a: readonly StateCategory[] | null, b: readonly StateCategory[] | undefined): readonly StateCategory[] | null {
  if (b === undefined) return a;
  return a === null ? b : a.filter((category) => b.includes(category));
}

/**
 * 給跨專案清單（GET /v1/items）的條件，不含排序、分頁與日期範圍（各版面自己加）。todayDate 是使用者所在地的今天。
 * 結果一定是空的時候回 null（例如「完成」加「已過期」：已過期只算還沒結束的），呼叫端不必送請求。
 */
export function myWorkConditions(filters: MyWorkFilters, todayDate: CalendarDate, weekStart: WeekStart): ItemQuery | null {
  const { stateCategory: dueCategories, ...dueRange } = dueQuery(filters.due, todayDate, weekStart);
  const categories = intersect(statusCategories(filters.status), dueCategories);
  if (categories?.length === 0) return null;
  return {
    ...(filters.type !== null && { type: [filters.type] }),
    ...(filters.status.kind === 'state' && { state: [filters.status.state] }),
    ...(categories !== null && { stateCategory: categories }),
    assignee: ASSIGNEE_ME,
    ...(filters.priority !== null && { priority: [filters.priority] }),
    ...dueRange,
    ...(filters.q !== '' && { q: filters.q }),
    ...(filters.project !== null && { project: [filters.project] }),
  };
}

function matchesStatus(item: Item, status: MyWorkStatus): boolean {
  if (status.kind === 'state') return item.state === status.state;
  const categories = statusCategories(status);
  return categories === null || categories.includes(item.stateCategory);
}

/**
 * 項目符不符合條件，和 myWorkConditions 送給後端的是同一個意思。只用在伺服器的結果還沒反映的項目
 * （看板上剛拖過去、送出中的卡片）；清單一律以伺服器的結果為準。me 是目前使用者的人員編號。
 */
export function matchesMyWorkFilters(item: Item, filters: MyWorkFilters, me: string, todayDate: CalendarDate, weekStart: WeekStart): boolean {
  return (
    (filters.type === null || item.type === filters.type) &&
    matchesStatus(item, filters.status) &&
    item.assignee?.personId === me &&
    (filters.priority === null || item.priority === filters.priority) &&
    matchesDue(filters.due, item.dueDate, item.stateCategory, todayDate, weekStart) &&
    (filters.project === null || item.projectKey === filters.project) &&
    matchesSearch(item, filters.q)
  );
}
