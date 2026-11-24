import type { SortDirection, StateCategory } from '../../api/blueprint';
import type { Priority } from '../../api/types';
import { compareDueThenKey } from '../../domain/dates';
import type { MyWorkColumnId } from './myWorkColumns';
import { groupingOf, type MyWorkGrouping } from './myWorkGroups';
import type { RowPlacement } from './useMyWorkRows';

/*
 * 「我的工作」表格的排序（docs/DESIGN.md 8.3「我的工作」）：點表頭在每一組裡面排，分組照舊。
 * 記在網址的 sort（和專案的表格同一種寫法「欄位:方向」），預設（到期日遞增）不寫。
 * 在網頁排，不送給伺服器：伺服器的清單照到期日一頁一頁給，前面的組（已過期、今天）載入就是完整的，換排序也不必重抓。
 * 排的是列記下的位置（useMyWorkRows 的 RowPlacement）：這一頁改過的列停在原位，和分組同一個規則。
 */

/** 欄位的名稱和專案的表格一樣（存的檢視也是這幾個）；project 只有我的工作有。 */
export type MyWorkSortField = 'number' | 'title' | 'state' | 'priority' | 'due_date' | 'project';

export interface MyWorkSort {
  readonly field: MyWorkSortField;
  readonly direction: SortDirection;
}

/** 每一欄排序用的欄位（表頭與窄螢幕的「排序」）。 */
export const SORT_FIELDS: Readonly<Record<MyWorkColumnId, MyWorkSortField>> = {
  key: 'number',
  title: 'title',
  state: 'state',
  priority: 'priority',
  due: 'due_date',
  project: 'project',
};

/** 沒有排序時的順序：到期日早的在前、沒有到期日的最後，同一天照編號。 */
export const DEFAULT_MY_WORK_SORT: MyWorkSort = { field: 'due_date', direction: 'asc' };

export const SORT_PARAM = 'sort';

const FIELDS: readonly MyWorkSortField[] = ['number', 'title', 'state', 'priority', 'due_date', 'project'];
const DIRECTIONS: readonly SortDirection[] = ['asc', 'desc'];

function isDefault(sort: MyWorkSort): boolean {
  return sort.field === DEFAULT_MY_WORK_SORT.field && sort.direction === DEFAULT_MY_WORK_SORT.direction;
}

/** 「欄位:方向」。 */
export function myWorkSortValue(sort: MyWorkSort): string {
  return `${sort.field}:${sort.direction}`;
}

/** 讀「欄位:方向」；不認得的與預設都是 null（網址可能是手打的、檢視可能是舊的：照預設排，不擋住清單）。 */
export function parseMyWorkSort(value: string | null): MyWorkSort | null {
  const [field, direction, ...rest] = (value ?? '').split(':');
  const knownField = FIELDS.find((candidate) => candidate === field);
  const knownDirection = DIRECTIONS.find((candidate) => candidate === direction);
  if (knownField === undefined || knownDirection === undefined || rest.length > 0) return null;
  const sort: MyWorkSort = { field: knownField, direction: knownDirection };
  return isDefault(sort) ? null : sort;
}

/**
 * 實際用的排序：依專案分組時沒有專案欄、每一組只有一個專案，依專案排就是預設——當成沒有排序，
 * 表頭、窄螢幕的下拉與存的檢視才不會留著一個看不見的排序（網址上的留著，換回依急迫程度時照舊）。
 */
export function effectiveMyWorkSort(sort: MyWorkSort | null, grouping: MyWorkGrouping): MyWorkSort | null {
  return sort?.field === 'project' && grouping === 'project' ? null : sort;
}

/** 網址上的排序（照分組換成實際用的，effectiveMyWorkSort）；null 是預設。 */
export function myWorkSortOf(params: URLSearchParams): MyWorkSort | null {
  return effectiveMyWorkSort(parseMyWorkSort(params.get(SORT_PARAM)), groupingOf(params));
}

/** 換上新的排序，其他參數（條件、分組、開著的面板）留著；預設不寫進網址。 */
export function withMyWorkSort(params: URLSearchParams, sort: MyWorkSort | null): URLSearchParams {
  const next = new URLSearchParams(params);
  if (sort === null || isDefault(sort)) next.delete(SORT_PARAM);
  else next.set(SORT_PARAM, myWorkSortValue(sort));
  return next;
}

/** 點欄位標題：換一欄從遞增開始，再點遞減，再點回到預設（和專案的表格一樣，domain/itemFilters.ts 的 nextSort）。 */
export function nextMyWorkSort(current: MyWorkSort | null, field: MyWorkSortField): MyWorkSort | null {
  const shown = current ?? DEFAULT_MY_WORK_SORT;
  if (shown.field !== field) return { field, direction: 'asc' };
  const next: MyWorkSort | null = shown.direction === 'asc' ? { field, direction: 'desc' } : null;
  return next === null || isDefault(next) ? null : next;
}

type Compare = (a: RowPlacement, b: RowPlacement) => number;

// 狀態、優先度、到期日和伺服器的跨專案排序同一個規則（CrossProjectQuery.cs）：狀態依類別的先後再依 key，
// 優先度「無」與沒有到期日兩個方向都排最後。編號與標題不同：網頁拿不到專案的建立時間，編號照代號的字母順序
// （和預設的「到期日再編號」同一個）；標題照中文的排序、數字照大小（「第 2 版」在「第 10 版」前面）。
// 排序只在網頁做，畫面上自己一致就好。
const CATEGORY_RANK: Readonly<Record<StateCategory, number>> = { backlog: 0, not_started: 1, in_progress: 2, done: 3, canceled: 4 };
const PRIORITY_RANK: Readonly<Record<Exclude<Priority, 'none'>, number>> = { low: 1, medium: 2, high: 3, urgent: 4 };

const collator = new Intl.Collator('zh-Hant', { numeric: true, sensitivity: 'base' });

function ordinal(a: string, b: string): number {
  if (a === b) return 0;
  return a < b ? -1 : 1;
}

function directed(compare: Compare, direction: SortDirection): Compare {
  return direction === 'asc' ? compare : (a, b) => compare(b, a);
}

/** 沒有值的兩個方向都排最後（方向只換有值的那些）。 */
function emptyLast<V>(valueOf: (row: RowPlacement) => V | null, compare: (a: V, b: V) => number, direction: SortDirection): Compare {
  return (a, b) => {
    const x = valueOf(a);
    const y = valueOf(b);
    if (x === null || y === null) return x === y ? 0 : x === null ? 1 : -1;
    return direction === 'asc' ? compare(x, y) : compare(y, x);
  };
}

function priorityRank(row: RowPlacement): number | null {
  return row.priority === 'none' ? null : PRIORITY_RANK[row.priority];
}

function comparatorOf(sort: MyWorkSort, projectName: (projectKey: string) => string | undefined): Compare {
  const { direction } = sort;
  switch (sort.field) {
    case 'number':
      return directed((a, b) => ordinal(a.projectKey, b.projectKey) || a.number - b.number, direction);
    case 'title':
      return directed((a, b) => collator.compare(a.title, b.title), direction);
    case 'state':
      return directed((a, b) => CATEGORY_RANK[a.stateCategory] - CATEGORY_RANK[b.stateCategory] || ordinal(a.state, b.state), direction);
    case 'priority':
      return emptyLast(priorityRank, (x, y) => x - y, direction);
    case 'due_date':
      return emptyLast((row) => row.dueDate, ordinal, direction);
    case 'project': {
      // 照畫面上的名稱；還沒讀到專案內容的先用代號。
      const nameOf = (row: RowPlacement) => projectName(row.projectKey) ?? row.projectKey;
      return directed((a, b) => collator.compare(nameOf(a), nameOf(b)), direction);
    }
  }
}

/** 一組裡的列照排序排（回傳新的陣列）；同值的照預設（到期日再編號），順序才穩定。 */
export function sortMyWorkRows<T extends RowPlacement>(
  rows: readonly T[],
  sort: MyWorkSort | null,
  projectName: (projectKey: string) => string | undefined,
): readonly T[] {
  if (sort === null) return rows.toSorted(compareDueThenKey);
  const compare = comparatorOf(sort, projectName);
  return rows.toSorted((a, b) => compare(a, b) || compareDueThenKey(a, b));
}
